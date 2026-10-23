using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;

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

        /// <summary>本局引擎。主机在开局后 <see cref="BindEngine"/>；客户端在收到快照时自动建立。</summary>
        public GameEngine Engine { get; private set; }

        /// <summary>客户端收到的开局配置（人数 / 扩展）与分配到的座位。</summary>
        public GameSetup RemoteSetup { get; private set; }

        public int RemoteSeat { get; private set; } = -1;

        /// <summary>传输层错误、主机拒绝，或客户端重放失败（可直接显示给玩家）。</summary>
        public event Action<string> ErrorArrived;

        /// <summary>收到开局配置（参数 2 = 本机座位）。</summary>
        public event Action<GameSetup, int> SetupArrived;

        /// <summary>客户端用快照建立了引擎（此时 <see cref="Engine"/> 已有值，可以开始初始化界面）。</summary>
        public event Action<GameState> SnapshotApplied;

        public NetworkGameSession(ITransport transport, ICardCatalog catalog = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.catalog = catalog ?? PlaceholderCardCatalog.Default;

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

        /// <summary>主机的房间码（客户端为 null）。</summary>
        public string JoinCode => transport is NomadCity.Net.RelayTransport relay ? relay.JoinCode : null;

        /// <summary>是否轮到本机行动。单机恒为 true（热座由界面自己控制座位）。</summary>
        public bool CanAct =>
            !transport.IsMultiplayer || (Engine != null && Engine.State.ActiveSeat == transport.LocalSeat);

        /// <summary>
        /// 采集（§7）与 2 人局弃设施牌（§12 变体 4）不是「轮到某人」的流程：
        /// 每名玩家只提交自己那一份，座位由命令自带，所以不能拿 <see cref="CanAct"/> 一概拦住。
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

            return false;
        }

        /// <summary>主机开局前绑定自己的引擎。</summary>
        public void BindEngine(GameEngine engine)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public void Host(GameSetup setup, int playerCount) => transport.Host(setup, playerCount);

        public void Join(string joinCode) => transport.Join(joinCode);

        public void Poll(float deltaTime)
        {
            transport.Poll(deltaTime);

            // 只有真正联机的对局才计时；单机热座永远不触发空闲关房。
            if (!transport.IsMultiplayer) return;
            idleSeconds += deltaTime;
        }

        /// <summary>对局已经 30 分钟没有任何命令（额度保险第 3 条）：上层应收房并断开。</summary>
        public bool IdleExpired => transport.IsMultiplayer && idleSeconds >= IdleTimeoutSeconds;

        /// <summary>连接是否已被动断开（断线后界面仍停留在本局，但所有操作都会被拒绝）。</summary>
        public bool ConnectionLost => connectionLost;

        public void Shutdown() => transport.Shutdown();

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

            if (!IsHost)
            {
                if (command.ActorSeat < 0)
                    return CommandResult.Fail("流程推进由主机负责，客户端不能直接发起。");

                if (command.ActorSeat != transport.LocalSeat)
                    return CommandResult.Fail("只能操作自己的座位。");

                if (!CanAct && !IsSelfPacedFlow(command))
                    return CommandResult.Fail("还没轮到你行动。");

                transport.Submit(CommandCodec.Encode(command));
                return CommandResult.Success;
            }

            CommandResult result = Engine.Dispatch(command);
            transport.Broadcast(CommandCodec.Encode(command), result.Ok, result.Error);
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

        // —— 事件处理 ——

        /// <summary>有客户端接入：把当前状态重新抓一份快照发出去，让它从「现在」开始重放命令。</summary>
        private void OnPeerJoined(int seat)
        {
            if (!IsHost || Engine == null) return;
            BroadcastSnapshot();
        }

        private void OnSetupArrived(GameSetup setup, int seat)
        {
            RemoteSetup = setup;
            RemoteSeat = seat;
            SetupArrived?.Invoke(setup, seat);

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

            CommandResult result = Engine.Dispatch(command);

            if (IsHost)
            {
                // 主机是权威：执行结果（包括拒绝原因）必须回给所有客户端。
                transport.Broadcast(dto, result.Ok, result.Error);
            }
            else if (!result.Ok)
            {
                // 客户端重放失败意味着两端状态已经分叉，必须立刻让玩家知道，而不是继续假装正常。
                OnTransportError($"重放主机命令失败：{result.Error}");
            }
        }

        private void OnTransportError(string message) => ErrorArrived?.Invoke(message);

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
