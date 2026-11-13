using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;

namespace NomadCity.Core.Engine
{
    /// <summary>一次「需要玩家点选」里的一个候选项：<see cref="Index"/> 是回填给引擎的下标，<see cref="Label"/> 是给玩家看的文案。</summary>
    public sealed class ChoiceOption
    {
        public readonly int Index;
        public readonly string Label;

        public ChoiceOption(int index, string label)
        {
            Index = index;
            Label = label ?? string.Empty;
        }

        public override string ToString() => Label;
    }

    /// <summary>这次选择要在「哪里」选：用来判断该走弹窗列表还是直接点地图（同源交互）。</summary>
    public enum ChoicePickKind
    {
        /// <summary>抽象选项（二选一、给几个资源、售卖数量……），只能弹窗。</summary>
        None = 0,

        /// <summary>点地图上的影响力空格（部署 / 移动 / 拿走 / 替换），与「部署」「调度」主要行动同源。</summary>
        InfluenceSlot = 1,

        /// <summary>点地图上的资源点本体（城市移动 / 探索目标），与「城市移动」「探索」主要行动同源。</summary>
        Node = 2,
    }

    /// <summary>
    /// 「点地图」类选择携带的候选清单：UI 不必再列一长串按钮，而是照主要行动的交互直接点地图。
    /// <see cref="ChoicePickKind.None"/> 的选择没有这个对象。
    /// 候选项的顺序与弹窗选项的下标一一对应，所以点中第几个候选就是答案几。
    /// </summary>
    public sealed class ChoicePick
    {
        public readonly ChoicePickKind Kind;
        public readonly IReadOnlyList<InfluenceSlotRef> Slots;
        public readonly IReadOnlyList<int> NodeIds;

        private ChoicePick(ChoicePickKind kind, IReadOnlyList<InfluenceSlotRef> slots, IReadOnlyList<int> nodes)
        {
            Kind = kind;
            Slots = slots ?? Array.Empty<InfluenceSlotRef>();
            NodeIds = nodes ?? Array.Empty<int>();
        }

        public static ChoicePick ForSlots(IReadOnlyList<InfluenceSlotRef> slots) =>
            new ChoicePick(ChoicePickKind.InfluenceSlot, slots, null);

        public static ChoicePick ForNodes(IReadOnlyList<int> nodeIds) =>
            new ChoicePick(ChoicePickKind.Node, null, nodeIds);

        /// <summary>候选里的第 index 个空格；越界返回 null。</summary>
        public InfluenceSlotRef? SlotAt(int index) =>
            index >= 0 && index < Slots.Count ? Slots[index] : (InfluenceSlotRef?)null;

        /// <summary>候选里的第 index 个资源点 id；越界返回 -1。</summary>
        public int NodeAt(int index) => index >= 0 && index < NodeIds.Count ? NodeIds[index] : -1;
    }

    /// <summary>
    /// 效果执行途中暂停下来、等玩家回答的一次选择：提示文案 + 候选项列表。
    /// <see cref="AllowCancel"/> = false 表示这一步必须选一个（例如牌面「二选一」）。
    /// <see cref="Pick"/> 非 null 时，UI 应改走「点地图」的交互（主要行动同源）。
    /// </summary>
    public sealed class ChoiceRequest
    {
        public readonly string Prompt;
        public readonly IReadOnlyList<ChoiceOption> Options;
        public readonly bool AllowCancel;
        public readonly ChoicePick Pick;

        public ChoiceRequest(string prompt, IReadOnlyList<ChoiceOption> options, bool allowCancel = true,
            ChoicePick pick = null)
        {
            Prompt = prompt ?? string.Empty;
            Options = options ?? Array.Empty<ChoiceOption>();
            AllowCancel = allowCancel;
            Pick = pick;
        }
    }

    /// <summary>记录式选择器答案用尽时抛出，把「还差哪个选择」带回给会话（内部流转，不是错误）。</summary>
    public sealed class ChoicePendingException : Exception
    {
        public readonly ChoiceRequest Request;

