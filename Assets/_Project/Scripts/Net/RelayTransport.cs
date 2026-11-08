using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Qos;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace NomadCity.Net
{
    /// <summary>
    /// <see cref="ITransport"/> 的 Unity Relay 实现。
    /// 这里只认 Relay 中继码；玩家看到的 6 位房间码由 <c>LobbyClient</c> 负责分发（阶段 3）。
    ///
    /// 拓扑：主机权威。主机收发并校验全部命令，客户端只负责发送自己的操作、重放主机广播回来的命令。
    /// 线格式（全部小端序，UTP 自带）：
    /// <code>
    /// [byte 消息类型] + 各自负载
    ///   Hello     (客户端 → 主机)：int 协议版本（连上后第一条，版本不一致主机直接断开）
    ///   Setup     (主机 → 刚接入的客户端)：int 协议版本 | int 座位 | int 人数 | int 扩展位标志
    ///   Command   (客户端 → 主机)：int 长度 | CommandWire 字节
    ///   Broadcast (主机 → 全部客户端)：byte 是否成功 | int 长度+错误文本 | int 长度+CommandWire 字节
    ///   Snapshot  (主机 → 客户端)：int 总长 | int 片号 | int 片数 | int 本片长 | 字节...
    ///   Heartbeat (主机 → 客户端)：空负载，用于客户端判定断线
    /// </code>
    ///
    /// 注意：本类只是「传输通道」，它本身不会让游戏变成联机 —— 还需要上层把命令流接到
    /// <see cref="Submit"/> / <see cref="RemoteCommandArrived"/> 上（阶段 3）。
    /// </summary>
    public sealed class RelayTransport : ITransport
    {
        /// <summary>Relay 连接类型，两端必须一致。udp 延迟最低；WebGL 才必须用 dtls/wss。</summary>
        private const string ConnectionType = "udp";

        /// <summary>单包上限。所有命令参数加起来远小于这个值，超出说明编码出了问题。</summary>
        private const int MaxPayloadBytes = 1024;

        /// <summary>开局快照每包携带的字节数。必须远小于 UTP / Relay 的单包上限（约 1200 字节）。</summary>
        private const int SnapshotChunkSize = 800;

        /// <summary>快照总长度上限，防止异常包把内存撑爆。</summary>
        private const int MaxSnapshotBytes = 64 * 1024;

        /// <summary>主机心跳间隔：没有操作时也定期发一点数据，客户端据此区分「安静」和「掉线」。</summary>
        private const float HostHeartbeatIntervalSeconds = 5f;

        /// <summary>客户端超过这个时长收不到任何主机消息 → 判定断线（额度保险第 4 条，不做无限重连）。</summary>
        private const float ClientSilenceTimeoutSeconds = 30f;

        /// <summary>远端发来的一包数据的类型标记。</summary>
        private enum WireMessage : byte
        {
            Setup = 1,
            Command = 2,
            Broadcast = 3,
            Snapshot = 4,
            Heartbeat = 5,
            Hello = 6,
            Chat = 7,
            SkipVoteRequest = 8,
            SkipVoteState = 9,
            PeerLeftNotice = 10,
            PeerReturnedNotice = 11,
        }

        // —— UTP 运行时 ——
        private NetworkDriver _driver;

        /// <summary>
        /// 可靠有序管道。所有消息（命令 / 广播 / 快照 / 投票 / 聊天）都必须走它：
        /// UTP 的默认管道是「不可靠」的（NetworkPipeline.Null），而开局快照要拆成几十个分片，
        /// 只要丢一片就永远拼不齐 —— 联机测试里客户端卡在选企业家界面、主机已经开始行动，
        /// 就是这个原因。命令走不可靠管道也会偶发「点了没反应、主机永远等不到」。
        /// </summary>
        private NetworkPipeline _reliablePipeline;
        private NativeList<NetworkConnection> _serverConnections;
        private NetworkConnection _clientConnection;

        // —— 主机侧状态 ——
        private GameSetup _setup;
        private int _seatCount = 4;

        /// <summary>与 <see cref="_serverConnections"/> 同下标的座位号，保证客户端自报的 Seat 不被采信。</summary>
        private readonly List<int> _connectionSeats = new List<int>();

        /// <summary>与 <see cref="_serverConnections"/> 同下标的身份令牌：重连时凭它恢复原座位。</summary>
        private readonly List<string> _connectionTokens = new List<string>();

        /// <summary>已掉线玩家保留的原座位：身份令牌 → 座位号。只有同一令牌（同一局同一端）能取回。</summary>
        private readonly Dictionary<string, int> _reservedSeats = new Dictionary<string, int>();

        /// <summary>主机侧：最近一次广播过的开局快照，供后加入的客户端补发。</summary>
        private byte[] _cachedSnapshot;

        /// <summary>待分帧发送的开局快照：一条连接一份，避免一帧灌满可靠管道的发送窗口。</summary>
        private readonly List<PendingSnapshot> _pendingSnapshots = new List<PendingSnapshot>();

        // —— 客户端拼包状态 ——
        private byte[] _snapshotBuffer;
        private bool[] _snapshotChunksSeen;
        private int _snapshotChunksReceived;

        // —— 身份状态 ——
        private bool _isHost;
        private bool _isMultiplayer;
        private bool _started;
        private bool _shutdown;
        private int _localSeat = -1;

        /// <summary>主机心跳计时。</summary>
        private float _heartbeatTimer;

        /// <summary>客户端距离上次收到主机消息已经过去的秒数。</summary>
        private float _silenceSeconds;

        /// <summary>本帧判定出的断线原因：等收包循环结束后再抛事件，避免上层在循环里 Shutdown 打乱 UTP。</summary>
        private string _pendingConnectionLost;

        // —— 客户端自动重连 ——
        private const int MaxReconnectAttempts = 6;

        /// <summary>本端身份令牌：随 Hello 报给主机，重连时凭它取回原座位。</summary>
        private readonly string _clientToken = LoadOrCreateToken();

        /// <summary>身份令牌存在本机，重启游戏后仍是同一个人 —— 重新进同一个房间就能回到原座位。</summary>
        private const string TokenPrefsKey = "NomadCity.PlayerToken";

        private static string LoadOrCreateToken()
        {
            string token = PlayerPrefs.GetString(TokenPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(token)) return token;

            token = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(TokenPrefsKey, token);
            PlayerPrefs.Save();
            return token;
        }

        /// <summary>
        /// 本机身份令牌（存在 PlayerPrefs，关掉游戏重开也不变）。
        /// Relay 重连靠它取回原座位，Lobby 层也用它识别「同一个玩家」，避免重启后多占一个席位。
        /// </summary>
        public static string LocalIdentity => LoadOrCreateToken();

        /// <summary>客户端的 Relay 中继码：断线后重新 JoinAllocation 用，不清空。</summary>
        private string _joinCode;

        private int _reconnectAttempt;
        private float _reconnectTimer;
        private bool _reconnectWaiting;

        public bool IsMultiplayer => _isMultiplayer;

        public bool IsHost => _isHost;

        /// <summary>本机座位；主机固定 0，客户端要等收到 Setup 才知道（收到前为 -1）。</summary>
        public int LocalSeat => _localSeat;

        /// <summary>
        /// 某个 Relay 座位上客户端的身份令牌（主机自己不在连接表里，返回 null）。
        /// 用来把 Lobby 名单里的选择（阵营颜色）精确对到座位上。
        /// </summary>
        public string TokenForSeat(int seat)
        {
            for (int i = 0; i < _connectionSeats.Count; i++)
                if (_connectionSeats[i] == seat) return _connectionTokens[i];

            return null;
        }

        /// <summary>主机创建中继后得到的房间码，供界面展示给其他玩家。客户端为 null。</summary>
        public string JoinCode { get; private set; }

        /// <summary>连接/分配期间是否有新的阶段进展（供 UI 显示「正在创建房间…」这类提示，可留空）。</summary>
        public event Action<string> StatusChanged;

        public event Action<CommandDto> CommandSubmitted;
        public event Action<CommandDto, int> RemoteCommandArrived;

        /// <summary>
        /// 主机广播回来的「这条命令被拒绝」：带上原命令与原因。
        /// 与 <see cref="ErrorArrived"/> 的区别是保留了命令本身 —— 界面才能判断被拒的是哪一步，
        /// 而不是把所有拒绝都当成当前正在等待的流程失败。
        /// </summary>
        public event Action<CommandDto, string> CommandRejectedArrived;
        public event Action<GameSetup, int> SetupArrived;
        public event Action<string> ErrorArrived;
        public event Action<byte[]> SnapshotArrived;
        public event Action<int> PeerJoined;
        public event Action<string> ConnectionLost;

        /// <summary>收到一条聊天：座位 + 内容。主机广播出去的、客户端收到的都走这里。</summary>
        public event Action<int, string> ChatArrived;

        /// <summary>某个座位掉线（参数是它占的座位）。主机在断开时触发，其他客户端由主机广播触发。</summary>
        public event Action<int> PeerLeft;

        /// <summary>掉线的那位玩家又回来了（重新进到原座位）。主机本地触发，其他客户端由主机广播触发。</summary>
        public event Action<int> PeerReturned;

        /// <summary>客户端正在自动重连（参数：第几次、还要等几秒）。</summary>
        public event Action<int, float> Reconnecting;

        /// <summary>主机侧：某客户端请求投票跳过 <c>targetSeat</c>（参数：投票人座位、目标座位）。</summary>
        public event Action<int, int> SkipVoteRequestArrived;

        /// <summary>客户端/主机侧：投票进度刷新（目标座位、已投座位掩码、需要票数；目标 -1 表示当前没有投票）。</summary>
        public event Action<int, int, int> SkipVoteArrived;

        /// <summary>作为主机开启一局：申请中继、绑定、取房间码。成功后 <see cref="JoinCode"/> 才有值。</summary>
        public void Host(GameSetup setup, int seatCount)
        {
            if (!BeginStart("主机")) return;

            _isHost = true;
            _localSeat = 0;
            _setup = setup ?? new GameSetup(seatCount);
            _seatCount = Mathf.Clamp(seatCount, 2, 4);
            HostAsync();
        }

        /// <summary>作为客户端用 Relay 中继码加入（房间码 → 中继码由 LobbyClient 完成）。</summary>
        public void Join(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Fail("中继码为空。");
                return;
            }

            if (!BeginStart("客户端")) return;

            _isHost = false;
            _joinCode = joinCode.Trim(); // 断线后重连要复用同一个中继码
            JoinAsync(_joinCode);
        }

        /// <summary>
        /// 提交一条命令。主机直接把它抛给本地监听者（随后由上层校验并 <see cref="Broadcast"/>）；
        /// 客户端则发给主机，等主机广播回来后才真正生效。
        /// </summary>
        public void Submit(CommandDto command)
        {
            if (_shutdown) return;

            if (_isHost)
            {
                CommandSubmitted?.Invoke(command);
                return;
            }

            if (!_clientConnection.IsCreated)
            {
                Fail("还没有连上主机，这条操作未能发送。");
                return;
            }

            SendCommand(_clientConnection, command);
        }

        /// <summary>主机把一条命令的执行结果广播给所有客户端（<paramref name="ok"/> 为 false 时带拒绝原因）。</summary>
        public void Broadcast(CommandDto command, bool ok, string error)
        {
            if (_shutdown) return;

            if (!_isHost)
            {
                Fail("只有主机可以广播命令结果。");
                return;
            }

            if (!_serverConnections.IsCreated) return;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;
                SendBroadcast(connection, command, ok, error);
            }
        }

        /// <summary>
        /// 主机广播开局快照。快照比单包上限大，这里按 <see cref="SnapshotChunkSize"/> 切片发送，
        /// 客户端收齐后拼回完整一份再抛 <see cref="SnapshotArrived"/>。最近一份会被缓存，
        /// 之后才接入的客户端在收到 Setup 后自动补发。
        /// </summary>
        public void BroadcastSnapshot(byte[] snapshot)
        {
            if (_shutdown) return;

            if (!_isHost)
            {
                Fail("只有主机可以广播开局快照。");
                return;
            }

            if (snapshot == null || snapshot.Length == 0)
            {
                Fail("开局快照为空，未发送。");
                return;
            }

            if (snapshot.Length > MaxSnapshotBytes)
            {
                Fail($"开局快照过大（{snapshot.Length} 字节，上限 {MaxSnapshotBytes}）。");
                return;
            }

            _cachedSnapshot = snapshot;
            if (!_serverConnections.IsCreated) return;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;
                SendSnapshot(connection, snapshot);
            }
        }

        /// <summary>单条聊天最长字数：中文 3 字节/字，120 字远小于单包上限。</summary>
        private const int MaxChatLength = 120;

        /// <summary>
        /// 发一条聊天。主机：直接广播给所有客户端并回显给自己；客户端：只发给主机，由主机按
        /// 连接改写座位后再广播回来（自己也会收到，保证所有人看到的顺序一致）。
        /// </summary>
        public void SendChat(string text)
        {
            if (_shutdown || string.IsNullOrWhiteSpace(text)) return;

            text = text.Trim();
            if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);

            if (_isHost)
            {
                BroadcastChat(0, text);
                ChatArrived?.Invoke(0, text);
                return;
            }

            if (!_clientConnection.IsCreated)
            {
                Fail("还没有连上主机，这条消息没发出去。");
                return;
            }

            SendChatTo(_clientConnection, 0, text);
        }

        /// <summary>主机把一条聊天广播给所有客户端。</summary>
        private void BroadcastChat(int seat, string text)
        {
            if (!_serverConnections.IsCreated) return;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;
                SendChatTo(connection, seat, text);
            }
        }

        private void SendChatTo(NetworkConnection connection, int seat, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            if (payload.Length > MaxPayloadBytes - 16)
            {
                Fail("这条聊天太长了，已丢弃。");
                return;
            }

            if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0)
            {
                Fail("聊天发送队列暂时不可用，这条消息没发出去。");
                return;
            }

            writer.WriteByte((byte)WireMessage.Chat);
            writer.WriteInt(seat);
            WritePayload(ref writer, payload);
            _driver.EndSend(writer);
        }

        /// <summary>这个座位现在还有人在线吗（单机恒为 true）。主机侧按已分配的连接判断。</summary>
        public bool IsSeatConnected(int seat)
        {
            if (!_isMultiplayer || seat < 0) return false;
            if (seat == _localSeat) return true;
            if (!_isHost || !_serverConnections.IsCreated) return true;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                if (_serverConnections[i].IsCreated && SeatAt(i) == seat) return true;
            }

            return false;
        }

        /// <summary>客户端：投票跳过某个座位，请求由主机统计。</summary>
        public void RequestSkipVote(int targetSeat)
        {
            if (_shutdown || _isHost) return;
            if (!_clientConnection.IsCreated)
            {
                Fail("还没有连上主机，这次投票没发出去。");
                return;
            }

            if (_driver.BeginSend(_reliablePipeline, _clientConnection, out DataStreamWriter writer) != 0) return;

            writer.WriteByte((byte)WireMessage.SkipVoteRequest);
            writer.WriteInt(targetSeat);
            _driver.EndSend(writer);
        }

        /// <summary>主机：把当前投票进度广播给所有客户端。</summary>
        public void BroadcastSkipVote(int targetSeat, int voterMask, int required)
        {
            if (_shutdown || !_isHost || !_serverConnections.IsCreated) return;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;

                if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0) continue;

                writer.WriteByte((byte)WireMessage.SkipVoteState);
                writer.WriteInt(targetSeat);
                writer.WriteInt(voterMask);
                writer.WriteInt(required);
                _driver.EndSend(writer);
            }
        }

        /// <summary>主机：把「某座位掉线」广播给所有还在线的客户端。</summary>
        public void BroadcastPeerLeft(int seat)
        {
            if (_shutdown || !_isHost || !_serverConnections.IsCreated) return;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;

                if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0) continue;

                writer.WriteByte((byte)WireMessage.PeerLeftNotice);
                writer.WriteInt(seat);
                _driver.EndSend(writer);
            }
        }

        /// <summary>主机：把「某个掉线玩家已回到原座位」广播给所有客户端。</summary>
        public void BroadcastPeerReturned(int seat)
        {
            if (_shutdown || !_isHost || !_serverConnections.IsCreated) return;

            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;

                if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0) continue;

                writer.WriteByte((byte)WireMessage.PeerReturnedNotice);
                writer.WriteInt(seat);
                _driver.EndSend(writer);
            }
        }

        /// <summary>每帧推进连接状态并收包。由上层（MonoBehaviour 的 Update）驱动。</summary>
        public void Poll(float deltaTime)
        {
            if (_shutdown) return;

            // 客户端断线后退避等待：这段时间没有 driver，也必须继续倒数，否则永远重连不上。
            if (!_isHost && _reconnectWaiting)
            {
                _reconnectTimer -= deltaTime;
                if (_reconnectTimer <= 0f)
                {
                    _reconnectWaiting = false;
                    TryReconnectAsync();
                }

                return;
            }

            if (!_driver.IsCreated) return;

            if (_isHost) PollHost(deltaTime);
            else PollClient(deltaTime);
        }

        /// <summary>断开所有连接并释放 UTP 资源。</summary>
        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            if (_driver.IsCreated)
            {
                if (_isHost && _serverConnections.IsCreated)
                {
                    for (int i = 0; i < _serverConnections.Length; i++)
                    {
                        if (_serverConnections[i].IsCreated) _driver.Disconnect(_serverConnections[i]);
                    }
                }
                else if (!_isHost && _clientConnection.IsCreated)
                {
                    _driver.Disconnect(_clientConnection);
                }

                _driver.ScheduleUpdate().Complete();
                _driver.Dispose();
            }

            if (_serverConnections.IsCreated) _serverConnections.Dispose();
            _connectionSeats.Clear();
            _connectionTokens.Clear();
            _reservedSeats.Clear();
            _cachedSnapshot = null;
            _pendingSnapshots.Clear();
            ResetSnapshotAssembly();
            _heartbeatTimer = 0f;
            _silenceSeconds = 0f;
            _pendingConnectionLost = null;
            _clientConnection = default;
            _driver = default;
            _isMultiplayer = false;
        }

        // —— 启动流程 ——

        /// <summary>Host / Join 的公共前置检查，避免重复启动。</summary>
        private bool BeginStart(string role)
        {
            if (_started)
            {
                Fail("联机通道已经启动过，不能重复创建。");
                return false;
            }

            _started = true;
            _isMultiplayer = true;
            StatusChanged?.Invoke($"正在以{role}身份连接 Unity 服务…");
            return true;
        }

        /// <summary>
        /// 给新建的 driver 建一条可靠有序管道。UTP 的默认管道不可靠，命令与快照都可能丢包；
        /// 快照丢一片就永远拼不齐，客户端会一直停在等开局数据的界面。
        /// </summary>
        private void CreateReliablePipeline()
        {
            _reliablePipeline = _driver.IsCreated
                ? _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage))
                : default;
        }

        /// <summary>
        /// 用 QoS 实测到各 Relay 区域的平均延迟，申请**最快**的那个节点。
        /// 之前「写死东京/新加坡」只是猜：玩家网络到东京未必比默认区域快，硬指定反而可能更慢。
        /// 测速失败或申请失败都退回 UGS 自动选择，不影响开房。
        /// </summary>
        private async Task<Allocation> CreateAllocationWithBestRegionAsync()
        {
            string bestRegion = await FindLowestLatencyRegionAsync();

            if (!string.IsNullOrEmpty(bestRegion))
            {
                try
                {
                    Allocation allocation = await RelayService.Instance.CreateAllocationAsync(_seatCount, bestRegion);
                    Debug.Log($"[Relay] 已按实测延迟选择中继区域：{bestRegion}（实际分配：{allocation.Region}）");
                    return allocation;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Relay] 区域 {bestRegion} 申请失败（{ex.Message}），改由 UGS 自动选择。");
                }
            }

            // 测速失败 / 指定区域失败时退回 UGS 自动选择。这条路径以前不打日志，
            // 于是「延迟为什么突然五六百毫秒」看不出是不是落到了远端区域，这里补上。
            Allocation fallback = await RelayService.Instance.CreateAllocationAsync(_seatCount, null);
            Debug.LogWarning($"[Relay] 未能按实测延迟选区域，已由 UGS 自动选择：{fallback.Region}（离玩家越远，往返越高）。");
            return fallback;
        }

        /// <summary>
        /// QoS 测速：返回延迟最低的 Relay 区域（顺带把每个区域的实测延迟打进 Console，
        /// 一眼就能看出「到底是网络慢还是区域选远了」）。失败返回 null。
        /// </summary>
        private static async Task<string> FindLowestLatencyRegionAsync()
        {
            try
            {
                IList<IQosAnnotatedResult> results =
                    await QosService.Instance.GetSortedRelayQosResultsAsync(null);

                if (results == null || results.Count == 0)
                {
                    Debug.LogWarning("[Relay] QoS 没有返回任何 Relay 区域，改由 UGS 自动选择。");
                    return null;
                }

                var report = new StringBuilder();
                for (int i = 0; i < results.Count; i++)
                {
                    if (i > 0) report.Append("，");
                    report.Append($"{results[i].Region} {results[i].AverageLatencyMs}ms");
                }

                Debug.Log($"[Relay] QoS 实测区域延迟（由快到慢）：{report}");
                return results[0].Region;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Relay] QoS 区域测速失败（{ex.Message}），改由 UGS 自动选择。");
                return null;
            }
        }

        private async void HostAsync()
        {
            try
            {
                await EnsureSignedInAsync();
                if (_shutdown) return;
                StatusChanged?.Invoke("正在申请 Relay 中继…");

                // 中继最大连接数按总人数申请（主机自身也占一条中继连接），多留余量比少留安全。
                Allocation allocation = await CreateAllocationWithBestRegionAsync();
                if (_shutdown) return;

                // 分配结果只有 10 秒有效期，拿到后必须立刻 Bind。
                _serverConnections = new NativeList<NetworkConnection>(_seatCount, Allocator.Persistent);

                RelayServerData serverData = BuildServerData(
                    allocation.ServerEndpoints,
                    allocation.AllocationIdBytes,
                    allocation.ConnectionData,
                    allocation.ConnectionData, // 主机自己的 hostConnectionData 与 connectionData 相同
                    allocation.Key);

                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref serverData);
                _driver = NetworkDriver.Create(settings);
                if (_shutdown)
                {
                    _driver.Dispose();
                    return;
                }

                CreateReliablePipeline();

                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    Fail("Relay 绑定失败（Bind）。");
                    return;
                }

                if (_driver.Listen() != 0)
                {
                    Fail("Relay 监听失败（Listen）。");
                    return;
                }

                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                if (_shutdown) return;
                // 注意：这是 Relay 的「中继码」，不是给玩家看的 6 位 Lobby 房间码，两者不要混用。
                Debug.Log($"[Relay] 主机已就绪，中继码：{JoinCode}，区域：{allocation.Region}");
                StatusChanged?.Invoke($"中继已就绪：{JoinCode}");
            }
            catch (Exception ex)
            {
                if (!_shutdown) Fail($"创建房间失败：{ex.Message}");
            }
        }

        private async void JoinAsync(string joinCode)
        {
            try
            {
                await EnsureSignedInAsync();
                if (_shutdown) return;
                StatusChanged?.Invoke("正在加入房间…");

                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                if (_shutdown) return;

                RelayServerData serverData = BuildServerData(
                    allocation.ServerEndpoints,
                    allocation.AllocationIdBytes,
                    allocation.ConnectionData,
                    allocation.HostConnectionData,
                    allocation.Key);

                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref serverData);
                _driver = NetworkDriver.Create(settings);
                if (_shutdown)
                {
                    _driver.Dispose();
                    return;
                }

                CreateReliablePipeline();

                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    Fail("Relay 绑定失败（Bind）。");
                    return;
                }

                _clientConnection = _driver.Connect();
                Debug.Log($"[Relay] 已向主机发起连接，区域：{allocation.Region}");
                StatusChanged?.Invoke("已连接房间，等待主机分配座位…");
            }
            catch (Exception ex)
            {
                if (!_shutdown) Fail($"加入房间失败：{ex.Message}");
            }
        }

        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>
        /// 用 Relay 分配结果拼出 UTP 需要的服务器数据。
        /// 这里走的是「字节数组」构造函数（host/port + 三段原始数据 + key），不依赖已被移除的 AllocationUtils。
        /// </summary>
        private static RelayServerData BuildServerData(
            List<RelayServerEndpoint> endpoints,
            byte[] allocationIdBytes,
            byte[] connectionData,
            byte[] hostConnectionData,
            byte[] key)
        {
            RelayServerEndpoint endpoint = SelectEndpoint(endpoints);
            if (endpoint == null)
            {
                throw new InvalidOperationException($"Relay 分配结果里没有 {ConnectionType} 端点。");
            }

            bool isSecure = !string.Equals(endpoint.ConnectionType, RelayServerEndpoint.ConnectionTypeUdp, StringComparison.OrdinalIgnoreCase);
            return new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocationIdBytes,
                connectionData,
                hostConnectionData,
                key,
                isSecure);
        }

        private static RelayServerEndpoint SelectEndpoint(List<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null) return null;

            for (int i = 0; i < endpoints.Count; i++)
            {
                RelayServerEndpoint endpoint = endpoints[i];
                if (endpoint != null && string.Equals(endpoint.ConnectionType, ConnectionType, StringComparison.OrdinalIgnoreCase))
                {
                    return endpoint;
                }
            }

            return null;
        }

        // —— 每帧收发 ——

        private void PollHost(float deltaTime)
        {
            if (!_driver.Bound) return;

            // 保活：让 Relay 侧知道这条绑定还在。
            _driver.ScheduleUpdate().Complete();

            // 开局快照按帧分批补发（窗口只有 32 个包，一次灌完会被丢）。
            FlushPendingSnapshots();

            // 心跳：安静时也定期给客户端发一个字节，客户端据此判断主机是否还活着。
            _heartbeatTimer += deltaTime;
            if (_heartbeatTimer >= HostHeartbeatIntervalSeconds)
            {
                _heartbeatTimer = 0f;
                for (int i = 0; i < _serverConnections.Length; i++)
                {
                    if (_serverConnections[i].IsCreated) SendHeartbeat(_serverConnections[i]);
                }
            }

            // 清掉已断开的连接。
            for (int i = 0; i < _serverConnections.Length; i++)
            {
                if (!_serverConnections[i].IsCreated)
                {
                    _serverConnections.RemoveAt(i);
                    RemoveSeatAt(i);
                    RemoveTokenAt(i);
                    --i;
                }
            }

            // 接纳新客户端。座位与开局配置等收到 Hello 再发：
            // 重连要凭身份令牌恢复原座位，Accept 这一刻还不知道对面是谁。
            NetworkConnection incoming;
            while ((incoming = _driver.Accept()) != default(NetworkConnection))
            {
                _serverConnections.Add(incoming);
                _connectionSeats.Add(-1);
                _connectionTokens.Add(null);
            }

            // 处理各连接的事件。
            for (int i = 0; i < _serverConnections.Length; i++)
            {
                NetworkConnection connection = _serverConnections[i];
                if (!connection.IsCreated) continue;

                NetworkEvent.Type eventType;
                while ((eventType = _driver.PopEventForConnection(connection, out DataStreamReader stream)) != NetworkEvent.Type.Empty)
                {
                    switch (eventType)
                    {
                        case NetworkEvent.Type.Data:
                            HandleClientMessage(ref stream, i);
                            break;

                        case NetworkEvent.Type.Disconnect:
                        {
                            int leftSeat = SeatAt(i);
                            string token = TokenAt(i);
                            Debug.Log($"[Relay] 客户端断开连接（座位 {leftSeat}）。");
                            _serverConnections[i] = default(NetworkConnection);

                            // 先记住是谁走的：同一令牌重连时可以回原座位。
                            if (leftSeat >= 0 && !string.IsNullOrEmpty(token)) _reservedSeats[token] = leftSeat;

                            if (leftSeat >= 0)
                            {
                                // 先告诉主机这局的上层（界面要立刻提示），再广播给其他客户端，
                                // 免得只有房主看到「有人掉线」，其他人还在猜为什么轮不到自己。
                                PeerLeft?.Invoke(leftSeat);
                                BroadcastPeerLeft(leftSeat);
                            }

                            break;
                        }
                    }
                }
            }
        }

        private void PollClient(float deltaTime)
        {
            _driver.ScheduleUpdate().Complete();

            if (!_clientConnection.IsCreated) return;

            NetworkEvent.Type eventType;
            while ((eventType = _clientConnection.PopEvent(_driver, out DataStreamReader stream)) != NetworkEvent.Type.Empty)
            {
                switch (eventType)
                {
                    case NetworkEvent.Type.Data:
                        _silenceSeconds = 0f;
                        HandleHostMessage(ref stream);
                        break;

                    case NetworkEvent.Type.Connect:
                        _silenceSeconds = 0f;
                        Debug.Log("[Relay] 已连上主机。");
                        SendHello(_clientConnection); // 报协议版本 + 身份令牌，主机据此分配或恢复座位
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _clientConnection = default(NetworkConnection);
                        _pendingConnectionLost = "与主机的连接已断开。";
                        break;
                }
            }

            // 先抛断线事件（上层可能在里面 Shutdown），再处理静默超时。
            if (_pendingConnectionLost != null)
            {
                string reason = _pendingConnectionLost;
                _pendingConnectionLost = null;
                BeginReconnect(reason);
                return;
            }

            // 30 秒收不到任何主机消息（正常时每 5 秒至少一个心跳）→ 判定断线，进入自动重连。
            if (!_clientConnection.IsCreated) return;

            _silenceSeconds += deltaTime;
            if (_silenceSeconds < ClientSilenceTimeoutSeconds) return;

            _silenceSeconds = 0f;
            _clientConnection = default(NetworkConnection);
            BeginReconnect($"与主机的连接已中断（{ClientSilenceTimeoutSeconds:0} 秒没有收到主机数据）。");
        }

        /// <summary>
        /// 玩家手动点「重连」：清掉现有连接，立刻再连一次，不等自动重连的退避计时。
        /// 自动重连没启动（例如客户端一直卡在等开局数据）时也照样可用；主机是权威端，没有可重连的对象。
        /// </summary>
        public void ReconnectNow()
        {
            if (_shutdown || _isHost) return;

            if (string.IsNullOrEmpty(_joinCode))
            {
                ConnectionLost?.Invoke("还没有可重连的房间信息，请回到大厅重新加入。");
                return;
            }

            if (_driver.IsCreated)
            {
                _driver.ScheduleUpdate().Complete();
                _driver.Dispose();
            }

            _driver = default;
            _clientConnection = default(NetworkConnection);
            ResetSnapshotAssembly();
            _silenceSeconds = 0f;
            _reconnectWaiting = false;
            _reconnectTimer = 0f;
            _reconnectAttempt = 0;

            StatusChanged?.Invoke("正在重新连接…");
            TryReconnectAsync();
        }

        /// <summary>
        /// 断线后进入退避重连：1、2、4、8、16、30 秒各试一次，仍失败才真正抛 <see cref="ConnectionLost"/>。
        /// 每次重连都重新申请 Relay 分配（旧连接的分配已失效），并带着身份令牌向主机申请恢复原座位。
        /// </summary>
        private void BeginReconnect(string reason)
        {
            _silenceSeconds = 0f;
            _clientConnection = default(NetworkConnection);
            ResetSnapshotAssembly();

            if (_driver.IsCreated)
            {
                _driver.ScheduleUpdate().Complete();
                _driver.Dispose();
            }

            _driver = default;

            if (_shutdown) return;

            // 还没拿到过座位 = 这次是首次加入就失败（房号错 / 人满 / 版本不一致）：
            // 原座位无从恢复，重连只是徒劳，直接报错。
            if (_localSeat < 0)
            {
                ConnectionLost?.Invoke(reason);
                return;
            }

            if (string.IsNullOrEmpty(_joinCode))
            {
                ConnectionLost?.Invoke(reason);
                return;
            }

            if (_reconnectAttempt >= MaxReconnectAttempts)
            {
                ConnectionLost?.Invoke($"{reason}（已自动重连 {MaxReconnectAttempts} 次仍未成功）");
                return;
            }

            float delay = Mathf.Min(30f, Mathf.Pow(2f, _reconnectAttempt)); // 1, 2, 4, 8, 16, 30 秒
            _reconnectAttempt++;
            _reconnectWaiting = true;
            _reconnectTimer = delay;

            Debug.Log($"[Relay] 连接中断（{reason}），{delay:0} 秒后第 {_reconnectAttempt}/{MaxReconnectAttempts} 次自动重连。");
            StatusChanged?.Invoke($"连接中断，{delay:0} 秒后自动重连（{_reconnectAttempt}/{MaxReconnectAttempts}）…");
            Reconnecting?.Invoke(_reconnectAttempt, delay);
        }

        /// <summary>重连一次：重新申请 Relay 分配 → 建 driver → Connect。失败就继续退避，直到次数用尽。</summary>
        private async void TryReconnectAsync()
        {
            if (_shutdown) return;

            try
            {
                await EnsureSignedInAsync();
                if (_shutdown) return;

                StatusChanged?.Invoke("正在重新连接…");
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(_joinCode);
                if (_shutdown) return;

                RelayServerData serverData = BuildServerData(
                    allocation.ServerEndpoints,
                    allocation.AllocationIdBytes,
                    allocation.ConnectionData,
                    allocation.HostConnectionData,
                    allocation.Key);

                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref serverData);
                _driver = NetworkDriver.Create(settings);
                if (_shutdown)
                {
                    _driver.Dispose();
                    _driver = default;
                    return;
                }

                CreateReliablePipeline();

                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                    throw new InvalidOperationException("Relay 绑定失败（Bind）。");

                _clientConnection = _driver.Connect();
                Debug.Log($"[Relay] 第 {_reconnectAttempt} 次重连已向主机发起连接，等待恢复座位…");
            }
            catch (Exception ex)
            {
                if (_shutdown) return;

                Debug.LogWarning($"[Relay] 第 {_reconnectAttempt} 次重连失败：{ex.Message}");
                BeginReconnect($"重连失败：{ex.Message}");
            }
        }

        /// <summary>主机侧：解析客户端发来的命令。</summary>
        private void HandleClientMessage(ref DataStreamReader stream, int index)
        {
            int seat = SeatAt(index);

            try
            {
                byte messageType = stream.ReadByte();

                // 还没分配座位（Hello 之前）：只接受 Hello，其它消息一律丢弃。
                if (messageType == (byte)WireMessage.Hello)
                {
                    HandleHello(ref stream, index);
                    return;
                }

                if (seat < 0) return;

                if (messageType == (byte)WireMessage.Chat)
                {
                    stream.ReadInt(); // 座位一律以主机分配为准，不采信客户端自报的值
                    byte[] chatBytes = ReadPayload(ref stream);
                    if (chatBytes == null || chatBytes.Length == 0) return;

                    string chatText = Encoding.UTF8.GetString(chatBytes);
                    if (string.IsNullOrWhiteSpace(chatText)) return;
                    if (chatText.Length > MaxChatLength) chatText = chatText.Substring(0, MaxChatLength);

                    BroadcastChat(seat, chatText);
                    ChatArrived?.Invoke(seat, chatText);
                    return;
                }

                if (messageType == (byte)WireMessage.SkipVoteRequest)
                {
                    int targetSeat = stream.ReadInt();
                    SkipVoteRequestArrived?.Invoke(seat, targetSeat);
                    return;
                }

                if (messageType != (byte)WireMessage.Command) return;

                byte[] payload = ReadPayload(ref stream);
                if (payload == null) return;

                CommandDto command = CommandWire.Read(payload, 0, payload.Length);

                // 座位一律以主机分配为准，不采信客户端自报的值。
                command.Seat = seat;
                RemoteCommandArrived?.Invoke(command, seat);
            }
            catch (Exception ex)
            {
                Fail($"收到无法解析的数据包：{ex.Message}");
            }
        }

        /// <summary>
        /// 主机侧：客户端接入后的第一条消息。内容 = 协议版本 + 上次的座位 + 身份令牌；
        /// 令牌能在保留表里查到就恢复原座位（重连），否则按新玩家分配。
        /// </summary>
        private void HandleHello(ref DataStreamReader stream, int index)
        {
            NetworkConnection connection = _serverConnections[index];
            int version = stream.ReadInt();

            // 版本不一致时后面可能根本没有令牌字段，先断开、别再往下读。
            if (version != NetProtocol.Version)
            {
                Fail($"座位申请的客户端协议版本 {version} 与主机 {NetProtocol.Version} 不一致，已断开该客户端。");
                _driver.Disconnect(connection);
                return;
            }

            int resumeSeat = stream.ReadInt();
            byte[] tokenBytes = ReadPayload(ref stream);
            string token = tokenBytes != null && tokenBytes.Length > 0 ? Encoding.UTF8.GetString(tokenBytes) : null;

            int seat = -1;
            bool resumed = false;

            // ① 同一身份还挂着一条活动连接（客户端刚重启、旧连接还没被判死）：
            //    踹掉旧的、座位直接交接，否则新人会占走别的座位，原座位反倒永远空着。
            int staleIndex = -1;
            for (int i = 0; i < _connectionTokens.Count && !string.IsNullOrEmpty(token); i++)
            {
                if (i != index && _connectionTokens[i] == token)
                {
                    staleIndex = i;
                    break;
                }
            }

            if (staleIndex >= 0)
            {
                int oldSeat = SeatAt(staleIndex);
                Debug.Log($"[Relay] 同一身份的旧连接仍在（座位 {oldSeat}），断开旧连接并交接座位。");
                if (oldSeat >= 0 && _serverConnections[staleIndex].IsCreated)
                    _driver.Disconnect(_serverConnections[staleIndex]);

                // 座位先摘下来，旧连接稍后的 Disconnect 就不会被当成「有人掉线」广播出去。
                _connectionSeats[staleIndex] = -1;
                _connectionTokens[staleIndex] = null;
                _reservedSeats.Remove(token);

                if (oldSeat >= 0)
                {
                    seat = oldSeat;
                    resumed = true;
                }
            }

            // ② 正常掉线重连：保留表里有这个令牌，且座位还空着。
            if (seat < 0 && !string.IsNullOrEmpty(token)
                && _reservedSeats.TryGetValue(token, out int reserved) && IsSeatFree(reserved))
            {
                seat = reserved;
                _reservedSeats.Remove(token);
                resumed = true;

                if (resumeSeat != seat)
                    Debug.LogWarning($"[Relay] 重连请求的座位 {resumeSeat} 与令牌记录的 {seat} 不一致，" +
                                     "以令牌为准（座位只看身份，不看客户端自报）。");
            }

            // ③ 全新的玩家：按最小空闲座位分配。
            if (seat < 0) seat = AllocateSeat();

            if (seat < 0)
            {
                Debug.LogWarning($"[Relay] 座位已满（{_seatCount} 人），拒绝新连接。");
                _driver.Disconnect(connection);
                return;
            }

            _connectionSeats[index] = seat;
            _connectionTokens[index] = token;
            SendSetup(connection, seat);

            // 已经开局的房间：新接入的客户端要立刻补一份当前快照，否则它会一直停在空状态。
            // 上层订阅了 PeerJoined 时改由它发「最新」快照（缓存的那份可能已经过时），这里就不再重复发。
            if (PeerJoined == null && _cachedSnapshot != null) SendSnapshot(connection, _cachedSnapshot);

            Debug.Log($"[Relay] 客户端接入（{(resumed ? "重连恢复" : "新加入")}），座位 {seat}。");
            StatusChanged?.Invoke(resumed ? $"座位 {seat} 已重连恢复。" : $"座位 {seat} 已接入。");

            // 通知上层：让它把「当前」状态重新抓一份快照发过来。
            PeerJoined?.Invoke(seat);

            if (resumed)
            {
                PeerReturned?.Invoke(seat);
                BroadcastPeerReturned(seat);
            }
        }

        /// <summary>
        /// 客户端：连上主机后先报协议版本、上次的座位与身份令牌。
        /// 座位只是提示，主机以令牌为准 —— 身份校验通过才会回到原座位。
        /// </summary>
        private void SendHello(NetworkConnection connection)
        {
            if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0) return;
            writer.WriteByte((byte)WireMessage.Hello);
            writer.WriteInt(NetProtocol.Version);
            writer.WriteInt(_localSeat);
            WritePayload(ref writer, Encoding.UTF8.GetBytes(_clientToken));
            _driver.EndSend(writer);
        }

        /// <summary>取第 <paramref name="index"/> 个连接对应的座位号。</summary>
        private int SeatAt(int index) =>
            index >= 0 && index < _connectionSeats.Count ? _connectionSeats[index] : -1;

        /// <summary>连接被移除时同步移除其座位记录。</summary>
        private void RemoveSeatAt(int index)
        {
            if (index >= 0 && index < _connectionSeats.Count) _connectionSeats.RemoveAt(index);
        }

        /// <summary>取第 <paramref name="index"/> 个连接上报的身份令牌（Hello 之前为 null）。</summary>
        private string TokenAt(int index) =>
            index >= 0 && index < _connectionTokens.Count ? _connectionTokens[index] : null;

        /// <summary>连接被移除时同步移除其身份令牌。</summary>
        private void RemoveTokenAt(int index)
        {
            if (index >= 0 && index < _connectionTokens.Count) _connectionTokens.RemoveAt(index);
        }

        /// <summary>这个座位此刻有没有活着的连接。</summary>
        private bool IsSeatFree(int seat)
        {
            for (int i = 0; i < _connectionSeats.Count; i++)
            {
                if (_connectionSeats[i] == seat) return false;
            }

            return true;
        }

        /// <summary>
        /// 取最小的空闲座位号（1 ~ _seatCount-1）；没有空位时返回 -1。
        /// 掉线玩家保留的原座位不给新人（只能由同一身份令牌取回），否则重连就回不到原位。
        /// </summary>
        private int AllocateSeat()
        {
            for (int seat = 1; seat < _seatCount; seat++)
            {
                if (_reservedSeats.ContainsValue(seat)) continue;

                bool used = false;
                for (int i = 0; i < _connectionSeats.Count; i++)
                {
                    if (_connectionSeats[i] == seat)
                    {
                        used = true;
                        break;
                    }
                }

                if (!used) return seat;
            }

            return -1;
        }

        /// <summary>客户端侧：处理主机发来的开局配置与命令广播。</summary>
        private void HandleHostMessage(ref DataStreamReader stream)
        {
            try
            {
                var messageType = (WireMessage)stream.ReadByte();

                switch (messageType)
                {
                    case WireMessage.Setup:
                    {
                        int version = stream.ReadInt();
                        if (version != NetProtocol.Version)
                        {
                            Fail($"主机协议版本 {version} 与本机 {NetProtocol.Version} 不一致，无法联机。" +
                                 "请让所有玩家使用同一版本的游戏。");
                            _clientConnection = default(NetworkConnection);
                            return;
                        }

                        int seat = stream.ReadInt();
                        int playerCount = stream.ReadInt();
                        var expansions = (ExpansionFlags)stream.ReadInt();

                        _localSeat = seat;
                        _reconnectAttempt = 0; // 真正拿到了座位才算重连成功，退回计数从下一次断线重新开始
                        _reconnectWaiting = false;
                        Debug.Log($"[Relay] 收到开局配置：{playerCount} 人，本机座位 {seat}。");
                        SetupArrived?.Invoke(new GameSetup(playerCount, expansions), seat);
                        break;
                    }

                    case WireMessage.Broadcast:
                    {
                        byte ok = stream.ReadByte();
                        byte[] errorBytes = ReadPayload(ref stream);
                        byte[] payload = ReadPayload(ref stream);
                        if (payload == null) return;

                        CommandDto command = CommandWire.Read(payload, 0, payload.Length);
                        if (ok != 0)
                        {
                            RemoteCommandArrived?.Invoke(command, command.Seat);
                        }
                        else
                        {
                            string error = errorBytes == null || errorBytes.Length == 0
                                ? "主机拒绝了这条操作。"
                                : Encoding.UTF8.GetString(errorBytes);
                            // 先报纯文本错误、再报带命令的拒绝：后者会给出更具体的原因与「可以重选」提示，
                            // 顺序反过来的话会被前者的通用文案盖掉。
                            ErrorArrived?.Invoke(error);
                            CommandRejectedArrived?.Invoke(command, error);
                        }

                        break;
                    }

                    case WireMessage.Snapshot:
                        HandleSnapshotChunk(ref stream);
                        break;

                    case WireMessage.Chat:
                    {
                        int chatSeat = stream.ReadInt();
                        byte[] chatBytes = ReadPayload(ref stream);
                        if (chatBytes != null && chatBytes.Length > 0)
                            ChatArrived?.Invoke(chatSeat, Encoding.UTF8.GetString(chatBytes));
                        break;
                    }

                    case WireMessage.SkipVoteState:
                        SkipVoteArrived?.Invoke(stream.ReadInt(), stream.ReadInt(), stream.ReadInt());
                        break;

                    case WireMessage.PeerLeftNotice:
                    {
                        int leftSeat = stream.ReadInt();
                        Debug.Log($"[Relay] 收到掉线通知：座位 {leftSeat}。");
                        PeerLeft?.Invoke(leftSeat);
                        break;
                    }

                    case WireMessage.PeerReturnedNotice:
                    {
                        int returnedSeat = stream.ReadInt();
                        Debug.Log($"[Relay] 收到重连通知：座位 {returnedSeat} 已回位。");
                        PeerReturned?.Invoke(returnedSeat);
                        break;
                    }

                    case WireMessage.Heartbeat:
                        // 心跳本身不做任何事：收到任何 Data 都已经在 PollClient 里把静默计时清零了。
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail($"收到无法解析的数据包：{ex.Message}");
            }
        }

        // —— 发包 ——

        private void SendSetup(NetworkConnection connection, int seat)
        {
            if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0) return;

            writer.WriteByte((byte)WireMessage.Setup);
            writer.WriteInt(NetProtocol.Version);
            writer.WriteInt(seat);
            writer.WriteInt(_setup != null ? _setup.PlayerCount : _seatCount);
            writer.WriteInt((int)(_setup != null ? _setup.Expansions : ExpansionFlags.None));
            _driver.EndSend(writer);
        }

        private void SendCommand(NetworkConnection connection, CommandDto command)
        {
            byte[] payload = CommandWire.Write(command);
            if (payload.Length > MaxPayloadBytes)
            {
                Fail($"命令包过大（{payload.Length} 字节），已丢弃。");
                return;
            }

            if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0)
            {
                // 发送队列忙 / 连接刚断时 BeginSend 会失败。以前这里直接 return，
                // 命令就静默消失了：客户端以为「已提交」，主机永远等不到这条操作。
                Fail("这条操作没能发出去（发送队列暂时不可用），请再点一次。");
                return;
            }

            writer.WriteByte((byte)WireMessage.Command);
            WritePayload(ref writer, payload);
            _driver.EndSend(writer);
        }

        private void SendBroadcast(NetworkConnection connection, CommandDto command, bool ok, string error)
        {
            byte[] payload = CommandWire.Write(command);
            byte[] errorBytes = string.IsNullOrEmpty(error) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(error);
            if (payload.Length + errorBytes.Length > MaxPayloadBytes)
            {
                Fail("广播包过大，已丢弃。");
                return;
            }

            if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0)
            {
                Fail("广播队列暂时不可用，这条命令结果没能发给其余玩家。");
                return;
            }

            writer.WriteByte((byte)WireMessage.Broadcast);
            writer.WriteByte((byte)(ok ? 1 : 0));
            WritePayload(ref writer, errorBytes);
            WritePayload(ref writer, payload);
            _driver.EndSend(writer);
        }

        /// <summary>主机 → 客户端：空心跳包，只为了让客户端知道主机还活着。</summary>
        private void SendHeartbeat(NetworkConnection connection)
        {
            if (_driver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer) != 0) return;
            writer.WriteByte((byte)WireMessage.Heartbeat);
            _driver.EndSend(writer);
        }

        /// <summary>
        /// 把快照切片发给一条连接：每片 [总长, 片号, 片数, 本片长度, 字节...]。
        /// 只登记成「待发」：可靠管道默认的发送窗口只有 32 个包，一帧内灌几十片会被丢，
        /// 客户端就永远拼不齐（表现正是卡在等开局数据）。真正的发送由 <see cref="FlushPendingSnapshots"/>
        /// 分帧推进。
        /// </summary>
        private void SendSnapshot(NetworkConnection connection, byte[] snapshot)
        {
            for (int i = _pendingSnapshots.Count - 1; i >= 0; i--)
            {
                if (_pendingSnapshots[i].Connection == connection) _pendingSnapshots.RemoveAt(i);
            }

            _pendingSnapshots.Add(new PendingSnapshot { Connection = connection, Bytes = snapshot });
            FlushPendingSnapshots();
        }

        /// <summary>每帧最多补发这么多片，给可靠管道留出收 ACK 的余地。</summary>
        private const int SnapshotChunksPerFrame = 8;

        private void FlushPendingSnapshots()
        {
            for (int i = _pendingSnapshots.Count - 1; i >= 0; i--)
            {
                PendingSnapshot pending = _pendingSnapshots[i];
                if (!pending.Connection.IsCreated || pending.Bytes == null)
                {
                    _pendingSnapshots.RemoveAt(i);
                    continue;
                }

                int chunkCount = (pending.Bytes.Length + SnapshotChunkSize - 1) / SnapshotChunkSize;
                int budget = SnapshotChunksPerFrame;

                while (pending.NextChunk < chunkCount && budget-- > 0)
                {
                    int index = pending.NextChunk;
                    int offset = index * SnapshotChunkSize;
                    int length = Mathf.Min(SnapshotChunkSize, pending.Bytes.Length - offset);

                    if (_driver.BeginSend(_reliablePipeline, pending.Connection, out DataStreamWriter writer) != 0) return;

                    writer.WriteByte((byte)WireMessage.Snapshot);
                    writer.WriteInt(pending.Bytes.Length);
                    writer.WriteInt(index);
                    writer.WriteInt(chunkCount);
                    writer.WriteInt(length);
                    for (int b = 0; b < length; b++) writer.WriteByte(pending.Bytes[offset + b]);

                    // 管道窗口满时 EndSend 会失败：这一片下一帧重发（客户端按片号去重，重复不影响）。
                    if (_driver.EndSend(writer) < 0) return;

                    pending.NextChunk++;
                }

                if (pending.NextChunk >= chunkCount) _pendingSnapshots.RemoveAt(i);
            }
        }

        /// <summary>一条连接待发的快照。</summary>
        private sealed class PendingSnapshot
        {
            public NetworkConnection Connection;
            public byte[] Bytes;
            public int NextChunk;
        }

        /// <summary>客户端把收到的快照切片拼起来；凑齐后抛 <see cref="SnapshotArrived"/>。</summary>
        private void HandleSnapshotChunk(ref DataStreamReader stream)
        {
            int total = stream.ReadInt();
            int index = stream.ReadInt();
            int chunkCount = stream.ReadInt();
            int length = stream.ReadInt();

            if (total <= 0 || total > MaxSnapshotBytes || chunkCount <= 0 || index < 0 || index >= chunkCount)
            {
                Fail("收到的快照包头非法，已丢弃。");
                ResetSnapshotAssembly();
                return;
            }

            int offset = index * SnapshotChunkSize;
            if (length < 0 || offset + length > total || length > stream.Length)
            {
                Fail("收到的快照切片长度非法，已丢弃。");
                ResetSnapshotAssembly();
                return;
            }

            if (_snapshotBuffer == null || _snapshotBuffer.Length != total ||
                _snapshotChunksSeen == null || _snapshotChunksSeen.Length != chunkCount)
            {
                _snapshotBuffer = new byte[total];
                _snapshotChunksSeen = new bool[chunkCount];
                _snapshotChunksReceived = 0;
            }

            for (int i = 0; i < length; i++) _snapshotBuffer[offset + i] = stream.ReadByte();

            if (!_snapshotChunksSeen[index])
            {
                _snapshotChunksSeen[index] = true;
                _snapshotChunksReceived++;
            }

            if (_snapshotChunksReceived < chunkCount) return;

            byte[] complete = _snapshotBuffer;
            ResetSnapshotAssembly();
            Debug.Log($"[Relay] 已收齐开局快照（{complete.Length} 字节）。");
            SnapshotArrived?.Invoke(complete);
        }

        private void ResetSnapshotAssembly()
        {
            _snapshotBuffer = null;
            _snapshotChunksSeen = null;
            _snapshotChunksReceived = 0;
        }

        /// <summary>写「int 长度 + 原始字节」。</summary>
        private static void WritePayload(ref DataStreamWriter writer, byte[] payload)
        {
            writer.WriteInt(payload.Length);
            for (int i = 0; i < payload.Length; i++)
            {
                writer.WriteByte(payload[i]);
            }
        }

        /// <summary>读「int 长度 + 原始字节」；长度非法时返回 null。</summary>
        private static byte[] ReadPayload(ref DataStreamReader reader)
        {
            if (reader.Length < 4) return null;

            int length = reader.ReadInt();
            if (length < 0 || length > reader.Length) return null;

            var buffer = new byte[length];
            for (int i = 0; i < length; i++) buffer[i] = reader.ReadByte();
            return buffer;
        }

        // —— 错误 ——

        private void Fail(string message)
        {
            Debug.LogError("[Relay] " + message);
            ErrorArrived?.Invoke(message);
        }
    }
}
