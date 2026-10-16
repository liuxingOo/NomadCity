using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 主要行动的共用合法性规则（规则规格 §6）。集中在这里，避免各行动命令各写一遍。
    /// 所有方法要么只读 state，要么显式说明会改动 state —— 校验用的方法一律只读。
    /// </summary>
    public static class ActionRules
    {
        /// <summary>校验「是不是轮到这名玩家在玩家行动阶段行动」（§6.1）。</summary>
        public static CommandResult ValidateActivePlayer(GameState state, int seat)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");

            // 开发者模式：放开「轮到谁 / 是不是行动阶段」这两道闸门，任何座位在任何阶段都能行动。
            if (state.DeveloperMode) return CommandResult.Success;

            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Phase != TurnPhase.PlayerActions) return CommandResult.Fail("当前不是玩家行动阶段。");
            if (seat < 0 || seat >= state.Players.Count) return CommandResult.Fail($"座位号 {seat} 无效。");
            if (seat != state.ActiveSeat) return CommandResult.Fail("还没轮到你行动。");
            return CommandResult.Success;
        }

        /// <summary>资源点是否已开放探索：非红色区域恒开放；红色区域要等人数变体规定的回合（§10.3）。</summary>
        public static bool IsNodeOpen(GameState state, MapNodeState node)
        {
            if (state.DeveloperMode) return true; // 开发者模式：红区也随时可探索
            if (!node.IsRedZone) return true;
            return state.TurnNumber >= PlayerCountVariant.For(state.Players.Count).RedZoneOpenTurn;
        }

        /// <summary>该资源点上是否停靠着「别人」的移动城市（自己的不算）。</summary>
        public static bool HasOpponentCity(GameState state, int nodeId, int seat)
        {
            foreach (PlayerState player in state.Players)
            {
                if (player.SeatIndex != seat && player.MobileCityNodeId == nodeId) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断能否把影响力放到某个空格（§6.4a 的两个条件）。
        /// planned 用来把「同一条命令里前面几步的效果」算进来（调度一次移动 2 个影响力时需要）；
        /// 传 null 表示只看当前 state。
        /// </summary>
        public static CommandResult CanPlaceInfluence(
            GameState state, int seat, InfluenceSlotRef slot, IReadOnlyDictionary<InfluenceSlotRef, int> planned = null)
        {
            InfluenceSlots slots = state.Map.SlotsOf(slot);
            if (slot.SlotIndex < 0 || slot.SlotIndex >= slots.Capacity)
                return CommandResult.Fail($"空格 {slot.SlotIndex} 超出该位置的容量。");

            int occupant = Occupant(slot, state, planned);
            if (occupant >= 0) return CommandResult.Fail("该影响力空格已经被占用。");

            if (slot.Kind == InfluenceSlotKind.Node)
            {
                MapNodeState node = state.Map.Node(slot.TargetId);
                if (!node.HasResourceToken) return CommandResult.Fail($"资源点 {node.Code} 还没有资源点指示物，不能放置影响力。");
                if (HasOpponentCity(state, node.Id, seat)) return CommandResult.Fail($"资源点 {node.Code} 上有对手的移动城市，不能放置影响力。");
            }

            return CommandResult.Success;
        }

        /// <summary>按当前 state 与已排定的改动，读取某个空格的所有者；空位为 -1。</summary>
        public static int Occupant(InfluenceSlotRef slot, GameState state, IReadOnlyDictionary<InfluenceSlotRef, int> planned)
        {
            if (planned != null && planned.TryGetValue(slot, out int plannedOwner)) return plannedOwner;
            return state.Map.OccupantOf(slot);
        }

        /// <summary>找该位置上的第一个空槽；没有则返回 -1（按当前 state）。</summary>
        public static int FirstFreeSlot(InfluenceSlots slots)
        {
            for (int i = 0; i < slots.Capacity; i++)
            {
                if (slots.IsEmpty(i)) return i;
            }
            return -1;
        }

        /// <summary>移除某条航道上「除 seat 外」的所有影响力，标记退回各自供应堆（§6.4d）。返回移除数量。</summary>
        public static int ClearRouteInfluencesOfOthers(GameState state, int routeId, int seat)
        {
            InfluenceSlots slots = state.Map.Route(routeId).Slots;
            int removed = 0;
            for (int i = 0; i < slots.Capacity; i++)
            {
                int owner = slots.OwnerOf(i);
                if (owner >= 0 && owner != seat)
                {
                    slots.Clear(i);
                    state.Player(owner).MarkersInSupply++;
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>移除某资源点上「除 seat 外」的所有影响力，标记退回各自供应堆（§6.4d）。返回移除数量。</summary>
        public static int ClearNodeInfluencesOfOthers(GameState state, int nodeId, int seat)
        {
            InfluenceSlots slots = state.Map.Node(nodeId).Slots;
            int removed = 0;
            for (int i = 0; i < slots.Capacity; i++)
            {
                int owner = slots.OwnerOf(i);
                if (owner >= 0 && owner != seat)
                {
                    slots.Clear(i);
                    state.Player(owner).MarkersInSupply++;
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>该航道上第一个对手玩家（用于路费支付对象），没有则返回 -1。</summary>
        public static int FirstOpponentOnRoute(GameState state, int routeId, int seat)
        {
            InfluenceSlots slots = state.Map.Route(routeId).Slots;
            for (int i = 0; i < slots.Capacity; i++)
            {
                int owner = slots.OwnerOf(i);
                if (owner >= 0 && owner != seat) return owner;
            }
            return -1;
        }
    }
}