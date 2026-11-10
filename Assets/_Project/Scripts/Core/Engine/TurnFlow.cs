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
            state.MainActionTakenThisRound = false;
            state.Phase = TurnPhase.PlayerActions;
        }

        /// <summary>
        /// 一名玩家结束本回合后调用（<see cref="EndTurnCommand"/>，以及联机「挂机跳过」）：轮到顺时针下一位；
        /// 一轮走完则轮次 +1；第 2 轮也走完就自动进入采集阶段（规则规格 6.1）。
        ///
        /// 注意：主要行动本身**不再**调用它 —— 做完主要行动后玩家还能在回合内任意次执行快速行动，
        /// 只有主动点「结束回合」才会走到这里。
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
                state.MainActionTakenThisRound = false; // 同一玩家继续做那一次额外主要行动
                return; // 该玩家本轮还能继续行动，不推进座位
            }

            state.ActiveSeat = state.NextSeat(state.ActiveSeat);
            state.MainActionTakenThisRound = false; // 换人 → 下一家的主要行动名额复位
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
            // 企业扩展：企业特效「同一特效一回合只能触发 1 次」，新回合全部复位。
            state.Enterprises?.ResetTurnEffects();
            // 起始玩家顺时针传递（§8）；「联邦理事会」若在本回合收尾阶段生效，则改由它指定的玩家担任。
            state.FirstPlayerSeat = state.PendingStartMarkerSeat >= 0
                ? state.PendingStartMarkerSeat
                : state.NextSeat(state.FirstPlayerSeat);
            state.PendingStartMarkerSeat = -1;
            state.ActiveSeat = state.FirstPlayerSeat;
            state.ActionRound = 1;
            BeginCharacterPlacement(state);
        }

        /// <summary>
        /// 不需要玩家点按钮的阶段边界（正式对局的唯一推进方式）：
        /// - 角色牌盖放阶段：所有人都盖放好（且第 1 回合开局入场已结束、2 人局弃牌已完成）→ 玩家行动第 1 轮；
        /// - 采集阶段：所有人的选择都结算完 → 收尾结算 → 下一回合（最后一回合则终局结算）。
        ///
        /// 只在 <see cref="GameEngine.Dispatch"/> 里、每条命令成功执行之后调用：
        /// 联机时客户端重放的是同一条命令，因此这里必须是纯状态判断，保证主机与各客户端推进到同一状态。
        /// </summary>
        public static bool TryAutoAdvance(GameState state)
        {
            if (state == null || state.IsGameOver) return false;

            if (state.Phase == TurnPhase.CharacterPlacement)
            {
                // 2 人局（§12 变体 4）：弃设施牌是盖放前的步骤，没弃完不进入玩家行动。
                if (state.SupplyDiscardRemaining > 0) return false;

                // 第 1 回合的「开局入场」（§4.3）也在盖放阶段里：要等每个人都选好初始资源点、并把翻出的事件牌结算完。
                if (state.TurnNumber == 1 && ChooseStartNodeCommand.CurrentOperator(state) >= 0) return false;

                if (!AllCharacterCardsPlaced(state)) return false;

                BeginPlayerActions(state);
                return true;
            }

            if (state.Phase == TurnPhase.Harvest && !state.HarvestPending)
            {
                AdvancePhase(state); // → 收尾阶段并执行收尾结算（§8）
                AdvancePhase(state); // → 下一回合的角色牌盖放 / 终局结算
                return true;
            }

            return false;
        }

        /// <summary>所有人要么已经盖放，要么手里和弃牌区都没有角色牌可盖了。</summary>
        private static bool AllCharacterCardsPlaced(GameState state)
        {
            if (state.Players.Count == 0) return false;

            foreach (PlayerState player in state.Players)
            {
                if (player.PlacedCharacterCard >= 0) continue;
                if (player.HandCharacterCards.Count > 0 || player.DiscardCharacterCards.Count > 0) return false;
            }

            return true;
        }
    }
}