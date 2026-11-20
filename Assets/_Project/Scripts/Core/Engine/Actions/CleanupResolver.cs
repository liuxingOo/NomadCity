using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 收尾阶段（§8）。进入收尾后**按本回合行动顺序逐个座位**结算：
    /// 1) 必须执行的收尾效果（如「雷蛇·策略」这类延迟结算）轮到该玩家时当场结算；
    /// 2) 可选择是否触发的效果（如「泥岩小队」，卡面有红色横箭头）停下来等玩家自己决定；
    /// 3) 什么可触发效果都没有的玩家直接跳过，并记进 <see cref="GameState.CleanupSkippedSeats"/> 供行动记录显示。
    ///
    /// 全部座位轮完后才回收角色牌、复位样式标记与本回合标志，再进入下一回合。
    /// 顺序有讲究：先判角色牌是否用过（要用到「本回合是否用过」标志），最后才复位这些标志。
    /// </summary>
    public static class CleanupResolver
    {
        /// <summary>进入收尾阶段：排出座位顺序，并结算到第一个需要玩家决定的座位。</summary>
        public static void Begin(GameState state)
        {
            // 持续计谋的「每收尾阶段一次」在本阶段重新可用（§9.9）。
            PersistentTacticRules.ResetCleanupUsage(state);

            state.CleanupSkippedSeats.Clear();
            state.CleanupOrder.Clear();
            state.CleanupIndex = 0;

            int seat = state.FirstPlayerSeat;
            for (int i = 0; i < state.Players.Count; i++)
            {
                state.CleanupOrder.Add(seat);
                seat = state.NextSeat(seat);
            }

            Advance(state);
        }

        /// <summary>
        /// 推进到下一个要玩家决定的座位：一路上「必须执行」的效果当场结算，
        /// 没有可触发效果的座位直接跳过并记一笔。轮完所有座位就结束本阶段。
        /// </summary>
        public static void Advance(GameState state)
        {
            while (state.CleanupIndex >= 0 && state.CleanupIndex < state.CleanupOrder.Count)
            {
                int seat = state.CleanupOrder[state.CleanupIndex];

                bool hadMandatory = HasMandatoryEffects(state, seat);
                if (hadMandatory) ResolveMandatoryEffectsFor(state, seat);

                // 还有「可选择是否触发」的效果：停下来等他自己决定。
                if (HasOptionalEffects(state, seat)) return;

                // 什么都没触发：直接跳过，给行动记录留一条。
                if (!hadMandatory) state.CleanupSkippedSeats.Add(seat);

                state.CleanupIndex++;
            }

            Finish(state);
        }

        /// <summary>当前这位玩家用掉一个效果后调用：还有可选效果就继续等，没有就轮到下一位。</summary>
        public static void AdvanceIfDone(GameState state)
        {
            if (state == null || state.Phase != TurnPhase.Cleanup) return;
            if (state.CleanupIndex < 0 || state.CleanupSeat < 0) return;
            if (HasOptionalEffects(state, state.CleanupSeat)) return;

            state.CleanupIndex++;
            Advance(state);
        }

        /// <summary>某位玩家此刻有没有「必须执行」的收尾效果。</summary>
        public static bool HasMandatoryEffects(GameState state, int seat)
        {
            foreach (DeferredEffect deferred in state.DeferredEffects)
                if (deferred.Seat == seat) return true;

            return false;
        }

        /// <summary>某位玩家此刻有没有「可以选择是否触发」的收尾效果。</summary>
        public static bool HasOptionalEffects(GameState state, int seat)
        {
            foreach (PersistentTacticState tactic in state.PersistentTactics)
            {
                if (tactic.Seat != seat) continue;
                if (tactic.UsedCleanupThisTurn) continue;
                if (PersistentTacticRules.CanUseCleanup(state, tactic)) return true;
            }

            return false;
        }

        /// <summary>把某位玩家的「必须执行」效果当场结算掉（默认选择器取首个合法候选）。</summary>
        private static void ResolveMandatoryEffectsFor(GameState state, int seat)
        {
            var mine = new List<DeferredEffect>();
            foreach (DeferredEffect deferred in state.DeferredEffects)
                if (deferred.Seat == seat) mine.Add(deferred);

            foreach (DeferredEffect deferred in mine)
            {
                state.DeferredEffects.Remove(deferred);

                // 延迟结算自身不能再把行动挂起：否则「本回合收尾移除一个影响力」会变成一条没人执行的挂起行动。
                EffectResolver.Execute(state, deferred.Seat, deferred.Ops, null,
                    EnterpriseEffectExtension.Instance, allowDefer: false);
            }
        }

        /// <summary>所有座位都轮完了：做收尾的固定步骤，然后进入下一回合 / 终局结算。</summary>
        private static void Finish(GameState state)
        {
            state.CleanupIndex = -1;
            state.CleanupOrder.Clear();

            // 1. 未使用盖放的角色牌与弃牌区一起回到手牌（§10.1.4）。
            ReclaimCharacterCards(state);

            // 2. 复位城市样式牌上的标记（§8.2）。
            ResetPatternMarkers(state);

            // 3. 清空「本回合」作用域的标志（§8）。
            foreach (PlayerState player in state.Players) player.ResetTurnScopedState();

            // 4. 探索没走完第三步的，随回合结束作废（§6.4c）。
            ResetExplorePending(state);

            TurnFlow.AdvanceFromCleanup(state);
        }

        /// <summary>不逐个座位、直接跑完整个收尾阶段（测试与「整段跳过」用）。</summary>
        public static void Resolve(GameState state)
        {
            PersistentTacticRules.ResetCleanupUsage(state);
            state.CleanupIndex = -1;
            state.CleanupOrder.Clear();

            ReclaimCharacterCards(state);
            ResetPatternMarkers(state);
            foreach (PlayerState player in state.Players) player.ResetTurnScopedState();
            ResetExplorePending(state);
            ResolveDeferredEffects(state);
        }

        /// <summary>
        /// 结算收尾阶段效果（§8.1 / §11.4 延迟结算）：按入队顺序依次执行
        /// <see cref="GameState.DeferredEffects"/> 里的效果。玩家点选类效果仍走默认选择器（取首个合法候选）。
        /// </summary>
        public static void ResolveDeferredEffects(GameState state)
        {
            if (state.DeferredEffects.Count == 0) return;

            var queue = new List<DeferredEffect>(state.DeferredEffects);
            state.DeferredEffects.Clear();
            foreach (DeferredEffect deferred in queue)
            {
                EffectResolver.Execute(state, deferred.Seat, deferred.Ops, null,
                    EnterpriseEffectExtension.Instance, allowDefer: false);
            }
        }

        /// <summary>清掉探索「待补影响力」的标记（§6.4c）：跨回合的探索不再允许补放。</summary>
        public static void ResetExplorePending(GameState state)
        {
            foreach (MapNodeState node in state.Map.Nodes) node.ExplorePending = false;
        }

        /// <summary>
        /// 角色牌回收（§10.1.4）：本回合**没有使用过**盖放的角色牌时，把盖放的牌与
        /// 弃牌区的全部角色牌一同收回手牌。用过则不回收（那张牌已进弃牌区，留在那里）。
        /// </summary>
        public static void ReclaimCharacterCards(GameState state)
        {
            foreach (PlayerState player in state.Players)
            {
                if (player.UsedCharacterCardThisTurn) continue;

                if (player.PlacedCharacterCard >= 0)
                {
                    player.HandCharacterCards.Add(player.PlacedCharacterCard);
                    player.PlacedCharacterCard = -1;
                }

                if (player.DiscardCharacterCards.Count > 0)
                {
                    player.HandCharacterCards.AddRange(player.DiscardCharacterCards);
                    player.DiscardCharacterCards.Clear();
                }
            }
        }

        /// <summary>复位城市样式牌上的标记（§8.2）：I 级放回「未使用」区，II 级向下移动一格。</summary>
        public static void ResetPatternMarkers(GameState state)
        {
            foreach (CityPatternState pattern in state.CityPatternStates) pattern.ResetForCleanup();
        }
    }
}
