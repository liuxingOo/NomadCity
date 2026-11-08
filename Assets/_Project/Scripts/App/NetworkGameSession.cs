using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using UnityEngine;

namespace NomadCity.App
{
    /// <summary>联机角色。None = 单机热座；Host = 本机开房；Client = 用房间码加入。</summary>
    public enum OnlineRole
    {
        None,
        Host,
        Client,
    }

    /// <summary>
    /// 联机对局的中枢（阶段 3）：把 <see cref="ITransport"/> 的命令流接到 <see cref="GameEngine"/>，
    /// 并守住「只能操作自己的座位」这道闸门。单机模式下它退化成一层薄包装，行为与直接调引擎一致。
    ///
    /// 拓扑是主机权威：
    /// - 主机：本机命令直接执行；远端命令按主机分配的座位重放执行；两者都把结果广播给所有客户端。
    /// - 客户端：本机命令只发给主机，不本地执行；等主机广播回来后再重放进本机引擎。
    /// - 流程推进类命令（<see cref="ICommand.ActorSeat"/> = -1，如推进阶段、采集结算）只允许主机发起。
    ///
    /// 它本身不是 MonoBehaviour：由 GameBootstrap 每帧调 <see cref="Poll"/> 驱动收发。
    /// </summary>
    public sealed class NetworkGameSession
    {
        private readonly ITransport transport;
        private readonly ICardCatalog catalog;

        /// <summary>对局空闲上限：这么久没有任何命令，视为放弃，上层应关房以节省 Relay 额度。</summary>
        private const float IdleTimeoutSeconds = 30f * 60f;

        /// <summary>先于 Setup 到达的快照先存着，等座位分配好再还原（避免界面拿到 -1 座位）。</summary>
        private byte[] pendingSnapshot;

        /// <summary>连接被动断开后置位：此后不再把命令当作单机命令本地执行。</summary>
        private bool connectionLost;

        /// <summary>距离上次命令（本机提交 / 远端广播）经过的秒数。</summary>
        private float idleSeconds;

        /// <summary>距离上次「牌桌有进展」（有命令成功执行）经过的秒数，给挂机投票用。</summary>
        private float secondsSinceProgress;

        // 主机侧的投票统计：目标座位 / 已投座位掩码 / 需要票数。
        private int skipTargetSeat = -1;
        private int skipVoterMask;
        private int skipRequiredVotes;

        /// <summary>本局引擎。主机在开局后 <see cref="BindEngine"/>；客户端在收到快照时自动建立。</summary>
        public GameEngine Engine { get; private set; }

        /// <summary>客户端收到的开局配置（人数 / 扩展）与分配到的座位。</summary>
        public GameSetup RemoteSetup { get; private set; }

        public int RemoteSeat { get; private set; } = -1;

        /// <summary>传输层错误、主机拒绝，或客户端重放失败（可直接显示给玩家）。</summary>
        public event Action<string> ErrorArrived;

        /// <summary>
        /// 本机提交的某条命令被主机拒绝（命令 + 原因）。与 <see cref="ErrorArrived"/> 的区别是
        /// 「带上了是哪条命令」：界面据此判断该不该撤销对应的流程（例如事件卡是否要还原重选），
        /// 不会被期间到达的其它错误（比如重复点击被拒）误伤。
        /// </summary>
        public event Action<ICommand, string> CommandRejected;

        /// <summary>收到开局配置（参数 2 = 本机座位）。</summary>
        public event Action<GameSetup, int> SetupArrived;

        /// <summary>客户端用快照建立了引擎（此时 <see cref="Engine"/> 已有值，可以开始初始化界面）。</summary>
        public event Action<GameState> SnapshotApplied;

        /// <summary>收到一条聊天（座位 + 内容）。单机模式等价于本机回显。</summary>
        public event Action<int, string> ChatReceived;

        /// <summary>有玩家掉线（座位）。主机本地检测，客户端由主机广播通知。</summary>
        public event Action<int> PeerLeft;

        /// <summary>掉线的玩家已重连回原座位（座位）。主机本地检测，客户端由主机广播通知。</summary>
        public event Action<int> PeerReturned;

