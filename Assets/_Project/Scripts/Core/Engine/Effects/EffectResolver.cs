using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 一次「进行一次建造」（<see cref="RewardKind.BuildOnce"/>）的选择结果：
    /// 建哪张设施牌、放在面板哪一格、用哪种付费方式。
    /// </summary>
    public readonly struct BuildChoice
    {
        public readonly int FacilityId;
        public readonly int PanelSlot;
        public readonly FacilityPayment Payment;

        public BuildChoice(int facilityId, int panelSlot, FacilityPayment payment)
        {
            FacilityId = facilityId;
            PanelSlot = panelSlot;
            Payment = payment;
        }
    }

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

        /// <summary>
        /// 为一次「二选一」（§11.4）选分支：返回分支下标；-1 表示无法选择，该效果跳过。
        /// </summary>
        int ChooseBranch(int seat, RewardOp op, GameState state, IReadOnlyList<EffectBranch> branches);

        /// <summary>
        /// 为一次「拿走 / 替换影响力」选要移除的影响力格（候选已过滤为「非空」，可以是任何人的）。
        /// 返回 null 表示作罢 —— 记为跳过，不阻断后续效果。
        /// </summary>
        InfluenceSlotRef? ChooseInfluenceToRemove(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates);

        /// <summary>
        /// 为一次「移动自己的影响力」选起点与终点（<paramref name="sources"/> 是自己占用的格、
        /// <paramref name="destinations"/> 是合法的空格）。返回 null 表示作罢。
        /// </summary>
        InfluenceMove? ChooseOwnInfluenceMove(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> sources, IReadOnlyList<InfluenceSlotRef> destinations);

        /// <summary>
        /// 为「免费城市移动 / 执行 1 次探索」等需要挑一个资源点的效果选目标（候选 = 资源点 id）。
        /// 返回 -1 表示作罢，该效果跳过。
        /// </summary>
        int ChooseNode(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates);

        /// <summary>
        /// 为「进行一次建造」选设施牌与面板空位（候选 = 设施牌 id / 面板格子下标）。
        /// 返回 null 表示作罢，跳过这次建造。
        /// </summary>
        BuildChoice? ChooseBuild(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> facilityIds, IReadOnlyList<int> panelSlots);

        /// <summary>为「从余下延伸枢纽中选 1 张并建设」选一张延伸枢纽（候选 = 设施牌 id）；-1 表示作罢。</summary>
        int ChooseExtensionHub(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates);

        /// <summary>
        /// 为「结算另一张设施的入场效果」在**自己面板**上挑一张设施（候选 = 面板格子下标）；-1 表示作罢。
        /// </summary>
        int ChoosePanelFacility(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidateSlots);

        /// <summary>
        /// 为「按单价售卖资源」给出各资源的出售数量（key = 资源种类，value = 数量；未列出的资源视为 0）。
        /// 返回 null 或空表示一件也不卖。单价与持有量一并传入，方便 UI 显示。
        /// </summary>
        IReadOnlyDictionary<ResourceType, int> ChooseSellAmounts(int seat, RewardOp op, GameState state,
            IReadOnlyDictionary<ResourceType, int> prices, IReadOnlyDictionary<ResourceType, int> holdings);

        /// <summary>
        /// 为「合计获得 <paramref name="total"/> 个资源」在 <paramref name="options"/> 之间自由分配
        /// （key = 资源种类，value = 数量；未列出的资源视为 0）。
        /// 引擎会把结果夹到「合计恰好 = total」，故实现只需给出想给多少。
        /// </summary>
        IReadOnlyDictionary<ResourceType, int> ChooseResourceSpread(int seat, RewardOp op, GameState state,
            int total, IReadOnlyList<ResourceType> options, IReadOnlyDictionary<ResourceType, int> holdings);

        /// <summary>
        /// 在若干资源种类里选 1 种（候选 = 资源种类，顺序即下标）。
        /// 用于「极境·策略：选持有量最少的一种」与「坎诺特·计谋：选要征收的一种」；返回 -1 表示作罢。
        /// </summary>
        int ChooseResourceType(int seat, RewardOp op, GameState state, IReadOnlyList<ResourceType> candidates);

        /// <summary>为「把设施供应区的 1 张牌放回牌堆底部」挑一张（候选 = 设施牌 id）；-1 表示作罢。</summary>
        int ChooseSupplyFacility(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates);

        /// <summary>
        /// 「可选链」的第 <paramref name="stepIndex"/> 步是否执行：返回 0 = 执行该步，非 0 = 终止
        /// （其后步骤全部跳过）。<paramref name="label"/> 是该步的牌面说明，供 UI 显示。
        /// </summary>
        int ChooseOptionalStep(int seat, RewardOp op, GameState state, int stepIndex, string label);
    }

    /// <summary>默认选择器：总是取第一个合法候选；没有候选则作罢。用于引擎自动结算与测试。</summary>
    public sealed class FirstAvailableChoices : IEffectChoices
    {
        public static readonly FirstAvailableChoices Default = new FirstAvailableChoices();

        public InfluenceSlotRef? ChooseInfluenceSlot(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates) =>
            candidates != null && candidates.Count > 0 ? candidates[0] : (InfluenceSlotRef?)null;

        public int ChooseBranch(int seat, RewardOp op, GameState state, IReadOnlyList<EffectBranch> branches) =>
            branches != null && branches.Count > 0 ? 0 : -1;

        public InfluenceSlotRef? ChooseInfluenceToRemove(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates) =>
            candidates != null && candidates.Count > 0 ? candidates[0] : (InfluenceSlotRef?)null;

        public InfluenceMove? ChooseOwnInfluenceMove(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> sources, IReadOnlyList<InfluenceSlotRef> destinations) =>
            sources != null && sources.Count > 0 && destinations != null && destinations.Count > 0
                ? new InfluenceMove(sources[0], destinations[0])
                : (InfluenceMove?)null;

        public int ChooseNode(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates) =>
            candidates != null && candidates.Count > 0 ? candidates[0] : -1;

        public BuildChoice? ChooseBuild(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> facilityIds, IReadOnlyList<int> panelSlots) =>
            facilityIds != null && facilityIds.Count > 0 && panelSlots != null && panelSlots.Count > 0
                ? new BuildChoice(facilityIds[0], panelSlots[0], FacilityPayment.Resources)
                : (BuildChoice?)null;

        public int ChooseExtensionHub(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates) =>
            candidates != null && candidates.Count > 0 ? candidates[0] : -1;

        public int ChoosePanelFacility(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> candidateSlots) =>
            candidateSlots != null && candidateSlots.Count > 0 ? candidateSlots[0] : -1;

        // 默认一件也不卖（「按单价售卖」是可选的，不卖即跳过，不阻断后续效果）。
        public IReadOnlyDictionary<ResourceType, int> ChooseSellAmounts(int seat, RewardOp op, GameState state,
            IReadOnlyDictionary<ResourceType, int> prices, IReadOnlyDictionary<ResourceType, int> holdings) =>
            EmptySellAmounts;

        private static readonly Dictionary<ResourceType, int> EmptySellAmounts = new();

        // 默认把全部合计数量都给第一种资源（谁都不选时也保证「合计 N 个」这一条成立）。
        public IReadOnlyDictionary<ResourceType, int> ChooseResourceSpread(int seat, RewardOp op, GameState state,
            int total, IReadOnlyList<ResourceType> options, IReadOnlyDictionary<ResourceType, int> holdings) =>
            options != null && options.Count > 0 && total > 0
                ? new Dictionary<ResourceType, int> { [options[0]] = total }
                : EmptySellAmounts;

        public int ChooseResourceType(int seat, RewardOp op, GameState state, IReadOnlyList<ResourceType> candidates) =>
            candidates != null && candidates.Count > 0 ? 0 : -1;

        public int ChooseSupplyFacility(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates) =>
            candidates != null && candidates.Count > 0 ? candidates[0] : -1;

        // 默认执行每一步可选步骤（不改变牌面效果；正式由玩家在弹窗里决定）。
        public int ChooseOptionalStep(int seat, RewardOp op, GameState state, int stepIndex, string label) => 0;
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
    /// 「延迟结算（本回合收尾阶段）」（§11.4）已实现：<see cref="RewardKind.Deferred"/> 把该项排进
    /// <see cref="GameState.DeferredEffects"/>，由 <c>CleanupResolver.ResolveDeferredEffects</c> 在收尾阶段统一执行。
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
            ExecuteSequence(state, seat, ops, entries, choices, extension, anchorNodeId);
            return new EffectReport(seat, entries, false, null);
        }

        /// <summary>
        /// 按序执行一段效果序列，把每条的执行记要追加进 <paramref name="entries"/>
        /// （供「二选一」的分支递归复用）。
        /// </summary>
        private static void ExecuteSequence(GameState state, int seat, IReadOnlyList<RewardOp> ops,
            List<EffectLogEntry> entries, IEffectChoices choices, IEffectExtension extension, int anchorNodeId)
        {
            foreach (RewardOp op in ops)
            {
                entries.Add(ExecuteOne(state, seat, op, choices, extension, anchorNodeId));
            }
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

            bool applied = TryApplyBase(state, seat, op, choices, extension, anchorNodeId, out string note);
            return new EffectLogEntry(op, applied, note);
        }

        /// <summary>执行一条基础规则效果；返回是否成功，失败时给出中文原因。</summary>
        private static bool TryApplyBase(GameState state, int seat, RewardOp op, IEffectChoices choices,
            IEffectExtension extension, int anchorNodeId, out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);

            switch (op.Kind)
            {
                case RewardKind.GainResource:
                    player.Resources.Add(op.Resource, op.Amount);
                    return true;

                case RewardKind.PayResource:
                    if (ActionRules.IgnoresCosts(state)) return true; // 开发者模式：不扣费
                    if (!player.Resources.TrySpend(op.Resource, op.Amount))
                    {
                        note = $"{op.Resource} 不足。";
                        return false;
                    }
                    return true;

                case RewardKind.GainVouchers:
                    player.Vouchers += op.Amount;
                    return true;

                case RewardKind.PayVouchers:
                    if (ActionRules.IgnoresCosts(state)) return true; // 开发者模式：不扣费
                    if (player.Vouchers < op.Amount)
                    {
                        note = "金券不足。";
                        return false;
                    }
                    player.Vouchers -= op.Amount;
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

                case RewardKind.Alternative:
                    return TryAlternative(state, seat, op, choices, extension, anchorNodeId, out note);

                case RewardKind.RemoveInfluence:
                    return TryRemoveInfluence(state, seat, op, choices, out note);

                case RewardKind.RelocateInfluence:
                    return TryRelocateInfluence(state, seat, op, choices, out note);

                case RewardKind.ReplaceInfluence:
                    return TryReplaceInfluence(state, seat, op, choices, out note);

                case RewardKind.GainVouchersPerCount:
                {
                    // 每数到一个计数单位（如「与核心指挥塔十字相邻的设施」）就获得 op.Amount 金券。
                    int count = CityPanelQuery.Count(state, seat, op.Count);
                    int gained = count * op.Amount;
                    player.Vouchers += gained;
                    return true;
                }

                case RewardKind.BuildOnce:
                    return TryBuildOnce(state, seat, op, choices, out note);

                case RewardKind.FreeCityMove:
                    return TryFreeCityMove(state, seat, op, choices, false, out note);

                case RewardKind.FreeCityMoveOwnInfluenced:
                    // 「极境·计谋」：候选再限制为「该资源点上有自己的影响力」。
                    return TryFreeCityMove(state, seat, op, choices, true, out note);

                case RewardKind.ExploreOnce:
                    return TryExploreOnce(state, seat, op, choices, extension, out note);

                case RewardKind.SettleAnotherEntryEffect:
                    return TrySettleAnotherEntryEffect(state, seat, choices, extension, anchorNodeId, out note);

                case RewardKind.PassStartMarker:
                    state.PendingStartMarkerSeat = seat;
                    return true;

                case RewardKind.SellResources:
                    return TrySellResources(state, seat, op, choices, out note);

                case RewardKind.BuildExtensionHub:
                    return TryBuildExtensionHub(state, seat, op, choices, out note);

                case RewardKind.GainSpread:
                    return TryGainSpread(state, seat, op, choices, out note);

                case RewardKind.PlaceInfluenceOnCityMoveRoute:
                    return TryPlaceInfluenceOnCityMoveRoute(state, seat, op, choices, out note);

                case RewardKind.RemoveOwnInfluence:
                    return TryRemoveOwnInfluence(state, seat, op, choices, out note);

                case RewardKind.ChooseLeastHeldResource:
                    return TryChooseLeastHeldResource(state, seat, op, choices, out note);

                case RewardKind.SupplyCardToDeckBottom:
                    return TrySupplyCardToDeckBottom(state, seat, op, choices, out note);

                case RewardKind.OptionalChain:
                    return TryOptionalChain(state, seat, op, choices, extension, anchorNodeId, out note);

                case RewardKind.RecallDiscardPerCard:
                    return TryRecallDiscardPerCard(state, seat, op, choices, extension, anchorNodeId, out note);

                case RewardKind.LevyResourceSale:
                    return TryLevyResourceSale(state, seat, op, choices, out note);

                case RewardKind.PaySpread:
                    return TryPaySpread(state, seat, op, choices, out note);

                case RewardKind.PlaceInfluencePerPatternMarkers:
                    return TryPlaceInfluencePerPatternMarkers(state, seat, op, choices, out note);

                case RewardKind.ExtraMainActions:
                    player.ExtraMainActionsThisTurn += op.Amount;
                    return true;

                case RewardKind.ForbidCharacterCardsThisRound:
                    player.CharacterCardBanRound = state.ActionRound;
                    return true;

                case RewardKind.Deferred:
                    if (op.Ops == null || op.Ops.Count == 0)
                    {
                        note = "延迟结算没有内嵌效果。";
                        return false;
                    }
                    state.DeferredEffects.Add(new DeferredEffect(seat, op.Ops));
                    return true;

                case RewardKind.OpponentsGainResource:
                    foreach (PlayerState other in state.Players)
                    {
                        if (other.SeatIndex == seat) continue;
                        other.Resources.Add(op.Resource, op.Amount);
                    }
                    return true;

                case RewardKind.OpponentsGainVouchers:
                    foreach (PlayerState other in state.Players)
                    {
                        if (other.SeatIndex == seat) continue;
                        other.Vouchers += op.Amount;
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

        /// <summary>
        /// 「合计获得 N 个资源，在若干种类里任意分配」（开采电铲）：让选择器给出各资源的数量，
        /// 再把结果**夹到合计恰好 = <see cref="RewardOp.Amount"/>**（多给的部分按顺序削掉、少给的补给第一种），
        /// 避免选择器给错数时凭空多出 / 少掉资源。
        /// </summary>
        private static bool TryGainSpread(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            IReadOnlyList<ResourceType> options = op.SpreadOptions;
            if (options == null || options.Count == 0) return false;

            PlayerState player = state.Player(seat);

            Dictionary<ResourceType, int> holdings = new Dictionary<ResourceType, int>();
            foreach (ResourceType type in options) holdings[type] = player.Resources.Get(type);

            IReadOnlyDictionary<ResourceType, int> asked =
                choices.ChooseResourceSpread(seat, op, state, op.Amount, options, holdings);

            int remaining = op.Amount;
            var parts = new List<string>();

            foreach (ResourceType type in options)
            {
                if (remaining <= 0) break;

                int want = asked != null && asked.TryGetValue(type, out int value) ? value : 0;
                if (want > remaining) want = remaining;
                if (want <= 0) continue;

                player.Resources.Add(type, want);
                parts.Add($"{type}×{want}");
                remaining -= want;
            }

            // 还有没分出去的余量时全给第一种资源，保证「合计 N 个」这一条成立（§11 尽力执行）。
            if (remaining > 0)
            {
                ResourceType type = options[0];
                player.Resources.Add(type, remaining);
                parts.Add($"{type}×{remaining}");
            }

            note = parts.Count == 0 ? "没有分配任何资源。" : $"合计获得 {string.Join("、", parts)}";
            return parts.Count > 0;
        }

        /// <summary>
        /// 「合计支付 N 个资源，在若干种类里任意分配」（复合动力系统特殊行动）：与 <see cref="TryGainSpread"/> 相对，
        /// 让选择器给出各资源的数量，夹到合计恰好 = <see cref="RewardOp.Amount"/> 后逐种扣除（开发者模式跳过）。
        /// </summary>
        private static bool TryPaySpread(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            IReadOnlyList<ResourceType> options = op.SpreadOptions;
            if (options == null || options.Count == 0) return false;

            if (ActionRules.IgnoresCosts(state))
            {
                note = "开发者模式：不扣费";
                return true;
            }

            PlayerState player = state.Player(seat);

            Dictionary<ResourceType, int> holdings = new Dictionary<ResourceType, int>();
            foreach (ResourceType type in options) holdings[type] = player.Resources.Get(type);

            IReadOnlyDictionary<ResourceType, int> asked =
                choices.ChooseResourceSpread(seat, op, state, op.Amount, options, holdings);

            int remaining = op.Amount;
            var parts = new List<string>();

            foreach (ResourceType type in options)
            {
                if (remaining <= 0) break;

                int want = asked != null && asked.TryGetValue(type, out int value) ? value : 0;
                if (want > remaining) want = remaining;
                if (want <= 0) continue;

                player.Resources.TrySpend(type, want);
                parts.Add($"{type}×{want}");
                remaining -= want;
            }

            // 还有没分出去的余量时按顺序从各种资源里补扣，保证「合计 N 个」这一条成立（§11 尽力执行）。
            if (remaining > 0)
            {
                foreach (ResourceType type in options)
                {
                    if (remaining <= 0) break;
                    int have = player.Resources.Get(type);
                    if (have <= 0) continue;

                    int take = Math.Min(have, remaining);
                    player.Resources.TrySpend(type, take);
                    parts.Add($"{type}×{take}");
                    remaining -= take;
                }
            }

            note = parts.Count == 0 ? "没有支付任何资源。" : $"合计支付 {string.Join("、", parts)}";
            return parts.Count > 0;
        }

        /// <summary>
        /// 「放置 X 个影响力，X = 当前特殊行动所在样式牌上自己的标记数，至多 cap 个」（军工业化区域特殊行动）。
        /// 计数来源是 <c>GameState.ActiveSpecialActionPatternId</c>；落点按部署规则（§6.4a）。
        /// </summary>
        private static bool TryPlaceInfluencePerPatternMarkers(GameState state, int seat, RewardOp op,
            IEffectChoices choices, out string note)
        {
            note = null;
            CityPatternState pattern = state.PatternStateOf(state.ActiveSpecialActionPatternId);
            if (pattern == null)
            {
                note = "当前不在任何城市样式牌的特殊行动结算中。";
                return false;
            }

            int count = pattern.CountOf(seat);
            if (count > op.Amount) count = op.Amount; // 至多 cap 个
            if (count <= 0)
            {
                note = "这张样式牌上没有自己的玩家标记，无法放置影响力。";
                return false;
            }

            RewardOp derived = RewardOp.Influence(InfluencePlacementScope.DeployRules, count);
            return TryPlaceInfluence(state, seat, derived, choices, -1, out note);
        }

        /// <summary>
        /// 「在经过的航道上放置 1 个影响力」（高性能动力设施）：城市移动只能移到相邻资源点、只经过一条航道，
        /// 故落点候选就是 <c>GameState.LastCityMoveRouteId</c> 那条航道上的空格。
        /// </summary>
        private static bool TryPlaceInfluenceOnCityMoveRoute(GameState state, int seat, RewardOp op,
            IEffectChoices choices, out string note)
        {
            note = null;
            int routeId = state.LastCityMoveRouteId;
            if (routeId < 0 || routeId >= state.Map.Routes.Count)
            {
                note = "还没有可放置影响力的城市移动航道。";
                return false;
            }

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

                var candidates = new List<InfluenceSlotRef>();
                AddRouteSlots(state, seat, routeId, candidates);
                if (candidates.Count == 0)
                {
                    failReason = "这条航道上没有可放置的影响力空格。";
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
            return placed > 0;
        }

        /// <summary>
        /// 二选一（§11.4）：由选择器挑一个分支，该分支的整段效果按序执行（分支内某条做不了不阻断其余条目）。
        /// 没有分支、或选择器放弃时，这条效果被跳过（不阻断后续效果）。
        /// </summary>
        private static bool TryAlternative(GameState state, int seat, RewardOp op, IEffectChoices choices,
            IEffectExtension extension, int anchorNodeId, out string note)
        {
            note = null;
            if (op.Branches == null || op.Branches.Count == 0)
            {
                note = "二选一没有分支。";
                return false;
            }

            int index = choices.ChooseBranch(seat, op, state, op.Branches);
            if (index < 0 || index >= op.Branches.Count)
            {
                note = "没有选择二选一的分支。";
                return false;
            }

            var entries = new List<EffectLogEntry>();
            ExecuteSequence(state, seat, op.Branches[index].Ops, entries, choices, extension, anchorNodeId);

            foreach (EffectLogEntry entry in entries)
            {
                if (entry.Applied) return true;
            }

            note = $"「{op.Branches[index]}」这一分支没有可执行的操作。";
            return false;
        }

        /// <summary>拿走场上 1 个影响力（可以是别人的）：清空槽位，并把标记退回**其所有者**的供应堆。</summary>
        private static bool TryRemoveInfluence(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            int removed = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                List<InfluenceSlotRef> candidates = OccupiedInfluenceCandidates(state);
                if (candidates.Count == 0)
                {
                    failReason = "场上没有可拿走的影响力。";
                    break;
                }

                InfluenceSlotRef? target = choices.ChooseInfluenceToRemove(seat, op, state, candidates);
                if (target == null)
                {
                    failReason = "放弃拿走影响力。";
                    break;
                }

                if (!candidates.Contains(target.Value))
                {
                    failReason = "选择的影响力格不在合法范围内。";
                    break;
                }

                TakeInfluence(state, target.Value);
                removed++;
            }

            if (failReason == null) return true;

            note = removed == 0 ? failReason : $"仅拿走了 {removed} 个影响力（{failReason}）";
            return removed > 0;
        }

        /// <summary>移动 1 个自己的影响力到别的空格：不增减影响力总数，所以不动玩家标记（§6.4b）。</summary>
        private static bool TryRelocateInfluence(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            int moved = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                List<InfluenceSlotRef> sources = OwnInfluenceCandidates(state, seat);
                if (sources.Count == 0)
                {
                    failReason = "你在场上没有影响力可以移动。";
                    break;
                }

                // 目标格要满足与「部署」相同的条件（§6.4b）。
                List<InfluenceSlotRef> destinations =
                    InfluenceCandidates(state, seat, InfluencePlacementScope.DeployRules, -1);
                if (destinations.Count == 0)
                {
                    failReason = "没有可放置影响力的空格。";
                    break;
                }

                InfluenceMove? move = choices.ChooseOwnInfluenceMove(seat, op, state, sources, destinations);
                if (move == null)
                {
                    failReason = "放弃移动影响力。";
                    break;
                }

                if (!sources.Contains(move.Value.From) || !destinations.Contains(move.Value.To))
                {
                    failReason = "选择的起点或终点不在合法范围内。";
                    break;
                }

                state.Map.SlotsOf(move.Value.From).Clear(move.Value.From.SlotIndex);
                state.Map.SlotsOf(move.Value.To).Place(move.Value.To.SlotIndex, seat);
                moved++;
            }

            if (failReason == null) return true;

            note = moved == 0 ? failReason : $"仅移动了 {moved} 个影响力（{failReason}）";
            return moved > 0;
        }

        /// <summary>
        /// 替换 1 个影响力：移除一个影响力（可以是别人的），随后**在同一个空格**放置一个自己的影响力。
        /// 该空格若已不能放置（例如该资源点停着对手的城市），则只移除、不放置（见 `Doc/CardEffects.md` §2）。
        /// </summary>
        private static bool TryReplaceInfluence(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);

            if (player.MarkersInSupply <= 0)
            {
                note = "玩家供应堆里没有标记，无法替换影响力（§11.3）。";
                return false;
            }

            int done = 0;      // 完整替换（移除 + 放置）的次数
            int partial = 0;   // 只移除了、没能放置的次数
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                if (player.MarkersInSupply <= 0)
                {
                    failReason = "玩家供应堆里没有标记了。";
                    break;
                }

                List<InfluenceSlotRef> candidates = OccupiedInfluenceCandidates(state);
                if (candidates.Count == 0)
                {
                    failReason = "场上没有可替换的影响力。";
                    break;
                }

                InfluenceSlotRef? target = choices.ChooseInfluenceToRemove(seat, op, state, candidates);
                if (target == null)
                {
                    failReason = "放弃替换影响力。";
                    break;
                }

                if (!candidates.Contains(target.Value))
                {
                    failReason = "选择的影响力格不在合法范围内。";
                    break;
                }

                InfluenceSlotRef slot = target.Value;
                TakeInfluence(state, slot);

                CommandResult place = ActionRules.CanPlaceInfluence(state, seat, slot);
                if (place.Ok)
                {
                    player.MarkersInSupply--;
                    state.Map.SlotsOf(slot).Place(slot.SlotIndex, seat);
                    done++;
                    continue;
                }

                partial++;
                failReason = $"已移除 {slot} 上的影响力，但无法在同一空格放置自己的影响力（{place.Error}）";
                break;
            }

            if (failReason == null) return true;

            note = failReason;
            return done + partial > 0; // 移除已经生效，算「尽量执行」的成功
        }

        /// <summary>移除一个影响力，并把该标记退回其所有者的供应堆。</summary>
        private static void TakeInfluence(GameState state, InfluenceSlotRef slot)
        {
            int owner = state.Map.OccupantOf(slot);
            if (owner < 0) return;

            state.Map.SlotsOf(slot).Clear(slot.SlotIndex);
            if (owner < state.Players.Count) state.Player(owner).MarkersInSupply++;
        }

        // —— 行动类效果（建设 / 城市移动 / 探索 / 结算他牌 / 售卖 / 延伸枢纽）——
        // 「尽量执行」：做不了就跳过并记原因，不阻断后续效果（§11.1.2）。

        /// <summary>「进行一次建造」（简陋工程营）：挑一张供应区设施牌与面板空位，照建设流程执行（照牌面费用付费）。</summary>
        private static bool TryBuildOnce(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);
            int done = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                if (!player.HasFreePanelSlot)
                {
                    failReason = "城市面板已经放满，没有空位可以建设。";
                    break;
                }
                if (state.FacilitySupply.Count == 0)
                {
                    failReason = "设施供应区没有可建设的牌。";
                    break;
                }

                var facilityIds = new List<int>(state.FacilitySupply);
                var panelSlots = new List<int>();
                for (int s = 0; s < PlayerState.CityPanelSlotCount; s++)
                {
                    if (s == PlayerState.CityPanelCoreSlot) continue;
                    if (player.CityPanelSlots[s] < 0) panelSlots.Add(s);
                }

                BuildChoice? pick = choices.ChooseBuild(seat, op, state, facilityIds, panelSlots);
                if (pick == null)
                {
                    failReason = "放弃这次建造。";
                    break;
                }

                // 直接复用建设命令：校验 / 扣费 / 上板 / 结算入场效果一条龙（含「唯一」等关键词）。
                // 用同一个 choices：嵌套建设的入场效果若也要点选，照样回到同一个会话上。
                var command = new BuildFacilityCommand(seat, pick.Value.FacilityId, pick.Value.PanelSlot,
                    pick.Value.Payment, choices);
                CommandResult check = command.Validate(state);
                if (!check.Ok)
                {
                    failReason = $"这次建造不可执行：{check.Error}";
                    break;
                }

                command.Apply(state);
                done++;
            }

            if (failReason == null) return true;

            note = done == 0 ? failReason : $"完成了 {done} 次建造（{failReason}）";
            return done > 0;
        }

        /// <summary>
        /// 「进行一次免费的城市移动」（高性能动力设施 / 极境·计谋导航通讯）：不付 3 源石，其余照 §6.4d ——
        /// 移到相邻且已开放、无对手城市的资源点，清掉所用航道与目标点上对手的影响力，
        /// 并在**出发点**放置 1 个自己的影响力。
        /// <paramref name="requireOwnInfluence"/> = true 时，候选再限制为「该资源点上有自己的影响力」（极境·计谋）。
        /// </summary>
        private static bool TryFreeCityMove(GameState state, int seat, RewardOp op, IEffectChoices choices,
            bool requireOwnInfluence, out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);
            if (player.MobileCityNodeId < 0)
            {
                note = "移动城市还没有停靠在任何资源点上。";
                return false;
            }

            int moved = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                int current = player.MobileCityNodeId;
                List<int> candidates = OpenAdjacentNodes(state, seat, current);
                if (requireOwnInfluence) candidates = NodesWithOwnInfluence(state, seat, candidates);
                if (candidates.Count == 0)
                {
                    failReason = requireOwnInfluence
                        ? "没有「相邻且有自己的影响力」的资源点可供城市移动。"
                        : "没有可供城市移动的相邻资源点。";
                    break;
                }

                int target = choices.ChooseNode(seat, op, state, candidates);
                if (target < 0 || !candidates.Contains(target))
                {
                    failReason = "放弃城市移动。";
                    break;
                }

                RouteState route = state.Map.FindRoute(current, target);
                ActionRules.ClearRouteInfluencesOfOthers(state, route.Id, seat);
                ActionRules.ClearNodeInfluencesOfOthers(state, target, seat);
                player.MobileCityNodeId = target;
                state.LastCityMoveRouteId = route.Id; // 供「在经过的航道上放置影响力」取落点候选

                PlaceInfluenceAtNode(state, seat, current); // §6.4d 步 3：在出发点放置 1 个影响力
                moved++;
            }

            if (failReason == null) return true;

            note = moved == 0 ? failReason : $"完成了 {moved} 次城市移动（{failReason}）";
            return moved > 0;
        }

        /// <summary>
        /// 「执行 1 次探索」（载具仓库的分支）：按 §6.4c 三步走完 —— 付路费 → 翻事件牌、自动取第一个
        /// 选项结算 → 在目标点放置 1 个影响力。事件选项本身的选择仍走 <paramref name="choices"/>。
        /// </summary>
        private static bool TryExploreOnce(GameState state, int seat, RewardOp op, IEffectChoices choices,
            IEffectExtension extension, out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);
            int done = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                List<int> candidates = ExplorableNodes(state, seat);
                if (candidates.Count == 0)
                {
                    failReason = "没有可探索的资源点（无指示物、已开放、且事件牌堆未空）。";
                    break;
                }

                int nodeId = choices.ChooseNode(seat, op, state, candidates);
                if (nodeId < 0 || !candidates.Contains(nodeId))
                {
                    failReason = "放弃这次探索。";
                    break;
                }

                MapNodeState node = state.Map.Node(nodeId);

                // 步 1：支付路费（从移动城市所在地出发，走最省路线）。
                IReadOnlyList<int> path =
                    TollCalculator.FindCheapestPath(state, seat, player.MobileCityNodeId, nodeId);
                if (path == null || path.Count == 0)
                {
                    failReason = "从移动城市所在地到目标点没有通路。";
                    break;
                }

                if (!ActionRules.IgnoresCosts(state))
                {
                    TollCalculator.Toll toll = TollCalculator.Compute(state, seat, path, null);
                    if (player.Vouchers < toll.Total)
                    {
                        failReason = $"路费需要 {toll.Total} 金券。";
                        break;
                    }
                    foreach (TollCalculator.Payment payment in toll.Payments)
                    {
                        player.Vouchers -= payment.Amount;
                        if (payment.PayeeSeat != TollCalculator.SupplyPayee)
                            state.Player(payment.PayeeSeat).Vouchers += payment.Amount;
                    }
                }

                // 步 2：翻事件牌、放指示物，并自动结算第一个选项（UI 接入后由玩家点选）。
                List<int> deck = ExploreCommand.DeckOf(state, node.Color);
                int cardId = deck[0];
                deck.RemoveAt(0);
                EventCardDef card = state.Catalog.Event(cardId);

                node.HasResourceToken = true;
                node.TokenResource = card.TokenResource;
                node.TokenAmount = card.TokenAmount;
                node.EventCardId = cardId;

                if (card.Options.Count > 0)
                    ExecuteOption(state, seat, card.Options[0], true, choices, extension, nodeId);
                node.EventResolved = true;

                // 步 3：在目标资源点放置 1 个影响力。
                PlaceInfluenceAtNode(state, seat, nodeId);
                node.ExplorePending = false;
                done++;
            }

            if (failReason == null) return true;

            note = done == 0 ? failReason : $"完成了 {done} 次探索（{failReason}）";
            return done > 0;
        }

        /// <summary>
        /// 「结算另一张设施的入场效果」（附属能源设施）：在自己面板上取一个**十字相邻的非彩色**设施，
        /// 执行它的入场效果（§11）。候选只保留真的带有入场效果的设施。
        /// </summary>
        private static bool TrySettleAnotherEntryEffect(GameState state, int seat, IEffectChoices choices,
            IEffectExtension extension, int anchorNodeId, out string note)
        {
            note = null;
            List<int> slots = AdjacentNonColorFacilitySlots(state, seat);
            if (slots.Count == 0)
            {
                note = "面板上没有可供结算入场效果的十字相邻非彩色设施。";
                return false;
            }

            int slot = choices.ChoosePanelFacility(seat, default, state, slots);
            if (slot < 0 || !slots.Contains(slot))
            {
                note = "放弃结算他牌入场效果。";
                return false;
            }

            int facilityId = state.Player(seat).CityPanelSlots[slot];
            FacilityCardDef card = state.Catalog.Facility(facilityId);

            var entries = new List<EffectLogEntry>();
            ExecuteSequence(state, seat, card.EntryEffects, entries, choices, extension, anchorNodeId);

            foreach (EffectLogEntry entry in entries)
            {
                if (entry.Applied) return true;
            }

            note = $"被结算的《{card.Name}》没有可执行的入场效果。";
            return false;
        }

        /// <summary>
        /// 「按单价售卖资源」（贸易街区）：由选择器给出各资源的出售数量，按 <see cref="SellPriceOf"/> 换成金券。
        /// 卖多少完全由玩家决定；不卖则这条效果跳过（不阻断后续效果）。
        /// </summary>
        private static bool TrySellResources(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);

            var prices = new Dictionary<ResourceType, int>();
            var holdings = new Dictionary<ResourceType, int>();
            foreach (ResourceType type in ResourceTypes.All)
            {
                int price = SellPriceOf(type);
                if (price <= 0) continue;
                prices[type] = price;
                holdings[type] = player.Resources.Get(type);
            }

            IReadOnlyDictionary<ResourceType, int> amounts =
                choices.ChooseSellAmounts(seat, op, state, prices, holdings);
            if (amounts == null || amounts.Count == 0)
            {
                note = "没有选择出售任何资源。";
                return false;
            }

            int vouchers = 0;
            foreach (KeyValuePair<ResourceType, int> entry in amounts)
            {
                if (entry.Value <= 0) continue;
                if (!prices.TryGetValue(entry.Key, out int price)) continue;

                int count = Math.Min(entry.Value, player.Resources.Get(entry.Key));
                if (count <= 0) continue;

                player.Resources.TrySpend(entry.Key, count);
                vouchers += count * price;
            }

            if (vouchers == 0)
            {
                note = "没有真正卖出任何资源。";
                return false;
            }

            player.Vouchers += vouchers;
            return true;
        }

        /// <summary>
        /// 「从余下的延伸枢纽中选 1 张并建设」（物流枢纽）：从 <see cref="GameState.ExtensionHubSupply"/>
        /// 里挑一张，直接放到自己面板的空格上并结算牌面分数（延伸枢纽牌面分数 -1、无费用、先到先得）。
        /// </summary>
        private static bool TryBuildExtensionHub(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);
            int done = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                if (!player.HasFreePanelSlot)
                {
                    failReason = "城市面板已经放满，无法建设延伸枢纽。";
                    break;
                }
                if (state.ExtensionHubSupply.Count == 0)
                {
                    failReason = "延伸枢纽已经全部被建走，没有余下的可选。";
                    break;
                }

                var candidates = new List<int>(state.ExtensionHubSupply);
                int hubId = choices.ChooseExtensionHub(seat, op, state, candidates);
                if (hubId < 0 || !candidates.Contains(hubId))
                {
                    failReason = "放弃建设延伸枢纽。";
                    break;
                }

                int slot = FirstFreeBuildSlot(player);
                if (slot < 0)
                {
                    failReason = "没有可用的面板空位。";
                    break;
                }

                FacilityCardDef card = state.Catalog.Facility(hubId);
                state.ExtensionHubSupply.Remove(hubId);
                player.CityPanelSlots[slot] = hubId;
                player.BuiltFacilities.Add(hubId);
                player.Score += card.Score;
                done++;
            }

            if (failReason == null) return true;

            note = done == 0 ? failReason : $"完成了 {done} 次延伸枢纽建设（{failReason}）";
            return done > 0;
        }

        /// <summary>「贸易街区」的售卖单价（金券）：源岩 3 / 源石 3 / 异铁 4 / 至纯源石 15。UI 报价时可复用。</summary>
        public static int SellPriceOf(ResourceType type) => type switch
        {
            ResourceType.Orirock => 3,
            ResourceType.Originium => 3,
            ResourceType.Oriron => 4,
            ResourceType.OriginitePrime => 15,
            _ => 0,
        };

        /// <summary>与 <paramref name="origin"/> 相邻（仅一条航道）、已开放且无对手城市的资源点。</summary>
        private static List<int> OpenAdjacentNodes(GameState state, int seat, int origin)
        {
            var list = new List<int>();
            foreach (int other in state.Map.NeighboursOf(origin))
            {
                if (list.Contains(other)) continue;

                MapNodeState node = state.Map.Node(other);
                if (!ActionRules.IsNodeOpen(state, node)) continue;
                if (ActionRules.HasOpponentCity(state, other, seat)) continue;
                list.Add(other);
            }
            return list;
        }

        /// <summary>从一份资源点候选里挑出「该点上至少有自己的 1 个影响力」的那些（极境·计谋）。</summary>
        private static List<int> NodesWithOwnInfluence(GameState state, int seat, IReadOnlyList<int> nodes)
        {
            var list = new List<int>();
            foreach (int id in nodes)
            {
                if (HasOwnInfluenceAtNode(state, seat, id)) list.Add(id);
            }
            return list;
        }

        /// <summary>该资源点的槽位里是否有属于 <paramref name="seat"/> 的影响力。</summary>
        private static bool HasOwnInfluenceAtNode(GameState state, int seat, int nodeId)
        {
            InfluenceSlots slots = state.Map.Node(nodeId).Slots;
            for (int i = 0; i < slots.Capacity; i++)
            {
                if (slots.OwnerOf(i) == seat) return true;
            }
            return false;
        }

        // —— 角色牌效果（RuleSpec §11；来源 Doc/CardEffects.md §3）——

        /// <summary>
        /// 「雷蛇·策略：安保协议」收尾阶段的延迟效果：移除**自己的** 1 个影响力，标记退回自己的供应堆。
        /// 候选只含自己在场上的影响力格。
        /// </summary>
        private static bool TryRemoveOwnInfluence(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            int removed = 0;
            string failReason = null;

            for (int i = 0; i < op.Amount; i++)
            {
                List<InfluenceSlotRef> candidates = OwnInfluenceCandidates(state, seat);
                if (candidates.Count == 0)
                {
                    failReason = "你在场上没有影响力可以移除。";
                    break;
                }

                InfluenceSlotRef? target = choices.ChooseInfluenceToRemove(seat, op, state, candidates);
                if (target == null)
                {
                    failReason = "放弃移除影响力。";
                    break;
                }

                if (!candidates.Contains(target.Value))
                {
                    failReason = "选择的影响力格不在合法范围内。";
                    break;
                }

                TakeInfluence(state, target.Value);
                removed++;
            }

            if (failReason == null) return true;

            note = removed == 0 ? failReason : $"仅移除了 {removed} 个影响力（{failReason}）";
            return removed > 0;
        }

        /// <summary>
        /// 「极境·策略：后勤调配」：在给定基础资源里选**自己持有量最少**的一种（并列时由玩家自选），
        /// 获得 <see cref="RewardOp.Amount"/> 个。
        /// </summary>
        private static bool TryChooseLeastHeldResource(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            IReadOnlyList<ResourceType> options = op.SpreadOptions;
            if (options == null || options.Count == 0)
            {
                note = "没有给出可选的资源种类。";
                return false;
            }

            PlayerState player = state.Player(seat);
            int min = int.MaxValue;
            foreach (ResourceType type in options)
            {
                int held = player.Resources.Get(type);
                if (held < min) min = held;
            }

            var candidates = new List<ResourceType>();
            foreach (ResourceType type in options)
            {
                if (player.Resources.Get(type) == min) candidates.Add(type);
            }

            int pick = choices.ChooseResourceType(seat, op, state, candidates);
            if (pick < 0 || pick >= candidates.Count)
            {
                note = "没有选择要获得的资源种类。";
                return false;
            }

            ResourceType chosen = candidates[pick];
            player.Resources.Add(chosen, op.Amount);
            note = $"获得 {chosen}×{op.Amount}（持有量最少的一种）";
            return true;
        }

        /// <summary>
        /// 「德克萨斯·策略：特别递送」：把设施供应区的 1 张牌放回设施牌堆**底部**（列表末尾），
        /// 之后按补充规则（§6.4e）从牌堆补牌至供应区 6 张。牌堆为空时这张牌成为唯一牌并被立刻翻回供应区。
        /// </summary>
        private static bool TrySupplyCardToDeckBottom(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            if (state.FacilitySupply.Count == 0)
            {
                note = "设施供应区没有可放回的牌。";
                return false;
            }

            var candidates = new List<int>(state.FacilitySupply);
            int cardId = choices.ChooseSupplyFacility(seat, op, state, candidates);
            if (cardId < 0 || !candidates.Contains(cardId))
            {
                note = "放弃把设施牌放回牌堆底部。";
                return false;
            }

            state.FacilitySupply.Remove(cardId);
            state.FacilityDeck.Add(cardId); // 牌堆底部（DrawTop 取 deck[0]，故末尾即底部）
            BoardSetup.RefillFacilitySupply(state);
            return true;
        }

        /// <summary>
        /// 「锡人·策略：建立威信」的可选链：逐段问「执行 / 终止」，任一段终止则其后全部跳过。
        /// 与二选一不同 —— 这里每段都是可选的，不是必选其一。
        /// </summary>
        private static bool TryOptionalChain(GameState state, int seat, RewardOp op, IEffectChoices choices,
            IEffectExtension extension, int anchorNodeId, out string note)
        {
            note = null;
            if (op.Branches == null || op.Branches.Count == 0)
            {
                note = "可选链没有步骤。";
                return false;
            }

            int executed = 0;
            for (int i = 0; i < op.Branches.Count; i++)
            {
                if (choices.ChooseOptionalStep(seat, op, state, i, op.Branches[i].Label) != 0) break;

                var entries = new List<EffectLogEntry>();
                ExecuteSequence(state, seat, op.Branches[i].Ops, entries, choices, extension, anchorNodeId);
                executed++;
            }

            if (executed == 0)
            {
                note = "没有执行任何可选步骤。";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 「锡人·计谋：人员召集」：把角色牌弃牌区的牌**全部**收回手牌；每收回 1 张，
        /// 在 <see cref="RewardOp.Branches"/> 的两项之间二选一执行。本牌此时还没进弃牌区，故不计入。
        /// </summary>
        private static bool TryRecallDiscardPerCard(GameState state, int seat, RewardOp op, IEffectChoices choices,
            IEffectExtension extension, int anchorNodeId, out string note)
        {
            note = null;
            PlayerState player = state.Player(seat);
            if (player.DiscardCharacterCards.Count == 0)
            {
                note = "角色牌弃牌区此时没有牌可以收回（本牌尚未进入弃牌区）。";
                return false;
            }

            var recalled = new List<int>(player.DiscardCharacterCards);
            player.DiscardCharacterCards.Clear();
            player.HandCharacterCards.AddRange(recalled);

            if (op.Branches != null && op.Branches.Count > 0)
            {
                foreach (int cardId in recalled)
                {
                    int index = choices.ChooseBranch(seat, op, state, op.Branches);
                    if (index < 0 || index >= op.Branches.Count) continue;

                    var entries = new List<EffectLogEntry>();
                    ExecuteSequence(state, seat, op.Branches[index].Ops, entries, choices, extension, anchorNodeId);
                }
            }

            note = $"收回了 {recalled.Count} 张角色牌。";
            return true;
        }

        /// <summary>
        /// 「坎诺特·计谋：征收物资」：出牌者从三种基础资源中选 1 种；所有玩家（含出牌者）把该种资源
        /// 按 <see cref="RewardOp.Amount"/> 金券 / 个卖光，所得金券归各自（出牌者的 +1 分由牌面另用 GainScore 表达）。
        /// </summary>
        private static bool TryLevyResourceSale(GameState state, int seat, RewardOp op, IEffectChoices choices,
            out string note)
        {
            note = null;
            var options = new List<ResourceType>
            {
                ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron,
            };

            int pick = choices.ChooseResourceType(seat, op, state, options);
            if (pick < 0 || pick >= options.Count)
            {
                note = "没有选择要征收的资源种类。";
                return false;
            }

            ResourceType type = options[pick];
            bool free = ActionRules.IgnoresCosts(state); // 开发者模式：不扣资源
            int price = op.Amount;
            int total = 0;

            foreach (PlayerState other in state.Players)
            {
                int count = other.Resources.Get(type);
                if (count <= 0) continue;

                if (!free) other.Resources.TrySpend(type, count);
                other.Vouchers += count * price;
                total += count;
            }

            if (total == 0)
            {
                note = "所有玩家都没有这种资源。";
                return false;
            }

            note = $"{type} 共卖出 {total} 个。";
            return true;
        }

        /// <summary>当前可被探索的资源点：无指示物、已开放、无对手城市、事件牌堆未空、有空位、从移动城市有通路。</summary>
        private static List<int> ExplorableNodes(GameState state, int seat)
        {
            var list = new List<int>();
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
                if (path == null || path.Count == 0) continue;

                list.Add(id);
            }
            return list;
        }

        /// <summary>面板上**与核心指挥塔十字相邻**、非彩色、且带有入场效果的设施所占的格子（附属能源设施的候选）。</summary>
        private static List<int> AdjacentNonColorFacilitySlots(GameState state, int seat)
        {
            var list = new List<int>();
            PlayerState player = state.Player(seat);

            foreach (int slot in CityPanelQuery.CoreNeighbors)
            {
                int cardId = CityPanelQuery.CardAt(player, slot);
                if (cardId < 0) continue;

                FacilityCardDef card = state.Catalog.Facility(cardId);
                if (card == null || card.Color == FacilityColor.Rainbow) continue;
                if (card.EntryEffects.Count == 0) continue;

                list.Add(slot);
            }
            return list;
        }

        /// <summary>玩家面板上第一个可建设的空位（跳过核心指挥塔那一格）；没有则 -1。</summary>
        private static int FirstFreeBuildSlot(PlayerState player)
        {
            for (int s = 0; s < PlayerState.CityPanelSlotCount; s++)
            {
                if (s == PlayerState.CityPanelCoreSlot) continue;
                if (player.CityPanelSlots[s] < 0) return s;
            }
            return -1;
        }

        /// <summary>在资源点的第一个合法空槽放置 1 个自己的影响力（标记不足或没有空槽则不放置）。返回是否放下。</summary>
        private static bool PlaceInfluenceAtNode(GameState state, int seat, int nodeId)
        {
            PlayerState player = state.Player(seat);
            if (player.MarkersInSupply <= 0) return false;

            MapNodeState node = state.Map.Node(nodeId);
            int slot = ActionRules.FirstFreeSlot(node.Slots);
            if (slot < 0) return false;

            var slotRef = InfluenceSlotRef.OnNode(nodeId, slot);
            if (!ActionRules.CanPlaceInfluence(state, seat, slotRef).Ok) return false;

            node.Slots.Place(slot, seat);
            player.MarkersInSupply--;
            return true;
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

        /// <summary>场上所有**非空**的影响力格，不分归属（「拿走 / 替换影响力」的候选）。</summary>
        private static List<InfluenceSlotRef> OccupiedInfluenceCandidates(GameState state)
        {
            var list = new List<InfluenceSlotRef>();
            MapState map = state.Map;

            for (int id = 0; id < map.Nodes.Count; id++)
            {
                InfluenceSlots slots = map.Node(id).Slots;
                for (int i = 0; i < slots.Capacity; i++)
                {
                    if (!slots.IsEmpty(i)) list.Add(InfluenceSlotRef.OnNode(id, i));
                }
            }

            for (int id = 0; id < map.Routes.Count; id++)
            {
                InfluenceSlots slots = map.Route(id).Slots;
                for (int i = 0; i < slots.Capacity; i++)
                {
                    if (!slots.IsEmpty(i)) list.Add(InfluenceSlotRef.OnRoute(id, i));
                }
            }

            return list;
        }

        /// <summary>场上所有属于 <paramref name="seat"/> 的影响力格（「移动自己的影响力」的候选源）。</summary>
        private static List<InfluenceSlotRef> OwnInfluenceCandidates(GameState state, int seat)
        {
            var list = new List<InfluenceSlotRef>();
            MapState map = state.Map;

            for (int id = 0; id < map.Nodes.Count; id++)
            {
                InfluenceSlots slots = map.Node(id).Slots;
                for (int i = 0; i < slots.Capacity; i++)
                {
                    if (slots.OwnerOf(i) == seat) list.Add(InfluenceSlotRef.OnNode(id, i));
                }
            }

            for (int id = 0; id < map.Routes.Count; id++)
            {
                InfluenceSlots slots = map.Route(id).Slots;
                for (int i = 0; i < slots.Capacity; i++)
                {
                    if (slots.OwnerOf(i) == seat) list.Add(InfluenceSlotRef.OnRoute(id, i));
                }
            }

            return list;
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

                    case RewardKind.RemoveInfluence:
                        if (OccupiedInfluenceCandidates(state).Count == 0)
                        {
                            reason = "场上没有可拿走的影响力";
                            return false;
                        }
                        break;

                    case RewardKind.RelocateInfluence:
                        if (OwnInfluenceCandidates(state, seat).Count == 0)
                        {
                            reason = "你在场上没有影响力可移动";
                            return false;
                        }
                        if (InfluenceCandidates(state, seat, InfluencePlacementScope.DeployRules, -1).Count == 0)
                        {
                            reason = "没有可放置影响力的空格";
                            return false;
                        }
                        break;

                    case RewardKind.ReplaceInfluence:
                        if (markers < op.Amount)
                        {
                            reason = "玩家标记不足";
                            return false;
                        }
                        if (OccupiedInfluenceCandidates(state).Count == 0)
                        {
                            reason = "场上没有可替换的影响力";
                            return false;
                        }
                        markers -= op.Amount;
                        break;

                    case RewardKind.PaySpread:
                    {
                        // 合计支付 op.Amount 个，在 SpreadOptions 之间任意分配；按顺序贪心试扣，凑不够即不可行。
                        int remaining = op.Amount;
                        if (op.SpreadOptions != null)
                        {
                            foreach (ResourceType type in op.SpreadOptions)
                            {
                                if (remaining <= 0) break;
                                int available = pool.Get(type);
                                if (available <= 0) continue;
                                int take = Math.Min(available, remaining);
                                pool.Set(type, available - take);
                                remaining -= take;
                            }
                        }
                        if (remaining > 0)
                        {
                            reason = "可分配资源不足";
                            return false;
                        }
                        break;
                    }

                    case RewardKind.Alternative:
                        if (!AlternativeFeasible(state, seat, op, anchorNodeId, extension, out string branchReason))
                        {
                            reason = branchReason;
                            return false;
                        }
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

        /// <summary>
        /// 对外暴露的费用可行性检查（只读试算，不改 state）：供命令的 <c>Validate</c> 复用，
        /// 与条件段走同一套 <see cref="CostFeasible"/> 逻辑（含 <see cref="RewardKind.PaySpread"/> 的自由分配）。
        /// </summary>
        public static bool CanPay(GameState state, int seat, IReadOnlyList<RewardOp> cost, out string reason) =>
            CostFeasible(state, seat, cost, -1, null, out reason);

        /// <summary>
        /// 二选一的分支可行性：只要**任一分支**能完整执行，这条效果就可执行（§11.4 玩家必须择一执行）。
        /// </summary>
        private static bool AlternativeFeasible(GameState state, int seat, RewardOp op, int anchorNodeId,
            IEffectExtension extension, out string reason)
        {
            reason = null;
            if (op.Branches == null || op.Branches.Count == 0)
            {
                reason = "二选一没有分支";
                return false;
            }

            foreach (EffectBranch branch in op.Branches)
            {
                if (CostFeasible(state, seat, branch.Ops, anchorNodeId, extension, out _)) return true;
            }

            reason = "二选一的所有分支都无法执行";
            return false;
        }
    }
}
