using NomadCity.Core.Model;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 回合与阶段机（规则规格 5 节）：共 8 回合，每回合 4 个阶段。
    /// 这里只负责「流程往前走」——不实现任何具体行动的效果，那些属于各自的行动模块。
    /// 全部是对 GameState 的纯流程转换，因此既能被引擎直接调用，也能包成命令走命令通道。
    /// </summary>
    public static class TurnFlow
    {
        /// <summary>一局的总回合数。</summary>
        public const int TotalTurns = 8;

        /// <summary>行动阶段的轮数：每回合 2 轮（规则规格 6.1）。</summary>
        public const int ActionRoundsPerTurn = 2;

        /// <summary>开局：回合 1、角色牌盖放阶段，并指定本回合的起始玩家。</summary>
        public static void Start(GameState state, int firstPlayerSeat)
        {
            state.IsGameOver = false;
            state.TurnNumber = 1;
            state.FirstPlayerSeat = firstPlayerSeat;
            state.ActiveSeat = firstPlayerSeat;
            state.ActionRound = 1;
            state.HarvestPending = false;
            BeginCharacterPlacement(state);
        }

        /// <summary>
        /// 进入「角色牌盖放」阶段。2 人局（§12 变体 4）要先要求每名玩家从设施供应区弃 1 张设施牌，
        /// 从本回合起始玩家起顺时针；其余人数没有这一步，<see cref="GameState.SupplyDiscardRemaining"/> 置 0。
        /// </summary>
        private static void BeginCharacterPlacement(GameState state)
        {
            state.Phase = TurnPhase.CharacterPlacement;

            if (PlayerCountVariant.For(state).DiscardsSupplyCardEachTurn)
            {
                state.SupplyDiscardRemaining = state.Players.Count;
                state.SupplyDiscardSeat = state.FirstPlayerSeat;
            }
            else
            {
                state.SupplyDiscardRemaining = 0;
                state.SupplyDiscardSeat = -1;
            }
        }

        /// <summary>
        /// 推进到下一个阶段（阶段边界）：
        /// 角色牌盖放 → 玩家行动 → 采集 → 收尾 →（下一回合 / 终局）。
        /// </summary>
        public static void AdvancePhase(GameState state)
        {
            if (state.IsGameOver) return;

            switch (state.Phase)
            {
                case TurnPhase.CharacterPlacement:
                    // 2 人局还等着弃设施牌时不放行（§12 变体 4），免得整个步骤被跳过。
                    if (state.SupplyDiscardRemaining > 0) return;
                    BeginPlayerActions(state);
                    break;
                case TurnPhase.PlayerActions:
                    state.Phase = TurnPhase.Harvest;
                    state.HarvestPending = true; // 采集要先由玩家自选资源点，见 GameEngine.ResolveHarvest
                    break;
                case TurnPhase.Harvest:
                    // 采集还没结算完（还有人没做选择）时不放行，免得整个采集阶段被跳过（§7）。
                    if (state.HarvestPending) return;
                    // 进入收尾阶段即执行收尾结算（§8）：样式标记复位、角色牌回收等。
                    state.Phase = TurnPhase.Cleanup;
                    CleanupResolver.Resolve(state);
                    break;
                case TurnPhase.Cleanup:
                    NextTurn(state);
                    break;
            }
        }

        /// <summary>进入玩家行动阶段：第 1 轮，从起始玩家开始。</summary>
        public static void BeginPlayerActions(GameState state)
        {
            state.ActionRound = 1;
            state.ActiveSeat = state.FirstPlayerSeat;
            state.Phase = TurnPhase.PlayerActions;
        }

        /// <summary>
        /// 一名玩家完成一个「主要行动」后调用：轮到顺时针下一位；
        /// 一轮走完则轮次 +1；第 2 轮也走完就自动进入采集阶段（规则规格 6.1）。
        ///
        /// 「源石工业中枢」特殊行动给当前玩家追加的「额外主要行动」先在这里消费：
        /// 还有余额就递减并**保持当前座位**，让该玩家在本轮内紧接着继续行动（§6.3f）。
        /// </summary>
        public static void CompletePlayerAction(GameState state)
        {
            if (state.Phase != TurnPhase.PlayerActions) return;

            PlayerState actor = state.Player(state.ActiveSeat);
            if (actor.ExtraMainActionsThisTurn > 0)
            {
                actor.ExtraMainActionsThisTurn--;
                return; // 该玩家本轮还能继续行动，不推进座位
            }

            state.ActiveSeat = state.NextSeat(state.ActiveSeat);
            if (state.ActiveSeat != state.FirstPlayerSeat) return; // 这一轮还没走完

            state.ActionRound++;
            if (state.ActionRound > ActionRoundsPerTurn)
            {
                state.Phase = TurnPhase.Harvest; // 两轮结束 → 采集（规则规格 5）
                state.HarvestPending = true;      // 采集由玩家自选资源点后再结算，见 GameEngine.ResolveHarvest
                return;
            }

            state.ActiveSeat = state.FirstPlayerSeat; // 第 2 轮仍从起始玩家开始
        }

        /// <summary>收尾 → 下一回合；已是最后一回合则进入终局并结算分数（§9）。</summary>
        private static void NextTurn(GameState state)
        {
            if (state.TurnNumber >= TotalTurns)
            {
                state.IsGameOver = true;
                state.PendingStartMarkerSeat = -1;
                FinalScoring.Settle(state);
                return;
            }

            state.TurnNumber++;
            // 起始玩家顺时针传递（§8）；「联邦理事会」若在本回合收尾阶段生效，则改由它指定的玩家担任。
            state.FirstPlayerSeat = state.PendingStartMarkerSeat >= 0
                ? state.PendingStartMarkerSeat
                : state.NextSeat(state.FirstPlayerSeat);
            state.PendingStartMarkerSeat = -1;
            state.ActiveSeat = state.FirstPlayerSeat;
            state.ActionRound = 1;
            BeginCharacterPlacement(state);
        }
    }
}