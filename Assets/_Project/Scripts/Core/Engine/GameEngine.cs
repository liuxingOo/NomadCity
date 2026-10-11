using System;
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

        /// <summary>第 8 回合收尾结束，进入终局结算。</summary>
        public event Action<GameState> GameEnded;

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

            BoardSetup.Run(state, catalog ?? PlaceholderCardCatalog.Default, rng ?? new Random(), firstPlayerSeat);
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
            bool wasOver = State.IsGameOver;

            CommandResult result = Dispatcher.Dispatch(State, command);
            if (!result.Ok) return result;

            if (command.ConsumesAction) TurnFlow.CompletePlayerAction(State);

            if (State.IsGameOver && !wasOver) GameEnded?.Invoke(State);
            else if (State.TurnNumber != beforeTurn) TurnStarted?.Invoke(State);

            if (State.Phase != beforePhase) PhaseChanged?.Invoke(State, beforePhase, State.Phase);

            return result;
        }
    }
}