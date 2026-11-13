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
        /// <summary>校验「是不是轮到这名玩家在玩家行动阶段行动」，**不看**角色牌是否处于连用中途（§6.1）。</summary>
        public static CommandResult ValidateActiveSeat(GameState state, int seat)
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

        /// <summary>校验「是不是轮到这名玩家在玩家行动阶段行动」（§6.1）。</summary>
        public static CommandResult ValidateActivePlayer(GameState state, int seat)
        {
            CommandResult turn = ValidateActiveSeat(state, seat);
            if (!turn.Ok) return turn;

            // §10.1.2：起始玩家连续使用策略与计谋时，两种效果之间不能插入其他主要 / 快速行动。
            // 角色牌自己的命令绕开这道闸门（见 UseCharacterCardCommand / EndCharacterCardUseCommand）。
            if (!state.DeveloperMode && state.Player(seat).CharacterSecondEffectPending)
                return CommandResult.Fail("使用两种角色牌效果之间不能插入其他行动（§10.1.2）：请先使用另一种效果或结束角色牌使用。");

            // 企业扩展：开局入场「7 选 5」没做完之前不能开始行动（EnterpriseExpansion.md §4.2）。
            if (!state.DeveloperMode && !state.Player(seat).SetupDiscardDone)
                return CommandResult.Fail("请先把开局手牌里的 2 张放回游戏盒（企业扩展开局入场）。");

            return CommandResult.Success;
        }

        /// <summary>
        /// 校验「本轮的主要行动名额还没用掉」（§6.1）：每轮 1 个主要行动，做完之后只能做快速行动，
        /// 或点「结束回合」把行动权交给下一位。
        ///
        /// <paramref name="isContinuation"/> 给多步行动的**收尾步**用（探索 / 城市移动的「放置影响力」）：
        /// 名额在第一步就已经算在这件事上了，收尾时不再查，否则中间插一次别的行动就会把这一步行封死。
        ///
        /// 必须由**主要行动**各自调用 —— 不能塞进 <see cref="ValidateActivePlayer"/>：
        /// 宣告城市样式这类快速行动也用它，塞进去会把「主要行动后仍可任意次快速行动」一起堵死。
        /// 「额外主要行动」余额（§6.3f）例外。
        /// </summary>
        public static CommandResult ValidateMainActionAvailable(GameState state, int seat, bool isContinuation = false)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.DeveloperMode) return CommandResult.Success;
            if (seat < 0 || seat >= state.Players.Count) return CommandResult.Success; // 座位问题交给 ValidateActiveSeat

            if (isContinuation) return CommandResult.Success;

            // 还有没走完的多步主要行动时不能另开一次，否则它的收尾步会变成这个玩家的第二次主要行动。
            if (state.CityMoveOriginNodeId >= 0)
                return CommandResult.Fail("城市移动还没走完（还要在出发点放置 1 个影响力），先完成它（§6.4d）。");
            foreach (MapNodeState node in state.Map.Nodes)
            {
                if (node.ExplorePending)
                    return CommandResult.Fail($"探索还没走完（还要在资源点 {node.Code} 放置 1 个影响力），先完成它（§6.4c）。");
            }

            if (!state.MainActionTakenThisRound) return CommandResult.Success;
            if (state.Player(seat).ExtraMainActionsThisTurn > 0) return CommandResult.Success;

            return CommandResult.Fail("本轮已经执行过主要行动：可以先做任意次快速行动，或点「结束回合」交给下一位（§6.1）。");
        }

        /// <summary>
        /// 校验「这名玩家在本轮能否使用角色牌」（§6.3f）：
        /// 「源石工业中枢」/「高效移动管理体系」特殊行动的「使用此行动的轮中不能使用角色牌」，
        /// 记为 <see cref="PlayerState.CharacterCardBanRound"/>，仅在该轮（= <see cref="GameState.ActionRound"/>）内生效。
        /// </summary>
        public static CommandResult ValidateCharacterCardAllowed(GameState state, int seat)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.DeveloperMode) return CommandResult.Success;

            if (seat >= 0 && seat < state.Players.Count &&
                state.Player(seat).CharacterCardBanRound == state.ActionRound)
                return CommandResult.Fail("本轮使用了城市样式特殊行动，不能再使用角色牌（§6.3f）。");

            return CommandResult.Success;
        }

        /// <summary>
        /// 开发者模式（仅调试）：跳过所有费用校验与扣除（建设 / 城市移动 / 探索路费 / 采集路费 / 效果里的付费），
        /// 让各行动与效果可以「不消耗资源」地反复测试。正式游戏请务必关闭。
        /// </summary>
        public static bool IgnoresCosts(GameState state) => state != null && state.DeveloperMode;

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

            // 企业扩展「开辟商路」：公路覆盖了该航道所有影响力格，不能再放新的影响力。
            if (slot.Kind == InfluenceSlotKind.Route && state.Map.Route(slot.TargetId).IsHighway)
                return CommandResult.Fail("这条航道上有公路，影响力格已被覆盖，不能再放置影响力。");

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