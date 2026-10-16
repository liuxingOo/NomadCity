using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 效果执行器的「选择供给」：凡牌面要求玩家指定落点／对象的效果，都由它回答。
    /// 由上层（UI / AI）实现；测试与无界面的自动结算用 <see cref="FirstAvailableChoices"/>。
    /// </summary>
    public interface IEffectChoices
    {
        /// <summary>
        /// 为一次「放置影响力」选落点。<paramref name="candidates"/> 已按作用域与合法性过滤好；
        /// 返回 null 表示这次放置作罢 —— 记为跳过，但不阻断后续效果（§11.1.2 尽量执行）。
        /// </summary>
        InfluenceSlotRef? ChooseInfluenceSlot(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates);
    }

    /// <summary>默认选择器：总是取第一个合法候选；没有候选则作罢。用于引擎自动结算与测试。</summary>
    public sealed class FirstAvailableChoices : IEffectChoices
    {
        public static readonly FirstAvailableChoices Default = new FirstAvailableChoices();

        public InfluenceSlotRef? ChooseInfluenceSlot(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates) =>
            candidates != null && candidates.Count > 0 ? candidates[0] : (InfluenceSlotRef?)null;
    }

    /// <summary>
    /// 扩展包专属效果的接入点。基础规则执行器**不认识**扩展效果（例如企业扩展的「选择 1 家企业」），
    /// 一律转交给已注册的扩展处理；没有扩展接手时该效果被跳过并记下原因。
    /// 这样基础流程不会因为扩展内容而改变（RuleSpec §13：不得把扩展内容硬编码进基础流程）。
    /// </summary>
    public interface IEffectExtension
    {
        /// <summary>本扩展是否受理该效果。实现里通常还要检查对应扩展开关是否已启用。</summary>
        bool Handles(RewardOp op, GameState state);

        /// <summary>执行一个扩展专属效果；返回 false 表示无法执行（记为跳过，不阻断后续效果）。</summary>
        bool Execute(RewardOp op, int seat, GameState state);
    }

    /// <summary>一条效果的执行记要。</summary>
    public readonly struct EffectLogEntry
    {
        public readonly RewardOp Op;

        /// <summary>是否成功执行。</summary>
        public readonly bool Applied;

        /// <summary>未执行时的原因；成功时为 null。</summary>
        public readonly string Note;

        public EffectLogEntry(RewardOp op, bool applied, string note)
        {
            Op = op;
            Applied = applied;
            Note = note;
        }

        public override string ToString() => Applied ? $"{Op} ✓" : $"{Op} ✗（{Note}）";
    }

    /// <summary>一次效果执行的报告。只读，供 UI 显示「做了什么／哪里没做成」。</summary>
    public sealed class EffectReport
    {
        public readonly int Seat;

        /// <summary>逐条效果的执行记要，按执行顺序。</summary>
        public readonly IReadOnlyList<EffectLogEntry> Entries;

        /// <summary>条件式效果被玩家主动放弃（未支付条件段，因此也未执行结果段）。</summary>
        public readonly bool Declined;

        /// <summary>整体说明：例如条件段未能完整执行。</summary>
        public readonly string Note;

        internal EffectReport(int seat, IReadOnlyList<EffectLogEntry> entries, bool declined, string note)
        {
            Seat = seat;
            Entries = entries ?? Array.Empty<EffectLogEntry>();
            Declined = declined;
            Note = note;
        }

        /// <summary>是否有任意一条效果真正执行成功。</summary>
        public bool AnyApplied
        {
            get
            {
                foreach (EffectLogEntry entry in Entries)
                {
                    if (entry.Applied) return true;
                }
                return false;
            }
        }

        /// <summary>人类可读的单行摘要，用于 HUD 状态行。</summary>
        public string Describe()
        {
            if (Declined) return Note ?? "效果被放弃。";
            if (Entries.Count == 0) return "没有可执行的效果。";

            var parts = new List<string>(Entries.Count);
            foreach (EffectLogEntry entry in Entries) parts.Add(entry.ToString());
            return string.Join("；", parts);
        }
    }

    /// <summary>
    /// 通用效果执行器（RuleSpec §11）。角色牌、设施牌、事件牌选项、城市样式效果……
    /// 凡「一串操作」都由这里统一结算，**不得为每张牌硬编码**（§11 明文要求）。
    ///
    /// 语义（§11.1–11.2）：
    /// - 按顺序执行：列表从左往右、从上往下。
    /// - 尽量执行：某项执行不了就跳过它，继续执行后面的。
    /// - 非条件式效果不可放弃：<see cref="Execute"/> 传入的效果一律尽力执行到位。
    /// - 条件式效果：<see cref="ExecuteOption"/>；条件段（「→」前）要么整段成功、要么整段放弃，
    ///   只有条件段被完整执行，结果段才执行（§11.2.2）。
    ///
    /// 分界：本类只实现**基础规则**的效果；扩展专属效果（如企业扩展的「选择 1 家企业」）不在这里
    /// 实现，而是转交 <see cref="IEffectExtension"/>，未接入时跳过（§11.4）。
    ///
    /// 尚未实现（等对应牌面数据到位再补，避免空造）：§11.4 的「二选一（or 分支）」与
    /// 「延迟结算（本回合收尾阶段）」—— 现有 22 张事件牌与 2 张「E」牌都未使用这两种节点。
    /// </summary>
    public static class EffectResolver
    {
        /// <summary>该效果类型是否只属于扩展包、基础规则不实现其语义。</summary>
        public static bool IsExtensionOnly(RewardKind kind) => kind switch
        {
            RewardKind.ChooseEnterprise => true, // 企业扩展
            _ => false,
        };

        /// <summary>
        /// 执行一串效果（不含条件式结构），尽力执行：某项做不了就跳过，继续后面的。
        /// <paramref name="anchorNodeId"/> 是触发这次效果的资源点（事件牌所属的点），
        /// 用于限定落点的 PlaceInfluence（<see cref="InfluencePlacementScope"/>）；无则为 -1。
        /// </summary>
        public static EffectReport Execute(GameState state, int seat, IReadOnlyList<RewardOp> ops,
            IEffectChoices choices = null, IEffectExtension extension = null, int anchorNodeId = -1)
        {
            var entries = new List<EffectLogEntry>();
            if (state == null || ops == null) return new EffectReport(seat, entries, false, null);

            choices ??= FirstAvailableChoices.Default;
            foreach (RewardOp op in ops)
            {
                entries.Add(ExecuteOne(state, seat, op, choices, extension, anchorNodeId));
            }
            return new EffectReport(seat, entries, false, null);
        }

        /// <summary>
        /// 执行一个事件牌选项（含条件式语义，§11.2）。
        /// <paramref name="acceptCost"/> 为 false 表示玩家主动放弃条件段（整个效果结束，不执行结果段）。
        /// 无条件段的选项直接执行结果段。
        /// </summary>
        public static EffectReport ExecuteOption(GameState state, int seat, EventOption option,
            bool acceptCost = true, IEffectChoices choices = null, IEffectExtension extension = null,
            int anchorNodeId = -1)
        {
            var entries = new List<EffectLogEntry>();
            if (state == null || option == null) return new EffectReport(seat, entries, false, null);

            choices ??= FirstAvailableChoices.Default;

            if (option.HasCost)
            {
                if (!acceptCost)
                    return new EffectReport(seat, entries, true, "玩家放弃了条件段（§11.2.1）。");

                if (!CostFeasible(state, seat, option.Cost, anchorNodeId, extension, out string reason))
                    return new EffectReport(seat, entries, true, $"条件段无法完整执行，效果结束：{reason}（§11.2.2）。");

                foreach (RewardOp op in option.Cost)
                {
                    entries.Add(ExecuteOne(state, seat, op, choices, extension, anchorNodeId));
                }
            }

            foreach (RewardOp op in option.Rewards)
            {
                entries.Add(ExecuteOne(state, seat, op, choices, extension, anchorNodeId));
            }

            return new EffectReport(seat, entries, false, null);
        }

        // —— 单条效果 ——

        private static EffectLogEntry ExecuteOne(GameState state, int seat, RewardOp op,
            IEffectChoices choices, IEffectExtension extension, int anchorNodeId)
        {
            if (IsExtensionOnly(op.Kind))
            {
                if (extension != null && extension.Handles(op, state))
                {
                    bool ok = extension.Execute(op, seat, state);
                    return new EffectLogEntry(op, ok, ok ? null : "扩展未能执行该效果。");
                }
                return new EffectLogEntry(op, false, "该效果属于扩展内容（例如企业扩展），当前未接入。");
            }

            if (seat < 0 || seat >= state.Players.Count)
                return new EffectLogEntry(op, false, $"座位号 {seat} 无效。");

            bool applied = TryApplyBase(state, seat, op, choices, anchorNodeId, out string note);
            return new EffectLogEntry(op, applied, note);
        }

        /// <summary>执行一条基础规则效果；返回是否成功，失败时给出中文原因。</summary>
        private static bool TryApplyBase(GameState state, int seat, RewardOp op, IEffectChoices choices,
            int anchorNodeId, out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);

            switch (op.Kind)
            {
                case RewardKind.GainResource:
                    player.Resources.Add(op.Resource, op.Amount);
                    // 至纯源石来自公共供应堆；§11.3 允许「不足」状态被容忍，不做阻断。
                    if (op.Resource == ResourceType.OriginitePrime) state.SupplyOriginitePrime -= op.Amount;
                    return true;

                case RewardKind.PayResource:
                    if (!player.Resources.TrySpend(op.Resource, op.Amount))
                    {
                        note = $"{op.Resource} 不足。";
                        return false;
                    }
                    if (op.Resource == ResourceType.OriginitePrime) state.SupplyOriginitePrime += op.Amount;
                    return true;

                case RewardKind.GainVouchers:
                    player.Vouchers += op.Amount;
                    state.SupplyVouchers -= op.Amount; // §11.3：允许不足
                    return true;

                case RewardKind.PayVouchers:
                    if (player.Vouchers < op.Amount)
                    {
                        note = "金券不足。";
                        return false;
                    }
                    player.Vouchers -= op.Amount;
                    state.SupplyVouchers += op.Amount;
                    return true;

                case RewardKind.GainScore:
                    player.Score += op.Amount;
                    return true;

                case RewardKind.LoseScore:
                    // 分数轨道最低为 0（§4.2 步 3：分数标记从「0」起步），扣到 0 为止。
                    player.Score = Math.Max(0, player.Score - op.Amount);
                    return true;

                case RewardKind.PlaceInfluence:
                    return TryPlaceInfluence(state, seat, op, choices, anchorNodeId, out note);

                case RewardKind.OpponentsGainResource:
                    foreach (PlayerState other in state.Players)
                    {
                        if (other.SeatIndex == seat) continue;
                        other.Resources.Add(op.Resource, op.Amount);
                        if (op.Resource == ResourceType.OriginitePrime) state.SupplyOriginitePrime -= op.Amount;
                    }
                    return true;

                case RewardKind.OpponentsGainVouchers:
                    foreach (PlayerState other in state.Players)
                    {
                        if (other.SeatIndex == seat) continue;
                        other.Vouchers += op.Amount;
                        state.SupplyVouchers -= op.Amount;
                    }
                    return true;

                default:
                    note = "基础执行器不认识的效果类型。";
                    return false;
            }
        }

        /// <summary>放置影响力：按作用域取合法空格，交给选择器定落点；标记不足或没有空格时跳过（§11.3）。</summary>
        private static bool TryPlaceInfluence(GameState state, int seat, RewardOp op, IEffectChoices choices,
            int anchorNodeId, out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);
            int placed = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                if (player.MarkersInSupply <= 0)
                {
                    failReason = "玩家供应堆里没有标记，无法放置影响力（§11.3）。";
                    break;
                }

                List<InfluenceSlotRef> candidates = InfluenceCandidates(state, seat, op.Scope, anchorNodeId);
                if (candidates.Count == 0)
                {
                    failReason = "没有可放置的影响力空格。";
                    break;
                }

                InfluenceSlotRef? target = choices.ChooseInfluenceSlot(seat, op, state, candidates);
                if (target == null)
                {
                    failReason = "放弃放置影响力。";
                    break;
                }

                if (!candidates.Contains(target.Value))
                {
                    failReason = "选择的空格不在合法范围内。";
                    break;
                }

                player.MarkersInSupply--;
                state.Map.SlotsOf(target.Value).Place(target.Value.SlotIndex, seat);
                placed++;
            }

            if (failReason == null) return true;

            note = placed == 0 ? failReason : $"仅放置了 {placed} 个影响力（{failReason}）";
            return placed > 0; // 尽力执行：已经放下的不收回
        }

        // —— 落点候选 ——

        /// <summary>按作用域列出可放置的空格（已过滤：空槽、资源点需要指示物、无对手城市）。</summary>
        private static List<InfluenceSlotRef> InfluenceCandidates(GameState state, int seat,
            InfluencePlacementScope scope, int anchorNodeId)
        {
            var list = new List<InfluenceSlotRef>();
            MapState map = state.Map;
            bool anchorValid = anchorNodeId >= 0 && anchorNodeId < map.Nodes.Count;

            switch (scope)
            {
                case InfluencePlacementScope.DeployRules:
                    // 通用「部署」落点：全图任意合法空格（不限点或航道）。
                    for (int id = 0; id < map.Nodes.Count; id++) AddNodeSlots(state, seat, id, list);
                    for (int id = 0; id < map.Routes.Count; id++) AddRouteSlots(state, seat, id, list);
                    break;

                case InfluencePlacementScope.ThisNodeOrAdjacentRoutes:
                    // 「在此资源点或相邻的航道上」。
                    if (anchorValid)
                    {
                        AddNodeSlots(state, seat, anchorNodeId, list);
                        foreach (RouteState route in map.Routes)
                        {
                            if (route.Connects(anchorNodeId)) AddRouteSlots(state, seat, route.Id, list);
                        }
                    }
                    break;

                case InfluencePlacementScope.AdjacentRoutesOnly:
                    // 「在此资源点相邻的航道上」（不含资源点本身）。
                    if (anchorValid)
                    {
                        foreach (RouteState route in map.Routes)
                        {
                            if (route.Connects(anchorNodeId)) AddRouteSlots(state, seat, route.Id, list);
                        }
                    }
                    break;
            }

            return list;
        }

        private static void AddNodeSlots(GameState state, int seat, int nodeId, List<InfluenceSlotRef> list)
        {
            InfluenceSlots slots = state.Map.Node(nodeId).Slots;
            for (int i = 0; i < slots.Capacity; i++)
            {
                if (!slots.IsEmpty(i)) continue;
                var candidate = InfluenceSlotRef.OnNode(nodeId, i);
                if (ActionRules.CanPlaceInfluence(state, seat, candidate).Ok) list.Add(candidate);
            }
        }

        private static void AddRouteSlots(GameState state, int seat, int routeId, List<InfluenceSlotRef> list)
        {
            InfluenceSlots slots = state.Map.Route(routeId).Slots;
            for (int i = 0; i < slots.Capacity; i++)
            {
                if (!slots.IsEmpty(i)) continue;
                var candidate = InfluenceSlotRef.OnRoute(routeId, i);
                if (ActionRules.CanPlaceInfluence(state, seat, candidate).Ok) list.Add(candidate);
            }
        }

        // —— 条件段可行性试算 ——

        /// <summary>
        /// 条件段可否被**完整**执行（只读试算，不改 state，§11.2.2）。
        /// 用局部副本累计消耗，避免「多步条件段里前面几步把钱花光、后面几步才发现不够」的漏判。
        /// </summary>
        private static bool CostFeasible(GameState state, int seat, IReadOnlyList<RewardOp> cost,
            int anchorNodeId, IEffectExtension extension, out string reason)
        {
            reason = null;
            if (cost == null || cost.Count == 0) return true;

            PlayerState player = state.Player(seat);
            ResourcePool pool = player.Resources; // 值拷贝，仅用于试算
            int vouchers = player.Vouchers;
            int markers = player.MarkersInSupply;

            foreach (RewardOp op in cost)
            {
                switch (op.Kind)
                {
                    case RewardKind.PayResource:
                        if (!pool.Has(op.Resource, op.Amount))
                        {
                            reason = $"{op.Resource} 不足";
                            return false;
                        }
                        pool.Set(op.Resource, pool.Get(op.Resource) - op.Amount);
                        break;

                    case RewardKind.PayVouchers:
                        if (vouchers < op.Amount)
                        {
                            reason = "金券不足";
                            return false;
                        }
                        vouchers -= op.Amount;
                        break;

                    case RewardKind.PlaceInfluence:
                        if (markers < op.Amount)
                        {
                            reason = "玩家标记不足";
                            return false;
                        }
                        if (InfluenceCandidates(state, seat, op.Scope, anchorNodeId).Count == 0)
                        {
                            reason = "没有可放置的影响力空格";
                            return false;
                        }
                        markers -= op.Amount;
                        break;

                    default:
                        // 扩展专属条件：没有扩展接手就无法执行。
                        if (IsExtensionOnly(op.Kind) &&
                            (extension == null || !extension.Handles(op, state)))
                        {
                            reason = "需要扩展内容，当前未接入";
                            return false;
                        }
                        break;
                }
            }

            return true;
        }
    }
}
