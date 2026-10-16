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
        /// 采集阶段结算完成：(state, 报告)。UI 可据此显示本次采集的收成与路费。
        /// ⚠️ 临时占位（M3）：目前进入采集阶段即自动结算，见 <see cref="ResolveHarvestAutomatically"/>。
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
            ICardCatalog catalog = null, Random rng = null)
        {
            var state = new GameState(setup)
            {
                SupplyOriginitePrime = 35, // 规则规格 3：至纯源石 35 个
                SupplyVouchers = 70,       // 规则规格 3：金券共 70 张
            };

            for (int seat = 0; seat < setup.PlayerCount; seat++)
            {
                state.Players.Add(new PlayerState(seat, (PlayerColor)seat)
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

            // 座位推进发生在 CommandApplied 之后，界面不会因为上面那次广播再刷新一次 ——
            // 不在这里补一次，顶栏与行动提示就会一直显示上一位玩家。
            if (State.ActiveSeat != beforeSeat) SeatAdvanced?.Invoke(State, beforeSeat, State.ActiveSeat);

            if (State.IsGameOver && !wasOver) GameEnded?.Invoke(State);
            else if (State.TurnNumber != beforeTurn) TurnStarted?.Invoke(State);

            if (State.Phase != beforePhase)
            {
                PhaseChanged?.Invoke(State, beforePhase, State.Phase);

                // ⚠️ 临时占位（M3）：进入采集阶段就自动结算。
                // 完整的 §7「玩家自选资源点 + 路费预览」UI 留到下一期，届时删除这里。
                if (State.Phase == TurnPhase.Harvest) ResolveHarvestAutomatically();
            }

            return result;
        }

        /// <summary>
        /// ⚠️ 临时占位（M3）：进入采集阶段时，按「每个座位采集其全部合法资源点」直接结算。
        /// <see cref="HarvestResolver.Plan"/> 内部会按预算自动跳过付不起的点，所以这个默认值不会产生非法状态。
        /// 采集不是「轮到某人」的主要行动，所以不走命令通道，直接作用于 state（与 TurnFlow 的其它阶段边界一致）。
        /// </summary>
        private void ResolveHarvestAutomatically()
        {
            var chosen = new Dictionary<int, IReadOnlyList<int>>();
            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                chosen[seat] = HarvestResolver.EligibleNodes(State, seat);
            }

            HarvestResolved?.Invoke(State, HarvestResolver.Resolve(State, chosen));
        }
    }
}