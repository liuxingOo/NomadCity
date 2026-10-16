using System.Collections.Generic;
using System.Text;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 行动记录：右上角一个按钮，点开一个聊天框式的面板，把每一步行动记成一行「玩家N 做了什么」。
    ///
    /// 它只订阅命令通道的成功事件（<see cref="CommandDispatcher.CommandApplied"/>），不判断任何规则 ——
    /// 规则已经执行完了，这里只负责把「已经发生的事」翻成人话。因此它和 GameScreenController 完全解耦：
    /// 谁执行了命令都记，包括开发者模式自动跑完的开局设置。
    ///
    /// 场景结构（建议）：
    /// Canvas
    ///   行动记录按钮            ← 挂在 <see cref="toggleButton"/> 上（必须在 Root 之外，否则收起后点不回来）
    ///   ActionLogPanel（空物体，挂本脚本，自身保持激活）
    ///     Panel                 ← <see cref="root"/>：面板本体，含背景 Image
    ///       ScrollView          ← <see cref="scrollRect"/>
    ///         Viewport（RectMask2D）
    ///           Content（ContentSizeFitter）  ← 正文
    ///       CloseButton         ← <see cref="closeButton"/>
    /// 正文既可以是一整块 TMP 文本（<see cref="bodyText"/> 挂在 Content 上），
    /// 也可以留空 —— 留空则本组件在 Content 下自己建一个。
    /// </summary>
    public sealed class ActionLogPanel : MonoBehaviour
    {
        [Tooltip("右上角的开关按钮；点一下展开／收起记录面板。必须放在 Root 之外。")]
        [SerializeField] private Button toggleButton;

        [Tooltip("记录面板的根节点；默认隐藏，Show/Hide 直接开关它")]
        [SerializeField] private GameObject root;

        [Tooltip("面板里的关闭按钮（可选）")]
        [SerializeField] private Button closeButton;

        [Tooltip("聊天正文所在的滚动区；用来在追加后自动滚到底（可选）")]
        [SerializeField] private ScrollRect scrollRect;

        [Tooltip("正文文本。为空时自动建一个挂在 ScrollRect 的 Content 下。")]
        [SerializeField] private TMP_Text bodyText;

        [Tooltip("最多保留多少行，超出就丢掉最早的（防止长时间游玩把文本撑爆）")]
        [SerializeField] private int maxLines = 300;

        private readonly List<string> lines = new List<string>();
        private GameEngine engine;

        /// <summary>记录面板是不是开着。</summary>
        public bool IsVisible => root != null && root.activeSelf;

        private void Awake()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (bodyText == null) bodyText = CreateBodyText();

            Hide();
        }

        /// <summary>接上引擎的命令通道，之后每条成功命令都会记一行。</summary>
        public void Bind(GameEngine engine)
        {
            this.engine = engine;
            if (engine != null) engine.Dispatcher.CommandApplied += OnCommandApplied;

            lines.Clear();
            Rebuild();
            Append("—— 行动记录 ——");

            // 没有正文文本 = 面板点开也是空的，界面上看不出是哪一步没接。直接点名。
            if (bodyText == null)
                Debug.LogWarning("[行动记录] 没有可用的正文文本：请在 Inspector 里把一个 TMP 文本拖到「Body Text」字段，" +
                                 "或把 ScrollView 的 ScrollRect 拖到「Scroll Rect」字段（会自动在 Content 下建一个）。", this);
        }

        private void OnDestroy()
        {
            if (engine != null) engine.Dispatcher.CommandApplied -= OnCommandApplied;
        }

        private void OnCommandApplied(GameState state, ICommand command) => Append(Describe(state, command));

        public void Toggle() => SetVisible(!IsVisible);

        public void Show() => SetVisible(true);

        public void Hide() => SetVisible(false);

        /// <summary>追加一行；超出上限就丢掉最早的行。</summary>
        public void Append(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            lines.Add(line);
            while (lines.Count > maxLines) lines.RemoveAt(0);

            Rebuild();
            if (IsVisible) ScrollToBottom();
        }

        private void SetVisible(bool visible)
        {
            if (root != null) root.SetActive(visible);
            if (visible) ScrollToBottom();
        }

        private void Rebuild()
        {
            if (bodyText == null) return;
            bodyText.text = string.Join("\n", lines);
        }

        /// <summary>
        /// 滚到底：先把布局刷一遍再设纵向归一化位置，否则文本刚变长时算出来的是旧高度，滚不到底。
        /// </summary>
        private void ScrollToBottom()
        {
            if (scrollRect == null || scrollRect.content == null) return;

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// 没有指定正文文本时，先看 Content 下有没有现成的 TMP 文本，有就直接用；没有才新建一个。
        ///
        /// 必须先找现成的：新建的那个是 AddComponent 出来的，只能用 TMP 的默认字体，
        /// 中文一个字都画不出来 —— 面板点开就是全空的，看起来像「记录没生效」。
        /// 场景里摆好的那个文本带着项目的中文字体，用它就不会有这个问题。
        /// </summary>
        private TMP_Text CreateBodyText()
        {
            if (scrollRect == null || scrollRect.content == null) return null;

            TMP_Text existing = scrollRect.content.GetComponentInChildren<TMP_Text>(true);
            if (existing != null) return existing;

            var go = new GameObject("BodyText", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(scrollRect.content, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            var label = go.GetComponent<TextMeshProUGUI>();
            label.fontSize = 16f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            return label;
        }

        // —— 把一条命令翻成一行中文 ——

        private static string Describe(GameState state, ICommand command)
        {
            switch (command)
            {
                case StartGameCommand start:
                    return $"—— 开局：起始玩家 {SeatName(start.FirstPlayerSeat)} ——";

                case ChooseStartNodeCommand choose:
                    return DescribeChooseStartNode(state, choose);

                case ResolveEventOptionCommand resolve:
                    return DescribeEventOption(state, resolve);

                case ExploreCommand explore:
                    return DescribeExplore(state, explore);

                case ExploreInfluenceCommand influence:
                    return $"{SeatName(influence.ActorSeat)} 在 {state.Map.Node(influence.NodeId).Code} 放置了 1 个影响力，探索完成。";

                case PlaceInfluenceCommand place:
                    return $"{SeatName(place.ActorSeat)} 在 {SlotLabel(state, place.Slot)} 部署了 1 个影响力。";

                case RelocateInfluenceCommand relocate:
                    return DescribeRelocate(state, relocate);

                case MoveMobileCityCommand move:
                    return $"{SeatName(move.ActorSeat)} 支付 {MoveMobileCityCommand.OriginiumCost} 源石，" +
                           $"把移动城市移到 {state.Map.Node(move.TargetNodeId).Code}。";

                case BuildFacilityCommand build:
                    return DescribeBuild(state, build);

                case AdvancePhaseCommand:
                    return $"—— 推进阶段：现在进入「{GameHud.StageLabel(state)}」 ——";

                default:
                    return $"{SeatName(command.ActorSeat)} 执行了「{command.Name}」。";
            }
        }

        /// <summary>开局入场（§4.3）：设初始资源点 → 翻牌 → 落指示物 → 顺带说出资源点类型。</summary>
        private static string DescribeChooseStartNode(GameState state, ChooseStartNodeCommand command)
        {
            MapNodeState node = state.Map.Node(command.NodeId);

            var sb = new StringBuilder($"{SeatName(command.ActorSeat)} 将 {node.Code} 设为初始资源点，翻开了事件牌");
            AppendCardAndToken(state, node, sb);

            // §4.3 步 2 的 B-01 特例，值得单独记一笔。
            if (node.Code == "B-01")
                sb.Append("；因选的是 B-01，立即获得 源岩×2、源石×2、异铁×2");

            return sb.ToString();
        }

        /// <summary>事件牌选项结算（§4.3 后半 / §6.4c 步 2）：选了什么、实际拿到什么。</summary>
        private static string DescribeEventOption(GameState state, ResolveEventOptionCommand command)
        {
            MapNodeState node = state.Map.Node(command.NodeId);
            EventCardDef card = state.Catalog.Event(node.EventCardId);
            EventOption option = command.OptionIndex < card.Options.Count ? card.Options[command.OptionIndex] : null;

            var sb = new StringBuilder($"{SeatName(command.ActorSeat)} 在事件牌《{card.Name}》中选择了");
            sb.Append(option != null && !string.IsNullOrEmpty(option.Text) ? $"「{option.Text}」" : $"第 {command.OptionIndex + 1} 个选项");
            sb.Append($"：{DescribeReport(command.Report)}。");
            return sb.ToString();
        }

        /// <summary>探索（§6.4c）：走了几条航道、付了多少路费、翻出什么牌、落什么指示物。</summary>
        private static string DescribeExplore(GameState state, ExploreCommand command)
        {
            MapNodeState node = state.Map.Node(command.NodeId);
            int hops = command.RouteIds != null ? command.RouteIds.Count : 0;
            int total = command.Toll.Total;

            var sb = new StringBuilder($"{SeatName(command.ActorSeat)} 探索 {node.Code}：");
            sb.Append(hops > 0 ? $"从移动城市出发走 {hops} 条航道、" : string.Empty);
            sb.Append(total > 0 ? $"支付路费 {total} 金券，" : "无需支付路费，");
            sb.Append("翻开了事件牌");
            AppendCardAndToken(state, node, sb);
            sb.Append("，还需要在目标资源点放置 1 个影响力。");
            sb.Append(DescribeTollDetail(state, command));
            return sb.ToString();
        }

        /// <summary>
        /// 路费明细（§6.6）：每条航道付了几券、付给谁、这条航道上当时有几位对手、这个收款方是玩家挑的还是引擎兜底。
        /// 记这一笔是为了让「为什么没弹收款方窗口」变成一个能直接读到的事实，而不是靠猜。
        /// </summary>
        private static string DescribeTollDetail(GameState state, ExploreCommand command)
        {
            IReadOnlyList<TollCalculator.Payment> payments = command.Toll.Payments;
            if (payments.Count == 0) return string.Empty;

            var sb = new StringBuilder("路费明细：");
            for (int i = 0; i < payments.Count; i++)
            {
                if (i > 0) sb.Append('；');

                TollCalculator.Payment payment = payments[i];
                int opponents = TollCalculator.OpponentsOnRoute(state, payment.RouteId, command.ActorSeat).Count;
                string payee = payment.PayeeSeat == TollCalculator.SupplyPayee ? "供应堆" : SeatName(payment.PayeeSeat);
                bool chosenByPlayer = command.Payees != null && command.Payees.ContainsKey(payment.RouteId);

                sb.Append($"航道 {RouteLabel(state, payment.RouteId)} 付 {payment.Amount} 券给 {payee}" +
                          $"（该航道 {opponents} 位对手，{(chosenByPlayer ? "你选的" : "引擎按第一位对手兜底")}）");
            }
            return sb.ToString();
        }

        private static string DescribeRelocate(GameState state, RelocateInfluenceCommand command)
        {
            var sb = new StringBuilder($"{SeatName(command.ActorSeat)} 调度了 {command.Moves.Count} 处影响力：");
            for (int i = 0; i < command.Moves.Count; i++)
            {
                if (i > 0) sb.Append('、');
                InfluenceMove move = command.Moves[i];
                sb.Append($"{SlotLabel(state, move.From)} → {SlotLabel(state, move.To)}");
            }
            return sb.ToString();
        }

        /// <summary>建设（§6.4e）：建了哪张牌、放在第几格、拿了多少分、入场效果是什么。</summary>
        private static string DescribeBuild(GameState state, BuildFacilityCommand command)
        {
            FacilityCardDef card = state.Catalog.Facility(command.FacilityId);

            var sb = new StringBuilder($"{SeatName(command.ActorSeat)} 建设了《{card.Name}》，放在城市面板第 {command.PanelSlot + 1} 格");
            if (card.Score > 0) sb.Append($"，得 {card.Score} 分");
            sb.Append('。');

            if (command.Report != null && command.Report.Entries.Count > 0)
                sb.Append($"《{card.Name}》入场效果：{DescribeReport(command.Report)}。");

            return sb.ToString();
        }

        /// <summary>追一句「事件牌《名字》，资源点类型：源石×1」——牌已经翻开了才有。</summary>
        private static void AppendCardAndToken(GameState state, MapNodeState node, StringBuilder sb)
        {
            if (node.HasResourceToken && node.EventCardId >= 0)
                sb.Append($"《{state.Catalog.Event(node.EventCardId).Name}》，资源点类型：{ResourceName(node.TokenResource)}×{node.TokenAmount}");
            else
                sb.Append("（这张牌没有可用的卡面素材）");
        }

        private static string DescribeReport(EffectReport report)
        {
            if (report == null) return "已结算";
            if (report.Declined) return report.Note ?? "放弃了这个选项";
            if (report.Entries.Count == 0) return "没有可执行的效果";

            var sb = new StringBuilder();
            for (int i = 0; i < report.Entries.Count; i++)
            {
                if (i > 0) sb.Append('、');
                EffectLogEntry entry = report.Entries[i];
                sb.Append(entry.Applied ? DescribeOp(entry.Op) : $"（没能执行：{entry.Note}）");
            }
            return sb.ToString();
        }

        /// <summary>效果说明：只补上资源的中文名，其余种类沿用 <see cref="RewardOp.ToString"/>。</summary>
        private static string DescribeOp(RewardOp op) => op.Kind switch
        {
            RewardKind.GainResource => $"获得 {ResourceName(op.Resource)}×{op.Amount}",
            RewardKind.PayResource => $"支付 {ResourceName(op.Resource)}×{op.Amount}",
            RewardKind.OpponentsGainResource => $"所有对手各获得 {ResourceName(op.Resource)}×{op.Amount}",
            _ => op.ToString(),
        };

        private static string SeatName(int seat) => seat < 0 ? "系统" : $"玩家{seat + 1}";

        /// <summary>影响力空格的写法：资源点上写「A-01 的第 2 个空格」，航道上写「航道 A-01–B-01」。</summary>
        private static string SlotLabel(GameState state, InfluenceSlotRef slot) =>
            slot.Kind == InfluenceSlotKind.Node
                ? $"{state.Map.Node(slot.TargetId).Code} 的第 {slot.SlotIndex + 1} 个空格"
                : $"航道 {RouteLabel(state, slot.TargetId)}";

        /// <summary>航道的显示名（交给 <see cref="MapState.RouteLabel"/> 统一拼，别在界面层另写一份）。</summary>
        private static string RouteLabel(GameState state, int routeId) => state.Map.RouteLabel(routeId);

        private static string ResourceName(ResourceType type) => type switch
        {
            ResourceType.Orirock => "源岩",
            ResourceType.Originium => "源石",
            ResourceType.Oriron => "异铁",
            _ => "至纯源石",
        };
    }
}