        /// <summary>本机正在自动重连（第几次、还要等几秒）。</summary>
        public event Action<int, float> Reconnecting;

        /// <summary>本机重连成功、重新拿到了座位（随后会收到一份新快照）。</summary>
        public event Action Reconnected;

        /// <summary>跳过投票进度变化（目标座位、已投座位掩码、需要票数；目标 -1 = 清空）。</summary>
        public event Action<int, int, int> SkipVoteChanged;

        public NetworkGameSession(ITransport transport, ICardCatalog catalog = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.catalog = catalog ?? PlaceholderCardCatalog.Default;

            // 聊天只在 Relay 实现里有（ITransport 是规则层接口，不掺聊天这种纯会话功能）。
            if (transport is NomadCity.Net.RelayTransport relayTransport)
            {
                relayTransport.ChatArrived += OnChatArrived;
                relayTransport.PeerLeft += OnPeerLeft;
                relayTransport.PeerReturned += OnPeerReturned;
                relayTransport.Reconnecting += OnReconnecting;
                relayTransport.SkipVoteRequestArrived += OnSkipVoteRequest;
                relayTransport.SkipVoteArrived += OnSkipVoteState;
                relayTransport.CommandRejectedArrived += OnCommandRejectedArrived;
            }

            transport.RemoteCommandArrived += OnRemoteCommandArrived;
            transport.ErrorArrived += OnTransportError;
            transport.SetupArrived += OnSetupArrived;
            transport.SnapshotArrived += OnSnapshotArrived;
            transport.PeerJoined += OnPeerJoined;
            transport.ConnectionLost += OnConnectionLost;
        }

        public bool IsMultiplayer => transport.IsMultiplayer;

        public bool IsHost => transport.IsHost;

        public int LocalSeat => transport.LocalSeat;

        /// <summary>某个座位对应客户端的身份令牌（仅 Relay 实现有；主机与未知座位为 null）。</summary>
        public string SeatToken(int seat) =>
            transport is NomadCity.Net.RelayTransport relay ? relay.TokenForSeat(seat) : null;

        /// <summary>主机的房间码（客户端为 null）。</summary>
        public string JoinCode => transport is NomadCity.Net.RelayTransport relay ? relay.JoinCode : null;

        /// <summary>是否轮到本机行动。单机恒为 true（热座由界面自己控制座位）。</summary>
        public bool CanAct =>
            !transport.IsMultiplayer || (Engine != null && Engine.State.ActiveSeat == transport.LocalSeat);

        /// <summary>
        /// 采集（§7）、2 人局弃设施牌（§12 变体 4）、开局入场与角色牌盖放这些流程不是「轮到某人」：
        /// 每名玩家只提交自己那一份（座位由命令自带），所以不能拿 <see cref="CanAct"/> 一概拦住。
        /// </summary>
        private bool IsSelfPacedFlow(ICommand command)
        {
            if (Engine == null || !transport.IsMultiplayer) return false;
            GameState state = Engine.State;

            if (command is SubmitHarvestChoiceCommand)
                return state.Phase == TurnPhase.Harvest && state.HarvestPending;

            if (command is DiscardSupplyFacilityCommand)
                return state.Phase == TurnPhase.CharacterPlacement
                       && state.SupplyDiscardRemaining > 0
                       && state.SupplyDiscardSeat == transport.LocalSeat;

            // 角色牌盖放（§4.2 阶段 1）：所有玩家同时盖放，谁都不等谁，盖完由引擎自动进入玩家行动。
            if (command is PlaceCharacterCardCommand)
                return state.Phase == TurnPhase.CharacterPlacement;

            // 开局入场（§4.3）：按「起始玩家起顺时针」逐个选初始资源点，再由本人结算事件选项。
            if (command is ChooseStartNodeCommand)
                return state.TurnNumber == 1
                       && state.Phase == TurnPhase.CharacterPlacement
                       && command.ActorSeat == ChooseStartNodeCommand.NextChooser(state);

            if (command is ResolveEventOptionCommand)
                return state.TurnNumber == 1 && state.Phase == TurnPhase.CharacterPlacement;

            return false;
        }

