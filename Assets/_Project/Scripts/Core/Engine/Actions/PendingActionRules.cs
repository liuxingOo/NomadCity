using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 挂起行动（行动事件）的合法性规则。界面用它过滤候选，命令的 Validate 也用它做最终裁决，
    /// 保证「效果给的建设只允许建多少金券」「只能移除自己的影响力」这类限制只有一份实现。
    /// </summary>
    public static class PendingActionRules
    {
        /// <summary>这条挂起行动是否属于该座位、种类是否匹配。命令 Validate 用。</summary>
        public static CommandResult ValidatePending(GameState state, int seat, PendingActionKind kind, int id)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (id <= 0) return CommandResult.Success; // 没有挂起 id：开发者入口，交给调用方另行校验

            PendingAction pending = state.FindPending(id);
            if (pending == null) return CommandResult.Fail("这条挂起行动已经不存在了。");
            if (pending.Seat != seat) return CommandResult.Fail("这不是你的挂起行动。");
            if (pending.Kind != kind)
                return CommandResult.Fail($"这条挂起行动是「{PendingAction.LabelOf(pending.Kind)}」，不是「{PendingAction.LabelOf(kind)}」。");

            return CommandResult.Success;
        }

        /// <summary>该座位是否还有任何合法目标可以完成这条挂起行动。</summary>
        public static bool HasAnyLegalTarget(GameState state, int seat, PendingActionKind kind, PendingActionLimits limits)
        {
            switch (kind)
            {
                case PendingActionKind.Explore:
                    return ExploreCandidates(state, seat, limits).Count > 0;
                case PendingActionKind.Build:
                    return BuildableFacilityIds(state, seat, limits).Count > 0;
                case PendingActionKind.RemoveInfluence:
                    return RemoveCandidates(state, seat, limits.Scope).Count > 0;
                case PendingActionKind.CityMove:
                    return CityMoveCandidates(state, seat, limits).Count > 0;
                case PendingActionKind.RelocateInfluence:
                    return HasRelocatableInfluence(state, seat);
                default:
                    return false;
            }
        }

        /// <summary>可探索的资源点（规则同探索行动：未开放、无指示物、无对手城市、事件牌堆非空、放得下影响力）。</summary>
        public static List<int> ExploreCandidates(GameState state, int seat, PendingActionLimits limits)
        {
            var list = new List<int>();
            if (state == null || seat < 0 || seat >= state.Players.Count) return list;

            PlayerState player = state.Player(seat);
            if (player.MobileCityNodeId < 0 || player.MarkersInSupply <= 0) return list;

            for (int id = 0; id < state.Map.Nodes.Count; id++)
            {
                MapNodeState node = state.Map.Node(id);
                if (node.HasResourceToken) continue;
                if (!ActionRules.IsNodeOpen(state, node)) continue;
                if (ActionRules.HasOpponentCity(state, id, seat)) continue;
                if (ExploreCommand.DeckOf(state, node.Color).Count == 0) continue;
                if (ActionRules.FirstFreeSlot(node.Slots) < 0) continue;

                IReadOnlyList<int> path =
                    TollCalculator.FindCheapestPath(state, seat, player.MobileCityNodeId, id);
                // 空列表是合法的：移动城市就停在目标点上，零路费。
                if (path == null) continue;

                TollCalculator.Toll toll = TollCalculator.Compute(state, seat, path, null);
                if (!limits.Free && player.Vouchers < toll.Total) continue;
                if (limits.MaxVoucherSpend >= 0 && toll.Total > limits.MaxVoucherSpend) continue;

                list.Add(id);
            }

            return list;
        }

        /// <summary>这条挂起行动能否探索该资源点。</summary>
        public static bool CanExplore(GameState state, int seat, int nodeId, PendingActionLimits limits) =>
            ExploreCandidates(state, seat, limits).Contains(nodeId);

        /// <summary>这次挂起建设可以选哪些设施（面板要有空位，并按免费 / 金券上限过滤）。</summary>
        public static List<int> BuildableFacilityIds(GameState state, int seat, PendingActionLimits limits)
        {
            var list = new List<int>();
            if (state == null || seat < 0 || seat >= state.Players.Count) return list;

            if (!HasPanelSpace(state.Player(seat))) return list;

            foreach (int id in state.FacilitySupply)
            {
                FacilityCardDef card = state.Catalog.Facility(id);
                if (card == null) continue;
                if (limits.MaxVoucherSpend >= 0 && card.VoucherCost > limits.MaxVoucherSpend) continue;
                if (!limits.Free)
                {
                    PlayerState player = state.Player(seat);
                    if (player.Vouchers < card.VoucherCost) continue;
                }

                list.Add(id);
            }

            return list;
        }

        /// <summary>
        /// 这次挂起的城市移动可以停靠哪些资源点：相邻、已开放、没有对手城市、出发点还放得下影响力，
        /// 并按限制过滤（极境·计谋要求目标点上有自己的影响力）。
        /// </summary>
        public static List<int> CityMoveCandidates(GameState state, int seat, PendingActionLimits limits)
        {
            var list = new List<int>();
            if (state == null || seat < 0 || seat >= state.Players.Count) return list;

            PlayerState player = state.Player(seat);
            int origin = player.MobileCityNodeId;
            if (origin < 0 || origin >= state.Map.Nodes.Count) return list;
            if (ActionRules.FirstFreeSlot(state.Map.Node(origin).Slots) < 0) return list;

            for (int id = 0; id < state.Map.Nodes.Count; id++)
            {
                if (id == origin) continue;
                if (state.Map.FindRoute(origin, id) == null) continue;

                MapNodeState node = state.Map.Node(id);
                if (!ActionRules.IsNodeOpen(state, node)) continue;
                if (ActionRules.HasOpponentCity(state, id, seat)) continue;
                if (limits.NodeScope == PendingNodeScope.OwnInfluence && !HasOwnInfluence(state, id, seat))
                    continue;

                list.Add(id);
            }

            return list;
        }

        /// <summary>这次挂起的城市移动能否停靠该资源点。</summary>
        public static bool CanCityMove(GameState state, int seat, int nodeId, PendingActionLimits limits) =>
            CityMoveCandidates(state, seat, limits).Contains(nodeId);

        /// <summary>是否还有能调度的影响力（自己有标记，且存在一个合法的目标空格）。</summary>
        public static bool HasRelocatableInfluence(GameState state, int seat)
        {
            if (state == null || seat < 0 || seat >= state.Players.Count) return false;

            var froms = new List<InfluenceSlotRef>();
            foreach (InfluenceSlotRef slot in AllSlots(state))
            {
                if (state.Map.OccupantOf(slot) == seat) froms.Add(slot);
            }

            if (froms.Count == 0) return false;

            foreach (InfluenceSlotRef to in AllSlots(state))
            {
                if (state.Map.OccupantOf(to) >= 0) continue;
                foreach (InfluenceSlotRef from in froms)
                {
                    if (from.Equals(to)) continue;
                    if (ActionRules.CanPlaceInfluence(state, seat, to, null).Ok) return true;
                }
            }

            return false;
        }

        private static IEnumerable<InfluenceSlotRef> AllSlots(GameState state)
        {
            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                    yield return InfluenceSlotRef.OnNode(node.Id, i);
            }

            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                    yield return InfluenceSlotRef.OnRoute(route.Id, i);
            }
        }

        private static bool HasOwnInfluence(GameState state, int nodeId, int seat)
        {
            MapNodeState node = state.Map.Node(nodeId);
            for (int i = 0; i < node.Slots.Capacity; i++)
            {
                if (state.Map.OccupantOf(InfluenceSlotRef.OnNode(nodeId, i)) == seat) return true;
            }

            return false;
        }

        /// <summary>可被移除的影响力格（按「任意 / 只限自己 / 只限他人」过滤）。</summary>
        public static List<InfluenceSlotRef> RemoveCandidates(GameState state, int seat, PendingInfluenceScope scope)
        {
            var list = new List<InfluenceSlotRef>();
            if (state == null) return list;

            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                {
                    var slot = InfluenceSlotRef.OnNode(node.Id, i);
                    if (Matches(state, slot, seat, scope)) list.Add(slot);
                }
            }

            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                {
                    var slot = InfluenceSlotRef.OnRoute(route.Id, i);
                    if (Matches(state, slot, seat, scope)) list.Add(slot);
                }
            }

            return list;
        }

        private static bool Matches(GameState state, InfluenceSlotRef slot, int seat, PendingInfluenceScope scope)
        {
            int owner = state.Map.OccupantOf(slot);
            if (owner < 0) return false;

            return scope switch
            {
                PendingInfluenceScope.Own => owner == seat,
                PendingInfluenceScope.Others => owner != seat,
                _ => true,
            };
        }

        private static bool HasPanelSpace(PlayerState player)
        {
            for (int s = 0; s < PlayerState.CityPanelSlotCount; s++)
            {
                if (s == PlayerState.CityPanelCoreSlot) continue;
                if (player.CityPanelSlots[s] < 0) return true;
            }

            return false;
        }
    }
}