        public ChoicePendingException(ChoiceRequest request)
            : base(request != null ? request.Prompt : "需要玩家选择。")
        {
            Request = request;
        }
    }

    /// <summary>
    /// 用一串**已定答案**回答引擎的选择请求：凡需要点选的节点都按下标顺序消耗一个答案。
    ///
    /// 答案用尽时（strict）抛 <see cref="ChoicePendingException"/>，把待问的选择交回
    /// <see cref="EffectChoiceSession"/>；strict = false 则不再抛（正式执行那一次用），用尽后一律
    /// 取第一项 / 放弃 —— 保证正式执行绝不会因为少答一项而中断。
    /// </summary>
    public sealed class RecordedChoices : IEffectChoices
    {
        private readonly IReadOnlyList<int> answers;
        private readonly bool strict;
        private int cursor;

        public RecordedChoices(IReadOnlyList<int> answers, bool strict = true)
        {
            this.answers = answers ?? Array.Empty<int>();
            this.strict = strict;
        }

        /// <summary>
        /// 这串答案本身（按提问顺序的下标；-1 = 放弃）。联机时由 <c>CommandCodec</c> 把它装进命令 DTO，
        /// 让主机回放用的也是玩家当场点的那些答案，而不是「第一个合法项」。
        /// </summary>
        public IReadOnlyList<int> Answers => answers;

        /// <summary>最近一次抛出的待问选择（供调试 / 断言）。</summary>
        public ChoiceRequest Pending { get; private set; }

        /// <summary>
        /// 按下标顺序取一个答案；<paramref name="allowCancel"/> 决定「没得选」时是放弃还是取第一项。
        /// <paramref name="pick"/> 非 null 时，把这次选择的候选一并带上，UI 据此改走「点地图」的交互。
        /// </summary>
        private int Take(int count, string prompt, IReadOnlyList<string> labels, bool allowCancel,
            ChoicePick pick = null)
        {
            if (count <= 0) return -1;

            if (cursor < answers.Count)
            {
                int value = answers[cursor++];
                if (value < 0) return -1;
                return value < count ? value : (allowCancel ? -1 : 0);
            }

            if (!strict) return allowCancel ? -1 : 0;

            Pending = new ChoiceRequest(prompt, BuildOptions(count, labels), allowCancel, pick);
            throw new ChoicePendingException(Pending);
        }

        private static IReadOnlyList<ChoiceOption> BuildOptions(int count, IReadOnlyList<string> labels)
        {
            var options = new List<ChoiceOption>(count);
            for (int i = 0; i < count; i++)
            {
                string label = labels != null && i < labels.Count ? labels[i] : $"选项 {i + 1}";
                options.Add(new ChoiceOption(i, label));
            }
            return options;
        }

        public InfluenceSlotRef? ChooseInfluenceSlot(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates)
        {
            string prompt = op.Kind == RewardKind.PlaceInfluenceOnCityMoveRoute
                ? "选择要在这条航道的哪个空格放置影响力"
                : "选择要放置影响力的位置";
            int pick = Take(candidates.Count, prompt, SlotLabels(state, candidates, false), true,
                ChoicePick.ForSlots(candidates));
            return pick < 0 ? null : candidates[pick];
        }

        public int ChooseBranch(int seat, RewardOp op, GameState state, IReadOnlyList<EffectBranch> branches)
        {
            var labels = new List<string>(branches.Count);
            for (int i = 0; i < branches.Count; i++) labels.Add($"选项 {i + 1}：{branches[i]}");
            return Take(branches.Count, "这张牌是二选一，请选择要执行的一项", labels, false);
        }

        public InfluenceSlotRef? ChooseInfluenceToRemove(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> candidates)
        {
            int pick = Take(candidates.Count, "选择要拿走的影响力（可以是任何人的）",
                SlotLabels(state, candidates, true), true, ChoicePick.ForSlots(candidates));
            return pick < 0 ? null : candidates[pick];
        }

