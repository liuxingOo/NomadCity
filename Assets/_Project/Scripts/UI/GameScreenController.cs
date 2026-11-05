using System;
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
        private CardPreviewPopup cardPreview;
        private CharacterHandPanel handPanel;

        /// <summary>状态栏左侧眼睛按钮弹出的「某位玩家城市面板」（只读）。没接线时点了只会提示。</summary>
        private PlayerCityPopup playerCityPopup;

        /// <summary>本机玩家座位（0 起）：非建设模式下城市面板固定显示这名玩家，建设模式下改用行动玩家。</summary>
        private int localSeat;

        /// <summary>各座位昵称（联机来自准备房间名单）；为空时回退成「玩家 N」。</summary>
        private string[] playerNames = new string[0];

        /// <summary>把界面文本里的「玩家 N」替换成昵称：Core 的错误原因、效果说明都走这一道。</summary>
        private static readonly System.Text.RegularExpressions.Regex PlayerRef =
            new System.Text.RegularExpressions.Regex(@"玩家\s*(\d+)");

        /// <summary>
        /// 联机模式下只允许操作本机座位。单机热座为 false：一台机器轮着操作所有座位。
        /// 开启后，凡是「按当前行动座位」构造的命令都会被本机座位闸门拦下，
        /// 避免主机顺手替客户端把行动做完（那样客户端会永远轮不到操作）。
        /// </summary>
        private bool lockToLocalSeat;

        /// <summary>
        /// 联机客户端：命令只提交给主机、本地不执行。于是「派发完立刻读 <c>engine.State</c>」的流程
        /// （探索 / 城市移动 / 开局选点要弹的事件牌）此刻看到的是旧状态，必须等主机回放后再补，
        /// 见 <see cref="SyncPendingEventCard"/>。
        /// </summary>
        private bool onlineClient;

        private ActionMode mode = ActionMode.None;

        /// <summary>建设流程（§6.4e）走到哪一步；<see cref="BuildStep.NotBuilding"/> 表示不在建设。</summary>
        private enum BuildStep
        {
            NotBuilding = 0, // 不在建设
            PickCard = 1,    // L0：在设施供应区 6 张牌里选一张
            Pay = 2,         // L1：放大卡面 + 选项一（资源）/ 选项二（金券）/ 取消建设
            Slot = 3,        // L2：城市面板空位高亮，选一格（其他行动按钮锁死）
            Confirm = 4,     // L3：确认弹窗「在此处建造《X》？+N 分；获得…」
            Effect = 5,      // L4：入场效果点选（可能连锁多次，带返回键）
        }

        private BuildStep buildStep = BuildStep.NotBuilding;

        /// <summary>
        /// 建设入场效果里「要玩家点选」的收集会话（§11）：在状态副本上把效果串试跑到需要选择处就停下，
        /// 逐个问完再正式派发。已跑完或没在收集时为 null。
        /// </summary>
        private EffectChoiceSession buildSession;

        /// <summary>正在建设的那次选择的暂存：会话结束后照它派发建设命令。</summary>
        private int buildSeat = -1;
        private int buildCardId = -1;
        private int buildSlot = -1;
        private FacilityPayment buildPayment = FacilityPayment.None;

        /// <summary>角色牌使用流程（§6.3a / §10.1）走到哪一步。</summary>
        private enum CharacterStep
        {
            None = 0,     // 不在使用角色牌
            Effect = 1,   // 正在收集这次效果里要玩家点选的节点（策略 / 计谋）
            Second = 2,   // 起始玩家用掉一种效果后，等玩家决定「继续用另一种 / 结束」
        }

        private CharacterStep characterStep = CharacterStep.None;

        /// <summary>正在使用角色牌的那名玩家与那张牌。</summary>
        private int characterSeat = -1;
        private int characterCardId = -1;

        /// <summary>这次要发动的是策略还是计谋。</summary>
        private CharacterEffectKind characterEffect;

        /// <summary>角色牌效果里「要玩家点选」的收集会话；已跑完或没在收集时为 null。</summary>
        private EffectChoiceSession characterSession;

        /// <summary>使用城市样式特殊行动（§6.3f）走到哪一步。</summary>
        private enum SpecialStep
        {
            None = 0,      // 不在使用特殊行动
            PickCard = 1,  // 从场上的样式牌里点一张（会付费用、结算效果）
            Effect = 2,    // 正在收集这次特殊行动里要玩家点选的节点（可能连锁多次，带返回键）
        }

        private SpecialStep specialStep = SpecialStep.None;

        /// <summary>这次要使用特殊行动的城市样式牌 id。</summary>
        private int specialCardId = -1;

        /// <summary>特殊行动效果里「要玩家点选」的收集会话；已跑完或没在收集时为 null。</summary>
        private EffectChoiceSession specialSession;

        // —— 采集阶段（§7）：玩家自选资源点（不再是自动结算） ——

        /// <summary>进入采集阶段后按「起始玩家起顺时针」排好的座位顺序。</summary>
        private readonly List<int> harvestOrder = new List<int>();

        /// <summary>正在选点的座位在 <see cref="harvestOrder"/> 里的下标；-1 表示没在采集选点。</summary>
        private int harvestIndex = -1;

        /// <summary>已定下的采集选择：座位号 → 要采的资源点 id（逐位玩家「确认」后写入）。</summary>
        private readonly Dictionary<int, List<int>> harvestChosen = new Dictionary<int, List<int>>();

        /// <summary>当前这位玩家此刻选中的资源点（默认全选，可在图上点进点出）。</summary>
        private readonly List<int> harvestCurrent = new List<int>();

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

        /// <summary>这张事件卡是不是「城市移动」（§6.4d）步 2 翻出来的 —— 是的话结算完选项还要补上步 3。</summary>
        private bool pendingEventFromCityMove;

        /// <summary>城市移动的出发点资源点 id（步 1–2 时记下，步 3 要在这里放影响力）。</summary>
        private int pendingEventOriginNode = -1;

        /// <summary>2 人局（§12 变体 4）：正用设施供应区面板做「盖放前弃 1 张设施牌」这一步。</summary>
        private bool supplyDiscardMode;

        /// <summary>正在结算的那个选项下标；结算完回到 -1。</summary>
        private int pendingEventOption = -1;

        /// <summary>这次选项要不要执行条件段（§11.2）；玩家点「放弃效果」时为 false。</summary>
        private bool pendingEventAcceptCost = true;

        /// <summary>事件选项效果的试跑会话（和建设 L4 同一套机制）：候选落点要玩家点地图或弹窗来答。</summary>
        private EffectChoiceSession eventSession;

        /// <summary>
        /// 联机客户端：事件选项已经交给主机、正等回放确认。此时事件卡先留着（最小化、锁住其它行动），
        /// 确认后由 <see cref="ConfirmOnlineResolve"/> 收尾；主机拒绝则由 <see cref="ShowOnlineError"/>
        /// 把卡面还原让玩家重选 —— 否则「已提交」这句假成功会让整局永久卡在这一步。
        /// </summary>
        private bool awaitingResolveConfirm;

        /// <summary>等确认期间要补的后续步：这张牌是不是探索翻的。</summary>
        private bool pendingResolveFromExplore;

        /// <summary>等确认期间要补的后续步：这张牌是不是城市移动翻的。</summary>
        private bool pendingResolveFromCityMove;

        /// <summary>等确认期间要补的后续步：城市移动的出发点。</summary>
        private int pendingResolveOriginNode = -1;

        public void Initialize(GameEngine engine, MapStateRenderer renderer, MapPointerInput pointerInput,
            GameHud hud, ActionPanel panel, EventCardPanel eventCardPanel = null, ChoicePopup choicePopup = null,
            FacilitySupplyPanel supplyPanel = null, CityPanelView cityPanel = null,
            CityPatternPanel patternPanel = null, CardPreviewPopup cardPreview = null,
            CharacterHandPanel handPanel = null, int localSeat = 0, bool lockToLocalSeat = false,
            string[] playerNames = null, PlayerCityPopup playerCityPopup = null, bool onlineClient = false)
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
            this.cardPreview = cardPreview;
            this.handPanel = handPanel;
            this.playerCityPopup = playerCityPopup;
            this.onlineClient = onlineClient;
            this.localSeat = localSeat;
            this.lockToLocalSeat = lockToLocalSeat;
            this.playerNames = playerNames ?? new string[0];

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
                panel.CharacterCardRequested += OnCharacterCardRequested;
            }

            if (eventCardPanel != null) eventCardPanel.OptionSelected += OnEventOptionSelected;
            if (supplyPanel != null) supplyPanel.CardClicked += OnSupplyCardClicked;
            if (cityPanel != null) cityPanel.SlotClicked += OnCitySlotClicked;
            if (patternPanel != null) patternPanel.CardClicked += OnPatternCardClicked;
            if (handPanel != null) handPanel.CardClicked += OnHandCardClicked;

            if (hud != null) hud.Bind(engine);
            if (hud != null) hud.SeatInspectRequested += OnSeatInspectRequested;
            if (playerCityPopup != null) playerCityPopup.SlotClicked += OnPlayerCitySlotClicked;

            SetMode(ActionMode.None);
            RefreshAll();

            // 万一进场时已经停在「等玩家选点」的采集阶段（例如重载场景），直接接上选点流程。
            if (engine != null && engine.HarvestNeedsChoices) BeginHarvestFlow();
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
                panel.CharacterCardRequested -= OnCharacterCardRequested;
            }

            if (eventCardPanel != null) eventCardPanel.OptionSelected -= OnEventOptionSelected;
            if (supplyPanel != null) supplyPanel.CardClicked -= OnSupplyCardClicked;
            if (cityPanel != null) cityPanel.SlotClicked -= OnCitySlotClicked;
            if (patternPanel != null) patternPanel.CardClicked -= OnPatternCardClicked;
            if (handPanel != null) handPanel.CardClicked -= OnHandCardClicked;
            if (hud != null) hud.SeatInspectRequested -= OnSeatInspectRequested;
            if (playerCityPopup != null) playerCityPopup.SlotClicked -= OnPlayerCitySlotClicked;
        }

        // —— 引擎事件 ——

        /// <summary>
        /// 状态栏每行左侧的眼睛按钮：弹这名玩家的城市面板（只读）。
        /// 座位号已经由 GameHud 按当行换算好，这里直接开；再点同一行则收起（弹窗内部处理）。
        /// </summary>
        private void OnSeatInspectRequested(int seat)
        {
            if (engine == null) return;

            if (playerCityPopup == null)
            {
                SetStatus("没接「玩家城市」弹窗，暂时看不了这行玩家的城市面板。");
                return;
            }

            playerCityPopup.Show(engine.State, seat, SeatLabel(seat));
        }

        private void OnCommandApplied(GameState state, ICommand command)
        {
            RefreshAll();

            // 联机客户端：事件选项的回放确认到了，这才真正收尾（关事件卡、补探索 / 城市移动的后续步）。
            // 这条分支必须在 SyncPendingEventCard 之前：两者都靠 pendingEvent* 判断「本地已经有一张牌」。
            if (onlineClient && awaitingResolveConfirm && command is ResolveEventOptionCommand applied
                && applied.ActorSeat == localSeat)
            {
                ConfirmOnlineResolve(applied);
                return;
            }

            SyncPendingEventCard(state, command);
        }

        // 阶段推进（含两轮行动自动进采集）发生在命令之后，这里补一次刷新，保证画面与状态一致。
        private void OnPhaseChanged(GameState state, TurnPhase from, TurnPhase to)
        {
            RefreshAll();

            // 进入采集阶段：不再自动结算，改成逐位玩家自选资源点（§7）。
            if (to == TurnPhase.Harvest && state.HarvestPending) BeginHarvestFlow();
        }

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

            // 「路费付给谁」「入场效果挑一个」这类小选择窗开着时不许换行动：换行动会清掉这次的探索计划或
            // 建设的暂存，弹窗却还留在屏幕上，之后再点它的选项，答案会落进一个已经没有主的流程里。
            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("请先答完当前弹出的选择。");
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
                SetStatus("当前不是玩家行动阶段——阶段会由流程自动推进。");
                return;
            }

            SetMode(requested);
        }

        private void OnCommitRequested()
        {
            if (engine == null) return;

            // 「路费付给谁」「入场效果挑一个」这类小选择窗还开着时不许再点「确认」：
            // 否则会重跑一遍 CommitExplore，把队列清空重建、弹窗被同一个问题覆盖，看起来就是「点了确认没反应／选项一闪就没了」。
            if (choicePopup != null && choicePopup.IsVisible) return;

            // 探索的「确认」= 用选好的目标点与航道正式执行探索（之后翻牌、选选项、补放影响力）。
            if (mode == ActionMode.Explore)
            {
                CommitExplore();
                return;
            }

            // 采集选点的「确认」= 定下这位玩家的选择，轮到下一位；全部选完即统一结算（§7）。
            if (mode == ActionMode.Harvest)
            {
                ConfirmHarvest();
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

        /// <summary>取消：退出当前行动，并丢弃已做的选点（含调度已暂存的步骤、建设的半程）。</summary>
        private void OnCancelRequested()
        {
            // 小选择窗开着时，「取消」只作用于那个窗口：否则会把正在进行的探索整段作废，
            // 而窗口还留在屏幕上，看上去像「点了取消却什么都没发生」。
            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("请先答完当前弹出的选择（要放弃这次探索就点那个窗口的取消）。");
                return;
            }

            // 事件选项的返回键（事件卡已最小化、正等玩家点地图时）：退掉最近一个答案重问；
            // 退无可退就回到事件卡，让玩家改选别的选项。
            if (eventSession != null)
            {
                if (eventSession.Back()) ContinueEventChoices();
                else BackToEventCard();
                return;
            }

            // 角色牌效果的「取消」= 返回键：退掉最近一个答案；退无可退就放弃这次使用（牌仍在盖放位）。
            if (characterSession != null)
            {
                if (characterSession.Back()) ContinueCharacterChoices();
                else ExitCharacterFlow("已返回，牌还在盖放位；可以重新点「使用角色牌」。");
                return;
            }

            // 城市样式特殊行动的「取消」= 返回键：退掉最近一个答案；退无可退就放弃这次使用（标记未动、费用未扣）。
            if (specialSession != null)
            {
                if (specialSession.Back()) ContinueSpecialChoices();
                else ExitSpecialFlow("已返回，没有使用任何城市样式特殊行动。");
                return;
            }

            // 采集选点的「取消」= 放弃手改，恢复成默认全选（不会动已定下的其他玩家的选择）。
            if (mode == ActionMode.Harvest)
            {
                LoadHarvestDefaults(HarvestSeat);
                SetStatus("已恢复为默认全选——不想采的点直接在图上点掉即可。");
                UpdateHint();
                return;
            }

            // 建设的「取消」= 返回键：逐层往回退一层，不撤销引擎状态（所有选择都在派发命令前完成）。
            if (mode == ActionMode.Build && buildStep != BuildStep.NotBuilding && buildStep != BuildStep.PickCard)
            {
                switch (buildStep)
                {
                    case BuildStep.Pay:
                        BackToPickCard();
                        return;
                    case BuildStep.Slot:
                        BackToBuildPay();
                        return;
                    case BuildStep.Confirm:
                        EnterBuildSlot();
                        return;
                    case BuildStep.Effect:
                        if (buildSession != null && buildSession.Back()) ContinueBuildChoices();
                        else BackToBuildConfirm();
                        return;
                }
            }

            if (mode == ActionMode.None) return;

            SetMode(ActionMode.None); // 内部会清空 relocateFrom 与 pendingMoves
            SetStatus("已取消当前行动。");
        }

        // —— 地图点击 ——

        private void OnMapClicked(MapHit hit)
        {
            if (engine == null) return;
            GameState state = engine.State;

            // 事件选项里的「点地图」选择（与建设 L4 / 主要行动同源）。此时事件卡是最小化状态，
            // 所以必须挡在下面「事件还没结算完」的守卫前面。
            if (eventSession != null && HandleEventMapPick(hit)) return;

            // 角色牌效果里的「点地图」选择（与部署 / 调度 / 城市移动同源）。
            if (characterSession != null && HandleCharacterMapPick(hit)) return;

            // 城市样式特殊行动效果里的「点地图」选择（与部署 / 调度 / 城市移动同源）。
            if (specialSession != null && HandleSpecialMapPick(hit)) return;

            // 事件卡弹窗还开着（展开或最小化）时，地图点击一律忽略 ——
            // 事件还没结算完就不该去点资源点（最小化只是为了方便看地图，不是放弃结算）。
            if (eventCardPanel != null && eventCardPanel.IsVisible) return;

            // 小选择弹窗（如「路费付给谁」）开着时同理，先把它答完。
            if (choicePopup != null && choicePopup.IsVisible) return;

            // 建设 L4 的「点地图」选择：与部署 / 调度 / 城市移动同源，直接点地图上的合法位置作答。
            if (mode == ActionMode.Build && buildStep == BuildStep.Effect && HandleBuildMapPick(hit)) return;

            // 开局入场（§4.3）：第 1 回合的角色牌盖放阶段，点绿色资源点本体即为「决定初始资源点」。
            // 此时还没有行动模式，所以放在最前面单独处理。
            if (mode == ActionMode.None && IsSetupPhase(state))
            {
                HandleChooseStartNode(hit, state);
                return;
            }

            // 采集阶段（§7）：点地图资源点加入 / 移除当前玩家的采集选择。
            if (mode == ActionMode.Harvest)
            {
                HandleHarvestPick(hit, state);
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
                // 城市都放下了，但可能还有人的事件牌没结算完（CurrentOperator 会停在那位身上）。
                int pending = ChooseStartNodeCommand.CurrentOperator(state);
                SetStatus(pending >= 0
                    ? $"{SeatLabel(pending)} 还在结算翻出的事件牌，选完奖励才轮得下一位。"
                    : "所有玩家都已经决定了初始资源点。");
                return;
            }

            if (hit.Kind != MapHitKind.NodeBody)
            {
                SetHint($"轮到 {SeatLabel(seat)}：请点击一个绿色资源点本体作为初始资源点。");
                return;
            }

            Dispatch(new ChooseStartNodeCommand(seat, hit.NodeId));

            // 联机客户端：命令才刚交给主机，状态还没回放（事件牌有没有翻出来都不知道），
            //   这里先不弹；回放后由 SyncPendingEventCard 补开。
            if (!onlineClient) ShowEventCard(seat, hit.NodeId);

            // 最后一名玩家定完初始资源点后，2 人局（§12 变体 4）的弃设施牌窗本该弹出 —— 但事件卡
            //   正开着时这里不会弹；等事件结算完 <see cref="ResolveEventOption"/> 会再刷一次把它放出来。
            RefreshFacilityViews();
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

            // 已经为同一个资源点弹着这张牌：直接算成功。联机客户端由 OnCommandApplied 补弹的这一手，
            // 可能比原调用点晚不到一帧就先把牌弹出来了，别重复 Show 一次把玩家的选择重置掉。
            if (eventCardPanel.IsVisible && pendingEventNodeId == nodeId) return true;

            MapNodeState node = engine.State.Map.Node(nodeId);
            if (!node.HasResourceToken || node.EventCardId < 0) return false;
            if (node.EventResolved) return false; // 已经结算过的牌不再弹（城市移动的目标点可能是早先探索过的点）

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
        /// 联机客户端专用：状态回放后补开「刚翻出来的那张事件牌」。
        ///
        /// 客户端派发命令只是把命令交给主机（<c>NetworkGameSession.Submit</c> 不本地执行），而
        /// <see cref="HandleChooseStartNode"/> / <see cref="ExecuteExplore"/> / <see cref="HandleMoveCity"/>
        /// 都是「派发完立刻 <see cref="ShowEventCard"/>」—— 此刻状态还是旧的，牌根本弹不出来。
        /// 后果不是难看而是卡死：开局选点翻出的牌没人结算，<c>ChooseStartNodeCommand.CurrentOperator</c>
        /// 会一直停在那位玩家身上，整局出不了「角色牌盖放」阶段。
        ///
        /// 主机与单机不走这里（它们的命令在 <see cref="Dispatch"/> 里当场生效，原调用点自己就弹了）。
        /// </summary>
        private void SyncPendingEventCard(GameState state, ICommand command)
        {
            if (!onlineClient || state == null || command == null || eventCardPanel == null) return;
            if (pendingEventNodeId >= 0 || eventCardPanel.IsVisible) return; // 本地已经有一张牌在结算

            int seat;
            int nodeId;
            bool fromExplore = false;
            bool fromCityMove = false;
            int originNode = -1;
            string lead;

            switch (command)
            {
                case ChooseStartNodeCommand start:
                    seat = start.ActorSeat;
                    nodeId = start.NodeId;
                    lead = $"{SeatLabel(seat)} 翻出了事件牌，请选择事件选项。";
                    break;

                case ExploreCommand explore:
                    seat = explore.ActorSeat;
                    nodeId = explore.NodeId;
                    fromExplore = true;
                    lead = "[探索] 目标资源点翻出了事件牌，请先选择事件选项。";
                    break;

                case MoveMobileCityCommand move:
                    seat = move.ActorSeat;
                    nodeId = move.TargetNodeId;
                    fromCityMove = true;
                    originNode = state.CityMoveOriginNodeId; // 状态已经回放，这里读到的才是真实出发点
                    lead = "[城市移动] 目标资源点翻出了事件牌，请先选择事件选项。";
                    break;

                default:
                    return;
            }

            // 别人翻出来的牌由他自己结算：别在旁观者的机器上也弹出来。
            if (seat != localSeat) return;

            if (!ShowEventCard(seat, nodeId))
            {
                // 没翻出牌（目标点本来就有指示物 / 这张牌已经结算过）：城市移动这里补上步 3，探索本来就不用补。
                if (fromCityMove && originNode >= 0)
                {
                    FinishCityMove(seat, originNode, null);
                    return;
                }

                // 牌翻出来了、却弹不出去（多半是 EventCardPanel 的 Card Faces 缺这张牌的卡面）：
                // 这时既不能自己选，也没法让主机替他选 —— 只能说清楚「卡在哪」，别再静默地把人晾着。
                MapNodeState node = state.Map.Node(nodeId);
                if (node != null && node.HasResourceToken && node.EventCardId >= 0 && !node.EventResolved)
                {
                    SetStatus($"{SeatLabel(seat)} 翻出了事件牌 {node.EventCardId}，" +
                              $"但事件卡弹窗里没有这张牌的卡面（EventCardPanel 的 Card Faces 缺少该素材），" +
                              "暂时无法选择奖励。");
                }
                return;
            }

            pendingEventFromExplore = fromExplore;
            pendingEventFromCityMove = fromCityMove;
            if (fromCityMove) pendingEventOriginNode = originNode;
            SetStatus(lead);
        }

        /// <summary>
        /// 玩家点了卡面上的某个选项。分两步问清楚再派发结算命令：
        /// 1) 选项含「→」条件段（§11.2）时，先问「执行条件段」还是「放弃效果」（放弃则整个效果结束）——两层都可返回；
        /// 2) 选项效果里若还有要玩家点选的地方（如「放置影响力」），与主要行动同源：直接点地图或弹窗作答。
        /// 结算完关掉事件卡；若这张牌是「探索」翻出来的，紧接着补第三步 —— 在目标资源点放影响力（§6.4c）。
        /// </summary>
        private void OnEventOptionSelected(int optionIndex)
        {
            if (engine == null || pendingEventNodeId < 0) return;

            // 上一条结算还在等主机确认：别再提交第二条，否则主机那边会多出一条「已经结算过了」的拒绝。
            if (awaitingResolveConfirm)
            {
                SetStatus("[联机] 上一条事件选项还在等主机确认，请稍等…");
                return;
            }

            EventOption option = EventOptionAt(engine.State, optionIndex);
            if (option == null)
            {
                SetStatus($"[事件] 选不出第 {optionIndex + 1} 个选项，请关掉事件卡重新点一次。");
                return;
            }

            pendingEventOption = optionIndex;

            // 没有条件段（或没接线弹窗）就直接执行结果段。
            if (!option.HasCost || choicePopup == null)
            {
                BeginEventEffects(option, acceptCost: true);
                return;
            }

            // 条件式选项：先让玩家决定要不要做「→」前那一段；返回则什么都不做，可以改选别的选项。
            choicePopup.Show(
                $"这个选项要先完成：{DescribeOps(option.Cost)}\n完成之后才能拿到结果段；放弃则结束这个效果。",
                new[] { "执行条件段", "放弃效果" }, choice =>
                {
                    if (choice == 0) BeginEventEffects(option, acceptCost: true);
                    else if (choice == 1) ResolveEventOption(acceptCost: false, answers: null);
                    else SetStatus("已返回，可以改选这张事件卡上的其它选项。");
                });
        }

        /// <summary>当前事件卡上的第 optionIndex 个选项；越界返回 null。</summary>
        private EventOption EventOptionAt(GameState state, int optionIndex)
        {
            if (state == null || pendingEventNodeId < 0) return null;

            MapNodeState node = state.Map.Node(pendingEventNodeId);
            if (node == null || node.EventCardId < 0) return null;
            if (EventCardPanel.SpriteIndexOf(node.EventCardId) < 0) return null; // 未知号段，ICardCatalog.Event 会抛

            EventCardDef card = state.Catalog.Event(node.EventCardId);
            return optionIndex >= 0 && optionIndex < card.Options.Count ? card.Options[optionIndex] : null;
        }

        /// <summary>把一串效果单元拼成给玩家看的短句（直接用 <see cref="RewardOp.ToString"/> 的中文描述）。</summary>
        private static string DescribeOps(IReadOnlyList<RewardOp> ops)
        {
            if (ops == null || ops.Count == 0) return "（无）";

            var parts = new List<string>(ops.Count);
            foreach (RewardOp op in ops) parts.Add(op.ToString());
            return string.Join("、", parts);
        }

        /// <summary>
        /// 在状态副本上试跑这个选项的效果：跑到需要玩家选择处就停下（点地图 / 弹窗作答），
        /// 答案收齐后原样重放给正式命令。机制与建设 L4 完全一致（§11 效果执行器 + 确定性重放）。
        /// </summary>
        private void BeginEventEffects(EventOption option, bool acceptCost)
        {
            if (engine == null || option == null) return;

            pendingEventAcceptCost = acceptCost;

            // 没接线弹窗就退回自动结算，别把玩家卡在流程里。
            if (choicePopup == null)
            {
                ResolveEventOption(acceptCost, answers: null);
                return;
            }

            GameState state = engine.State;
            int nodeId = pendingEventNodeId;
            int seat = pendingEventSeat;

            eventSession = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.ExecuteOption(sandbox, seat, option, acceptCost,
                    choices, anchorNodeId: nodeId));
            eventSession.Advance();
            ContinueEventChoices();
        }

        /// <summary>抛出 / 点选事件选项的下一个待答选择；退无可退就退回事件卡重选选项。</summary>
        private void ContinueEventChoices()
        {
            if (eventSession == null) return;

            if (eventSession.IsComplete)
            {
                IReadOnlyList<int> answers = eventSession.Answers;
                eventSession = null;
                ResolveEventOption(pendingEventAcceptCost, answers);
                return;
            }

            ChoiceRequest request = eventSession.Pending;

            // 候选是地图位置的走「点地图」：把事件卡最小化让地图可点，与部署 / 调度同源。
            if (request.Pick != null && request.Pick.Kind != ChoicePickKind.None)
            {
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                if (eventCardPanel != null) eventCardPanel.Collapse();
                if (panel != null) panel.SetLocked(true); // 其余行动按钮锁死，只留「取消」当返回键
                SetStatus("请直接点地图上列出的位置作答；点「取消」可退回重选选项。");
                return;
            }

            EffectChoiceSession session = eventSession;
            var options = new List<string>(request.Options.Count);
            foreach (ChoiceOption item in request.Options) options.Add(item.Label);

            choicePopup.Show(request.Prompt, options, choice =>
            {
                if (session != eventSession) return; // 会话已作废（例如中途换了行动）

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinueEventChoices();
                    return;
                }

                if (session.Back()) ContinueEventChoices();
                else BackToEventCard();
            });
        }

        /// <summary>退回事件卡：丢弃这次选项的收集，恢复卡面让玩家改选别的选项（不撤销引擎状态）。</summary>
        private void BackToEventCard()
        {
            eventSession = null;
            if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
            if (panel != null) panel.SetLocked(false);
            if (eventCardPanel != null) eventCardPanel.Expand();
            SetStatus("已返回，可以重新选择这张事件卡上的选项。");
        }

        /// <summary>事件选项里「点地图」的那一次选择：把点击翻译成选项下标（与建设 L4 共用同一段翻译）。</summary>
        private bool HandleEventMapPick(MapHit hit)
        {
            if (eventSession == null || eventSession.IsComplete) return false;

            ChoicePick pick = eventSession.Pending != null ? eventSession.Pending.Pick : null;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (!TryTranslateMapPick(pick, hit, out int index)) return true;

            eventSession.Answer(index);
            ContinueEventChoices();
            return true;
        }

        /// <summary>
        /// 照暂存的选择派发事件结算命令，然后收尾（关面板、探索补第三步）。
        /// <paramref name="answers"/> 为 null 表示这次选项没有要玩家点选的地方（交给引擎自动结算）。
        /// </summary>
        private void ResolveEventOption(bool acceptCost, IReadOnlyList<int> answers)
        {
            if (engine == null || pendingEventNodeId < 0) return;

            int seat = pendingEventSeat;
            int nodeId = pendingEventNodeId;
            int optionIndex = pendingEventOption;
            bool fromExplore = pendingEventFromExplore;
            bool fromCityMove = pendingEventFromCityMove;
            int cityMoveOrigin = pendingEventOriginNode;

            IEffectChoices choices = answers == null ? null : new RecordedChoices(answers, strict: false);
            var command = new ResolveEventOptionCommand(seat, nodeId, optionIndex, acceptCost,
                choices: choices);

            if (!Dispatch(command)) return; // 结算失败时把面板留着，好让玩家换个选项重试

            // 联机客户端：上面的 Dispatch 只是「交给主机」，本地状态一个字都没变。
            // 这时候若照单机那样收尾（关掉事件卡、清掉 pendingEvent*），主机一旦拒绝，
            // 玩家就再也没有重试的入口 —— 整局会永久停在「等他选事件奖励」这一步。
            // 所以收尾推迟到回放确认（ConfirmOnlineResolve），拒绝则由 ShowOnlineError 还原卡面。
            if (onlineClient)
            {
                awaitingResolveConfirm = true;
                pendingResolveFromExplore = fromExplore;
                pendingResolveFromCityMove = fromCityMove;
                pendingResolveOriginNode = cityMoveOrigin;
                eventSession = null;
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                if (panel != null) panel.SetLocked(true);   // 只留「取消」当返回键
                if (eventCardPanel != null) eventCardPanel.Collapse(); // 留着牌面，等确认再关
                SetStatus("[联机] 已提交事件选项，等待主机确认…");
                return;
            }

            eventSession = null;
            if (panel != null) panel.SetLocked(false);
            if (eventCardPanel != null)
            {
                eventCardPanel.Expand();
                eventCardPanel.Hide();
            }

            ClearPendingEvent();

            // 事件卡关掉了，若正好轮到 2 人局弃设施牌（§12 变体 4），这一步把弃牌窗放出来。
            RefreshFacilityViews();

            if (fromCityMove)
            {
                // 城市移动·步 3：事件结算完，在出发点放 1 个自己的影响力（走完这步才占用主要行动名额）。
                FinishCityMove(seat, cityMoveOrigin, DescribeEventStep(command));
                return;
            }

            if (!fromExplore)
            {
                SetStatus(DescribeEventStep(command));
                return;
            }

            // 探索·步 3：事件结算完，在目标资源点放 1 个自己的影响力（走完这步才占用主要行动名额）。
            var place = new ExploreInfluenceCommand(seat, nodeId);
            CommandResult result = DispatchToEngine(place);
            SetMode(ActionMode.None);
            SetStatus(result.Ok
                ? $"{DescribeEventStep(command)}；[探索] 已在目标资源点放置 1 个影响力。"
                : $"{DescribeEventStep(command)}；[探索] 放置影响力失败：{result.Error}");
        }

        /// <summary>
        /// 联机客户端：主机回放了这次事件结算（<paramref name="applied"/> 的 <see cref="ResolveEventOptionCommand.Report"/>
        /// 才是本局真实结果）。现在才关事件卡、清 pendingEvent*，并补探索 / 城市移动的后续步。
        /// </summary>
        private void ConfirmOnlineResolve(ResolveEventOptionCommand applied)
        {
            if (applied == null) return;

            bool fromExplore = pendingResolveFromExplore;
            bool fromCityMove = pendingResolveFromCityMove;
            int cityMoveOrigin = pendingResolveOriginNode;

            awaitingResolveConfirm = false;
            pendingResolveFromExplore = false;
            pendingResolveFromCityMove = false;
            pendingResolveOriginNode = -1;

            eventSession = null;
            if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
            if (panel != null) panel.SetLocked(false);
            if (eventCardPanel != null)
            {
                eventCardPanel.Expand();
                eventCardPanel.Hide();
            }

            ClearPendingEvent();

            // 事件卡关掉了，若正好轮到 2 人局弃设施牌（§12 变体 4），这一步把弃牌窗放出来。
            RefreshFacilityViews();

            if (fromCityMove)
            {
                FinishCityMove(applied.ActorSeat, cityMoveOrigin, DescribeEventStep(applied));
                return;
            }

            if (!fromExplore)
            {
                SetStatus(DescribeEventStep(applied));
                return;
            }

            // 探索·步 3：目标资源点放 1 个影响力。客户端同样只是提交，结果等主机再回放。
            CommandResult result = DispatchToEngine(new ExploreInfluenceCommand(applied.ActorSeat, applied.NodeId));
            SetMode(ActionMode.None);
            SetStatus(result.Ok
                ? $"{DescribeEventStep(applied)}；[探索] 已提交在目标资源点放置 1 个影响力。"
                : $"{DescribeEventStep(applied)}；[探索] 放置影响力失败：{result.Error}");
        }

        /// <summary>关事件卡时一并复位「正在结算哪张牌」的全部暂存字段。</summary>
        private void ClearPendingEvent()
        {
            pendingEventSeat = -1;
            pendingEventNodeId = -1;
            pendingEventFromExplore = false;
            pendingEventFromCityMove = false;
            pendingEventOriginNode = -1;
            pendingEventOption = -1;
            pendingEventAcceptCost = true;
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
            for (int i = 0; i < opponents.Count; i++) labels.Add($"付给 {SeatLabel(opponents[i])}");

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

            // 联机客户端：命令只提交给主机，此刻状态还没回放 —— 事件牌由 SyncPendingEventCard 回放后再弹。
            if (onlineClient)
            {
                SetStatus("[探索] 已提交给主机，等待判定…");
                return;
            }

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

            int seat = state.ActiveSeat;

            // 出发点：派发前先记下来 —— 联机客户端在命令回放之前，state.CityMoveOriginNodeId 还是旧值。
            int originNode = state.Player(seat).MobileCityNodeId;

            // 步 1–2（§6.4d）：付 3 源石、移动、清对手影响力，目标点没有指示物就翻牌补上。它不占行动名额。
            if (!Dispatch(new MoveMobileCityCommand(seat, hit.NodeId))) return;

            SetMode(ActionMode.None);

            // 联机客户端：状态还没回放，牌有没有翻出来还不知道；「先弹牌还是补步 3」交给回放后的
            //   SyncPendingEventCard 统一处理（否则会读着旧状态直接补步 3，与事件流程重复）。
            if (onlineClient)
            {
                SetStatus("[城市移动] 已提交给主机，等待判定…");
                return;
            }

            // 步 2 翻出了事件牌：先让玩家结算选项，结算完在 ResolveEventOption 里补上步 3。
            if (ShowEventCard(seat, hit.NodeId))
            {
                pendingEventFromCityMove = true;
                pendingEventOriginNode = originNode;
                SetStatus("[城市移动] 目标资源点翻出了事件牌，请先选择事件选项。");
                return;
            }

            FinishCityMove(seat, originNode, null);
        }

        /// <summary>
        /// 城市移动（§6.4d）步 3：在**出发点**放 1 个自己的影响力。走完这步才占用主要行动名额，
        /// 所以前面的步 1–2（以及中途的事件选项）都不能替它收尾。
        /// <paramref name="prefix"/> 是前面事件结算的简报（没有就传 null）。
        /// </summary>
        private void FinishCityMove(int seat, int originNodeId, string prefix)
        {
            CommandResult result = DispatchToEngine(new MoveCityInfluenceCommand(seat, originNodeId));
            SetMode(ActionMode.None);

            string lead = string.IsNullOrEmpty(prefix) ? "[城市移动]" : prefix + "；[城市移动]";
            SetStatus(result.Ok
                ? $"{lead} 已在出发点放置 1 个影响力。"
                : $"{lead} 放置影响力失败：{result.Error}");
        }

        // —— 建设（§6.4e）——

        /// <summary>
        /// L0：在设施牌弹窗里点了第 index 个牌位 —— 记下这张牌与建设者，收起选牌窗口，
        /// 进入 L1「放大卡面 + 选付费方式」。
        /// </summary>
        private void OnSupplyCardClicked(int index)
        {
            if (engine == null) return;

            // 2 人局（§12 变体 4）：这块面板此刻是「弃设施牌窗」，点一张 = 把它放回游戏盒。
            if (supplyDiscardMode)
            {
                HandleSupplyDiscard(index);
                return;
            }

            GameState state = engine.State;

            // 只读查看（常驻操作区「设施」按钮打开的那种）：点牌只放大看卡面，绝不进建设、绝不扣费。
            // 这段必须排在下面「不是建设就 return」之前 —— 查看时 mode 是 None，排后面就什么都不做了。
            if (supplyPanel != null && supplyPanel.IsViewOnly)
            {
                int previewId = SupplyCardAt(state, index);
                if (previewId >= 0) OpenFacilityPreview(state, previewId);
                return;
            }

            if (mode != ActionMode.Build || buildStep != BuildStep.PickCard) return;

            int cardId = SupplyCardAt(state, index);
            if (cardId < 0) return;

            buildSeat = ViewSeat(state);
            buildCardId = cardId;

            // 选中即收起：把选牌窗口让位给「放大卡面 + 选费用」这一步。
            if (supplyPanel != null) supplyPanel.SetOpen(false);
            EnterBuildPay();
        }

        /// <summary>
        /// 2 人局（§12 变体 4）：在设施供应区面板上点一张牌 = 把它放回游戏盒。
        /// 引擎会立刻从牌堆补牌让供应区保持 6 张；还有下一位玩家要弃就留在面板上，弃完自动收起。
        /// </summary>
        private void HandleSupplyDiscard(int index)
        {
            GameState state = engine.State;
            int cardId = SupplyCardAt(state, index);
            if (cardId < 0) return;

            if (!Dispatch(new DiscardSupplyFacilityCommand(state.SupplyDiscardSeat, cardId))) return;

            if (state.SupplyDiscardRemaining > 0)
                SetStatus($"轮到玩家 {state.SupplyDiscardSeat + 1}：请从设施供应区选 1 张牌放回游戏盒（§12 变体 4）。");
        }

        /// <summary>
        /// L1：放大卡面 + 三个按钮 —— 选项一（资源付费）、选项二（金券付费）、取消建设（回 L0）。
        /// 「免费」关键词或没写费用的牌不必问，直接进 L2；只有一种费用时另一个按钮自动隐藏。
        /// </summary>
        private void EnterBuildPay()
        {
            if (engine == null) return;

            GameState state = engine.State;
            FacilityCardDef card = state.Catalog.Facility(buildCardId);
            if (card == null) return;

            bool hasResources = card.ResourceCost.Count > 0;
            bool hasVouchers = card.VoucherCost > 0;

            if (card.IsFree || (!hasResources && !hasVouchers))
            {
                buildPayment = FacilityPayment.None;
                EnterBuildSlot();
                return;
            }

            if (cardPreview == null)
            {
                SetStatus("建设要用「卡面放大」弹窗来选付费方式，但它没接线。");
                return;
            }

            buildStep = BuildStep.Pay;

            PlayerState player = state.Player(buildSeat);
            IReadOnlyList<RewardOp> resourceCost = BuildFacilityCommand.EffectiveResourceCost(state, buildSeat, card);
            bool free = ActionRules.IgnoresCosts(state); // 开发者模式：不问付得起付不起

            string resourceText = hasResources ? $"选项一 · 资源：{DescribeCost(resourceCost)}" : string.Empty;
            string voucherText = hasVouchers ? $"选项二 · 金券：{card.VoucherCost} 张" : string.Empty;

            cardPreview.ShowForBuild(state, buildCardId,
                resourceText,
                hasResources && (free || FacilityCardDef.CanPayWithResources(resourceCost, player.Resources, player.Vouchers)),
                () =>
                {
                    buildPayment = FacilityPayment.Resources;
                    FinishBuildPay();
                },
                voucherText, hasVouchers && (free || player.Vouchers >= card.VoucherCost),
                () =>
                {
                    buildPayment = FacilityPayment.Vouchers;
                    FinishBuildPay();
                },
                BackToPickCard);

            UpdateHint();
        }

        /// <summary>L1 选好付费方式：收起卡面弹窗，进入 L2 选位。</summary>
        private void FinishBuildPay()
        {
            if (cardPreview != null) cardPreview.Hide();
            EnterBuildSlot();
        }

        /// <summary>L2：收起所有弹窗，城市面板空位高亮、其他行动按钮锁死，等玩家点一格。</summary>
        private void EnterBuildSlot()
        {
            buildStep = BuildStep.Slot;

            if (cardPreview != null) cardPreview.Hide();
            if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
            if (panel != null) panel.SetLocked(true);

            RefreshFacilityViews();
            UpdateHint();
        }

        /// <summary>L2 的返回键：回到 L1 重选付费方式；免建的牌没有 L1，直接回 L0。</summary>
        private void BackToBuildPay()
        {
            if (buildPayment == FacilityPayment.None)
            {
                BackToPickCard();
                return;
            }

            EnterBuildPay();
        }

        /// <summary>退回 L0 重选要建的设施牌（整条流程复位；已收集的入场效果答案一并丢弃）。</summary>
        private void BackToPickCard()
        {
            if (cardPreview != null) cardPreview.Hide();
            if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
            if (panel != null) panel.SetLocked(false);

            buildStep = BuildStep.PickCard;
            buildCardId = -1;
            buildSlot = -1;
            buildPayment = FacilityPayment.None;
            buildSession = null;

            if (supplyPanel != null) supplyPanel.SetOpen(true);
            RefreshFacilityViews();
            UpdateHint();
        }

        /// <summary>点了城市面板的第 slot 格：非建设模式弹出放大卡面；L2 则记下落点并弹 L3 确认。</summary>
        private void OnCitySlotClicked(int slot)
        {
            if (engine == null) return;

            GameState state = engine.State;

            // 非建设模式：点已建成的设施 → 弹出放大卡面，方便玩家看清卡面。
            if (mode != ActionMode.Build)
            {
                OpenCardPreview(state, slot);
                return;
            }

            if (buildStep != BuildStep.Slot)
            {
                SetStatus(buildStep == BuildStep.Effect
                    ? "请先答完入场效果的选择（点弹窗里的选项，或直接点地图上合法的位置）。"
                    : "请先在弹出的窗口里选好要建的牌与付费方式。");
                return;
            }

            buildSlot = slot;
            EnterBuildConfirm();
        }

        /// <summary>
        /// L3：确认弹窗「在此处建造《X》？+N 分；获得…」。确定 → L4 结算入场效果；取消 → 回 L2 选位。
        /// 分数取自卡面（可为负），「获得」是入场效果的预览。
        /// </summary>
        private void EnterBuildConfirm()
        {
            if (engine == null) return;

            GameState state = engine.State;
            FacilityCardDef card = state.Catalog.Facility(buildCardId);
            if (card == null) return;

            buildStep = BuildStep.Confirm;

            if (choicePopup == null)
            {
                SetStatus("建设确认弹窗没接线，直接按「确定」处理。");
                BeginBuildEffects();
                return;
            }

            string title = $"在此处建造《{card.Name}》？\n得分 {card.Score:+0;-0;0}；获得：{DescribeEntryEffects(card)}";

            choicePopup.Show(title, new[] { "确定建造" }, choice =>
            {
                if (engine == null) return;

                if (choice == 0) BeginBuildEffects();
                else EnterBuildSlot();
            });
        }

        /// <summary>
        /// L4：先确认好了「在此处建造」，再逐个问清入场效果里要玩家点选的节点，最后正式派发建设命令。
        /// 建到「面板正在显示的那名玩家」名下，保证建完当场就能在面板上看到（§6.4e）。
        /// </summary>
        private void BeginBuildEffects()
        {
            if (engine == null) return;

            GameState state = engine.State;
            FacilityCardDef card = state.Catalog.Facility(buildCardId);
            IReadOnlyList<RewardOp> effects = card != null ? card.EntryEffects : null;

            buildStep = BuildStep.Effect;

            // 没有入场效果（或没接线弹窗）就直接建，省掉一轮无意义的会话。
            if (effects == null || effects.Count == 0 || choicePopup == null)
            {
                DispatchBuild(Array.Empty<int>());
                return;
            }

            // 在状态副本上试跑整串入场效果：跑到需要选择处就停下，等玩家点完再继续。
            // 部分效果会连锁（二选一里再挑落点、探索翻出的事件选项、结算他牌的入场效果……），
            // 每轮都从副本重跑，所以后面几步的候选清单永远是基于前面答案的真实状态（§11）。
            buildSession = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, buildSeat, effects, choices));
            buildSession.Advance();
            ContinueBuildChoices();
        }

        /// <summary>
        /// 抛出 / 点选入场效果的下一个待答选择：
        /// 候选是地图位置（放 / 移 / 拿走影响力、选资源点）的走「点地图」，与主要行动同源；
        /// 其余抽象选项走弹窗。两边的返回键都是「退回上一个选择」，退无可退回 L3 确认。
        /// </summary>
        private void ContinueBuildChoices()
        {
            if (buildSession == null) return;

            if (buildSession.IsComplete)
            {
                IReadOnlyList<int> answers = buildSession.Answers;
                buildSession = null;
                DispatchBuild(answers);
                return;
            }

            ChoiceRequest request = buildSession.Pending;

            if (request.Pick != null && request.Pick.Kind != ChoicePickKind.None)
            {
                // 「同源」交互：与部署 / 调度 / 城市移动一样直接点地图上的合法位置，不再列一长串按钮。
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                UpdateHint();
                return;
            }

            var options = new List<string>(request.Options.Count);
            foreach (ChoiceOption option in request.Options) options.Add(option.Label);

            EffectChoiceSession session = buildSession;
            choicePopup.Show(request.Prompt, options, choice =>
            {
                if (session != buildSession) return; // 会话已被作废（例如中途换了行动）

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinueBuildChoices();
                    return;
                }

                // 返回键（弹窗上的取消）：退掉最近一个答案；退无可退就回 L3 确认。
                if (session.Back()) ContinueBuildChoices();
                else BackToBuildConfirm();
            });
        }

        /// <summary>L4 退到 L3：丢弃本次入场效果的收集，重新显示确认弹窗（再确定会重新开始收集）。</summary>
        private void BackToBuildConfirm()
        {
            buildSession = null;
            EnterBuildConfirm();
        }

        /// <summary>
        /// 「点地图」时把点击翻译成候选清单里的下标：与主要行动（部署 / 调度 / 城市移动 / 探索）同源，
        /// 建设 L4 与事件选项共用这一段。候选以外的位置点不出答案，只给一句提示（不打断流程）。
        /// 返回 false 表示这次点击不在候选之列（提示已写进状态行）。
        /// </summary>
        private bool TryTranslateMapPick(ChoicePick pick, MapHit hit, out int index)
        {
            index = -1;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (pick.Kind == ChoicePickKind.InfluenceSlot)
            {
                if (!hit.IsSlot)
                {
                    SetStatus("这里不是合法候选，请点击地图上列出的那些影响力空格。");
                    return false;
                }

                for (int i = 0; i < pick.Slots.Count; i++)
                {
                    if (pick.Slots[i].Equals(hit.Slot))
                    {
                        index = i;
                        return true;
                    }
                }
            }
            else
            {
                if (hit.Kind != MapHitKind.NodeBody)
                {
                    SetStatus("这里不是合法候选，请点击地图上的资源点本体。");
                    return false;
                }

                for (int i = 0; i < pick.NodeIds.Count; i++)
                {
                    if (pick.NodeIds[i] == hit.NodeId)
                    {
                        index = i;
                        return true;
                    }
                }
            }

            SetStatus("这个位置不在本次的候选项里，请点引导文字里列出的那些。");
            return false;
        }

        /// <summary>建设 L4 里「点地图」的那一次选择：照候选清单把点击翻译成选项下标。</summary>
        private bool HandleBuildMapPick(MapHit hit)
        {
            if (buildSession == null || buildSession.IsComplete) return false;

            ChoicePick pick = buildSession.Pending != null ? buildSession.Pending.Pick : null;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (!TryTranslateMapPick(pick, hit, out int index)) return true; // 不在候选之列，已提示

            buildSession.Answer(index);
            ContinueBuildChoices();
            return true;
        }

        /// <summary>照暂存的选择派发建设命令；成功后退出建设模式并把结果写进状态行。</summary>
        private void DispatchBuild(IReadOnlyList<int> answers)
        {
            if (engine == null) return;

            // 把玩家点出来的答案交给引擎重放一遍：引擎是确定性的，正式执行会得到与试跑一致的结果。
            var command = new BuildFacilityCommand(buildSeat, buildCardId, buildSlot, buildPayment,
                new RecordedChoices(answers, strict: false));

            if (!Dispatch(command))
            {
                // 引擎拒绝（费用不够、与「唯一」冲突、格子被占……）：原因已写进状态行，退回 L3 让玩家改。
                BackToBuildConfirm();
                return;
            }

            int seat = buildSeat;
            int cardId = buildCardId;
            int slot = buildSlot;
            SetMode(ActionMode.None);

            string suffix = command.Report != null && command.Report.Entries.Count > 0
                ? $"；入场效果：{command.Report.Describe()}"
                : string.Empty;
            SetStatus($"[建设] 玩家{seat + 1} 建造了《{FacilityName(engine.State, cardId)}》" +
                      $"，放在城市面板第 {slot + 1} 格{suffix}");
        }

        /// <summary>点已建成的设施格子时弹出放大卡面；空格子不响应。取牌座位必须与所显示的面板一致。</summary>
        private void OpenCardPreview(GameState state, int slot)
        {
            int seat = ViewSeat(state);
            PlayerState player = seat >= 0 && seat < state.Players.Count ? state.Players[seat] : null;

            int cardId = player != null && slot >= 0 && slot < player.CityPanelSlots.Length
                ? player.CityPanelSlots[slot]
                : -1;
            if (cardId < 0) return;

            if (cardPreview == null)
            {
                SetStatus("卡面放大弹窗没接线，看不到放大图。");
                return;
            }

            cardPreview.Show(state, cardId);
        }

        /// <summary>
        /// 点了「某位玩家城市面板」弹窗里的某一格：和常驻城市面板一样，弹该设施的放大卡面。
        /// 座位用弹窗正在看的那个人（不是 ViewSeat），放大弹窗自己会跳到最上层，所以不用先收起弹窗。
        /// </summary>
        private void OnPlayerCitySlotClicked(int seat, int slot)
        {
            if (engine == null) return;

            GameState state = engine.State;
            PlayerState player = seat >= 0 && seat < state.Players.Count ? state.Players[seat] : null;
            int cardId = player != null && slot >= 0 && slot < player.CityPanelSlots.Length
                ? player.CityPanelSlots[slot]
                : -1;
            if (cardId < 0) return;

            if (cardPreview == null)
            {
                SetStatus("卡面放大弹窗没接线，看不到放大图。");
                return;
            }

            cardPreview.Show(state, cardId);
        }

        /// <summary>设施供应区只读查看时点牌：放大这张设施牌，什么都不结算。</summary>
        private void OpenFacilityPreview(GameState state, int cardId)
        {
            if (cardPreview == null)
            {
                SetStatus("卡面放大弹窗没接线，看不到放大图。");
                return;
            }

            cardPreview.Show(state, cardId);
        }

        /// <summary>手牌只读查看时点牌：放大这张角色牌，什么都不结算。</summary>
        private void OpenCharacterPreview(GameState state, int cardId)
        {
            if (cardPreview == null)
            {
                SetStatus("卡面放大弹窗没接线，看不到放大图。");
                return;
            }

            cardPreview.ShowCharacter(state, cardId);
        }

        /// <summary>城市面板（样式牌弹窗）只读查看时点牌：放大这张样式牌，什么都不结算。</summary>
        private void OpenPatternPreview(GameState state, int cardId)
        {
            if (cardPreview == null)
            {
                SetStatus("卡面放大弹窗没接线，看不到放大图。");
                return;
            }

            cardPreview.ShowCityPattern(state, cardId);
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

        /// <summary>建设 L3 确认弹窗里那句「获得：…」：把这张牌的入场效果翻成人话（复用 RewardOp 自带的描述）。</summary>
        private static string DescribeEntryEffects(FacilityCardDef card)
        {
            if (card == null || card.EntryEffects.Count == 0) return "无入场效果";

            var parts = new List<string>(card.EntryEffects.Count);
            foreach (RewardOp op in card.EntryEffects) parts.Add(op.ToString());
            return string.Join("、", parts);
        }

        // —— 宣告城市样式（§6.3b / §6.5b）——

        /// <summary>
        /// 在样式牌弹窗里点了第 index 张牌：先算出这张牌的所有合法摆法（条件可旋转、不可翻转，§10.2）。
        /// 只有一组摆法就直接宣告；有多组就先让玩家挑用哪几格 —— 同一设施只能用一次（§6.5b 步 1），
        /// 挑哪几格会影响后面还能宣告哪些样式，所以这是玩家的权利，不能替玩家定。
        /// 失败时弹窗留着，好让玩家换一张样式牌重试（原因由引擎给出，如「凑不出条件」）。
        /// </summary>
        private void OnPatternCardClicked(int index)
        {
            if (engine == null) return;

            // 只读查看（常驻操作区「城市面板」按钮打开的那种）：点牌只放大看卡面，绝不宣告、绝不付费。
            // 必须排在下面「不是特殊行动 / 不是宣告就 return」之前 —— 查看时 mode 是 None。
            if (patternPanel != null && patternPanel.IsViewOnly)
            {
                GameState viewState = engine.State;
                if (index < 0 || index >= viewState.CityPatternCards.Count) return;
                OpenPatternPreview(viewState, viewState.CityPatternCards[index]);
                return;
            }

            // 特殊行动（§6.3f）：点样式牌 = 选要用哪张牌解锁的特殊行动，不用凑条件，直接进收费用 + 结算效果。
            if (mode == ActionMode.SpecialAction)
            {
                // 正在逐项收集这次特殊行动要玩家点选的东西时，不许换牌：换了会把半程的答案丢掉。
                if (specialSession != null)
                {
                    SetStatus("请先答完当前特殊行动要你点选的选择。");
                    return;
                }

                GameState patternState = engine.State;
                if (index < 0 || index >= patternState.CityPatternCards.Count) return;
                BeginSpecialAction(patternState.CityPatternCards[index]);
                return;
            }

            if (mode != ActionMode.Declare) return;

            GameState state = engine.State;
            int cardId = state.CityPatternCards[index];
            CityPatternCardDef card = state.Catalog.CityPattern(cardId);

            List<PatternMatch> matches = card.HasCondition
                ? CityPanelQuery.FindAll(state.Player(state.ActiveSeat), state.Catalog, card.Condition)
                : new List<PatternMatch>();

            // 凑不出（或条件还没录入）就直接派发，让引擎给出准确原因；
            // 只有一组摆法也不必问，直接用它。
            if (matches.Count <= 1 || choicePopup == null)
            {
                DeclarePatternWithPlacement(cardId, matches.Count > 1 ? 0 : -1);
                return;
            }

            var labels = new List<string>(matches.Count);
            for (int i = 0; i < matches.Count; i++)
                labels.Add($"摆法 {i + 1}：{DescribePatternSlots(matches[i])}（旋转 {matches[i].RotationDegrees}°）");

            choicePopup.Show(
                $"《{PatternName(state, cardId)}》有 {matches.Count} 组摆法，你想用哪几格？\n（用过的设施不能再用于下一次宣告）",
                labels, choice =>
                {
                    if (choice >= 0) DeclarePatternWithPlacement(cardId, choice);
                    else SetStatus("已返回，可以重新选择要宣告的城市样式牌。");
                });
        }

        /// <summary>照选定的摆法派发宣告命令，并把「用了哪几格 + 旋转角度」写进状态行。</summary>
        private void DeclarePatternWithPlacement(int cardId, int placementIndex)
        {
            if (engine == null || mode != ActionMode.Declare) return;

            GameState state = engine.State;
            var command = new DeclareCityPatternCommand(state.ActiveSeat, cardId, placementIndex);
            if (!Dispatch(command)) return; // 失败时弹窗留着，好让玩家换一张样式牌重试

            int seat = state.ActiveSeat;
            string slots = DescribePatternSlots(command.Match);
            int rotation = command.Match.RotationDegrees;

            SetMode(ActionMode.None); // 收起弹窗

            SetStatus($"[宣告] 玩家{seat + 1} 宣告了《{PatternName(state, cardId)}》" +
                      $"，使用城市面板 {slots}（旋转 {rotation}°）");
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

        // —— 使用城市样式特殊行动（§6.3f / §10.2-4）——

        /// <summary>
        /// 在样式牌弹窗里点了某张牌：这张牌解锁的特殊行动要收费用、结算效果，
        /// 其中可能有要玩家点选的节点（放置 / 移动影响力、自由分配支付……）。
        /// 先在状态副本上把「费用 + 效果」试跑到需要选择处就停下，逐个问完再正式派发（与建设 L4 / 角色牌同源）。
        /// 收集期间锁住其他行动按钮，只留「取消」当返回键。
        /// </summary>
        private void BeginSpecialAction(int cardId)
        {
            if (engine == null) return;

            GameState state = engine.State;
            CityPatternCardDef card = state.Catalog.CityPattern(cardId);
            if (card == null || !card.HasSpecialAction)
            {
                SetStatus($"《{PatternName(state, cardId)}》的特殊行动还没录入，暂时无法使用。");
                return;
            }

            specialCardId = cardId;
            specialStep = SpecialStep.Effect;

            IReadOnlyList<RewardOp> cost = card.SpecialActionCost;
            IReadOnlyList<RewardOp> effects = card.SpecialActionEffects;

            if (panel != null) panel.SetLocked(true);

            // 没有要玩家点选的选择（或没接线弹窗）就直接派发，省掉一轮无意义的会话。
            if (choicePopup == null)
            {
                DispatchSpecialAction(Array.Empty<int>());
                return;
            }

            int seat = state.ActiveSeat;
            specialSession = new EffectChoiceSession(state, (sandbox, choices) =>
            {
                // 与命令同款：先记「当前特殊行动所在样式牌」，供 PlaceInfluencePerPatternMarkers 取计数来源。
                int previous = sandbox.ActiveSpecialActionPatternId;
                sandbox.ActiveSpecialActionPatternId = cardId;
                EffectResolver.Execute(sandbox, seat, cost, choices);
                EffectReport report = EffectResolver.Execute(sandbox, seat, effects, choices);
                sandbox.ActiveSpecialActionPatternId = previous;
                return report;
            });
            specialSession.Advance();
            ContinueSpecialChoices();
        }

        /// <summary>抛出 / 点选特殊行动的下一个待答选择：点地图的候选走「点地图」，其余走弹窗（带返回键）。</summary>
        private void ContinueSpecialChoices()
        {
            if (specialSession == null) return;

            if (specialSession.IsComplete)
            {
                IReadOnlyList<int> answers = specialSession.Answers;
                specialSession = null;
                DispatchSpecialAction(answers);
                return;
            }

            ChoiceRequest request = specialSession.Pending;

            if (request.Pick != null && request.Pick.Kind != ChoicePickKind.None)
            {
                // 「同源」交互：与部署 / 调度 / 城市移动一样直接点地图上的合法位置，不再列一长串按钮。
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                UpdateHint();
                return;
            }

            var options = new List<string>(request.Options.Count);
            foreach (ChoiceOption option in request.Options) options.Add(option.Label);

            EffectChoiceSession session = specialSession;
            choicePopup.Show(request.Prompt, options, choice =>
            {
                if (session != specialSession) return; // 会话已被作废（例如中途换了行动）

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinueSpecialChoices();
                    return;
                }

                // 返回键：退掉最近一个答案；退无可退就放弃这次使用（标记未动、费用未扣）。
                if (session.Back()) ContinueSpecialChoices();
                else ExitSpecialFlow("已返回，没有使用任何城市样式特殊行动。");
            }, request.AllowCancel);
        }

        /// <summary>特殊行动效果里「点地图」的那一次选择：照候选清单把点击翻译成选项下标。</summary>
        private bool HandleSpecialMapPick(MapHit hit)
        {
            if (specialSession == null || specialSession.IsComplete) return false;

            ChoicePick pick = specialSession.Pending != null ? specialSession.Pending.Pick : null;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (!TryTranslateMapPick(pick, hit, out int index)) return true; // 不在候选之列，已提示

            specialSession.Answer(index);
            ContinueSpecialChoices();
            return true;
        }

        /// <summary>照收集到的答案派发「使用城市样式特殊行动」命令，并把结出的效果写进状态行。</summary>
        private void DispatchSpecialAction(IReadOnlyList<int> answers)
        {
            if (engine == null) return;

            GameState state = engine.State;
            int seat = state.ActiveSeat;
            int cardId = specialCardId;

            var command = new UsePatternSpecialActionCommand(seat, cardId,
                new RecordedChoices(answers, strict: false));

            if (!Dispatch(command))
            {
                // 引擎拒绝（没宣告过 / 牌上标记不可用 / 本回合已用过……）：原因已写进状态行，
                // 回到「选牌」这一步，让玩家换一张样式牌重试（与宣告失败时留着弹窗同理）。
                specialSession = null;
                specialStep = SpecialStep.PickCard;
                specialCardId = -1;
                if (panel != null) panel.SetLocked(false);
                return;
            }

            string suffix = command.Report != null && command.Report.Entries.Count > 0
                ? $"；{command.Report.Describe()}"
                : string.Empty;

            ExitSpecialFlow($"[特殊行动] 玩家{seat + 1} 使用了《{PatternName(state, cardId)}》的特殊行动{suffix}");
        }

        /// <summary>退出特殊行动流程：清掉收集会话、解锁其他行动按钮、收起样式牌弹窗，并可选地写一句状态。</summary>
        private void ExitSpecialFlow(string status)
        {
            specialSession = null;
            specialStep = SpecialStep.None;
            specialCardId = -1;
            if (panel != null) panel.SetLocked(false);
            SetMode(ActionMode.None); // 收起样式牌弹窗
            if (!string.IsNullOrEmpty(status)) SetStatus(status);
        }

        // —— 盖放与使用角色牌（§4.2 阶段 1 / §6.3a / §10.1）——

        /// <summary>
        /// 这名玩家现在能不能盖放角色牌：盖放阶段 + 第 1 回合开局入场已走完 + 2 人局弃设施已完成 + 本人还没盖放。
        /// 与 <see cref="CharacterHandPanel"/> 的刷新共用这一个判断，避免「看着能点、点了报错」。
        /// </summary>
        private static bool CanPlaceCharacter(GameState state, PlayerState player)
        {
            if (state == null || player == null) return false;
            if (state.Phase != TurnPhase.CharacterPlacement) return false;
            // 第 1 回合：开局入场（选初始资源点、结算事件牌）还没走完，先别盖放。
            if (state.TurnNumber == 1 && ChooseStartNodeCommand.CurrentOperator(state) >= 0) return false;
            // 2 人局变体：弃设施牌完成前不能盖放（§12 变体 4）。
            if (state.SupplyDiscardRemaining > 0) return false;
            return player.PlacedCharacterCard < 0;
        }

        /// <summary>盖放阶段点手牌里的第 index 张：把它盖放到本机玩家面前（§10.1.1）。</summary>
        private void OnHandCardClicked(int index)
        {
            if (engine == null) return;

            GameState state = engine.State;
            int seat = ViewSeat(state);
            PlayerState player = seat >= 0 && seat < state.Players.Count ? state.Players[seat] : null;
            if (player == null) return;

            // 与 CharacterHandPanel.Refresh 用同一套候选池：手牌为空时盖放要从弃牌区选（§10.1.1）。
            IReadOnlyList<int> pool = player.HandCharacterCards.Count > 0
                ? player.HandCharacterCards
                : player.DiscardCharacterCards;
            if (index < 0 || index >= pool.Count) return;

            int cardId = pool[index];

            // 现在不能盖放（阶段不对 / 开局入场没走完 / 2 人局还没弃设施 / 本人已盖放过）：
            // 这一下改成交给「放大看卡面」—— 看牌和盖放是两回事，不能因为不能盖放就连看都不给看。
            if (!CanPlaceCharacter(state, player))
            {
                OpenCharacterPreview(state, cardId);
                return;
            }

            if (Dispatch(new PlaceCharacterCardCommand(seat, cardId)))
            {
                // 最后一名玩家盖放完，引擎已经自动进入玩家行动；状态行给一句明确交代。
                if (state.Phase == TurnPhase.PlayerActions)
                {
                    SetStatus("所有人已盖放完毕——自动进入玩家行动第 1 轮。");
                    return;
                }

                CharacterCardDef def = state.Catalog.Character(cardId);
                SetStatus($"[盖放] 玩家{seat + 1} 盖放了《{(def != null ? def.Name : cardId.ToString())}》。");
            }
        }

        /// <summary>
        /// 点了「使用角色牌」（§6.3a）：先问这张盖放牌的「策略 / 计谋」，再逐个问清效果里要玩家点选的节点。
        /// 起始玩家用掉一种效果后接着弹「继续用另一种 / 结束」（§10.1.2）。
        /// </summary>
        private void OnCharacterCardRequested()
        {
            if (engine == null) return;

            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("请先答完当前弹出的选择。");
                return;
            }

            GameState state = engine.State;

            // 开发者模式放开阶段闸门，正常玩法只能在玩家行动阶段用。
            if (!state.DeveloperMode && state.Phase != TurnPhase.PlayerActions)
            {
                SetStatus("「使用角色牌」是玩家行动阶段的快速行动——请先把阶段推进过去。");
                return;
            }

            // 快速行动可以从任意「选了没做」的主要行动状态里直接发出：先退回 None，免得选中的部署 / 调度选点还挂着。
            SetMode(ActionMode.None);

            int seat = state.ActiveSeat;
            if (seat < 0 || seat >= state.Players.Count)
            {
                SetStatus("当前没有轮到任何玩家行动。");
                return;
            }

            PlayerState player = state.Player(seat);
            if (player.PlacedCharacterCard < 0)
            {
                SetStatus("你本回合还没有盖放角色牌，无法使用（§10.1.1）。");
                return;
            }
            if (player.UsedCharacterCardThisTurn)
            {
                SetStatus("本回合已经使用过这张角色牌了（§6.5a）。");
                return;
            }

            CharacterCardDef def = state.Catalog.Character(player.PlacedCharacterCard);
            if (def == null || !def.HasEffects)
            {
                SetStatus($"「{(def != null ? def.Name : player.PlacedCharacterCard.ToString())}」" +
                          "的策略 / 计谋效果还没有录入。");
                return;
            }

            if (choicePopup == null)
            {
                SetStatus("使用角色牌的弹窗没接线，无法选策略 / 计谋。");
                return;
            }

            characterSeat = seat;
            characterCardId = player.PlacedCharacterCard;

            var options = new List<string>(2);
            var kinds = new List<CharacterEffectKind>(2);
            if (!player.UsedCharacterStrategyThisTurn)
            {
                options.Add($"策略 · {def.StrategyName}");
                kinds.Add(CharacterEffectKind.Strategy);
            }
            if (!player.UsedCharacterTacticsThisTurn)
            {
                options.Add($"计谋 · {def.TacticsName}");
                kinds.Add(CharacterEffectKind.Tactics);
            }

            choicePopup.Show($"使用《{def.Name}》的哪个效果？", options, choice =>
            {
                if (choice < 0 || choice >= kinds.Count)
                {
                    SetStatus("已取消使用角色牌。");
                    return;
                }
                BeginCharacterEffect(kinds[choice]);
            });
        }

        /// <summary>
        /// 发动已选定的那一种效果：在状态副本上试跑整串效果，跑到需要选择处就停下，
        /// 等玩家点完再继续（与建设 L4 / 事件选项共用同一套 <see cref="EffectChoiceSession"/>）。
        /// 收集期间锁住其他行动按钮，只留「取消」当返回键。
        /// </summary>
        private void BeginCharacterEffect(CharacterEffectKind kind)
        {
            if (engine == null) return;

            GameState state = engine.State;
            CharacterCardDef def = state.Catalog.Character(characterCardId);
            IReadOnlyList<RewardOp> ops = def != null ? def.OpsOf(kind) : null;

            characterEffect = kind;
            characterStep = CharacterStep.Effect;

            if (panel != null) panel.SetLocked(true);

            if (ops == null || ops.Count == 0 || choicePopup == null)
            {
                DispatchCharacterEffect(Array.Empty<int>());
                return;
            }

            characterSession = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, characterSeat, ops, choices));
            characterSession.Advance();
            ContinueCharacterChoices();
        }

        /// <summary>抛出 / 点选这次角色牌效果的下一个待答选择：点地图的候选走「点地图」，其余走弹窗。</summary>
        private void ContinueCharacterChoices()
        {
            if (characterSession == null) return;

            if (characterSession.IsComplete)
            {
                IReadOnlyList<int> answers = characterSession.Answers;
                characterSession = null;
                DispatchCharacterEffect(answers);
                return;
            }

            ChoiceRequest request = characterSession.Pending;

            if (request.Pick != null && request.Pick.Kind != ChoicePickKind.None)
            {
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                UpdateHint();
                return;
            }

            var options = new List<string>(request.Options.Count);
            foreach (ChoiceOption option in request.Options) options.Add(option.Label);

            EffectChoiceSession session = characterSession;
            choicePopup.Show(request.Prompt, options, choice =>
            {
                if (session != characterSession) return; // 会话已被作废（例如中途换了行动）

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinueCharacterChoices();
                    return;
                }

                // 返回键：退掉最近一个答案；退无可退就放弃这次效果（牌还在盖放位，未被弃置）。
                if (session.Back()) ContinueCharacterChoices();
                else
                {
                    ExitCharacterFlow("已返回，牌还在盖放位；可以重新点「使用角色牌」。");
                }
            }, request.AllowCancel);
        }

        /// <summary>角色牌效果里「点地图」的那一次选择：照候选清单把点击翻译成选项下标。</summary>
        private bool HandleCharacterMapPick(MapHit hit)
        {
            if (characterSession == null || characterSession.IsComplete) return false;

            ChoicePick pick = characterSession.Pending != null ? characterSession.Pending.Pick : null;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (!TryTranslateMapPick(pick, hit, out int index)) return true; // 不在候选之列，已提示

            characterSession.Answer(index);
            ContinueCharacterChoices();
            return true;
        }

        /// <summary>照收集到的答案派发「使用角色牌」命令；起始玩家用掉一种后接着问是否连用另一种。</summary>
        private void DispatchCharacterEffect(IReadOnlyList<int> answers)
        {
            if (engine == null) return;

            var command = new UseCharacterCardCommand(characterSeat, characterEffect,
                new RecordedChoices(answers, strict: false));

            if (!Dispatch(command))
            {
                // 引擎拒绝（未盖放 / 已用过 / 非起始玩家连用……）：原因已写进状态行。
                ExitCharacterFlow(null);
                return;
            }

            CharacterCardDef def = engine.State.Catalog.Character(characterCardId);
            string effectName = def != null ? def.NameOf(characterEffect) : characterEffect.ToString();
            string suffix = command.Report != null && command.Report.Entries.Count > 0
                ? $"；效果：{command.Report.Describe()}"
                : string.Empty;
            SetStatus($"[角色牌] 玩家{characterSeat + 1} 发动《{(def != null ? def.Name : characterCardId.ToString())}》" +
                      $"的{effectName}{suffix}");

            characterSession = null;

            // 起始玩家用掉一种效果后，牌还留在盖放位等决定是否连用（§10.1.2）。
            if (engine.State.Player(characterSeat).CharacterSecondEffectPending) BeginSecondEffectPrompt();
            else ExitCharacterFlow(null);
        }

        /// <summary>起始玩家用掉一种效果后：问「继续用另一种 / 结束」，期间不得插入其他行动（§10.1.2）。</summary>
        private void BeginSecondEffectPrompt()
        {
            if (engine == null) return;

            characterStep = CharacterStep.Second;

            if (choicePopup == null)
            {
                characterStep = CharacterStep.None;
                SetStatus("起始玩家已用掉一种角色牌效果：再点「使用角色牌」发动另一种，或点「取消」结束。");
                return;
            }

            choicePopup.Show(
                "起始玩家可以按任意顺序连续发动策略与计谋；两种效果之间不能插入别的行动（§10.1.2）。",
                new[] { "继续使用另一种效果", "结束角色牌使用" },
                choice =>
                {
                    if (choice == 0)
                    {
                        PlayerState p = engine.State.Player(characterSeat);
                        CharacterEffectKind other = p.UsedCharacterStrategyThisTurn
                            ? CharacterEffectKind.Tactics
                            : CharacterEffectKind.Strategy;
                        BeginCharacterEffect(other);
                    }
                    else
                    {
                        EndCharacterUse();
                    }
                });
        }

        /// <summary>结束角色牌使用：把盖放牌移入弃牌区（§10.1.3）。</summary>
        private void EndCharacterUse()
        {
            if (engine == null) return;

            bool ok = Dispatch(new EndCharacterCardUseCommand(characterSeat));
            int seat = characterSeat;
            ExitCharacterFlow(ok ? $"[角色牌] 玩家{seat + 1} 结束使用角色牌，牌已移入弃牌区。" : null);
        }

        /// <summary>退出角色牌使用流程：清掉收集会话、解锁其他行动按钮，并可选地写一句状态。</summary>
        private void ExitCharacterFlow(string status)
        {
            characterSession = null;
            characterStep = CharacterStep.None;
            if (panel != null) panel.SetLocked(false);
            if (!string.IsNullOrEmpty(status)) SetStatus(status);
        }

        // —— 内部工具 ——

        /// <summary>联机层的命令入口：由 GameBootstrap 注入；为 null 时直接进引擎（单机默认行为）。</summary>
        private Func<ICommand, CommandResult> commandGateway;

        /// <summary>
        /// 由 GameBootstrap 注入联机会话的命令入口（阶段 3）。单机不注入，保持直连引擎。
        /// 注入后联机的主机 / 客户端分流全部发生在联机会话（App 层的 NetworkGameSession）内，界面无感知。
        /// </summary>
        public void SetCommandGateway(Func<ICommand, CommandResult> gateway) => commandGateway = gateway;

        /// <summary>
        /// **全项目唯一的引擎命令写入口**：单机直接进引擎；联机经注入的网关交给联机会话分流，
        /// 而不是让各个调用点各自直连引擎。
        /// 需要自定义提示文案的调用点（探索步 3 / 城市移动步 3）直接用本方法拿 <see cref="CommandResult"/> 自己拼字。
        /// </summary>
        private CommandResult DispatchToEngine(ICommand command)
        {
            // 联机座位闸门：界面里不少行动是「按当前行动座位」构造的（单机热座靠它轮转）。
            // 联机时轮到别人就得停手，否则主机能替客户端把行动全部做完，客户端永远没有可点的时候。
            if (lockToLocalSeat && command != null && command.ActorSeat >= 0 && command.ActorSeat != localSeat)
            {
                string waiting = $"现在轮到玩家 {engine.State.ActiveSeat + 1} 行动，请等待。";
                SetStatus(waiting);
                return CommandResult.Fail(waiting);
            }

            // 兜住「命令执行时抛异常」：异常在这里冒出去就没人接，玩家看到的现象只会是
            // 「点了没反应 / 流程卡住」，Console 里也只是一条不容易对上号的堆栈。
            try
            {
                return commandGateway != null ? commandGateway(command) : engine.Dispatch(command);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                string reason = $"「{command?.Name}」执行时出错：{ex.Message}";
                SetStatus($"[联机] {reason}");
                return CommandResult.Fail(reason);
            }
        }

        /// <summary>
        /// 通用命令下发：失败时把引擎的原因原样透出，成功时提示已执行。
        /// </summary>
        private bool Dispatch(ICommand command)
        {
            CommandResult result = DispatchToEngine(command);

            if (!result.Ok)
            {
                SetStatus(result.Error);
                return false;
            }

            SetStatus($"已执行：{command.Name}");
            return true;
        }

        private void SetMode(ActionMode next) => SetMode(next, false);

        /// <summary>
        /// 切换模式。<paramref name="force"/> 为 false 时，如果采集选点流程已经在跑，就忽略「切回 None」——
        /// 因为最后一轮主要行动派发完命令后，调用方还会补一句 SetMode(None)，
        /// 那句会把刚起步的采集选点流程顶掉（§7）。采集自己收尾要走 <c>force=true</c>。
        /// </summary>
        private void SetMode(ActionMode next, bool force)
        {
            if (!force && next == ActionMode.None && mode == ActionMode.Harvest
                && engine != null && engine.HarvestNeedsChoices)
            {
                return;
            }

            mode = next;
            relocateFrom = null;
            pendingMoves.Clear();

            // 退出行动时一并丢掉探索的选点 / 路线 / 收款方选择，并把地图高亮清掉。
            exploreTargetNode = -1;
            exploreRoutes.Clear();
            explorePayees.Clear();
            payeeQueue.Clear();
            RefreshExploreHighlight();

            // 退出采集选点（或换模式）时把采集流程整个丢掉：换模式后再点地图不该还留在采集语义里。
            if (next != ActionMode.Harvest)
            {
                harvestOrder.Clear();
                harvestIndex = -1;
                harvestChosen.Clear();
                harvestCurrent.Clear();
            }
            RefreshHarvestHighlight();

            // 建设整条流程复位：换行动 / 建成退出后，选中的牌、落点、付费方式与收集到的答案都不该还算数。
            buildStep = next == ActionMode.Build ? BuildStep.PickCard : BuildStep.NotBuilding;
            buildCardId = -1;
            buildSlot = -1;
            buildPayment = FacilityPayment.None;
            buildSession = null;
            if (cardPreview != null) cardPreview.Hide();

            // 特殊行动整条流程复位：换行动 / 用完退出后，选中的牌与收集到的答案都不该还算数。
            specialStep = next == ActionMode.SpecialAction ? SpecialStep.PickCard : SpecialStep.None;
            specialCardId = -1;
            specialSession = null;

            // 设施牌弹窗不常驻：进建设模式才弹出，退出（含「取消」、建成后自动退出）就收起。
            // 宿主容器保持激活，这里只切弹窗根。2 人局弃设施牌（§12 变体 4）借用同一块面板，也把它留着。
            if (supplyPanel != null) supplyPanel.SetOpen(next == ActionMode.Build || supplyDiscardMode);

            // 城市样式牌弹窗同理（§6.3b）：进宣告模式弹出 6 张样式牌，选中一张或取消即收起；
            // 使用特殊行动（§6.3f）也要从这 6 张里点一张，所以一并弹出。
            if (patternPanel != null)
                patternPanel.SetOpen(next == ActionMode.Declare || next == ActionMode.SpecialAction);

            // 设施供应区 / 城市面板只有建设模式下可点，跟着模式一起刷新。
            RefreshFacilityViews();

            // 万一还有「付给谁」的小窗留在屏幕上（例如中途换了行动），一并收掉：
            // 否则它的回调会在这次探索作废之后才被打进来，把某个过期选择写进已经没人管的计划里。
            if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();

            if (panel != null)
            {
                panel.SetLocked(false); // 退出建设（或换行动）时把「锁住其他行动」解开
                panel.SetMode(mode);
            }
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
                    int seat = ChooseStartNodeCommand.CurrentOperator(state);
                    const string place = "先点「手牌」小标签展开手牌，再点其中一张盖放（§4.2 阶段 1）。";

                    // 已经放下移动城市、正被事件牌挡住的那位：提示先选奖励，别急着切给下一位。
                    if (seat >= 0 && state.Player(seat).MobileCityNodeId >= 0)
                    {
                        SetHint($"{SeatLabel(seat)} 正在结算事件牌：请选择这次翻出的奖励，选完才轮到下一位选初始资源点。");
                        return;
                    }

                    SetHint(seat < 0
                        ? $"{place}初始资源点都决定好了——所有人盖放完自动进入玩家行动。"
                        : $"{place}轮到 {SeatLabel(seat)}：请点击一个绿色资源点本体作为初始资源点。");
                    return;
                }

                // 第 2 回合起没有开局入场，盖放阶段直接开始；所有人盖放完由引擎自动进入玩家行动。
                if (mode == ActionMode.None && state.Phase == TurnPhase.CharacterPlacement)
                {
                    if (state.SupplyDiscardRemaining > 0)
                    {
                        SetHint($"2 人局：轮到 {SeatLabel(state.SupplyDiscardSeat)} 从设施供应区弃 1 张设施牌，然后开始盖放。");
                        return;
                    }

                    string who = lockToLocalSeat ? string.Empty : $"{SeatLabel(ViewSeat(state))} ";
                    SetHint($"{who}盖放角色牌：先点「手牌」小标签展开手牌，再点其中一张盖放；所有人盖放完自动进入玩家行动。");
                    return;
                }

                // 角色牌效果收集 / 连用确认期间，优先显示它的引导。
                if (characterStep == CharacterStep.Effect && characterSession != null)
                {
                    SetHint(CharacterEffectHint());
                    return;
                }

                if (characterStep == CharacterStep.Second)
                {
                    panel.SetHint("角色牌：请在弹出的窗口里选「继续使用另一种效果」或「结束角色牌使用」（§10.1.2）。");
                    return;
                }

                // 采集阶段：还在选点（走下面的 Harvest 分支）或自己已提交、等其他人提交（引擎收齐后自动结算并进入下一回合）。
                if (state.Phase == TurnPhase.Harvest && state.HarvestPending
                    && lockToLocalSeat && state.HarvestChoices.ContainsKey(localSeat))
                {
                    panel.SetHint("你已提交本回合的采集选择，等待其他玩家提交…");
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
                    switch (buildStep)
                    {
                        case BuildStep.Pay:
                            panel.SetHint("建设：在放大卡面上选一种付费方式（选项一 / 选项二），或点「取消建设」重新选牌。");
                            break;
                        case BuildStep.Slot:
                            panel.SetHint("建设：请点击你城市面板上亮起来的一个空位（点「取消」返回上一步）。");
                            break;
                        case BuildStep.Confirm:
                            panel.SetHint("建设：确认弹窗里点「确定建造」，或点「取消」返回选位。");
                            break;
                        case BuildStep.Effect:
                            SetHint(BuildEffectHint());
                            break;
                        default:
                            panel.SetHint("建设：点窗口里的任意一张设施牌。");
                            break;
                    }
                    break;
                case ActionMode.Declare:
                    panel.SetHint("宣告城市样式：请从弹出的 6 张样式牌里点一张（引擎会自动匹配组合）。");
                    break;
                case ActionMode.SpecialAction:
                    SetHint(specialStep == SpecialStep.Effect
                        ? SpecialEffectHint()
                        : "特殊行动：请从弹出的 6 张样式牌里点一张——付费用并结算它解锁的特殊行动（§6.3f）。");
                    break;
                case ActionMode.Harvest:
                    panel.SetHint(HarvestHintText());
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

        /// <summary>
        /// 建设 L4 的引导：这一步要「点地图」就把候选位置念出来，否则提示在弹窗里选。
        /// 候选清单直接来自引擎的 <see cref="ChoicePick"/>，所以不会出现「提示说合法、点下去却不行」。
        /// </summary>
        private string BuildEffectHint()
        {
            ChoiceRequest request = buildSession != null ? buildSession.Pending : null;
            if (request == null) return "建设：正在结算入场效果……";

            if (request.Pick == null || request.Pick.Kind == ChoicePickKind.None)
                return $"建设入场效果：{request.Prompt}";

            var where = new List<string>();
            if (request.Pick.Kind == ChoicePickKind.InfluenceSlot)
            {
                foreach (InfluenceSlotRef slot in request.Pick.Slots) where.Add(SlotName(slot));
                return $"建设入场效果：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
            }

            foreach (int nodeId in request.Pick.NodeIds) where.Add($"资源点 {engine.State.Map.Node(nodeId).Code}");
            return $"建设入场效果：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
        }

        /// <summary>城市样式特殊行动效果的引导：候选要「点地图」就把位置念出来，否则提示在弹窗里选。</summary>
        private string SpecialEffectHint()
        {
            ChoiceRequest request = specialSession != null ? specialSession.Pending : null;
            if (request == null) return "特殊行动：正在结算效果……";

            if (request.Pick == null || request.Pick.Kind == ChoicePickKind.None)
                return $"特殊行动效果：{request.Prompt}";

            var where = new List<string>();
            if (request.Pick.Kind == ChoicePickKind.InfluenceSlot)
            {
                foreach (InfluenceSlotRef slot in request.Pick.Slots) where.Add(SlotName(slot));
                return $"特殊行动效果：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
            }

            foreach (int nodeId in request.Pick.NodeIds) where.Add($"资源点 {engine.State.Map.Node(nodeId).Code}");
            return $"特殊行动效果：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
        }

        /// <summary>角色牌效果的引导：候选要「点地图」就把位置念出来，否则提示在弹窗里选。</summary>
        private string CharacterEffectHint()
        {
            ChoiceRequest request = characterSession != null ? characterSession.Pending : null;
            if (request == null) return "角色牌：正在结算效果……";

            if (request.Pick == null || request.Pick.Kind == ChoicePickKind.None)
                return $"角色牌效果：{request.Prompt}";

            var where = new List<string>();
            if (request.Pick.Kind == ChoicePickKind.InfluenceSlot)
            {
                foreach (InfluenceSlotRef slot in request.Pick.Slots) where.Add(SlotName(slot));
                return $"角色牌效果：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
            }

            foreach (int nodeId in request.Pick.NodeIds) where.Add($"资源点 {engine.State.Map.Node(nodeId).Code}");
            return $"角色牌效果：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
        }

        private string SlotName(InfluenceSlotRef slot) => slot.Kind == InfluenceSlotKind.Node
            ? $"资源点 {engine.State.Map.Node(slot.TargetId).Code} 第 {slot.SlotIndex + 1} 格"
            : $"航道 {engine.State.Map.RouteLabel(slot.TargetId)} 第 {slot.SlotIndex + 1} 格";

        // —— 采集阶段（§7）：逐位玩家自选资源点 ——

        /// <summary>当前正在选点的那位玩家的座位号；-1 表示没在采集选点。</summary>
        private int HarvestSeat =>
            harvestIndex >= 0 && harvestIndex < harvestOrder.Count ? harvestOrder[harvestIndex] : -1;

        /// <summary>
        /// 进入采集阶段：按「起始玩家起顺时针」排好顺序，默认全选当前玩家的合法采集点，
        /// 把别的行动按钮锁死、只留「确认 / 取消」。逐位选完才统一结算（§7）。
        /// </summary>
        private void BeginHarvestFlow()
        {
            GameState state = engine.State;

            harvestOrder.Clear();
            harvestChosen.Clear();

            // 联机：每台机器只选自己的座位，提交给主机汇总；单机热座仍按起始玩家起的顺序逐位选。
            if (lockToLocalSeat)
            {
                if (state.HarvestChoices.ContainsKey(localSeat))
                {
                    // 已经交过了（例如中途重连后收到快照）：不再弹选点，等其他人。
                    SetMode(ActionMode.None, true);
                    SetStatus("你已提交本回合的采集选择，等待其他玩家…");
                    return;
                }

                if (localSeat >= 0 && localSeat < state.Players.Count) harvestOrder.Add(localSeat);
            }
            else if (state.Players.Count > 0)
            {
                int seat = state.FirstPlayerSeat;
                for (int i = 0; i < state.Players.Count; i++)
                {
                    harvestOrder.Add(seat);
                    seat = state.NextSeat(seat);
                }
            }

            harvestIndex = harvestOrder.Count > 0 ? 0 : -1;
            LoadHarvestDefaults(HarvestSeat);
            SetMode(ActionMode.Harvest);
            if (panel != null) panel.SetLocked(true); // 采集期间不是选行动，其它按钮锁死

            int current = HarvestSeat;
            if (current < 0)
                SetStatus("进入采集阶段：没有可采集的资源点，点「确认」继续即可。");
            else if (lockToLocalSeat)
                SetStatus("进入采集阶段：请选择你要采的资源点（默认全选，可点地图上的资源点增删），提交后等待其他人。");
            else
                SetStatus($"进入采集阶段：轮到玩家 {current + 1} 选择要采的资源点（默认全选，可点地图上的资源点增删）。");
        }

        /// <summary>把某位玩家的采集选择重置为「全部合法资源点」（采集的惯例：能采就采）。</summary>
        private void LoadHarvestDefaults(int seat)
        {
            harvestCurrent.Clear();
            if (seat >= 0) harvestCurrent.AddRange(HarvestResolver.EligibleNodes(engine.State, seat));

            RefreshHarvestHighlight();
        }

        /// <summary>采集选点期间把当前选中的资源点亮到地图上；退出采集时清空。</summary>
        private void RefreshHarvestHighlight()
        {
            if (renderer != null)
                renderer.SetHighlightedNodes(mode == ActionMode.Harvest ? harvestCurrent : null);
        }

        /// <summary>点地图资源点：合法就加入 / 移除当前选择，提示行实时显示已选点与路费。</summary>
        private void HandleHarvestPick(MapHit hit, GameState state)
        {
            int seat = HarvestSeat;
            if (seat < 0) return;

            if (hit.NodeId < 0)
            {
                SetStatus("请点资源点本体（或它上面的影响力）来加入 / 移除采集。");
                return;
            }

            MapNodeState node = state.Map.Node(hit.NodeId);
            if (!HarvestResolver.IsEligible(state, seat, node))
            {
                SetStatus($"{node.Code} 上没有你的影响力，也没有你的移动城市停靠，采不了。");
                return;
            }

            if (harvestCurrent.Contains(node.Id)) harvestCurrent.Remove(node.Id);
            else harvestCurrent.Add(node.Id);

            RefreshHarvestHighlight();
            UpdateHint();
        }

        /// <summary>定下当前玩家的选择、轮到下一位；全部选完即调用引擎统一结算（§7）。</summary>
        private void ConfirmHarvest()
        {
            int seat = HarvestSeat;

            // 联机：只提交自己这一份，由引擎在最后一位提交时统一结算（SubmitHarvestChoiceCommand）。
            if (lockToLocalSeat)
            {
                if (seat < 0) return;

                var pick = new List<int>(harvestCurrent);
                SetMode(ActionMode.None, true); // 提交后不再允许改选

                CommandResult submit = DispatchToEngine(new SubmitHarvestChoiceCommand(seat, pick));
                if (!submit.Ok)
                {
                    SetStatus($"[采集] 提交失败：{submit.Error}");
                    return;
                }

                // 别人还没交完就提示等待；最后一人的结算简报由 OnHarvestResolved 覆盖。
                if (engine.State.HarvestPending)
                    SetStatus("已提交本回合的采集选择，等待其他玩家…");
                return;
            }

            if (seat >= 0) harvestChosen[seat] = new List<int>(harvestCurrent);

            harvestIndex++;

            int next = HarvestSeat;
            if (next >= 0)
            {
                LoadHarvestDefaults(next);
                SetStatus($"玩家 {seat + 1} 的选择已记下。轮到玩家 {next + 1}（默认全选，可点地图上的资源点增删）。");
                UpdateHint();
                return;
            }

            // 全部选完：交给引擎统一结算（先算后付，保证「不能用本阶段收到的金券」）。
            var chosen = new Dictionary<int, IReadOnlyList<int>>();
            foreach (KeyValuePair<int, List<int>> pair in harvestChosen)
                chosen[pair.Key] = pair.Value;

            SetMode(ActionMode.None, true); // 强制退出采集选点（内部会清掉采集流程并解锁按钮）
            // 采集结算同样包成了命令，走唯一入口下发；联机时客户端能靠重放这条命令复现结算。
            CommandResult result = DispatchToEngine(new ResolveHarvestCommand(chosen));
            if (!result.Ok) SetStatus($"[采集] 结算失败：{result.Error}"); // 成功简报由 OnHarvestResolved 播报
        }

        /// <summary>采集选点的引导文本：列出已选资源点、路费与可选上限。</summary>
        private string HarvestHintText()
        {
            int seat = HarvestSeat;
            if (seat < 0) return "采集阶段：选点已完成。";

            HarvestPlan plan = HarvestResolver.Plan(engine.State, seat, harvestCurrent);

            var codes = new List<string>();
            foreach (int nodeId in plan.Nodes) codes.Add(engine.State.Map.Node(nodeId).Code);
            string list = codes.Count == 0 ? "无" : string.Join("、", codes);

            string who = lockToLocalSeat ? "你" : SeatLabel(seat);
            string tail = lockToLocalSeat
                ? "点地图上的资源点可加入或移除；「确认」提交给主机，「取消」恢复默认全选。"
                : "点地图上的资源点可加入或移除；「确认」交给下一位，「取消」恢复默认全选。";

            return $"采集（{who}）：已选 {plan.Nodes.Count} 个点〔{list}〕，路费 {plan.Toll.Total} / 手上 {plan.Budget} 金券。" + tail;
        }

        private void SetHint(string text)
        {
            if (panel != null) panel.SetHint(LocalizePlayerRefs(text));
        }

        private void SetStatus(string text)
        {
            if (hud != null) hud.SetStatus(LocalizePlayerRefs(text));
        }

        /// <summary>
        /// 联机层（主机拒绝、重放失败、断线）的错误：直接写进状态行，别只留在 Console。
        /// 客户端提交命令本身只知道「已提交给主机」，真正被拒绝的原因全靠这条显示出来；
        /// 联机测试时如果看不到它，很容易把「命令被拒」误判成「卡住了」。
        /// </summary>
        public void ShowOnlineError(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            // 刚才提交的事件选项没被主机接受：把事件卡还原，让玩家能重新选一项再提交。
            // 不还原的话，卡面已经收起、pendingEvent* 也还留着，玩家看不出「该重试」，
            // 主机那边则永远停在「等他选事件奖励」。
            if (awaitingResolveConfirm && pendingEventNodeId >= 0)
            {
                awaitingResolveConfirm = false;
                pendingResolveFromExplore = false;
                pendingResolveFromCityMove = false;
                pendingResolveOriginNode = -1;
                BackToEventCard();
                SetStatus($"[联机] {message}（可以重新选择这张事件卡的选项）");
                return;
            }

            SetStatus($"[联机] {message}");
        }

        /// <summary>座位显示名：优先用准备房间同步来的昵称，没有时回退成「玩家 N」。</summary>
        private string SeatLabel(int seat)
        {
            if (seat < 0) return "—";

            string name = seat < playerNames.Length && !string.IsNullOrWhiteSpace(playerNames[seat])
                ? playerNames[seat]
                : $"玩家 {seat + 1}";
            return name;
        }

        /// <summary>
        /// 把文本里的「玩家 N」换成昵称。引擎给的原因、效果说明都是 Core 拼出来的，
        /// 不持有昵称，所以在界面这一层统一翻译一次；单机热座没有昵称时原样返回。
        /// </summary>
        private string LocalizePlayerRefs(string text)
        {
            if (string.IsNullOrEmpty(text) || playerNames.Length == 0) return text;

            return PlayerRef.Replace(text, match =>
            {
                int seat = int.Parse(match.Groups[1].Value) - 1;
                return seat >= 0 && seat < playerNames.Length && !string.IsNullOrWhiteSpace(playerNames[seat])
                    ? playerNames[seat]
                    : match.Value;
            });
        }

        /// <summary>顶栏「行动座位」在单机热座下跟着实际操作者走（盖放／采集阶段引擎不轮转 ActiveSeat）。</summary>
        private int HudOperatorSeat(GameState state)
        {
            if (lockToLocalSeat || state == null) return -1;
            if (state.Phase == TurnPhase.CharacterPlacement) return ViewSeat(state);
            if (state.Phase == TurnPhase.Harvest && state.HarvestPending && HarvestSeat >= 0) return HarvestSeat;
            return ViewSeat(state);
        }

        private void RefreshAll()
        {
            if (renderer != null && renderer.IsReady) renderer.Refresh();
            if (hud != null)
            {
                hud.SetOperatorSeat(HudOperatorSeat(engine != null ? engine.State : null));
                hud.Refresh();
            }
            RefreshFacilityViews();

            // 「看别人城市」的弹窗开着时跟着状态刷新（没开就什么都不做）。
            if (playerCityPopup != null) playerCityPopup.Refresh(engine != null ? engine.State : null);

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

            // 2 人局（§12 变体 4）：盖放角色牌前每名玩家要先弃 1 张设施牌 —— 借设施供应区面板当弃牌窗。
            // 第 1 回合的「开局入场」（§4.3 决定初始资源点 + 结算事件牌）在回合结构之前，所以弃牌窗要等
            // 开局入场整个走完再弹，否则遮罩会挡住开局选点的地图点击。
            //（命令与阶段事件都会走到 RefreshAll），避免散落的开关时机。
            bool setupDone = ChooseStartNodeCommand.CurrentOperator(state) < 0;
            bool discard = state.Phase == TurnPhase.CharacterPlacement
                           && state.SupplyDiscardRemaining > 0 && setupDone
                           && pendingEventNodeId < 0 // 事件卡还开着时先让位，免得遮罩挡住卡面
                           // 联机：弃牌也由本人操作，别人的回合不打开这块面板
                           && (!lockToLocalSeat || state.SupplyDiscardSeat == localSeat);
            if (discard != supplyDiscardMode)
            {
                supplyDiscardMode = discard;
                if (supplyPanel != null) supplyPanel.SetOpen(discard || buildMode);

                if (discard)
                    SetStatus($"轮到玩家 {state.SupplyDiscardSeat + 1}：请从设施供应区选 1 张牌放回游戏盒（§12 变体 4）。");
                else if (lockToLocalSeat && setupDone && pendingEventNodeId < 0
                         && state.Phase == TurnPhase.CharacterPlacement && state.SupplyDiscardRemaining > 0)
                    SetStatus($"轮到玩家 {state.SupplyDiscardSeat + 1} 弃设施牌（§12 变体 4），完成前不能盖放角色牌。");
                else if (state.SupplyDiscardRemaining == 0 && state.Phase == TurnPhase.CharacterPlacement)
                    SetStatus("设施牌已弃置完毕，可以盖放角色牌了。");
            }

            if (supplyPanel != null) supplyPanel.Refresh(state, buildMode || supplyDiscardMode);
            if (cityPanel != null) cityPanel.Refresh(state, ViewSeat(state), buildMode);

            // 样式牌弹窗只在宣告 / 特殊行动模式下可点（弹窗本身由 SetMode 开关）。
            if (patternPanel != null)
                patternPanel.Refresh(state, mode == ActionMode.Declare || mode == ActionMode.SpecialAction);

            // 角色牌手牌托盘：控制器只告知「本机玩家还有没有手牌」；是否展开由玩家点小标签自己决定（默认收起）。
            bool placementPhase = state.Phase == TurnPhase.CharacterPlacement;
            if (handPanel != null)
            {
                int handSeat = ViewSeat(state);
                PlayerState handPlayer = handSeat >= 0 && handSeat < state.Players.Count ? state.Players[handSeat] : null;
                // §10.1.1：手牌为空时先回收弃牌区再盖放 —— 盖放阶段弃牌区也算「还有牌可盖」。
                bool hasCards = handPlayer != null
                                && (handPlayer.HandCharacterCards.Count > 0
                                    || (placementPhase && handPlayer.DiscardCharacterCards.Count > 0));
                // 真正可点：和上面 OnHandCardClicked 共用同一个判断（CanPlaceCharacter），
                // 免得两处条件写歪成「看着能点、点了却只是放大」。
                bool canPlaceNow = CanPlaceCharacter(state, handPlayer);
                // 「能不能看」只看手里有没有牌。开局入场（选城市 / 结算事件牌）期间也要能翻开对一下手牌，
                // 只是这一下点上去只放大 —— 那个限制在 canPlaceNow 里，不要拿它去关整块托盘。
                handPanel.SetAvailable(hasCards);
                handPanel.Refresh(state, handSeat, canPlaceNow);
            }
        }

        /// <summary>
        /// 城市面板 / 角色牌手牌显示哪个座位：
        /// - 联机：固定显示本机玩家的城市（回合结束座次推进，本机面板不该跟着变，否则刚建好的设施会「看不见」）；
        /// - 单机热座：跟着当前该操作的人走 —— 一台机器轮着操作所有玩家，面板/手牌必须切到正在行动的那个人，
        ///   否则轮到玩家 2 时界面还停在玩家 1，看不到自己的城市也没法建设。
        /// 座位越界时退回 0，保证永远有一个合法座位可显示。
        /// </summary>
        private int ViewSeat(GameState state)
        {
            if (state == null) return 0;

            if (lockToLocalSeat)
            {
                int seat = localSeat;
                if (seat < 0 || seat >= state.Players.Count) seat = state.ActiveSeat;
                if (seat < 0 || seat >= state.Players.Count) seat = 0;
                return seat;
            }

            // 单机热座：盖放阶段没有座位轮转（ActiveSeat 一直停在起始玩家），
            // 谁还没盖放就把界面切给谁，否则盖完 1 号以后 2–4 号永远轮不到。
            if (state.Phase == TurnPhase.CharacterPlacement)
            {
                // 第 1 回合的「开局入场」：选初始资源点与结算事件牌都算同一个人的操作，
                // 事件没结算完之前不要把界面切给下一位（否则玩家 1 还在选奖励，顶栏已经变成玩家 2）。
                int chooser = ChooseStartNodeCommand.CurrentOperator(state);
                if (chooser >= 0) return chooser;

                // 2 人局变体：先弃设施牌，显示正在弃牌的那位。
                if (state.SupplyDiscardRemaining > 0
                    && state.SupplyDiscardSeat >= 0 && state.SupplyDiscardSeat < state.Players.Count)
                    return state.SupplyDiscardSeat;

                // 从起始玩家开始顺时针找第一个「还没盖放且手里/弃牌区有牌」的人。
                int count = state.Players.Count;
                int start = state.FirstPlayerSeat;
                if (start < 0 || start >= count) start = 0;
                for (int i = 0; i < count; i++)
                {
                    int s = (start + i) % count;
                    PlayerState p = state.Players[s];
                    if (p.PlacedCharacterCard < 0
                        && (p.HandCharacterCards.Count > 0 || p.DiscardCharacterCards.Count > 0))
                        return s;
                }
            }

            int active = state.ActiveSeat;
            if (active < 0 || active >= state.Players.Count) active = 0;
            return active;
        }

        /// <summary>采集阶段结算的简报（全部玩家选点确认后由 GameEngine.ResolveHarvest 统一结算）。</summary>
        private static string DescribeHarvest(HarvestReport report)
        {
            var sb = new StringBuilder("[采集] 结算 —— ");
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
