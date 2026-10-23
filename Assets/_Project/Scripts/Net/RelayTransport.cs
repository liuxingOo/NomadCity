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
        }

        // —— UTP 运行时 ——
        private NetworkDriver _driver;
        private NativeList<NetworkConnection> _serverConnections;
        private NetworkConnection _clientConnection;

        // —— 主机侧状态 ——
        private GameSetup _setup;
        private int _seatCount = 4;

        /// <summary>与 <see cref="_serverConnections"/> 同下标的座位号，保证客户端自报的 Seat 不被采信。</summary>
        private readonly List<int> _connectionSeats = new List<int>();

        /// <summary>主机侧：最近一次广播过的开局快照，供后加入的客户端补发。</summary>
        private byte[] _cachedSnapshot;

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

        public bool IsMultiplayer => _isMultiplayer;

        public bool IsHost => _isHost;

        /// <summary>本机座位；主机固定 0，客户端要等收到 Setup 才知道（收到前为 -1）。</summary>
        public int LocalSeat => _localSeat;

        /// <summary>主机创建中继后得到的房间码，供界面展示给其他玩家。客户端为 null。</summary>
        public string JoinCode { get; private set; }

        /// <summary>连接/分配期间是否有新的阶段进展（供 UI 显示「正在创建房间…」这类提示，可留空）。</summary>
        public event Action<string> StatusChanged;

        public event Action<CommandDto> CommandSubmitted;
        public event Action<CommandDto, int> RemoteCommandArrived;
        public event Action<GameSetup, int> SetupArrived;
        public event Action<string> ErrorArrived;
        public event Action<byte[]> SnapshotArrived;
        public event Action<int> PeerJoined;
        public event Action<string> ConnectionLost;

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
            JoinAsync(joinCode.Trim());
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

        /// <summary>每帧推进连接状态并收包。由上层（MonoBehaviour 的 Update）驱动。</summary>
        public void Poll(float deltaTime)
        {
            if (_shutdown || !_driver.IsCreated) return;

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
            _cachedSnapshot = null;
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

        private async void HostAsync()
        {
            try
            {
                await EnsureSignedInAsync();
                if (_shutdown) return;
                StatusChanged?.Invoke("正在申请 Relay 中继…");

                // 中继最大连接数按总人数申请（主机自身也占一条中继连接），多留余量比少留安全。
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(_seatCount, null);
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
                    --i;
                }
            }

            // 接纳新客户端，并立刻把座位与开局配置发过去。
            NetworkConnection incoming;
            while ((incoming = _driver.Accept()) != default(NetworkConnection))
            {
                // 座位从 1 开始分配（0 是主机），优先复用已断开的客户端腾出的空位。
                int seat = AllocateSeat();
                if (seat < 0)
                {
                    Debug.LogWarning($"[Relay] 座位已满（{_seatCount} 人），拒绝新连接。");
                    _driver.Disconnect(incoming);
                    continue;
                }

                _serverConnections.Add(incoming);
                _connectionSeats.Add(seat);
                SendSetup(incoming, seat);

                // 已经开局的房间：新接入的客户端要立刻补一份当前快照，否则它会一直停在空状态。
                // 上层订阅了 PeerJoined 时改由它发「最新」快照（缓存的那份可能已经过时），这里就不再重复发。
                if (PeerJoined == null && _cachedSnapshot != null) SendSnapshot(incoming, _cachedSnapshot);
                Debug.Log($"[Relay] 客户端接入，分配座位 {seat}。");

                // 通知上层：让它把「当前」状态重新抓一份快照发过来。
                PeerJoined?.Invoke(seat);
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
                            HandleClientMessage(ref stream, SeatAt(i), connection);
                            break;

                        case NetworkEvent.Type.Disconnect:
                            Debug.Log("[Relay] 客户端断开连接。");
                            _serverConnections[i] = default(NetworkConnection);
                            break;
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
                        SendHello(_clientConnection); // 先报协议版本，主机不一致会直接断开
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
                ConnectionLost?.Invoke(reason);
                return;
            }

            // 30 秒收不到任何主机消息（正常时每 5 秒至少一个心跳）→ 判定断线，主动断开、不重连。
            if (!_clientConnection.IsCreated) return;

            _silenceSeconds += deltaTime;
            if (_silenceSeconds < ClientSilenceTimeoutSeconds) return;

            _silenceSeconds = 0f;
            _clientConnection = default(NetworkConnection);
            ConnectionLost?.Invoke($"与主机的连接已中断（{ClientSilenceTimeoutSeconds:0} 秒没有收到主机数据）。");
        }

        /// <summary>主机侧：解析客户端发来的命令。</summary>
        private void HandleClientMessage(ref DataStreamReader stream, int seat, NetworkConnection connection)
        {
            try
            {
                byte messageType = stream.ReadByte();

                if (messageType == (byte)WireMessage.Hello)
                {
                    HandleHello(ref stream, seat, connection);
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

        /// <summary>主机侧：客户端接入后的第一条消息携带协议版本；不一致就断开这一端。</summary>
        private void HandleHello(ref DataStreamReader stream, int seat, NetworkConnection connection)
        {
            int version = stream.ReadInt();
            if (version == NetProtocol.Version)
            {
                StatusChanged?.Invoke($"座位 {seat} 的客户端协议版本 {version}，校验通过。");
                return;
            }

            Fail($"座位 {seat} 的客户端协议版本 {version} 与主机 {NetProtocol.Version} 不一致，已断开该客户端。");
            _driver.Disconnect(connection);
        }

        /// <summary>客户端：连上主机后先报自己的协议版本，让主机在开局前就能拒绝不同版本。</summary>
        private void SendHello(NetworkConnection connection)
        {
            if (_driver.BeginSend(connection, out DataStreamWriter writer) != 0) return;
            writer.WriteByte((byte)WireMessage.Hello);
            writer.WriteInt(NetProtocol.Version);
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

        /// <summary>取最小的空闲座位号（1 ~ _seatCount-1）；没有空位时返回 -1。</summary>
        private int AllocateSeat()
        {
            for (int seat = 1; seat < _seatCount; seat++)
            {
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
                            ErrorArrived?.Invoke(error);
                        }

                        break;
                    }

                    case WireMessage.Snapshot:
                        HandleSnapshotChunk(ref stream);
                        break;

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
            if (_driver.BeginSend(connection, out DataStreamWriter writer) != 0) return;

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

            if (_driver.BeginSend(connection, out DataStreamWriter writer) != 0) return;

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

            if (_driver.BeginSend(connection, out DataStreamWriter writer) != 0) return;

            writer.WriteByte((byte)WireMessage.Broadcast);
            writer.WriteByte((byte)(ok ? 1 : 0));
            WritePayload(ref writer, errorBytes);
            WritePayload(ref writer, payload);
            _driver.EndSend(writer);
        }

        /// <summary>主机 → 客户端：空心跳包，只为了让客户端知道主机还活着。</summary>
        private void SendHeartbeat(NetworkConnection connection)
        {
            if (_driver.BeginSend(connection, out DataStreamWriter writer) != 0) return;
            writer.WriteByte((byte)WireMessage.Heartbeat);
            _driver.EndSend(writer);
        }

        /// <summary>把快照切片发给一条连接：每片 [总长, 片号, 片数, 本片长度, 字节...]。</summary>
        private void SendSnapshot(NetworkConnection connection, byte[] snapshot)
        {
            int chunkCount = (snapshot.Length + SnapshotChunkSize - 1) / SnapshotChunkSize;

            for (int index = 0; index < chunkCount; index++)
            {
                int offset = index * SnapshotChunkSize;
                int length = Mathf.Min(SnapshotChunkSize, snapshot.Length - offset);

                if (_driver.BeginSend(connection, out DataStreamWriter writer) != 0) return;

                writer.WriteByte((byte)WireMessage.Snapshot);
                writer.WriteInt(snapshot.Length);
                writer.WriteInt(index);
                writer.WriteInt(chunkCount);
                writer.WriteInt(length);
                for (int i = 0; i < length; i++) writer.WriteByte(snapshot[offset + i]);
                _driver.EndSend(writer);
            }
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
