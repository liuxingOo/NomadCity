using System.Collections.Generic;
using System.Text;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Game;
using UnityEngine;

namespace NomadCity.UI
{
    /// <summary>
    /// 界面与引擎之间的编排者：把「点地图 + 点按钮」翻译成命令，再把命令结果反映回界面。
    ///
    /// 规则判断一概不在这里 —— 非法操作会把引擎给出的中文原因原样显示在 HUD 状态行上，
    /// 界面只负责记住「当前在选什么」（行动模式 + 调度的选点步骤）。
    /// </summary>
    public sealed class GameScreenController : MonoBehaviour
    {
        private GameEngine engine;
        private MapStateRenderer renderer;
        private MapPointerInput pointerInput;
        private GameHud hud;
        private ActionPanel panel;
        private EventCardPanel eventCardPanel;
        private ChoicePopup choicePopup;
        private FacilitySupplyPanel supplyPanel;
        private CityPanelView cityPanel;
        private CityPatternPanel patternPanel;

        private ActionMode mode = ActionMode.None;

        /// <summary>建设（§6.4e）选中的设施供应区牌位下标；-1 表示还没选牌。</summary>
        private int buildSupplyIndex = -1;

        /// <summary>调度模式下已选中的起点（尚未组成一步）。</summary>
        private InfluenceSlotRef? relocateFrom;

        /// <summary>调度模式下已攒好的移动步骤（1–2 步）。</summary>
        private readonly List<InfluenceMove> pendingMoves = new List<InfluenceMove>();

        /// <summary>探索（§6.4c）已选定的目标资源点 id；-1 表示还没选。</summary>
        private int exploreTargetNode = -1;

        /// <summary>探索计划要走的航道：先给默认最省方案，玩家可在地图上点航道空格增删。</summary>
        private readonly List<int> exploreRoutes = new List<int>();

        /// <summary>探索的「付给谁」选择（航道 id → 座位号，§6.6）。</summary>
        private readonly Dictionary<int, int> explorePayees = new Dictionary<int, int>();

        /// <summary>还等着玩家选收款方的航道（一条航道上有多位对手时才需要），逐条弹小选择窗。</summary>
        private readonly Queue<PayeeChoice> payeeQueue = new Queue<PayeeChoice>();

        /// <summary>
        /// 一条待选收款方的航道：连同「此刻能选哪几位对手」一起存进队列。
        /// 选项列表与标题用的必须是同一份数据，否则会出现「标题写 0 位对手、按钮却按 1 位算」这种自相矛盾的弹窗。
        /// </summary>
        private readonly struct PayeeChoice
        {
            public readonly int RouteId;
            public readonly IReadOnlyList<int> Opponents;

            public PayeeChoice(int routeId, IReadOnlyList<int> opponents)
            {
                RouteId = routeId;
                Opponents = opponents;
            }
        }

        /// <summary>本次探索需要选收款方、但小选择弹窗没接线（只在接线缺失时置真，用来在状态行里点名）。</summary>
        private bool payeePopupMissing;

        /// <summary>当前正等待结算的事件卡所在资源点；面板关闭后回到 -1。</summary>
        private int pendingEventNodeId = -1;

        /// <summary>该事件卡所属座位（谁翻的牌就由谁选选项）。</summary>
        private int pendingEventSeat = -1;

        /// <summary>这张事件卡是不是「探索」（§6.4c）翻出来的 —— 是的话结算完选项还要补上第三步。</summary>
        private bool pendingEventFromExplore;

        public void Initialize(GameEngine engine, MapStateRenderer renderer, MapPointerInput pointerInput,
            GameHud hud, ActionPanel panel, EventCardPanel eventCardPanel = null, ChoicePopup choicePopup = null,
            FacilitySupplyPanel supplyPanel = null, CityPanelView cityPanel = null,
            CityPatternPanel patternPanel = null)
        {
            this.engine = engine;
            this.renderer = renderer;
            this.pointerInput = pointerInput;
            this.hud = hud;
            this.panel = panel;
            this.eventCardPanel = eventCardPanel;
            this.choicePopup = choicePopup;
            this.supplyPanel = supplyPanel;
            this.cityPanel = cityPanel;
            this.patternPanel = patternPanel;

            if (engine != null)
            {
                engine.Dispatcher.CommandApplied += OnCommandApplied;
                engine.PhaseChanged += OnPhaseChanged;
                engine.SeatAdvanced += OnSeatAdvanced;
                engine.HarvestResolved += OnHarvestResolved;
                engine.GameEnded += OnGameEnded;
            }

            if (pointerInput != null) pointerInput.Clicked += OnMapClicked;

            if (panel != null)
            {
                panel.ModeRequested += OnModeRequested;
                panel.CommitRequested += OnCommitRequested;
                panel.CancelRequested += OnCancelRequested;
                panel.AdvancePhaseRequested += OnAdvancePhaseRequested;
            }

            if (eventCardPanel != null) eventCardPanel.OptionSelected += OnEventOptionSelected;
            if (supplyPanel != null) supplyPanel.CardClicked += OnSupplyCardClicked;
            if (cityPanel != null) cityPanel.SlotClicked += OnCitySlotClicked;
            if (patternPanel != null) patternPanel.CardClicked += OnPatternCardClicked;

            if (hud != null) hud.Bind(engine);

            SetMode(ActionMode.None);
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (engine != null)
            {
                engine.Dispatcher.CommandApplied -= OnCommandApplied;
                engine.PhaseChanged -= OnPhaseChanged;
                engine.SeatAdvanced -= OnSeatAdvanced;
                engine.HarvestResolved -= OnHarvestResolved;
                engine.GameEnded -= OnGameEnded;
            }

            if (pointerInput != null) pointerInput.Clicked -= OnMapClicked;

            if (panel != null)
            {
                panel.ModeRequested -= OnModeRequested;
                panel.CommitRequested -= OnCommitRequested;
                panel.CancelRequested -= OnCancelRequested;
                panel.AdvancePhaseRequested -= OnAdvancePhaseRequested;
            }

            if (eventCardPanel != null) eventCardPanel.OptionSelected -= OnEventOptionSelected;
            if (supplyPanel != null) supplyPanel.CardClicked -= OnSupplyCardClicked;
            if (cityPanel != null) cityPanel.SlotClicked -= OnCitySlotClicked;
            if (patternPanel != null) patternPanel.CardClicked -= OnPatternCardClicked;
        }

