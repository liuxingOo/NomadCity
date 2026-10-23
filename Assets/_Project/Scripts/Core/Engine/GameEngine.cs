using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 规则引擎的门面：持有 GameState、分发命令，并在状态推进后广播「发生了什么」。
    /// Unity 层（GameBootstrap）只跟它打交道；具体行动规则以后挂在各自的模块里。
    /// </summary>
    public sealed class GameEngine
    {
        public GameState State { get; }

        public CommandDispatcher Dispatcher { get; } = new CommandDispatcher();

        /// <summary>阶段发生变化：(state, 旧阶段, 新阶段)。扩展包可在此挂载额外步骤（规则规格 13.4）。</summary>
        public event Action<GameState, TurnPhase, TurnPhase> PhaseChanged;

        /// <summary>进入新回合（含第 1 回合）。</summary>
        public event Action<GameState> TurnStarted;

        /// <summary>
        /// 一名玩家走完主要行动、行动座位推进到下一家：(state, 旧座位, 新座位)。
        /// 单独广播是必须的：<see cref="CommandDispatcher.CommandApplied"/> 发生在推进**之前**，
        /// 界面若只监听它，顶栏会一直停在「上一位行动者」身上，看起来永远慢一拍。
        /// </summary>
        public event Action<GameState, int, int> SeatAdvanced;

        /// <summary>第 8 回合收尾结束，进入终局结算。</summary>
        public event Action<GameState> GameEnded;

        /// <summary>
        /// 采集阶段结算完成：(state, 报告)。UI 据此显示本次采集的收成与路费。
        /// 由 <see cref="ResolveHarvest"/> 广播 —— 界面收集完各玩家的选择后调用它。
        /// </summary>
        public event Action<GameState, HarvestReport> HarvestResolved;

        public GameEngine(GameState state) => State = state;

        /// <summary>
        /// 依据模式配置建立一局初始状态：建立玩家、设置公共供应堆、挂上已构建的地图，
        /// 然后由 <see cref="BoardSetup"/> 完成发牌、设施供应区、初始资金等开局设置。
        /// firstPlayerSeat 为 §4.3 步 1 选出的起始玩家（初始资金按它发放）。
        /// 卡牌一律用 id 表示：catalog 决定「id 对应什么牌」，rng 决定洗牌结果（同种子可复现）。
        /// </summary>
        public static GameEngine Create(GameSetup setup, int firstPlayerSeat = 0, MapState map = null,
            ICardCatalog catalog = null, Random rng = null, PlayerColor[] seatColors = null,
            int[] seatOrder = null)
        {
            var state = new GameState(setup);

            // 开局随机排出的行动顺序：必须先于 BoardSetup（初始资金按顺序发放）。
            if (seatOrder != null && seatOrder.Length == setup.PlayerCount)
                state.SeatOrder = (int[])seatOrder.Clone();

            for (int seat = 0; seat < setup.PlayerCount; seat++)
            {
                // 座位颜色由开局前的选色决定；没传时退回「座位号即颜色」的默认分配。
                PlayerColor color = seatColors != null && seat < seatColors.Length
                    ? seatColors[seat]
                    : (PlayerColor)seat;

                state.Players.Add(new PlayerState(seat, color)
                {
                    MarkersInSupply = 30, // 规则规格 4.2：每人 30 个玩家标记
                });
            }

            if (map != null) state.Map = map;

            // 卡牌目录是静态查表数据，规则代码（如决定初始资源点要翻事件牌）通过 state.Catalog 访问。
            state.Catalog = catalog ?? PlaceholderCardCatalog.Default;
            BoardSetup.Run(state, state.Catalog, rng ?? new Random(), firstPlayerSeat);
            return new GameEngine(state);
        }

        /// <summary>
        /// 唯一入口：校验 → 执行 → 广播。UI 与联机都走这里，不直接改 State。
        /// 若命令是一次「主要行动」（ConsumesAction），执行成功后自动把行动顺序推进到下一位玩家
        /// （走完两轮则由 <see cref="TurnFlow.CompletePlayerAction"/> 收进采集阶段）。
        /// </summary>
        public CommandResult Dispatch(ICommand command)
        {
            TurnPhase beforePhase = State.Phase;
            int beforeTurn = State.TurnNumber;
            int beforeSeat = State.ActiveSeat;
            bool wasOver = State.IsGameOver;

            CommandResult result = Dispatcher.Dispatch(State, command);
            if (!result.Ok) return result;

            if (command.ConsumesAction) TurnFlow.CompletePlayerAction(State);

            // 采集结算也走命令通道；界面仍按原有事件接收结算简报（§7）。
            // 联机时由各玩家提交自己的选择，最后一条提交会当场结算并带回报告。
            if (command is ResolveHarvestCommand harvest && harvest.Report != null)
                HarvestResolved?.Invoke(State, harvest.Report);
            else if (command is SubmitHarvestChoiceCommand submit && submit.Report != null)
                HarvestResolved?.Invoke(State, submit.Report);

            // 座位推进发生在 CommandApplied 之后，界面不会因为上面那次广播再刷新一次 ——
            // 不在这里补一次，顶栏与行动提示就会一直显示上一位玩家。
            if (State.ActiveSeat != beforeSeat) SeatAdvanced?.Invoke(State, beforeSeat, State.ActiveSeat);

            if (State.IsGameOver && !wasOver) GameEnded?.Invoke(State);
            else if (State.TurnNumber != beforeTurn) TurnStarted?.Invoke(State);

            if (State.Phase != beforePhase)
            {
                PhaseChanged?.Invoke(State, beforePhase, State.Phase);
            }

            return result;
        }

        /// <summary>采集阶段是否还等着玩家自选资源点（界面据此进入选点流程）。</summary>
        public bool HarvestNeedsChoices => State.Phase == TurnPhase.Harvest && State.HarvestPending;

        /// <summary>
        /// 结算整个采集阶段（§7）：传入每个座位选择的资源点后统一结算，并广播 <see cref="HarvestResolved"/>。
        /// 采集不是「轮到某人」的主要行动（没有 ActiveSeat、不占行动名额），所以不推进座位；
        /// 但它同样包成 <see cref="ResolveHarvestCommand"/> 走 <see cref="Dispatch"/> 唯一入口，
        /// 这样联机时客户端可以靠重放同一条命令复现结算。
        /// </summary>
        public HarvestReport ResolveHarvest(IReadOnlyDictionary<int, IReadOnlyList<int>> chosenBySeat)
        {
            var command = new ResolveHarvestCommand(chosenBySeat);
            Dispatch(command);
            return command.Report;
        }
    }
}