        public InfluenceMove? ChooseOwnInfluenceMove(int seat, RewardOp op, GameState state,
            IReadOnlyList<InfluenceSlotRef> sources, IReadOnlyList<InfluenceSlotRef> destinations)
        {
            int from = Take(sources.Count, "选择要移动的「你自己的」影响力", SlotLabels(state, sources, true), true,
                ChoicePick.ForSlots(sources));
            if (from < 0) return null;

            int to = Take(destinations.Count, "选择移动到哪个空格", SlotLabels(state, destinations, false), true,
                ChoicePick.ForSlots(destinations));
            if (to < 0) return null;

            return new InfluenceMove(sources[from], destinations[to]);
        }

        public int ChooseNode(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates) labels.Add(NodeLabel(state, id));

            int pick = Take(candidates.Count, NodePrompt(op), labels, true, ChoicePick.ForNodes(candidates));
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseRoute(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates) labels.Add($"航道 {state.Map.RouteLabel(id)}");
            int pick = Take(candidates.Count, "选择 1 条航道", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseEnterpriseBoard(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates)
            {
                EnterpriseBoardDef board = EnterpriseCatalog.Board(id);
                labels.Add(board != null ? board.Name : $"企业 {id}");
            }
            int pick = Take(candidates.Count, "选择 1 家合作企业", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseEnterpriseEffectLevel(int seat, RewardOp op, GameState state, int boardId,
            IReadOnlyList<int> candidates)
        {
            EnterpriseBoardDef board = EnterpriseCatalog.Board(boardId);
            var labels = new List<string>(candidates.Count);
            foreach (int level in candidates)
            {
                EnterpriseLevel entry = board?.Level(level);
                labels.Add(entry != null ? $"{level} 级：{entry.Text}" : $"{level} 级");
            }
            int pick = Take(candidates.Count, $"触发「{board?.Name}」的哪个企业特效", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseRhineDepartment(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates)
            {
                RhineDepartmentDef department = EnterpriseCatalog.Department(id);
                labels.Add(department != null ? department.Name : $"科室板 {id + 1}");
            }

            int pick = Take(candidates.Count, "选择要切换到的科室板", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseOpponent(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int other in candidates) labels.Add($"玩家{other + 1}");

            int pick = Take(candidates.Count, "选择 1 位对手失去分数", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseDiscardedCharacterCard(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates)
                labels.Add($"《{state.Catalog.Character(id)?.Name ?? $"角色牌 {id}"}》");
            int pick = Take(candidates.Count, "从角色牌弃牌区选择 1 张加入手牌", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseOperatorCard(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count + 1);
            foreach (int id in candidates)
                labels.Add($"《{state.Catalog.Character(id)?.Name ?? $"干员牌 {id}"}》");
            labels.Add("全部洗回牌堆，获得 1 分");

            int pick = Take(candidates.Count + 1,
                "选择 1 张干员牌加入手牌（也可以全部洗回换 1 分）", labels, false);
            return pick >= 0 && pick < candidates.Count ? candidates[pick] : -1;
        }

        public int ChooseCharacterCardIntoBox(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates)
                labels.Add($"《{state.Catalog.Character(id)?.Name ?? $"角色牌 {id}"}》");
            int pick = Take(candidates.Count, "选择 1 张角色牌放回游戏盒（不能保留）", labels, false);
            return pick < 0 ? -1 : candidates[pick];
        }

        public BuildChoice? ChooseBuild(int seat, RewardOp op, GameState state,
            IReadOnlyList<int> facilityIds, IReadOnlyList<int> panelSlots, bool freeOnly = false)
        {
            var facilityLabels = new List<string>(facilityIds.Count);
            foreach (int id in facilityIds) facilityLabels.Add($"《{FacilityLabel(state, id)}》");

            int f = Take(facilityIds.Count, "选择要建设的设施牌", facilityLabels, true);
            if (f < 0) return null;

            var slotLabels = new List<string>(panelSlots.Count);
            foreach (int s in panelSlots) slotLabels.Add($"城市面板第 {s + 1} 格");

            int p = Take(panelSlots.Count, "选择要放到城市面板哪一格", slotLabels, true);
            if (p < 0) return null;

            // 卡牌给的免费建造不问付费方式（问了也不付），直接免单。
            if (freeOnly) return new BuildChoice(facilityIds[f], panelSlots[p], FacilityPayment.None);

            List<FacilityPayment> pays = PaymentOptions(state.Catalog.Facility(facilityIds[f]));
            var payLabels = new List<string>(pays.Count);
            foreach (FacilityPayment pay in pays) payLabels.Add(PaymentLabel(pay));

            int pi = Take(pays.Count, "选择这次建设的付费方式（§6.4e 步 1）", payLabels, false);
            if (pi < 0) return null;

            return new BuildChoice(facilityIds[f], panelSlots[p], pays[pi]);
        }

        public int ChooseExtensionHub(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates) labels.Add($"《{FacilityLabel(state, id)}》");

            int pick = Take(candidates.Count, "从余下的延伸枢纽里选 1 张建设（红 / 黄 / 蓝）", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChoosePanelFacility(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidateSlots)
        {
            PlayerState player = state.Player(seat);
            var labels = new List<string>(candidateSlots.Count);
            foreach (int slot in candidateSlots)
            {
                int cardId = CityPanelQuery.CardAt(player, slot);
                labels.Add($"城市面板第 {slot + 1} 格《{FacilityLabel(state, cardId)}》");
            }

            int pick = Take(candidateSlots.Count, "选择要结算入场效果的那张设施（十字相邻的非彩色设施）", labels, true);
            return pick < 0 ? -1 : candidateSlots[pick];
        }

        public IReadOnlyDictionary<ResourceType, int> ChooseSellAmounts(int seat, RewardOp op, GameState state,
            IReadOnlyDictionary<ResourceType, int> prices, IReadOnlyDictionary<ResourceType, int> holdings)
        {
            var amounts = new Dictionary<ResourceType, int>();

            foreach (ResourceType type in ResourceTypes.All)
            {
                if (!prices.TryGetValue(type, out int price) || price <= 0) continue;

                int held = holdings.TryGetValue(type, out int has) ? has : 0;
                if (held <= 0) continue;

                var labels = new List<string>(held + 1);
                for (int n = 0; n <= held; n++)
                {
                    labels.Add(n == 0 ? "不卖" : $"卖 {n} 个（换 {n * price} 金券）");
                }

                string prompt = $"要卖多少个{ResourceLabel(type)}？（持有 {held}，单价 {price} 金券）";
                int count = Take(held + 1, prompt, labels, true);

                if (count > 0) amounts[type] = count;
            }

            return amounts;
        }

        public int ChooseResourceType(int seat, RewardOp op, GameState state, IReadOnlyList<ResourceType> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (ResourceType type in candidates) labels.Add(ResourceLabel(type));

            string prompt = op.Kind == RewardKind.ChooseLeastHeldResource
                ? "从持有量最少的资源里选 1 种"
                : "从这三种资源里选 1 种";
            int pick = Take(candidates.Count, prompt, labels, true);
            return pick < 0 ? -1 : pick;
        }

        public int ChooseSupplyFacility(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates)
        {
            var labels = new List<string>(candidates.Count);
            foreach (int id in candidates) labels.Add($"《{FacilityLabel(state, id)}》");

            int pick = Take(candidates.Count, "选 1 张设施供应区的牌放回设施牌堆底部", labels, true);
            return pick < 0 ? -1 : candidates[pick];
        }

        public int ChooseOptionalStep(int seat, RewardOp op, GameState state, int stepIndex, string label)
        {
            var options = new List<string> { "执行", "终止" };
            string prompt = string.IsNullOrEmpty(label)
                ? $"第 {stepIndex + 1} 步：是否执行？（选「终止」则其后步骤全部跳过）"
                : $"第 {stepIndex + 1} 步：{label}？（选「终止」则其后步骤全部跳过）";

            int pick = Take(options.Count, prompt, options, false);
            return pick == 0 ? 0 : -1;
        }

        /// <summary>
        /// 「合计获得／支付 N 个资源」的分配：按 <paramref name="options"/> 的顺序逐种问「给／付几个」，
        /// 问到只剩最后一种（或余量已分配完）为止 —— 余量直接给最后没问到的那种，
        /// 所以「全给第一种」只会弹一次窗，「0／0／3」则弹两次。
        /// 支付（<see cref="RewardKind.PaySpread"/>）时每种最多到「持有量」为止，避免选出付不起的数。
        /// </summary>
        public IReadOnlyDictionary<ResourceType, int> ChooseResourceSpread(int seat, RewardOp op, GameState state,
            int total, IReadOnlyList<ResourceType> options, IReadOnlyDictionary<ResourceType, int> holdings)
        {
            var amounts = new Dictionary<ResourceType, int>();
            if (options == null || options.Count == 0 || total <= 0) return amounts;

            bool paying = op.Kind == RewardKind.PaySpread;
            string verb = paying ? "付" : "给";

            int remaining = total;
            for (int i = 0; i < options.Count && remaining > 0; i++)
            {
                ResourceType type = options[i];

                int max = remaining;
                if (paying && holdings != null && holdings.TryGetValue(type, out int have) && have < max) max = have;

                // 只剩这一种资源时余量全给它，不再多问一次。
                if (i == options.Count - 1)
                {
                    amounts[type] = max;
                    break;
                }

                if (max <= 0) continue;

                var labels = new List<string>(max + 1);
                for (int n = 0; n <= max; n++) labels.Add(n == 0 ? "不给" : $"{n} 个");

                int count = Take(max + 1,
                    $"合计 {total} 个，还剩 {remaining} 个：{verb}多少个{ResourceLabel(type)}？", labels, false);

                if (count > 0) amounts[type] = count;
                remaining -= count > 0 ? count : 0;
            }

            return amounts;
        }

        // —— 文案 ——

        private static IReadOnlyList<string> SlotLabels(GameState state, IReadOnlyList<InfluenceSlotRef> slots,
            bool withOwner)
        {
            var labels = new List<string>(slots.Count);
            foreach (InfluenceSlotRef slot in slots)
            {
                if (!withOwner)
                {
                    labels.Add(SlotLabel(state, slot));
                    continue;
                }

                int owner = state.Map.OccupantOf(slot);
                string who = owner >= 0 && owner < state.Players.Count ? $"玩家{owner + 1} 的 " : "（空）";
                labels.Add(who + SlotLabel(state, slot));
            }
            return labels;
        }

        private static string SlotLabel(GameState state, InfluenceSlotRef slot) => slot.Kind == InfluenceSlotKind.Node
            ? $"资源点 {state.Map.Node(slot.TargetId).Code} 第 {slot.SlotIndex + 1} 格"
            : $"航道 {state.Map.RouteLabel(slot.TargetId)} 第 {slot.SlotIndex + 1} 格";

        private static string NodeLabel(GameState state, int nodeId)
        {
            MapNodeState node = state.Map.Node(nodeId);
            return $"{node.Code}（{ColorLabel(node.Color)}资源点）";
        }

        private static string NodePrompt(RewardOp op) => op.Kind switch
        {
            RewardKind.FreeCityMove => "选择城市要移动到哪个相邻资源点",
            RewardKind.ExploreOnce => "选择要探索的资源点",
            _ => "选择目标资源点",
        };

        private static string FacilityLabel(GameState state, int facilityId)
        {
            FacilityCardDef card = state.Catalog.Facility(facilityId);
            return card != null ? card.Name : $"设施牌 {facilityId}";
        }

        private static List<FacilityPayment> PaymentOptions(FacilityCardDef card)
        {
            var pays = new List<FacilityPayment>();

            if (card == null || card.IsFree || (card.ResourceCost.Count == 0 && card.VoucherCost == 0))
            {
                pays.Add(FacilityPayment.None);
                return pays;
            }

            if (card.ResourceCost.Count > 0) pays.Add(FacilityPayment.Resources);
            if (card.VoucherCost > 0) pays.Add(FacilityPayment.Vouchers);
            if (pays.Count == 0) pays.Add(FacilityPayment.None);
            return pays;
        }

        private static string PaymentLabel(FacilityPayment payment) => payment switch
        {
            FacilityPayment.Resources => "用资源费用支付",
            FacilityPayment.Vouchers => "用金券费用支付",
            _ => "免费建造（跳过支付）",
        };

        private static string ColorLabel(NodeColor color) => color switch
        {
            NodeColor.Green => "绿色",
            NodeColor.Yellow => "黄色",
            _ => "红色",
        };

        private static string ResourceLabel(ResourceType type) => type switch
        {
            ResourceType.Orirock => "源岩",
            ResourceType.Originium => "源石",
            ResourceType.Oriron => "异铁",
            ResourceType.OriginitePrime => "至纯源石",
            _ => type.ToString(),
        };
    }

    /// <summary>会话里「跑一遍效果」的那一步；参数是**状态副本**与答案来源。</summary>
    public delegate EffectReport EffectStep(GameState sandbox, IEffectChoices choices);

    /// <summary>
    /// 可暂停的效果执行会话：把「先问后改」做成可以一步步驱动的形式，供 UI 弹窗收集玩家点选。
    ///
    /// 为什么需要它：<see cref="IEffectChoices"/> 是同步接口，UI 弹窗却要等玩家点击；
    /// 而部分效果会**连锁**（「二选一」分支里再选落点、探索翻出的事件选项、结算他牌的入场效果……），
    /// 候选清单取决于前面的答案，所以没法在事前一次性问完。
    ///
    /// 做法：在**状态副本**上反复重跑这一步效果 ——
    /// 每轮先按已收集的答案回答，缺答案就停下并回传 <see cref="ChoiceRequest"/>；
    /// 玩家答完再从头重跑（副本每次都重新克隆），于是连锁节点的候选清单永远是对的。
    /// 全部答完的那一轮，把同一串答案交给正式命令即可得到同样的结果（引擎是确定性的）。
    /// </summary>
    public sealed class EffectChoiceSession
    {
        private readonly GameState seed;
        private readonly EffectStep step;
        private readonly List<int> answers = new();

        public EffectChoiceSession(GameState seed, EffectStep step)
        {
            this.seed = seed ?? throw new ArgumentNullException(nameof(seed));
            this.step = step ?? throw new ArgumentNullException(nameof(step));
        }

        /// <summary>当前等待玩家回答的选择；已跑完或没开始时为 null。</summary>
        public ChoiceRequest Pending { get; private set; }

        /// <summary>是否已经把这一步效果整段跑完（没有更多要问的了）。</summary>
        public bool IsComplete { get; private set; }

        /// <summary>跑完那一轮的效果报告（在状态副本上产生，供显示「做了什么」）。</summary>
        public EffectReport Result { get; private set; }

        /// <summary>跑完那一轮的状态副本（复制后的最终状态；正式状态未被改动）。</summary>
        public GameState Sandbox { get; private set; }

        /// <summary>已收集的答案，按提问顺序；可直接交给 <see cref="RecordedChoices"/> 做正式执行。</summary>
        public IReadOnlyList<int> Answers => answers;

        /// <summary>推进到下一个待答选择；若已无待答（含一开始就不需要选择）则置为完成。</summary>
        public void Advance()
        {
            IsComplete = false;
            Pending = null;

            GameState sandbox = seed.Clone();
            try
            {
                Result = step(sandbox, new RecordedChoices(answers));
                Sandbox = sandbox;
                IsComplete = true;
            }
            catch (ChoicePendingException pending)
            {
                Pending = pending.Request;
            }
        }

        /// <summary>回答当前选择（选项下标；-1 = 放弃这一步）并继续推进。</summary>
        public void Answer(int index)
        {
            if (IsComplete || Pending == null)
                throw new InvalidOperationException("当前没有等待回答的选择。");

            answers.Add(index);
            Advance();
        }

        /// <summary>
        /// 回退一步：丢掉最近收集的那个答案再重跑，于是又停在上一个（或再往前的）选择上。
        /// 已经没有答案可退时返回 false —— 调用方据此退回上一个界面（如建设流程的「确认」窗）。
        /// </summary>
        public bool Back()
        {
            if (answers.Count == 0) return false;

            answers.RemoveAt(answers.Count - 1);
            Advance();
            return true;
        }
    }
}