        // —— 引擎事件 ——

        private void OnCommandApplied(GameState state, ICommand command) => RefreshAll();

        // 阶段推进（含两轮行动自动进采集）发生在命令之后，这里补一次刷新，保证画面与状态一致。
        private void OnPhaseChanged(GameState state, TurnPhase from, TurnPhase to) => RefreshAll();

        // 行动座位推进同样发生在命令之后（CommandApplied 已经发过了），不补这一刷，
        // 顶栏与行动提示就会一直显示上一位玩家 —— 看起来就是「座次永远慢一拍」。
        private void OnSeatAdvanced(GameState state, int from, int to) => RefreshAll();

        private void OnHarvestResolved(GameState state, HarvestReport report)
        {
            RefreshAll();

            if (report == null || !report.Ok)
            {
                string reason = report == null ? "无报告" : report.Error;
                SetStatus($"[采集] 未结算：{reason}");
                return;
            }

            SetStatus(DescribeHarvest(report));
        }

        /// <summary>第 8 回合收尾结束、终局分已结算（§9）：退出当前行动并播出胜者与各人最终分数。</summary>
        private void OnGameEnded(GameState state)
        {
            SetMode(ActionMode.None);
            RefreshAll();
            SetStatus(DescribeFinal(state));
        }

        // —— 面板事件 ——

        private void OnModeRequested(ActionMode requested)
        {
            if (engine == null) return;

            // 「路费付给谁」的小选择窗开着时不许换行动：换行动会清掉这次的探索计划，
            // 弹窗却还留在屏幕上，之后再点它的选项，答案会落进一个已经没有主的流程里。
            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("请先选完「路费付给谁」。");
                return;
            }

            // 事件牌还没结算完（面板展开或最小化）时不许换行动：探索的三步是一个整体，
            // 中间插进别的行动会绕过「在目标点放置影响力」这一步（§6.4c）。
            if (eventCardPanel != null && eventCardPanel.IsVisible)
            {
                SetStatus("事件牌还没结算完，先在上面选一个事件选项。");
                return;
            }

            // 开发者模式下不做阶段限制：开局盖放阶段也能直接选行动按钮。
            if (requested != ActionMode.None && !engine.State.DeveloperMode
                && engine.State.Phase != TurnPhase.PlayerActions)
            {
                SetStatus("当前不是玩家行动阶段——先点「结束回合」把阶段推进过去。");
                return;
            }

            SetMode(requested);
        }

        private void OnCommitRequested()
        {
            if (engine == null) return;

            // 「路费付给谁」的小选择窗还开着时不许再点「确认」：否则会重跑一遍 CommitExplore，
            // 把队列清空重建、弹窗被同一个问题覆盖，看起来就是「点了确认没反应／选项一闪就没了」。
            if (choicePopup != null && choicePopup.IsVisible) return;

            // 探索的「确认」= 用选好的目标点与航道正式执行探索（之后翻牌、选选项、补放影响力）。
            if (mode == ActionMode.Explore)
            {
                CommitExplore();
                return;
            }

            if (mode != ActionMode.Relocate) return;

            if (pendingMoves.Count == 0)
            {
                SetStatus("还没有可提交的调度步骤。");
                return;
            }

            var moves = new List<InfluenceMove>(pendingMoves);
            if (Dispatch(new RelocateInfluenceCommand(engine.State.ActiveSeat, moves)))
                SetMode(ActionMode.None);
        }

        /// <summary>取消：退出当前行动，并丢弃已做的选点（含调度已暂存的步骤）。</summary>
        private void OnCancelRequested()
        {
            // 小选择窗开着时，「取消」只作用于那个窗口：否则会把正在进行的探索整段作废，
            // 而窗口还留在屏幕上，看上去像「点了取消却什么都没发生」。
            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("请先选完「路费付给谁」（要放弃这次探索就点那个窗口的取消）。");
                return;
            }

            if (mode == ActionMode.None) return;

