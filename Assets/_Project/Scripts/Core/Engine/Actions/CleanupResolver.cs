using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 收尾阶段（§8）。进入收尾时执行，负责把「本回合」留下的痕迹清干净，
    /// 以及把未使用的角色牌收回手牌。回合／起始玩家的推进由 <see cref="TurnFlow"/> 负责，不在这里做。
    ///
    /// 顺序有讲究：先判角色牌是否用过（要用到「本回合是否用过」标志），最后才复位这些标志。
    /// </summary>
    public static class CleanupResolver
    {
        /// <summary>执行整个收尾阶段。</summary>
        public static void Resolve(GameState state)
        {
            // 1. 未使用盖放的角色牌与弃牌区一起回到手牌（§10.1.4）。
            ReclaimCharacterCards(state);

            // 2. 复位城市样式牌上的标记（§8.2）。
            ResetPatternMarkers(state);

            // 3. 清空「本回合」作用域的标志（§8）。
            foreach (PlayerState player in state.Players) player.ResetTurnScopedState();

            // 4. 探索没走完第三步（放置影响力）的，随回合结束作废：主要行动必须在本回合内完成（§6.4c）。
            ResetExplorePending(state);

            // 5. §8.1「结算收尾阶段效果」按本回合行动顺序结算（延迟结算队列，§11.4）。
            ResolveDeferredEffects(state);
        }

        /// <summary>
        /// 结算收尾阶段效果（§8.1 / §11.4 延迟结算）：按入队顺序（即本回合的行动顺序）依次执行
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
                    EnterpriseEffectExtension.Instance);
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