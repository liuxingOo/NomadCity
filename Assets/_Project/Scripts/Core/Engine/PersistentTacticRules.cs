using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 4 张「持续留在场上（∞）」计谋的规则：收尾阶段的可选操作、支付替代、影响力被移除时的转移，
    /// 以及牌下资源 / 标记耗尽后的弃牌。卡面见 <c>Doc/EnterpriseExpansion.md</c> §9.9。
    /// </summary>
    public static class PersistentTacticRules
    {
        /// <summary>收尾阶段开始：所有持续计谋的「每收尾阶段一次」重置。</summary>
        public static void ResetCleanupUsage(GameState state)
        {
            foreach (PersistentTacticState tactic in state.PersistentTactics)
                tactic.UsedCleanupThisTurn = false;
        }

        /// <summary>当前是否还有玩家能在收尾阶段发动持续计谋（用于暂停自动推进）。</summary>
        public static bool HasCleanupActionsAvailable(GameState state)
        {
            foreach (PersistentTacticState tactic in state.PersistentTactics)
            {
                if (!tactic.UsedCleanupThisTurn && CanUseCleanup(state, tactic)) return true;
            }
            return false;
        }

        /// <summary>某张持续计谋现在能否发动它的收尾能力。</summary>
        public static bool CanUseCleanup(GameState state, PersistentTacticState tactic)
        {
            if (tactic == null) return false;
            PlayerState player = state.Player(tactic.Seat);

            switch (tactic.Kind)
            {
                case PersistentTacticKind.MudrockSquad:
                    if (player.Resources.Get(ResourceType.Orirock) < 2) return false;
                    return player.MarkersInSupply > 0 && HasPlaceableSlot(state, tactic.Seat);

                case PersistentTacticKind.ForEveryone:
                    return tactic.Markers >= 2 && player.MarkersInSupply > 0 &&
                           HasPlaceableSlot(state, tactic.Seat);

                default:
                    return false;
            }
        }

        /// <summary>
        /// 发动一次收尾能力。
        /// 泥岩小队：支付 2 源岩 → 放置 1 个影响力。
        /// 为了大家：移除牌上 2 个标记 → 放置 1 个影响力。
        /// </summary>
        public static bool UseCleanup(GameState state, PersistentTacticState tactic, IEffectChoices choices)
        {
            if (!CanUseCleanup(state, tactic)) return false;

            PlayerState player = state.Player(tactic.Seat);
            int markersSpent = 0;
            bool paid = false;

            if (tactic.Kind == PersistentTacticKind.MudrockSquad)
            {
                if (!player.Resources.TrySpend(ResourceType.Orirock, 2)) return false;
                paid = true;
            }
            else if (tactic.Kind == PersistentTacticKind.ForEveryone)
            {
                tactic.Markers -= 2;
                markersSpent = 2;
            }
            else
            {
                return false;
            }

            var report = EffectResolver.Execute(state, tactic.Seat,
                new[] { RewardOp.Influence(InfluencePlacementScope.DeployRules, 1) },
                choices, EnterpriseEffectExtension.Instance);

            if (!report.AnyApplied)
            {
                // 放置被放弃 / 失败：把刚付出的代价退回，避免白付。
                if (paid) player.Resources.Add(ResourceType.Orirock, 2);
                if (markersSpent > 0) tactic.Markers += markersSpent;
                return false;
            }

            tactic.UsedCleanupThisTurn = true;
            DiscardIfEmpty(state, tactic);
            return true;
        }

        /// <summary>
        /// 隐蔽仓库：支付资源时可以用牌上的标记代替（每次支付至多 2 个）。
        /// 返回 true 表示发生了替代，<paramref name="remaining"/> 是仍需用资源支付的数量。
        /// </summary>
        public static bool TrySubstituteForPayment(GameState state, int seat, ResourceType resource,
            int amount, IEffectChoices choices, out int remaining)
        {
            int budget = 2;
            return TrySubstituteForPayment(state, seat, resource, amount, ref budget, choices, out remaining);
        }

        /// <summary>
        /// 带共享预算的版本：一次支付里多行费用共用「至多 2 个标记」的额度。
        /// <paramref name="markerBudget"/> 会被扣减到剩余额度。
        /// </summary>
        public static bool TrySubstituteForPayment(GameState state, int seat, ResourceType resource,
            int amount, ref int markerBudget, IEffectChoices choices, out int remaining)
        {
            remaining = amount;
            if (markerBudget <= 0)
            {
                markerBudget = 0;
                return false;
            }

            PersistentTacticState tactic = state.FindPersistentTactic(seat, PersistentTacticKind.HiddenWarehouse);
            if (tactic == null || tactic.Markers <= 0 || amount <= 0) return false;

            int max = System.Math.Min(markerBudget, System.Math.Min(tactic.Markers, amount));
            if (max <= 0)
            {
                markerBudget = 0;
                return false;
            }

            var branches = new[]
            {
                new EffectBranch("正常用资源支付", System.Array.Empty<RewardOp>()),
                new EffectBranch($"用「隐蔽仓库」上的标记代替 {max} 个", System.Array.Empty<RewardOp>()),
            };
            int pick = choices.ChooseBranch(seat, default, state, branches);
            if (pick != 1) return false;

            tactic.Markers -= max;
            markerBudget -= max;
            remaining = amount - max;
            DiscardIfEmpty(state, tactic);
            return true;
        }

        /// <summary>
        /// 为了大家：自己的影响力被移除 / 替换时，可以把它放到这张牌上（而不是回到标记供应堆）。
        /// 返回 true 表示已经放到牌上，调用方**不要**再把标记加回供应堆。
        /// </summary>
        public static bool TryCaptureRemovedInfluence(GameState state, int seat, IEffectChoices choices)
        {
            PersistentTacticState tactic = state.FindPersistentTactic(seat, PersistentTacticKind.ForEveryone);
            if (tactic == null) return false;

            var branches = new[]
            {
                new EffectBranch("放回标记供应堆", System.Array.Empty<RewardOp>()),
                new EffectBranch("放到「为了大家」牌上", System.Array.Empty<RewardOp>()),
            };
            int pick = choices.ChooseBranch(seat, default, state, branches);
            if (pick != 1) return false;

            tactic.Markers++;
            return true;
        }

        /// <summary>工程订购 / 为了大家 / 隐蔽仓库：牌下资源或标记耗尽时移到所有者的角色牌弃牌区。</summary>
        public static void DiscardIfEmpty(GameState state, PersistentTacticState tactic)
        {
            if (tactic == null) return;

            bool empty;
            switch (tactic.Kind)
            {
                case PersistentTacticKind.EngineeringOrder:
                    empty = tactic.AttachedFacilityIds.Count == 0;
                    break;
                case PersistentTacticKind.ForEveryone:
                case PersistentTacticKind.HiddenWarehouse:
                    empty = tactic.Markers <= 0;
                    break;
                default:
                    return;
            }

            if (!empty) return;

            state.PersistentTactics.Remove(tactic);
            state.Player(tactic.Seat).DiscardCharacterCards.Add(tactic.CardId);
        }

        /// <summary>该玩家现在是否至少有一个可以放影响力的空格（§6.4a 部署规则）。</summary>
        public static bool HasPlaceableSlot(GameState state, int seat)
        {
            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                {
                    if (ActionRules.CanPlaceInfluence(state, seat,
                        InfluenceSlotRef.OnNode(node.Id, i)).Ok) return true;
                }
            }
            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                {
                    if (ActionRules.CanPlaceInfluence(state, seat,
                        InfluenceSlotRef.OnRoute(route.Id, i)).Ok) return true;
                }
            }
            return false;
        }
    }
}