            SetMode(ActionMode.None); // 内部会清空 relocateFrom 与 pendingMoves
            SetStatus("已取消当前行动。");
        }

        private void OnAdvancePhaseRequested()
        {
            if (engine == null) return;

            // 同上：还有待回答的小选择窗时不许推进阶段，否则这次探索会被整段丢弃。
            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("请先选完「路费付给谁」。");
                return;
            }

            SetMode(ActionMode.None);
            Dispatch(new AdvancePhaseCommand());
        }

        // —— 地图点击 ——

        private void OnMapClicked(MapHit hit)
        {
            if (engine == null) return;
            GameState state = engine.State;

            // 事件卡弹窗还开着（展开或最小化）时，地图点击一律忽略 ——
            // 事件还没结算完就不该去点资源点（最小化只是为了方便看地图，不是放弃结算）。
            if (eventCardPanel != null && eventCardPanel.IsVisible) return;

            // 小选择弹窗（如「路费付给谁」）开着时同理，先把它答完。
            if (choicePopup != null && choicePopup.IsVisible) return;

            // 开局入场（§4.3）：第 1 回合的角色牌盖放阶段，点绿色资源点本体即为「决定初始资源点」。
            // 此时还没有行动模式，所以放在最前面单独处理。
            if (mode == ActionMode.None && IsSetupPhase(state))
            {
                HandleChooseStartNode(hit, state);
                return;
            }

            switch (mode)
            {
                case ActionMode.Place:
                    HandlePlace(hit, state);
                    break;
                case ActionMode.Relocate:
                    HandleRelocate(hit, state);
                    break;
                case ActionMode.Explore:
                    HandleExplore(hit, state);
                    break;
                case ActionMode.MoveCity:
                    HandleMoveCity(hit, state);
                    break;
                case ActionMode.Declare:
                    // 宣告只发生在弹窗里，地图点击不参与；遮罩正常时不一定会走到这里，兜一句提示。
                    SetHint("宣告城市样式：请从弹出的 6 张样式牌里点一张。");
                    break;
            }
        }

        /// <summary>是不是「第 1 回合的角色牌盖放阶段」——它兼作开局入场，用来决定初始资源点（§4.3）。</summary>
        private static bool IsSetupPhase(GameState state) =>
            state.TurnNumber == 1 && state.Phase == TurnPhase.CharacterPlacement;

        /// <summary>
        /// 开局入场选初始资源点：点绿色资源点本体，把当前该选的玩家的移动城市停靠上去。
        /// 「轮到谁」由引擎按「起始玩家起顺时针」决定，不是按谁点的算（同屏热座）。
        /// </summary>
        private void HandleChooseStartNode(MapHit hit, GameState state)
        {
            int seat = ChooseStartNodeCommand.NextChooser(state);
            if (seat < 0)
            {
                SetStatus("所有玩家都已经决定了初始资源点。");
                return;
            }

            if (hit.Kind != MapHitKind.NodeBody)
            {
                SetHint($"轮到玩家 {seat + 1}：请点击一个绿色资源点本体作为初始资源点。");
                return;
            }

            Dispatch(new ChooseStartNodeCommand(seat, hit.NodeId));
            ShowEventCard(seat, hit.NodeId);
        }

        // —— 事件卡（开局翻出的牌） ——

        /// <summary>
        /// 把引擎刚翻开的那张事件卡弹出来让玩家选选项（开局入场与探索都会翻牌）。
        /// 卡面与选项都来自引擎记在资源点上的 <see cref="MapNodeState.EventCardId"/>。
        /// 返回是否真的弹出来了 —— 没有可用的卡面素材时返回 false，调用方据此提示玩家。
        /// </summary>
        private bool ShowEventCard(int seat, int nodeId)
        {
            if (eventCardPanel == null || engine == null) return false;

            MapNodeState node = engine.State.Map.Node(nodeId);
            if (!node.HasResourceToken || node.EventCardId < 0) return false;

            // 先挡掉未知号段：ICardCatalog.Event 对无效 id 会抛异常而不是返回 null。
            if (EventCardPanel.SpriteIndexOf(node.EventCardId) < 0) return false;

            EventCardDef card = engine.State.Catalog.Event(node.EventCardId);
            if (card.Options.Count == 0) return false;

            eventCardPanel.Show(node.EventCardId, card.Options.Count);
            pendingEventSeat = seat;
            pendingEventNodeId = nodeId;
            return true;
        }

        /// <summary>
        /// 玩家点了卡面上的某个选项：派发结算命令，把结算简报写回 HUD 状态行，然后关掉面板。
        /// 若这张牌是「探索」翻出来的，结算完紧接着补上第三步 —— 在目标资源点放影响力（§6.4c）。
        /// 选项的「是否支付条件段」目前一律为 true（条件段的 UI 开关尚未做，见 §11.2）。
        /// </summary>
        private void OnEventOptionSelected(int optionIndex)
        {
            if (engine == null || pendingEventNodeId < 0) return;

            int seat = pendingEventSeat;
            int nodeId = pendingEventNodeId;
            bool fromExplore = pendingEventFromExplore;

            var command = new ResolveEventOptionCommand(seat, nodeId, optionIndex, acceptCost: true);
            if (!Dispatch(command)) return; // 结算失败时把面板留着，好让玩家换个选项重试

            if (eventCardPanel != null) eventCardPanel.Hide();
            pendingEventSeat = -1;
            pendingEventNodeId = -1;
            pendingEventFromExplore = false;

            if (!fromExplore)
            {
                SetStatus(DescribeEventStep(command));
                return;
            }

            // 探索·步 3：事件结算完，在目标资源点放 1 个自己的影响力（走完这步才占用主要行动名额）。
            var place = new ExploreInfluenceCommand(seat, nodeId);
            CommandResult result = engine.Dispatch(place);
            SetMode(ActionMode.None);
            SetStatus(result.Ok
                ? $"{DescribeEventStep(command)}；[探索] 已在目标资源点放置 1 个影响力。"
                : $"{DescribeEventStep(command)}；[探索] 放置影响力失败：{result.Error}");
        }

        private static string DescribeEventStep(ResolveEventOptionCommand command) =>
            command.Report != null ? $"[事件] {command.Report.Describe()}" : "[事件] 已结算事件选项。";

        /// <summary>
        /// 探索（§6.4c）：
        /// 1. 先点一个「没有指示物且已开放」的资源点本体作为目标；引擎默认给一套最省路线并高亮；
        /// 2. 之后可以点航道空格增删路线（绕远路只是多付钱，§6.6），点「确认」正式执行；
        /// 3. 执行时若某条航道上有多位对手，逐条弹小选择窗问「付给谁」；
        /// 4. 引擎付路费、翻对应颜色的事件牌并放上指示物，随后把牌弹出来选选项；
        /// 5. 选完选项由 <see cref="OnEventOptionSelected"/> 补第三步（在目标点放影响力）。
        /// </summary>
        private void HandleExplore(MapHit hit, GameState state)
        {
            if (hit.Kind == MapHitKind.NodeBody)
            {
                SelectExploreTarget(hit.NodeId, state);
                return;
            }

            if (hit.Kind == MapHitKind.RouteSlot)
            {
                ToggleExploreRoute(hit.Slot.TargetId, state);
                return;
            }

            SetHint("请点击一个资源点本体作为探索目标，或点击航道空格增删路线。");
        }

        /// <summary>选/换探索目标点；用命令自身的校验做预检，非法就把引擎给的原因说出来。</summary>
        private void SelectExploreTarget(int nodeId, GameState state)
        {
            var probe = new ExploreCommand(state.ActiveSeat, nodeId);
            CommandResult check = probe.Validate(state);
            if (!check.Ok)
            {
                SetStatus(check.Error);
                return;
            }

            exploreTargetNode = nodeId;
            explorePayees.Clear();

            // 先给一套默认最省方案，玩家再按需点航道空格增删。
            exploreRoutes.Clear();
            IReadOnlyList<int> defaultPath = TollCalculator.FindCheapestPath(
                state, state.ActiveSeat, state.Player(state.ActiveSeat).MobileCityNodeId, nodeId);
            if (defaultPath != null) exploreRoutes.AddRange(defaultPath);

            RefreshExploreHighlight();
            SetStatus($"[探索] 目标 {state.Map.Node(nodeId).Code}，默认路线 {exploreRoutes.Count} 条航道。可点航道空格增删路线，或点「确认」开始探索。");
            UpdateHint();
        }

        /// <summary>点航道空格：把这条航道加入／移出本次探索路线。</summary>
        private void ToggleExploreRoute(int routeId, GameState state)
        {
            if (exploreTargetNode < 0)
            {
                SetStatus("请先点一个探索目标资源点，再挑要走哪几条航道。");
                return;
            }

            if (!exploreRoutes.Remove(routeId)) exploreRoutes.Add(routeId);

            RefreshExploreHighlight();
            SetStatus(exploreRoutes.Contains(routeId)
                ? $"[探索] 已加入航道 {RouteLabel(routeId)}。"
                : $"[探索] 已移除航道 {RouteLabel(routeId)}。");
            UpdateHint();
        }

        /// <summary>把当前探索路线画到地图上（取消时传空即可清掉高亮）。</summary>
        private void RefreshExploreHighlight()
        {
            if (renderer != null) renderer.SetHighlightedRoutes(exploreRoutes);
        }

        /// <summary>
        /// 探索「确认」：先用命令校验（含自选路线的连通性、金券是否够）做预检，
        /// 再把需要玩家挑收款方的航道排成队列，逐条问完才真正执行。
        /// </summary>
        private void CommitExplore()
        {
            GameState state = engine.State;

            if (exploreTargetNode < 0)
            {
                SetStatus("请先点一个要探索的资源点。");
                return;
            }

            // 每次「确认」都从零开始收收款方选择：上一次探索留下的选择绝不能带进这一次。
            // 换了行动座位之后它多半不再合法，会造成「弹窗里明明列了玩家2、提交时却说这条航道上没有他」的自相矛盾，
            // 而且它会卡在校验那一步直接拒绝探索（连弹窗都到不了）。这里清空是唯一可靠的时机：
            // 弹窗在本次调用里填充，派发也在本次调用里完成，中间没有第二次「确认」插进来的机会。
            explorePayees.Clear();

            var probe = new ExploreCommand(state.ActiveSeat, exploreTargetNode, exploreRoutes, explorePayees);
            CommandResult check = probe.Validate(state);
            if (!check.Ok)
            {
                SetStatus(check.Error);
                return;
            }

            payeeQueue.Clear();
            payeePopupMissing = false;

            // 正式规则：一条航道上要**同时**站着 ≥2 位不同对手才需要玩家挑收款方（§6.6）。
            // 开发者模式放宽到 1，方便只要路上有对手就能把弹窗调出来测。
            int minimumOpponents = state.DeveloperMode ? 1 : 2;

            foreach (int routeId in probe.PlanRoutes(state))
            {
                int cost = TollCalculator.RouteCost(state, state.ActiveSeat, routeId);

                // 参数顺序是 (routeId, seat) —— 反着写编译器不会报错（两个都是 int），
                // 引擎却会去查「第 ActiveSeat 条航道」并把 routeId 当成座位号，
                // 于是这条航道永远算出 0～1 位对手，收款方窗口一次也弹不出来。
                IReadOnlyList<int> opponents = TollCalculator.OpponentsOnRoute(state, routeId, state.ActiveSeat);
                bool needsChoice = cost != 0 && opponents.Count >= minimumOpponents;

                if (needsChoice) payeeQueue.Enqueue(new PayeeChoice(routeId, opponents));
            }

            AdvancePayeeChoice();
        }

        /// <summary>处理「付给谁」：队列空了就执行探索，否则弹下一个航道的收款方选择。</summary>
        private void AdvancePayeeChoice()
        {
            if (payeeQueue.Count == 0)
            {
                ExecuteExplore();
                return;
            }

            PayeeChoice choice = payeeQueue.Dequeue();
            int routeId = choice.RouteId;
            IReadOnlyList<int> opponents = choice.Opponents;

            // 标题与选项都取自同一份快照。快照里没有对手就没有可选项 —— 这是入队判断出了问题，
            // 记一笔并跳过，绝不能弹一个标题写着「0 位对手」、里面一个选项都没有的空窗
            // （那看起来就像按钮坏了，实际是这条航道本来就没对手可付）。
            if (opponents == null || opponents.Count == 0)
            {
                Debug.LogError($"[探索] 航道 {RouteLabel(routeId)} 被排进了「付给谁」队列，但快照里 0 位对手，已跳过。", this);
                AdvancePayeeChoice();
                return;
            }

            if (choicePopup == null)
            {
                // 需要挑收款方却没接弹窗：退回默认（该航道第一位对手），并在状态行 + Console 里点名，避免静默失效。
                payeePopupMissing = true;
                Debug.LogError($"[探索] 航道 {RouteLabel(routeId)} 需要选择收款方，" +
                               "但 GameScreenController 没有拿到 ChoicePopup。请检查 GameBootstrap 物体上的 " +
                               "「Choice Popup」字段是否拖入了场景里的 ChoicePopup 物体（且该物体是激活状态）。", this);
                AdvancePayeeChoice();
                return;
            }

            var labels = new List<string>(opponents.Count);
            for (int i = 0; i < opponents.Count; i++) labels.Add($"付给 玩家{opponents[i] + 1}");

            choicePopup.Show($"航道 {RouteLabel(routeId)} 上有 {opponents.Count} 位对手，路费付给谁？", labels, index =>
            {
                if (index < 0)
                {
                    AbortExplore();
                    return;
                }

                explorePayees[routeId] = opponents[index];
                AdvancePayeeChoice();
            });
        }

        /// <summary>真正派发探索命令（付路费、翻牌、放指示物），随后弹出事件牌让玩家选选项。</summary>
        private void ExecuteExplore()
        {
            GameState state = engine.State;
            int seat = state.ActiveSeat;
            int nodeId = exploreTargetNode;

            var command = new ExploreCommand(seat, nodeId, new List<int>(exploreRoutes), new Dictionary<int, int>(explorePayees));
            if (!Dispatch(command))
            {
                SetMode(ActionMode.None); // 失败原因已由 Dispatch 写进状态行，这里只把选点与高亮清掉
                return;
            }

            SetMode(ActionMode.None);
            pendingEventFromExplore = ShowEventCard(seat, nodeId);

            if (!pendingEventFromExplore)
                SetStatus("这张事件牌没有可用的卡面素材，请先在 EventCardPanel 的 Card Faces 里补上。");
            else
                SetStatus(DescribeToll(command));
        }

        /// <summary>
        /// 把本次路费写成「总额 + 每条航道付给谁 + 这条航道上当时有几位对手」，直接显示在状态行上。
        /// 写法里带上「几位对手」是故意的：这样「该弹收款方窗却没弹」不再是一个只能靠猜的现象 ——
        /// 从状态行就能直接读出来，是这条航道本来只有 1 位对手（规则上不用选），还是真的漏了。
        /// </summary>
        private string DescribeToll(ExploreCommand command)
        {
            var toll = command.Toll;

            if (toll.Total <= 0)
                return "[探索] 这次不用付路费。请选择事件选项。";

            var sb = new StringBuilder($"[探索] 已支付路费 {toll.Total} 金券：");
            for (int i = 0; i < toll.Payments.Count; i++)
            {
                TollCalculator.Payment p = toll.Payments[i];
                int opponents = TollCalculator.OpponentsOnRoute(engine.State, p.RouteId, command.ActorSeat).Count;
                bool chosenByPlayer = command.Payees != null && command.Payees.ContainsKey(p.RouteId);

                if (i > 0) sb.Append('、');
                sb.Append($"航道 {RouteLabel(p.RouteId)} {p.Amount} 券→");
                sb.Append(p.PayeeSeat == TollCalculator.SupplyPayee ? "供应堆" : $"玩家{p.PayeeSeat + 1}");
                sb.Append($"（该航道 {opponents} 位对手");

                // 一条航道上站着 ≥2 位对手就必须由玩家来挑（§6.6）；走到这里还被引擎兜底，说明弹窗那一步漏了。
                if (opponents >= 2 && !chosenByPlayer)
                    sb.Append("，本该让你选却没有弹窗，已按第一位对手结算——这是个缺陷");
                sb.Append('）');
            }

            sb.Append("。请选择事件选项。");

            if (payeePopupMissing)
                sb.Append("（注意：有航道需要你选收款方，但小选择弹窗没有接线，已按第一位对手结算。）");

            return sb.ToString();
        }

        private void AbortExplore()
        {
            SetMode(ActionMode.None);
            SetStatus("已取消探索。");
        }

        /// <summary>航道的显示名（交给 <see cref="MapState.RouteLabel"/> 统一拼，界面层不再另写一份）。</summary>
        private string RouteLabel(int routeId) => engine.State.Map.RouteLabel(routeId);

        private void HandlePlace(MapHit hit, GameState state)
        {
            if (!hit.IsSlot)
            {
                SetHint("请点击资源点左侧或航道上的空格（圆环）。");
                return;
            }

            if (Dispatch(new PlaceInfluenceCommand(state.ActiveSeat, hit.Slot)))
                SetMode(ActionMode.None);
        }

        private void HandleRelocate(MapHit hit, GameState state)
        {
            if (!hit.IsSlot)
            {
                SetHint("请点击影响力空格。");
                return;
            }

            if (!relocateFrom.HasValue)
            {
                int owner = state.Map.OccupantOf(hit.Slot);
                if (owner != state.ActiveSeat)
                {
                    SetStatus(owner < 0 ? "这个空格上没有影响力。" : "这个空格上的影响力不是你的。");
                    return;
                }

                relocateFrom = hit.Slot;
                UpdateHint();
                return;
            }

            if (hit.Slot.Equals(relocateFrom.Value))
            {
                SetStatus("起点与目标不能是同一个空格。");
                return;
            }

            if (pendingMoves.Count >= 2)
            {
                SetStatus("一次调度至多 2 步，请先点「确认」提交，或点「取消」退出。");
                return;
            }

            pendingMoves.Add(new InfluenceMove(relocateFrom.Value, hit.Slot));
            relocateFrom = null;
            UpdateHint();
        }

        private void HandleMoveCity(MapHit hit, GameState state)
        {
            if (hit.Kind != MapHitKind.NodeBody)
            {
                SetHint("请点击一个资源点本体（移动城市要停靠的目标）。");
                return;
            }

            if (Dispatch(new MoveMobileCityCommand(state.ActiveSeat, hit.NodeId)))
                SetMode(ActionMode.None);
        }

        // —— 建设（§6.4e）——

        /// <summary>在设施牌弹窗里点了第 index 个牌位：记下来并收起弹窗，等玩家再点自己城市面板的空位。</summary>
        private void OnSupplyCardClicked(int index)
        {
            if (engine == null || mode != ActionMode.Build) return;

            GameState state = engine.State;
            int cardId = SupplyCardAt(state, index);
            if (cardId < 0) return;

            buildSupplyIndex = index;

            // 选中即收起：弹窗和它的全屏遮罩一起收掉，把左下角的城市面板让出来给玩家点空格。
            // 想换一张牌就再点一次「建设」（重开会清掉上次的选择）。
            if (supplyPanel != null) supplyPanel.SetOpen(false);

            UpdateHint();
        }

        /// <summary>点了自己城市面板的第 slot 格：需要选付费方式就弹小窗，否则直接建设。</summary>
        private void OnCitySlotClicked(int slot)
        {
            if (engine == null || mode != ActionMode.Build) return;

            GameState state = engine.State;
            if (buildSupplyIndex < 0)
            {
                SetStatus("先点「建设」在弹出的设施牌窗口里选一张要建的牌。");
                return;
            }

            int cardId = SupplyCardAt(state, buildSupplyIndex);
            if (cardId < 0)
            {
                SetStatus("刚才选的那张设施牌已经不在供应区了，请重新选一张。");
                return;
            }

            FacilityCardDef card = state.Catalog.Facility(cardId);
            if (card == null) return;

            bool canPayResources = card.ResourceCost.Count > 0;
            bool canPayVouchers = card.VoucherCost > 0;

            // 「免费」关键词、或两种费用都没写：不用问，直接建。
            if (card.IsFree || (!canPayResources && !canPayVouchers))
            {
                ExecuteBuild(state, cardId, slot, FacilityPayment.None);
                return;
            }

            // 只有一种费用可选时也不用弹窗（41 张牌的数据录完后多半两种都有）。
            if (!canPayResources || !canPayVouchers)
            {
                ExecuteBuild(state, cardId, slot,
                    canPayResources ? FacilityPayment.Resources : FacilityPayment.Vouchers);
                return;
            }

            if (choicePopup == null)
            {
                SetStatus($"《{card.Name}》有资源 / 金券两种费用，但小选择弹窗没接线，问不了付费方式。");
                return;
            }

            var options = new List<string>
            {
                $"用资源支付：{DescribeCost(card.ResourceCost)}",
                $"用金券支付：{card.VoucherCost} 张金券",
            };

            choicePopup.Show($"《{card.Name}》用哪种费用支付（§6.4e 步 1）？", options, choice =>
            {
                if (engine == null) return;

                if (choice == 0) ExecuteBuild(engine.State, cardId, slot, FacilityPayment.Resources);
                else if (choice == 1) ExecuteBuild(engine.State, cardId, slot, FacilityPayment.Vouchers);
                else SetStatus("已取消这次建设。");
            });
        }

        /// <summary>派发建设命令；成功后退出建设模式并把结果写进状态行。</summary>
        private void ExecuteBuild(GameState state, int cardId, int slot, FacilityPayment payment)
        {
            if (engine == null) return;

            int seat = state.ActiveSeat;
            var command = new BuildFacilityCommand(seat, cardId, slot, payment);
            if (!Dispatch(command)) return;

            buildSupplyIndex = -1;
            SetMode(ActionMode.None);

            string suffix = command.Report != null && command.Report.Entries.Count > 0
                ? $"；入场效果：{command.Report.Describe()}"
                : string.Empty;
            SetStatus($"[建设] 玩家{seat + 1} 建造了《{FacilityName(state, cardId)}》" +
                      $"，放在城市面板第 {slot + 1} 格{suffix}");
        }

        /// <summary>设施供应区第 index 个牌位上的设施牌 id；没有则 -1。</summary>
        private static int SupplyCardAt(GameState state, int index) =>
            state != null && index >= 0 && index < state.FacilitySupply.Count ? state.FacilitySupply[index] : -1;

        private static string FacilityName(GameState state, int cardId)
        {
            FacilityCardDef card = state.Catalog.Facility(cardId);
            return card != null ? card.Name : $"设施牌 {cardId}";
        }

        private static string DescribeCost(IReadOnlyList<RewardOp> ops)
        {
            if (ops == null || ops.Count == 0) return "无";

            var parts = new List<string>(ops.Count);
            foreach (RewardOp op in ops) parts.Add(op.ToString());
            return string.Join("、", parts);
        }

        // —— 宣告城市样式（§6.3b / §6.5b）——

        /// <summary>
        /// 在样式牌弹窗里点了第 index 张牌：派发宣告命令，引擎自动挑出第一组合法摆法
        /// （条件可旋转、不可翻转，§10.2）。成功就收起弹窗，并把「用了城市面板哪几格 + 旋转角度」写进状态行；
        /// 失败时弹窗留着，好让玩家换一张样式牌重试（原因由引擎给出，如「凑不出条件」或「条件还没录入」）。
        /// </summary>
        private void OnPatternCardClicked(int index)
        {
            if (engine == null || mode != ActionMode.Declare) return;

            GameState state = engine.State;
            if (index < 0 || index >= state.CityPatternCards.Count) return;

            int cardId = state.CityPatternCards[index];
            var command = new DeclareCityPatternCommand(state.ActiveSeat, cardId);
            if (!Dispatch(command)) return;

            int seat = state.ActiveSeat;
            string slots = DescribePatternSlots(command.Match);
            int rotation = command.Match.RotationDegrees;
            string suffix = command.Report != null && command.Report.Entries.Count > 0
                ? $"；宣告效果：{command.Report.Describe()}"
                : string.Empty;

            SetMode(ActionMode.None); // 收起弹窗

            SetStatus($"[宣告] 玩家{seat + 1} 宣告了《{PatternName(state, cardId)}》" +
                      $"，使用城市面板 {slots}（旋转 {rotation}°）{suffix}");
        }

        /// <summary>把匹配用到的面板格子写成「第 N 格」（下标 0 起，显示按 1 起）。</summary>
        private static string DescribePatternSlots(PatternMatch match)
        {
            if (match.Slots == null || match.Slots.Count == 0) return "（无）";

            var parts = new List<string>(match.Slots.Count);
            foreach (int slot in match.Slots) parts.Add($"第 {slot + 1} 格");
            return string.Join("、", parts);
        }

        private static string PatternName(GameState state, int cardId)
        {
            CityPatternCardDef card = state.Catalog.CityPattern(cardId);
            return card != null ? card.ToString() : $"样式牌 {cardId}";
        }

        // —— 内部工具 ——

        /// <summary>走命令通道。失败时把引擎的原因原样透出；成功时提示已执行。</summary>
        private bool Dispatch(ICommand command)
        {
            CommandResult result = engine.Dispatch(command);

            if (!result.Ok)
            {
                SetStatus(result.Error);
                return false;
            }

            SetStatus($"已执行：{command.Name}");
            return true;
        }

        private void SetMode(ActionMode next)
        {
            mode = next;
            relocateFrom = null;
            pendingMoves.Clear();

            // 退出行动时一并丢掉探索的选点 / 路线 / 收款方选择，并把地图高亮清掉。
            exploreTargetNode = -1;
            exploreRoutes.Clear();
            explorePayees.Clear();
            payeeQueue.Clear();
            RefreshExploreHighlight();

            // 建设的选牌同理：换行动后原来那张牌不该还算「已选中」。
            buildSupplyIndex = -1;

            // 设施牌弹窗不常驻：进建设模式才弹出，退出（含「取消」、建成后自动退出）就收起。
            // 宿主容器保持激活，这里只切弹窗根。
            if (supplyPanel != null) supplyPanel.SetOpen(next == ActionMode.Build);

            // 城市样式牌弹窗同理（§6.3b）：进宣告模式弹出 6 张样式牌，选中一张或取消即收起。
            if (patternPanel != null) patternPanel.SetOpen(next == ActionMode.Declare);

            // 设施供应区 / 城市面板只有建设模式下可点，跟着模式一起刷新。
            RefreshFacilityViews();

            // 万一还有「付给谁」的小窗留在屏幕上（例如中途换了行动），一并收掉：
            // 否则它的回调会在这次探索作废之后才被打进来，把某个过期选择写进已经没人管的计划里。
            if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();

            if (panel != null) panel.SetMode(mode);
            UpdateHint();
        }

        private void UpdateHint()
        {
            if (panel == null) return;

            if (engine != null)
            {
                GameState state = engine.State;

                if (state.IsGameOver)
                {
                    panel.SetHint("本局已结束。");
                    return;
                }

                if (mode == ActionMode.None && IsSetupPhase(state))
                {
                    int seat = ChooseStartNodeCommand.NextChooser(state);
                    panel.SetHint(seat < 0
                        ? "初始资源点都决定好了——点「结束回合」进入玩家行动。"
                        : $"轮到玩家 {seat + 1}：请点击一个绿色资源点本体作为初始资源点。");
                    return;
                }
            }

            switch (mode)
            {
                case ActionMode.None:
                    panel.SetHint("选择一个行动。");
                    break;
                case ActionMode.Place:
                    panel.SetHint("请点击一个空的影响力空格。");
                    break;
                case ActionMode.Relocate:
                    if (relocateFrom.HasValue)
                        panel.SetHint($"起点已选：{relocateFrom.Value}。请点击目标空格。");
                    else if (pendingMoves.Count == 0)
                        panel.SetHint("先点击你自己的一个影响力。");
                    else
                        panel.SetHint($"已选 {pendingMoves.Count}/2 步。可继续选点，或点「确认」提交。");
                    break;
                case ActionMode.MoveCity:
                    panel.SetHint("请点击一个与移动城市相邻的资源点。");
                    break;
                case ActionMode.Build:
                    panel.SetHint(buildSupplyIndex < 0
                        ? "建设：点窗口里的任意一张设施牌。"
                        : "建设：请点击你城市面板上的一个空位。");
                    break;
                case ActionMode.Declare:
                    panel.SetHint("宣告城市样式：请从弹出的 6 张样式牌里点一张（引擎会自动匹配组合）。");
                    break;
                case ActionMode.Explore:
                {
                    GameState exploreState = engine != null ? engine.State : null;
                    if (exploreState == null || exploreTargetNode < 0)
                    {
                        panel.SetHint("探索：请点击一个没有资源点指示物、且已开放探索的资源点本体。");
                        break;
                    }

                    int toll = TollCalculator.Compute(exploreState, exploreState.ActiveSeat, exploreRoutes, explorePayees).Total;
                    panel.SetHint($"探索目标：{exploreState.Map.Node(exploreTargetNode).Code}；已选 {exploreRoutes.Count} 条航道、路费 {toll} 券。" +
                                  "可点航道空格增删路线，或点「确认」开始探索。");
                    break;
                }
            }
        }

        private void SetHint(string text)
        {
            if (panel != null) panel.SetHint(text);
        }

        private void SetStatus(string text)
        {
            if (hud != null) hud.SetStatus(text);
        }

        private void RefreshAll()
        {
            if (renderer != null && renderer.IsReady) renderer.Refresh();
            if (hud != null) hud.Refresh();
            RefreshFacilityViews();

            // 引导文字也跟着状态走：开局选点轮次、本局结束这些都不经过 SetMode，
            // 只靠命令/阶段事件刷新，所以并在这里一起更新。
            UpdateHint();
        }

        /// <summary>刷新设施供应区、城市面板与城市样式牌弹窗（内容 + 可点状态）。漏接线时静默跳过。</summary>
        private void RefreshFacilityViews()
        {
            if (engine == null) return;

            GameState state = engine.State;
            bool buildMode = mode == ActionMode.Build;

            if (supplyPanel != null) supplyPanel.Refresh(state, buildMode);
            if (cityPanel != null) cityPanel.Refresh(state, state.ActiveSeat, buildMode);

            // 样式牌弹窗只在宣告模式下可点（弹窗本身由 SetMode 开关）。
            if (patternPanel != null) patternPanel.Refresh(state, mode == ActionMode.Declare);
        }

        /// <summary>采集阶段自动结算的简报（本轮为占位实现，见 GameEngine.ResolveHarvestAutomatically）。</summary>
        private static string DescribeHarvest(HarvestReport report)
        {
            var sb = new StringBuilder("[采集] 自动结算 —— ");
            for (int i = 0; i < report.Results.Count; i++)
            {
                HarvestResult result = report.Results[i];
                if (i > 0) sb.Append("；");
                sb.Append($"玩家{result.Seat + 1} 采 {result.Nodes.Count} 点/付 {result.VouchersPaid} 券");
            }
            return sb.ToString();
        }

        /// <summary>终局简报（§9）：胜者 + 各人最终分数。同分并列时把并列的座位都列出来。</summary>
        private static string DescribeFinal(GameState state)
        {
            IReadOnlyList<int> winners = FinalScoring.Winners(state);

            var sb = new StringBuilder("[终局] 胜者 ");
            for (int i = 0; i < winners.Count; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append($"玩家{winners[i] + 1}");
            }
            if (winners.Count > 1) sb.Append("（并列）");

            sb.Append(" · 各人分数 ");
            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                if (seat > 0) sb.Append(" / ");
                sb.Append(state.Players[seat].Score);
            }
            return sb.ToString();
        }
    }
}