        /// <summary>主机开局前绑定自己的引擎。</summary>
        public void BindEngine(GameEngine engine)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));

            // 准备房间阶段也一直在 Poll，计时早跑过头了；开局这一刻才算「牌桌刚开始」，
            // 否则刚进游戏就会立刻满足挂机投票的等待时间。
            NoteProgress();
        }

        public void Host(GameSetup setup, int playerCount) => transport.Host(setup, playerCount);

        public void Join(string joinCode) => transport.Join(joinCode);

        public void Poll(float deltaTime)
        {
            transport.Poll(deltaTime);

            // 只有真正联机的对局才计时；单机热座永远不触发空闲关房。
            if (!transport.IsMultiplayer) return;
            idleSeconds += deltaTime;
            secondsSinceProgress += deltaTime;
        }

        /// <summary>对局已经 30 分钟没有任何命令（额度保险第 3 条）：上层应收房并断开。</summary>
        public bool IdleExpired => transport.IsMultiplayer && idleSeconds >= IdleTimeoutSeconds;

        /// <summary>连接是否已被动断开（断线后界面仍停留在本局，但所有操作都会被拒绝）。</summary>
        public bool ConnectionLost => connectionLost;

        public void Shutdown() => transport.Shutdown();

        /// <summary>
        /// 玩家手动点「重连」：清掉「已断开」标记并立刻重连一次（不等自动退避）。
        /// 只在客户端有意义 —— 主机是权威端，没有可重连的对象。
        /// </summary>
        public void ReconnectNow()
        {
            if (IsHost) return;
            if (transport is not NomadCity.Net.RelayTransport relay) return;

            connectionLost = false;
            relay.ReconnectNow();
        }

        /// <summary>
        /// 界面的唯一命令入口（对应 <see cref="GameScreenController"/> 的 DispatchToEngine）。
        /// 单机：直接执行并返回真实结果；主机：执行 + 广播；客户端：只发送，返回「已提交」。
        /// </summary>
        public CommandResult Submit(ICommand command)
        {
            if (command == null) return CommandResult.Fail("没有要执行的命令。");

            // 断线后传输层已不再联机，若继续走下面的分支会被误当成单机命令在本地执行，必须先拦住。
            if (connectionLost) return CommandResult.Fail("与主机的连接已断开，本局无法继续操作。");
            if (Engine == null) return CommandResult.Fail("引擎还没有建立。");

            if (!transport.IsMultiplayer) return Engine.Dispatch(command);

            idleSeconds = 0f;
            NoteProgress();

            if (!IsHost)
            {
                if (command.ActorSeat < 0)
                    return CommandResult.Fail("流程推进由主机负责，客户端不能直接发起。");

                if (command.ActorSeat != transport.LocalSeat)
                    return CommandResult.Fail("只能操作自己的座位。");

                if (!CanAct && !IsSelfPacedFlow(command))
                    return CommandResult.Fail("还没轮到你行动。");

                CommandDto dto = CommandCodec.Encode(command);
                Debug.Log($"[联机] 本机提交「{command.Name}」座位 {command.ActorSeat}：{Describe(dto)}");
                transport.Submit(dto);
                return CommandResult.Success;
            }

            CommandDto hostDto = CommandCodec.Encode(command);
            CommandResult result = DispatchCommand(command);
            Debug.Log($"[联机] 主机执行本机「{command.Name}」座位 {command.ActorSeat}：" +
                      $"{(result.Ok ? "已执行" : "被拒绝（" + result.Error + "）")}");
            transport.Broadcast(hostDto, result.Ok, result.Error);
            return result;
        }

        /// <summary>主机把当前状态抓成快照并广播（开局完成后调用一次；后加入者会自动补发）。</summary>
        public void BroadcastSnapshot()
        {
            if (Engine == null)
            {
                OnTransportError("引擎还没有建立，无法发送开局快照。");
                return;
            }

            if (!IsHost)
            {
                OnTransportError("只有主机可以发送开局快照。");
                return;
            }

            byte[] bytes;
            try
            {
                bytes = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(Engine.State));
            }
            catch (Exception ex)
            {
                // 例如延迟结算队列非空时快照暂不支持 —— 必须让主机玩家知道发不出去，而不是静默失败。
                OnTransportError($"无法生成开局快照：{ex.Message}");
                return;
            }

            transport.BroadcastSnapshot(bytes);
        }

        /// <summary>
        /// 发一条聊天。联机：主机广播给所有客户端，客户端只发给主机（由主机按连接改写座位再广播，
        /// 自己也会收到，保证所有人看到的顺序一致）；单机：直接回显给本机。
        /// </summary>
        public void SendChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string trimmed = text.Trim();

            if (connectionLost)
            {
                ErrorArrived?.Invoke("与主机的连接已断开，这条消息没发出去。");
                return;
            }

            if (!transport.IsMultiplayer)
            {
                ChatReceived?.Invoke(Mathf.Max(0, transport.LocalSeat), trimmed);
                return;
            }

            if (transport is NomadCity.Net.RelayTransport relayTransport)
            {
                relayTransport.SendChat(trimmed);
                return;
            }

            ErrorArrived?.Invoke("当前连接不支持聊天。");
        }

        private void OnChatArrived(int seat, string text) => ChatReceived?.Invoke(seat, text);

        // —— 掉线与挂机跳过 ——

        /// <summary>这个座位现在还有人在线吗（单机恒为 true）。客户端拿不到主机侧连接信息时按在线处理。</summary>
        public bool IsSeatConnected(int seat)
        {
            if (!transport.IsMultiplayer) return true;

            return transport is NomadCity.Net.RelayTransport relay ? relay.IsSeatConnected(seat) : true;
        }

        /// <summary>投票跳过某个座位。主机自己投就直接进统计；客户端发给主机。</summary>
        public void RequestSkipVote(int targetSeat)
        {
            if (!transport.IsMultiplayer || connectionLost || Engine == null) return;
            if (targetSeat < 0 || targetSeat == transport.LocalSeat) return;

            if (IsHost)
            {
                HandleSkipVoteRequest(transport.LocalSeat, targetSeat);
                return;
            }

            if (transport is NomadCity.Net.RelayTransport relay) relay.RequestSkipVote(targetSeat);
        }

        private void OnPeerLeft(int seat)
        {
            // 掉线的人可能正卡在投票目标上：把它之前投的票一起清掉，避免界面显示过期的进度。
            if (skipTargetSeat >= 0 && seat == skipTargetSeat) ResetSkipVote();

            PeerLeft?.Invoke(seat);
        }

        private void OnPeerReturned(int seat)
        {
            // 人回来了就不用再投票跳过他：把针对他的那轮投票作废。
            if (skipTargetSeat == seat) ResetSkipVote();

            PeerReturned?.Invoke(seat);
        }

        private void OnReconnecting(int attempt, float delay) => Reconnecting?.Invoke(attempt, delay);

        private void OnSkipVoteRequest(int voterSeat, int targetSeat) => HandleSkipVoteRequest(voterSeat, targetSeat);

        private void OnSkipVoteState(int targetSeat, int voterMask, int required)
            => SkipVoteChanged?.Invoke(targetSeat, voterMask, required);

        private void HandleSkipVoteRequest(int voterSeat, int targetSeat)
        {
            if (!IsHost || connectionLost || Engine == null) return;

            GameState state = Engine.State;
            if (state == null || state.IsGameOver) return;
            if (voterSeat < 0 || voterSeat == targetSeat) return;
            if (!SkipPendingActionCommand.IsPending(state, targetSeat)) return;

            // 主机兜底校验时长：客户端界面虽然也会等够时间才显示按钮，但票数只认主机这边。
            float delay = IsSeatConnected(targetSeat)
                ? SkipPendingActionCommand.VoteDelaySeconds
                : SkipPendingActionCommand.VoteDelayDisconnectedSeconds;
            if (secondsSinceProgress < delay) return;

            if (skipTargetSeat != targetSeat)
            {
                // 换了个跳过对象：先把上一轮的进度清掉，别让界面同时挂着两个半截投票。
                if (skipTargetSeat >= 0)
                {
                    SkipVoteChanged?.Invoke(-1, 0, 0);
                    if (transport is NomadCity.Net.RelayTransport previous)
                        previous.BroadcastSkipVote(-1, 0, 0);
                }

                skipTargetSeat = targetSeat;
                skipVoterMask = 0;
                skipRequiredVotes = RequiredVotes(state.Players.Count);
            }

            skipVoterMask |= 1 << voterSeat;

            if (CountVotes(skipVoterMask) < skipRequiredVotes)
            {
                SkipVoteChanged?.Invoke(skipTargetSeat, skipVoterMask, skipRequiredVotes);
                if (transport is NomadCity.Net.RelayTransport relay)
                    relay.BroadcastSkipVote(skipTargetSeat, skipVoterMask, skipRequiredVotes);
                return;
            }

            int target = skipTargetSeat;
            ResetSkipVote();

            CommandResult result = Submit(new SkipPendingActionCommand(target));
            if (!result.Ok)
                OnTransportError($"跳过座位 {target + 1} 的行动失败：{result.Error}");
        }

        /// <summary>「其余玩家」的过半数：2 人 1 票、3 人 2 票、4 人 2 票。</summary>
        private static int RequiredVotes(int playerCount)
        {
            int others = Math.Max(1, playerCount - 1);
            return others / 2 + 1;
        }

        private static int CountVotes(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }

            return count;
        }

        private void ResetSkipVote()
        {
            if (skipTargetSeat < 0 && skipVoterMask == 0) return;

            skipTargetSeat = -1;
            skipVoterMask = 0;
            skipRequiredVotes = 0;
            SkipVoteChanged?.Invoke(-1, 0, 0);
        }

        /// <summary>牌桌有进展：重置挂机计时，并清掉上一轮的投票进度。</summary>
        private void NoteProgress()
        {
            secondsSinceProgress = 0f;
            ResetSkipVote();
        }

        // —— 事件处理 ——

        /// <summary>有客户端接入：把当前状态重新抓一份快照发出去，让它从「现在」开始重放命令。</summary>
        private void OnPeerJoined(int seat)
        {
            if (!IsHost || Engine == null) return;
            BroadcastSnapshot();
        }

        private void OnSetupArrived(GameSetup setup, int seat)
        {
            // 之前已经拿到过座位 = 这是一次重连（断线后主机重新分配/恢复的原座位）。
            bool reconnected = RemoteSeat >= 0;

            RemoteSetup = setup;
            RemoteSeat = seat;
            SetupArrived?.Invoke(setup, seat);

            if (reconnected) Reconnected?.Invoke();

            if (pendingSnapshot != null)
            {
                byte[] bytes = pendingSnapshot;
                pendingSnapshot = null;
                OnSnapshotArrived(bytes);
            }
        }

        private void OnSnapshotArrived(byte[] bytes)
        {
            if (RemoteSeat < 0)
            {
                pendingSnapshot = bytes;
                return;
            }

            try
            {
                GameStateSnapshot snapshot = GameStateSnapshotCodec.Read(bytes, 0, bytes.Length);
                GameState state = snapshot.Restore(catalog);
                Engine = new GameEngine(state);
                idleSeconds = 0f;
                NoteProgress();
                SnapshotApplied?.Invoke(state);
            }
            catch (Exception ex)
            {
                OnTransportError($"开局快照解析失败：{ex.Message}");
            }
        }

        private void OnRemoteCommandArrived(CommandDto dto, int seat)
        {
            if (Engine == null) return;

            idleSeconds = 0f;
            NoteProgress();

            ICommand command;
            try
            {
                command = CommandCodec.Decode(dto);
            }
            catch (Exception ex)
            {
                OnTransportError($"收到无法还原的命令：{ex.Message}");
                return;
            }

            long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            CommandResult result = DispatchCommand(command);

            if (IsHost)
            {
                Debug.Log($"[联机] 主机收到座位 {seat} 的「{command.Name}」：" +
                          $"{(result.Ok ? "已执行" : "被拒绝（" + result.Error + "）")}");

                // 拒绝原因不能只回给客户端：主机自己也必须看到，否则画面上只会表现为「对方点了没反应、
                // 流程一直等他」。这里复用同一套错误通道，让它同时写进 Console、状态行与行动记录。
                if (!result.Ok)
                    OnTransportError($"拒绝了座位 {seat + 1} 的操作「{command.Name}」：{result.Error}");

                // 主机是权威：执行结果（包括拒绝原因）必须回给所有客户端。
                transport.Broadcast(dto, result.Ok, result.Error);

                // 拆时间：客户端的「操作往返」= 两端网络 + 主机这段处理。
                // 这条日志很小，出问题时可据此判断该查网络还是查引擎（正常情况下 1 ms 上下）。
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) * 1000.0
                                   / System.Diagnostics.Stopwatch.Frequency;
                Debug.Log($"[联机] 主机处理「{command.Name}」耗时 {elapsedMs:0.0} ms");
            }
            else
            {
                Debug.Log($"[联机] 本机重放主机命令「{command.Name}」：" +
                          $"{(result.Ok ? "已执行" : "失败（" + result.Error + "）")}");

                if (!result.Ok)
                {
                    // 客户端重放失败意味着两端状态已经分叉，必须立刻让玩家知道，而不是继续假装正常。
                    OnTransportError($"重放主机命令失败：「{command.Name}」{result.Error}");

                    // 只有本机座位发出的命令被拒，才可能是「我这一步没成功」；界面据此精确还原对应流程。
                    if (command.ActorSeat == transport.LocalSeat)
                        CommandRejected?.Invoke(command, result.Error);
                }
            }
        }

        /// <summary>
        /// 引擎执行一条命令，并把「<see cref="ICommand.Apply"/> 里抛异常」变成一条可显示的错误。
        ///
        /// 不接住的话后果很严重：异常会一路冒到 <c>RelayTransport</c> 的收包循环，被统一报成
        /// 「收到无法解析的数据包」，主机也不会广播结果 —— 客户端只能干等，整局卡在那个操作上，
        /// 玩家看到的现象就是「他点了，我这边什么都没发生」。
        /// </summary>
        private CommandResult DispatchCommand(ICommand command)
        {
            try
            {
                return Engine.Dispatch(command);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                string reason = $"「{command?.Name}」执行时出错：{ex.Message}";
                OnTransportError(reason);
                return CommandResult.Fail(reason);
            }
        }

        /// <summary>把 DTO 的槽位写成一行短文本，联机出问题时能直接从 Console 看出「传了什么」。</summary>
        private static string Describe(CommandDto dto)
        {
            string items = dto.Items == null || dto.Items.Length == 0
                ? "无"
                : string.Join(",", dto.Items);
            return $"Kind={dto.Kind} Seat={dto.Seat} P0={dto.P0} P1={dto.P1} P2={dto.P2} " +
                   $"P3={dto.P3} Flags={dto.Flags} Items=[{items}]";
        }

        private void OnTransportError(string message) => ErrorArrived?.Invoke(message);

        /// <summary>
        /// 主机把「这条命令被拒绝」广播回来：还原出命令本身再往上抛，
        /// 界面据此只撤销对应的那一步（而不是所有错误都当成事件结算失败）。
        /// </summary>
        private void OnCommandRejectedArrived(CommandDto dto, string error)
        {
            if (IsHost) return;

            try
            {
                ICommand command = CommandCodec.Decode(dto);
                if (command.ActorSeat == transport.LocalSeat)
                    CommandRejected?.Invoke(command, error);
            }
            catch (Exception ex)
            {
                // 还原不了命令就只留错误行（ErrorArrived 另有通道），不要把异常抛进收包循环。
                Debug.LogWarning($"[联机] 被拒绝的命令无法还原：{ex.Message}");
            }
        }

        /// <summary>
        /// 断线（主机掉线 / 30 秒无消息）：立刻释放 Relay，并把原因交给界面上报。
        /// 这里主动 Shutdown 是额度保险第 4 条 —— 不做无限后台重连，不让空连接继续计分钟。
        /// </summary>
        private void OnConnectionLost(string reason)
        {
            if (connectionLost) return;
            connectionLost = true;
            transport.Shutdown();
            ErrorArrived?.Invoke(reason);
        }
    }
}
