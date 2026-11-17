using System;
using System.Collections.Generic;
using System.Text;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Rules;
using NomadCity.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 界面与引擎之间的编排者：把「点地图 + 点按钮」翻译成命令，再把命令结果反映回界面。
    ///
    /// 规则判断一概不在这里 —— 非法操作会把引擎给出的中文原因原样显示在 HUD 状态行上，
    /// 界面只负责记住「当前在选什么」（行动模式 + 调度的选点步骤）。
    /// </summary>
    public sealed partial class GameScreenController : MonoBehaviour
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

        /// <summary>本地已经弹过多少条「向所有玩家展示」记录；用来只亮出新记录。</summary>
        private int revealedSeen;
        private CharacterHandPanel handPanel;

        /// <summary>手牌区「已盖放 / 弃牌区」按钮打开的查看窗（运行时创建，只读状态）。</summary>
        private CharacterPilePanel pilePanel;

        /// <summary>状态栏左侧眼睛按钮弹出的「某位玩家城市面板」（只读）。没接线时点了只会提示。</summary>
        private PlayerCityPopup playerCityPopup;

        /// <summary>操作区的「企业」按钮：只有开局启用了企业扩展才显示。</summary>
        [SerializeField] private Button companyButton;

        /// <summary>「企业」按钮打开的企业板总览（运行时创建，和聊天 / 行动记录同一套弹窗规格）。</summary>
        private EnterpriseBoardPanel enterpriseBrowser;

        /// <summary>开发者模式按钮与面板：只有以开发者模式启动的对局才会创建。</summary>
        private Button developerButton;
        private DeveloperPanel developerPanel;

        /// <summary>带窗口外框、可最小化的确认弹窗（开局停靠这类要一边看地图一边确认的场合）。</summary>
        private ConfirmWindow confirmWindow;

        /// <summary>操作区的「决策」按钮：只有开局启用了感染者扩展才显示。</summary>
        [SerializeField] private Button decisionButton;

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

        /// <summary>
        /// 开局入场（§4.3）里「已经点中、但玩家还没确认」的初始资源点。
        /// 确认前不提交命令：点错一次就会翻事件牌、决定整局起手，必须给一次反悔机会。
        /// -1 表示没有待确认的停靠。
        /// </summary>
        private int pendingStartNodeId = -1;

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

        /// <summary>企业特效里「要玩家点选」的收集会话；已跑完或没在收集时为 null。</summary>
        private EffectChoiceSession enterpriseSession;

        /// <summary>
        /// 收尾阶段的「持续计谋」提示是否正开着。<see cref="RefreshAll"/> 会被命令与阶段事件频繁调用，
        /// 不加这道闸门会反复弹同一个窗；玩家选完「发动 / 跳过」后复位。
        /// </summary>
        private bool persistentCleanupPrompting;

        /// <summary>这次要发动收尾能力的持续计谋（选完「发动」后记下来，答案收齐再派发命令）。</summary>
        private PersistentTacticState cleanupTactic;

        /// <summary>收尾能力里「要玩家点选」的收集会话（落点与部署同源，可点地图）；没在收集时为 null。</summary>
        private EffectChoiceSession cleanupSession;

        /// <summary>终局结算窗是否已经弹过 —— <see cref="RefreshAll"/> 会被反复调用，不加标记会重复弹窗。</summary>
        private bool finalPrompted;

        /// <summary>终局结算窗在等命令回调全部走完后再弹（结束回合的调用方事后还会补一次 SetMode，会把窗收掉）。</summary>
        private bool finalPromptPending;

        /// <summary>正在触发的企业特效：座位 / 企业板 id / 合作等级。</summary>
        private int enterpriseSeat = -1;
        private int enterpriseBoardId = -1;
        private int enterpriseLevel;

        /// <summary>当前这次收集会话是不是「提升合作等级」（否则是「触发企业特效」）。</summary>
        private bool enterpriseAdvance;

        /// <summary>
        /// 这次企业特效是开发者面板触发的：跳过「只能在自己行动阶段触发」的时机校验，
        /// 其余点选流程与正常触发完全一致。
        /// </summary>
        private bool enterpriseIgnoreTiming;

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

        /// <summary>企业扩展开局入场：正在做「7 选 5」的座位；-1 = 不在这一步。</summary>
        private int setupDiscardSeat = -1;

        /// <summary>「7 选 5」已选的第 1 张牌；-1 = 还在选第 1 张。</summary>
        private int setupDiscardFirst = -1;

        /// <summary>「7 选 5」已选的第 2 张牌；-1 = 还在选第 2 张（或已重新选择）。</summary>
        private int setupDiscardSecond = -1;

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

        /// <summary>
        /// 联机客户端：上一条命令已交给主机、回放还没回来。期间拦住新的提交 ——
        /// 否则连点两下会发出两条一模一样的命令：主机执行第一条、拒绝第二条，
        /// 那条拒绝会搅乱正在等待的流程（典型现象：事件选项选了没反应、卡在事件卡上）。
        /// </summary>
        private bool onlineSubmitPending;

        /// <summary>在途命令的兜底超时：回放迟迟不来时不能把界面永久锁死。</summary>
        private float onlineSubmitDeadline;

        /// <summary>在途命令的提交时刻，用来算「提交 → 主机回放」的往返耗时（排查延迟用）。</summary>
        private float onlineSubmitAt;

        private const float OnlineSubmitTimeoutSeconds = 5f;

        public void Initialize(GameEngine engine, MapStateRenderer renderer, MapPointerInput pointerInput,
            GameHud hud, ActionPanel panel, EventCardPanel eventCardPanel = null, ChoicePopup choicePopup = null,
            FacilitySupplyPanel supplyPanel = null, CityPanelView cityPanel = null,
            CityPatternPanel patternPanel = null, CardPreviewPopup cardPreview = null,
            CharacterHandPanel handPanel = null, int localSeat = 0, bool lockToLocalSeat = false,
            string[] playerNames = null, PlayerCityPopup playerCityPopup = null, bool onlineClient = false)
        {
            // 联机客户端会在收到快照时重接一次界面；先解绑旧的一批，否则一次点击会被派发两条命令。
            if (this.engine != null)
            {
                this.engine.Dispatcher.CommandApplied -= OnCommandApplied;
                this.engine.PhaseChanged -= OnPhaseChanged;
                this.engine.SeatAdvanced -= OnSeatAdvanced;
                this.engine.HarvestResolved -= OnHarvestResolved;
                this.engine.GameEnded -= OnGameEnded;
            }

            if (this.pointerInput != null)
            {
                this.pointerInput.Clicked -= OnMapClicked;
                this.pointerInput.Missed -= OnMapMissed;
            }

            if (this.panel != null)
            {
                this.panel.ModeRequested -= OnModeRequested;
                this.panel.CancelRequested -= OnCancelRequested;
                this.panel.CharacterCardRequested -= OnCharacterCardRequested;
            }

            if (this.eventCardPanel != null) this.eventCardPanel.OptionSelected -= OnEventOptionSelected;
            if (this.supplyPanel != null) this.supplyPanel.CardClicked -= OnSupplyCardClicked;
            if (this.cityPanel != null) this.cityPanel.SlotClicked -= OnCitySlotClicked;
            if (this.patternPanel != null) this.patternPanel.CardClicked -= OnPatternCardClicked;
            if (this.handPanel != null)
            {
                this.handPanel.CardClicked -= OnHandCardClicked;
                this.handPanel.PlacedCardsClicked -= OnPlacedCardsClicked;
                this.handPanel.DiscardPileClicked -= OnDiscardPileClicked;
            }
            if (this.hud != null) this.hud.SeatInspectRequested -= OnSeatInspectRequested;
            if (this.playerCityPopup != null) this.playerCityPopup.SlotClicked -= OnPlayerCitySlotClicked;

            this.engine = engine;
            // 重新接界面（含联机快照重接）时，把历史「展示」记录全部视为已看过，避免重放旧卡面。
            revealedSeen = engine != null ? engine.State.PublicReveals.Count : 0;
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
            this.finalPrompted = false;
            this.finalPromptPending = false;
            if (confirmWindow != null) confirmWindow.Hide();

            if (engine != null)
            {
                engine.Dispatcher.CommandApplied += OnCommandApplied;
                engine.PhaseChanged += OnPhaseChanged;
                engine.SeatAdvanced += OnSeatAdvanced;
                engine.HarvestResolved += OnHarvestResolved;
                engine.GameEnded += OnGameEnded;
            }

            if (pointerInput != null)
            {
                pointerInput.Clicked += OnMapClicked;
                pointerInput.Missed += OnMapMissed;
            }

            if (panel != null)
            {
                panel.ModeRequested += OnModeRequested;
                panel.CancelRequested += OnCancelRequested;
                panel.CharacterCardRequested += OnCharacterCardRequested;
                panel.EndTurnRequested += OnEndTurnRequested;
            }

            if (companyButton != null) companyButton.onClick.AddListener(OnCompanyButtonClicked);

            if (eventCardPanel != null) eventCardPanel.OptionSelected += OnEventOptionSelected;
            if (supplyPanel != null) supplyPanel.CardClicked += OnSupplyCardClicked;
            if (cityPanel != null) cityPanel.SlotClicked += OnCitySlotClicked;
            if (patternPanel != null) patternPanel.CardClicked += OnPatternCardClicked;
            if (handPanel != null)
            {
                handPanel.CardClicked += OnHandCardClicked;
                handPanel.PlacedCardsClicked += OnPlacedCardsClicked;
                handPanel.DiscardPileClicked += OnDiscardPileClicked;
                // 把「已盖放 / 弃牌区」按钮挪到操作区上方，别让它遮行动记录。
                handPanel.PlacePileButtonsAbove(panel != null ? panel.PopupRoot : null);
            }

            if (hud != null) hud.Bind(engine);
            if (hud != null) hud.SeatInspectRequested += OnSeatInspectRequested;
            if (playerCityPopup != null) playerCityPopup.SlotClicked += OnPlayerCitySlotClicked;

            SetMode(ActionMode.None);
            RefreshAll();
            ApplyExpansionButtons();
            EnsureDeveloperButton();

            // 万一进场时已经停在「等玩家选点」的采集阶段（例如重载场景），直接接上选点流程。
            if (engine != null && engine.HarvestNeedsChoices) BeginHarvestFlow();
        }

        /// <summary>
        /// 「企业」「决策」两颗操作区按钮只在对应扩展包开局时出现 —— 开房时没勾就整颗隐藏，
        /// 不留一颗点了没反应的死按钮。扩展包信息来自 <see cref="GameState.Setup"/>：
        /// 主机由开房配置决定，联机客户端随开局快照一起拿到，两端表现一致。
        /// </summary>
        private void ApplyExpansionButtons()
        {
            GameSetup setup = engine != null ? engine.State.Setup : null;
            bool enterpriseEnabled = setup != null && setup.IsEnterpriseEnabled;
            SetButtonVisible(companyButton, enterpriseEnabled, makeInteractable: true);
            SetButtonVisible(decisionButton, setup != null && setup.Has(ExpansionFlags.Infected));
        }

        /// <summary>
        /// 显隐操作区里的扩展按钮。<paramref name="makeInteractable"/> 用于场景里被存成
        /// 「不可点」的按钮（企业按钮就是），只显示不解锁的话点上去毫无反应。
        /// </summary>
        private static void SetButtonVisible(Button button, bool visible, bool makeInteractable = false)
        {
            if (button == null) return;

            if (button.gameObject.activeSelf != visible)
                button.gameObject.SetActive(visible);

            if (makeInteractable && button.interactable != visible)
                button.interactable = visible;
        }

        /// <summary>
        /// 本局以开发者模式启动时，在操作区补一颗「开发者模式」按钮。
        /// 场景里没有预留这颗按钮，所以复制「企业 / 决策」按钮的样式并摆在它下面；
        /// 两个模板都没有时退化成画布右下角的一颗按钮。
        /// </summary>
        private void EnsureDeveloperButton()
        {
            if (engine == null || engine.State == null || !engine.State.DeveloperMode) return;
            if (developerButton != null) return;

            Button template = companyButton != null ? companyButton : decisionButton;
            if (template != null) developerButton = CreateDeveloperButtonFromTemplate(template);

            if (developerButton == null)
            {
                Canvas canvas = ResolveCanvas();
                if (canvas != null) developerButton = CreateDeveloperButtonOnCanvas(canvas);
            }

            if (developerButton == null)
                Debug.LogWarning("[开发者模式] 没能创建「开发者模式」按钮。", this);
        }

        private Button CreateDeveloperButtonFromTemplate(Button template)
        {
            GameObject clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = "Btn_DeveloperMode";

            var templateRect = template.GetComponent<RectTransform>();
            var rect = clone.GetComponent<RectTransform>();
            if (templateRect != null && rect != null)
            {
                rect.anchorMin = templateRect.anchorMin;
                rect.anchorMax = templateRect.anchorMax;
                rect.pivot = templateRect.pivot;
                rect.sizeDelta = templateRect.sizeDelta;
                // 排在模板按钮正下方一行。
                rect.anchoredPosition = templateRect.anchoredPosition
                                        + new Vector2(0f, -(templateRect.sizeDelta.y + 8f));
            }

            clone.SetActive(true);
            Button button = clone.GetComponent<Button>();
            if (button == null) return null;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OpenDeveloperPanel);
            button.interactable = true;
            SetButtonLabel(clone, "开发者模式");
            UiButtonTint.Ensure(button);
            return button;
        }

        private Button CreateDeveloperButtonOnCanvas(Canvas canvas)
        {
            GameObject go = new GameObject("Btn_DeveloperMode",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-24f, 24f);
            rect.sizeDelta = new Vector2(168f, 44f);

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.247f, 0.208f, 0.153f, 0.95f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(OpenDeveloperPanel);

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            if (hud != null) label.font = hud.UiFont;
            label.text = "开发者模式";
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.92f, 0.86f, 1f);
            label.raycastTarget = false;

            UiButtonTint.Ensure(button);
            return button;
        }

        private static void SetButtonLabel(GameObject button, string text)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
        }

        private Canvas ResolveCanvas()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null && panel != null) canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null && hud != null) canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            return canvas;
        }

        private void OpenDeveloperPanel()
        {
            if (engine == null || engine.State == null || !engine.State.DeveloperMode) return;

            if (developerPanel == null)
            {
                Canvas canvas = ResolveCanvas();
                if (canvas == null)
                {
                    SetStatus("找不到画布，无法打开开发者面板。");
                    return;
                }

                developerPanel = DeveloperPanel.Create(canvas.transform, hud != null ? hud.UiFont : null);
                developerPanel.Submit = SubmitDeveloperCommand;
                developerPanel.TriggerEnterpriseEffectRequested = BeginDeveloperEnterpriseTrigger;
            }

            int initialSeat = engine.State.ActiveSeat >= 0 ? engine.State.ActiveSeat : localSeat;
            developerPanel.Open(engine.State, initialSeat);
        }

        /// <summary>拿一个带窗口外框的确认弹窗：第一次用时在画布上搭出来，之后复用同一个。</summary>
        private ConfirmWindow EnsureConfirmWindow()
        {
            if (confirmWindow != null) return confirmWindow;

            Canvas canvas = ResolveCanvas();
            if (canvas == null) return null;

            confirmWindow = ConfirmWindow.Create(canvas.transform, hud != null ? hud.UiFont : null);
            confirmWindow.SetCollapseAnchor(ResolveOperationBar(canvas));
            return confirmWindow;
        }

        /// <summary>
        /// 统一的「从一组选项里挑一个」弹窗：优先用可折行、可最小化的窗口弹窗；
        /// 窗口没接线时才退回紧凑的小选择弹窗。返回 false 表示两种弹窗都没接线。
        /// </summary>
        private bool ShowChoiceWindow(string title, string prompt, IReadOnlyList<string> options,
            Action<int> picked, bool allowCancel = true)
        {
            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show(title, prompt, options, picked, allowCancel);
                return true;
            }

            if (choicePopup != null)
            {
                choicePopup.Show(prompt, options, picked, allowCancel);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 弹出一次引擎给出的选择：企业特效 / 企业升级这类选项带整段效果说明，
        /// 小选择弹窗的 320×50 按钮装不下，统一改用能折行、可最小化的窗口弹窗。
        /// 其余选择（事件卡 / 建设 / 角色牌效果）也用同一套窗口，只是标题固定为「请选择」。
        /// </summary>
        private void ShowChoiceRequest(ChoiceRequest request, Action<int> picked)
        {
            var options = new List<string>(request.Options.Count);
            foreach (ChoiceOption option in request.Options) options.Add(option.Label);

            ShowChoiceWindow("请选择", request.Prompt, options, picked, request.AllowCancel);
        }

        /// <summary>
        /// 操作区（右下角那块按钮格）的矩形：最小化后的确认按钮要贴在它顶边外侧。
        /// 从「行动记录」按钮往上两级就是它，找不到就退回底部居中。
        /// </summary>
        private static RectTransform ResolveOperationBar(Canvas canvas)
        {
            if (canvas == null) return null;

            ActionLogPanel log = canvas.GetComponentInChildren<ActionLogPanel>(true);
            RectTransform toggle = log != null ? log.ToggleRect : null;
            if (toggle == null) return null;

            // 行动记录按钮 → 按钮格（PanelFill）→ 操作区（OperationBar）
            return toggle.parent is RectTransform fill ? fill.parent as RectTransform : null;
        }

        /// <summary>有确认窗（或小选择窗）开着时不许换行动：否则弹窗还留在屏幕上，答案会落进已经换掉的流程。</summary>
        private bool HasPromptOpen =>
            (choicePopup != null && choicePopup.IsVisible) || (confirmWindow != null && confirmWindow.IsVisible);

        /// <summary>开发者面板的提交入口：走和正式流程相同的命令通道，并把结果显示到状态行。</summary>
        public CommandResult SubmitDeveloperCommand(ICommand command)
        {
            CommandResult result = DispatchToEngine(command);
            SetStatus(result.Ok ? $"开发者：{command.Name}" : result.Error);

            // 开发者指定建设：设施落地后，像手动建设一样弹窗外结算入场效果。
            if (result.Ok && command is DeveloperBuildFacilityCommand devBuild)
                BeginFacilityEntryEffect(devBuild.ActorSeat, devBuild.FacilityId, devBuild.PanelSlot);

            return result;
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

            if (pointerInput != null)
            {
                pointerInput.Clicked -= OnMapClicked;
                pointerInput.Missed -= OnMapMissed;
            }

            if (panel != null)
            {
                panel.ModeRequested -= OnModeRequested;
                panel.CancelRequested -= OnCancelRequested;
                panel.CharacterCardRequested -= OnCharacterCardRequested;
                panel.EndTurnRequested -= OnEndTurnRequested;
            }

            if (companyButton != null) companyButton.onClick.RemoveListener(OnCompanyButtonClicked);

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
            // 本机命令绕了一圈回来了：把「提交 → 回放」的耗时打出来，便于区分是网络往返慢还是本地卡。
            // 同时写进状态栏：客户端如果是打包版（没有 Console 窗口），也能直接看到这个数字。
            if (onlineClient && onlineSubmitPending && command != null && command.ActorSeat == localSeat)
            {
                int roundTripMs = Mathf.RoundToInt((Time.unscaledTime - onlineSubmitAt) * 1000f);
                Debug.Log($"[联机] 操作往返 {roundTripMs} ms（{command.Name}）");
                SetStatus($"[联机] 操作往返 {roundTripMs} ms（{command.Name}）");
            }

            // 主机回放已经到了：在途的那条命令有结果了，解除提交闸门。
            onlineSubmitPending = false;

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
            if (confirmWindow != null) confirmWindow.Hide();

            string summary = DescribeFinal(state);
            SetStatus(summary);

            if (finalPrompted) return;

            // 终局结算窗不能在这里直接弹：结束回合的调用方在命令派发完之后还会补一次 SetMode，
            // 而 SetMode 会收起小选择窗 —— 当场弹出来的终局窗会被这一句立刻收掉，玩家什么都看不到。
            // 所以先记下，等这一帧的命令回调全部走完，再由 Update 弹。
            finalPromptPending = true;
            if (developerPanel != null) developerPanel.Close(); // 别让开发者面板盖在终局窗前面
        }

        /// <summary>终局结算窗：与「结束回合」等确认窗同一套规格（半透明窗口 + 名次明细 + 单个关闭按钮）。</summary>
        private void TryShowFinalPrompt()
        {
            if (!finalPromptPending || finalPrompted) return;
            if (engine == null || !engine.State.IsGameOver) return;

            ConfirmWindow window = EnsureConfirmWindow();
            if (window == null && choicePopup == null) return;

            finalPromptPending = false;
            finalPrompted = true;
            string prompt = BuildFinalPrompt(engine.State, DescribeFinal(engine.State));
            Action<int> picked = _ => SetStatus("终局已结算：可以查看最终地图，或退出本局。");

            if (window != null)
            {
                window.Show("终局结算", prompt, new[] { "查看最终地图" }, picked, allowCancel: false);
                return;
            }

            choicePopup.Show(prompt, new[] { "查看最终地图" }, _ => picked(0), allowCancel: false);
        }

        /// <summary>终局结算窗正文：胜者与名次在前，随后按分数从高到低列出每位玩家，并附上区域控制加分明细。</summary>
        private static string BuildFinalPrompt(GameState state, string summary)
        {
            var order = new List<int>();
            for (int seat = 0; seat < state.Players.Count; seat++) order.Add(seat);
            order.Sort((a, b) => state.Players[b].Score.CompareTo(state.Players[a].Score));

            IReadOnlyList<DistrictControl> controls = FinalScoring.EvaluateDistricts(state);

            var sb = new StringBuilder(summary);
            sb.Append("\n\n最终名次：");
            int rank = 0;
            int lastScore = int.MinValue;
            for (int i = 0; i < order.Count; i++)
            {
                int seat = order[i];
                PlayerState player = state.Players[seat];
                if (player.Score != lastScore)
                {
                    rank = i + 1;
                    lastScore = player.Score;
                }

                string districtInfo = DescribeDistrictContribution(controls, seat);
                sb.Append($"\n{rank}. 玩家{seat + 1} —— {player.Score} 分（{districtInfo}）");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 把某位玩家在终局控制的区块及其加分列出来，形如「A 区 +3、E 区 +4」；
        /// 一个区块都没控制时返回「无区域控制」。
        /// </summary>
        private static string DescribeDistrictContribution(IReadOnlyList<DistrictControl> controls, int seat)
        {
            var parts = new List<string>();
            foreach (DistrictControl control in controls)
            {
                if (control.Controller != seat) continue;
                parts.Add($"{control.District} 区 +{control.Score}");
            }
            return parts.Count == 0 ? "无区域控制" : string.Join("、", parts);
        }

        // —— 面板事件 ——

        private void OnModeRequested(ActionMode requested)
        {
            if (engine == null) return;

            // 卡牌效果授予的行动必须做完，不能中途换别的行动（换掉就会绕开效果给的行动）。
            if (requested != ActionMode.None && activeRequest != null && requested != mode)
            {
                SetStatus($"「{PendingAction.LabelOf(activeRequest.Kind)}」是卡牌效果授予的，必须做完才能换行动。");
                return;
            }

            // 操作区的「关闭」也不能把正在进行的行动事件清掉：清掉了下一帧又得从头重开一遍。
            if (requested == ActionMode.None && activeRequest != null)
            {
                SetStatus($"「{PendingAction.LabelOf(activeRequest.Kind)}」还没做完，请按提示继续，或按各步骤的取消键。");
                return;
            }

            // 「路费付给谁」「入场效果挑一个」这类小选择窗开着时不许换行动：换行动会清掉这次的探索计划或
            // 建设的暂存，弹窗却还留在屏幕上，之后再点它的选项，答案会落进一个已经没有主的流程里。
            if (HasPromptOpen)
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

            // 行动按钮只在玩家行动阶段可用。
            if (requested != ActionMode.None
                && engine.State.Phase != TurnPhase.PlayerActions)
            {
                SetStatus("当前不是玩家行动阶段——阶段会由流程自动推进。");
                return;
            }

            // 联机：只有轮到自己才能选行动。否则界面会跟着当前行动座位切给别人，
            // 看起来就是「变成 2 号的视角，还能替他操作」——那是单机热座的玩法。
            if (lockToLocalSeat && requested != ActionMode.None
                && engine.State.ActiveSeat != localSeat)
            {
                SetStatus($"现在轮到 {SeatLabel(engine.State.ActiveSeat)} 行动，请等待。");
                return;
            }

            SetMode(requested);
        }

        /// <summary>
        /// 调度（§6.4b）排好一步后弹「现在就执行吗」：确定 → 把已排定的 1–2 步一起落地；
        /// 「再走一步」→ 留在调度模式继续选点；取消 → 退出调度并丢弃已排定的步骤。
        /// 原先这颗按钮在行动区顶部，现在改成执行前的确认窗。
        /// </summary>
        private void ShowRelocateCommit()
        {
            if (pendingMoves.Count == 0) return;

            var options = new List<string> { "确定执行调度" };
            if (pendingMoves.Count < 2) options.Add("再走一步");

            Action<int> picked = choice =>
            {
                if (choice == 0)
                {
                    CommitRelocate();
                    return;
                }

                if (choice == 1)
                {
                    SetStatus("请继续点击起点与目标空格，排第 2 步。");
                    UpdateHint();
                    return;
                }

                // 卡牌效果授予的调度不能取消：清掉已排的步骤，回到「重新选起点」，效果不会作废。
                if (activeRequest != null)
                {
                    SetMode(ActionMode.Relocate);
                    SetStatus("这次调度是卡牌效果授予的，必须执行：已清空排定的步骤，请重新点自己的一个影响力。");
                    return;
                }

                SetMode(ActionMode.None);
                SetStatus("已取消调度，排定的步骤一并丢弃。");
            };

            string prompt = $"已排定 {pendingMoves.Count} 步调度。现在就执行吗？";

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show("确认调度", prompt, options, picked);
                return;
            }

            if (choicePopup == null)
            {
                CommitRelocate();
                return;
            }

            choicePopup.Show(prompt, options, picked);
        }

        /// <summary>把已排定的调度步骤一次性交给引擎。</summary>
        private void CommitRelocate()
        {
            if (engine == null || pendingMoves.Count == 0)
            {
                SetStatus("还没有可提交的调度步骤。");
                return;
            }

            var moves = new List<InfluenceMove>(pendingMoves);
            int seat = ActionSeat;
            int pendingId = activeRequest != null && activeRequest.Kind == PendingActionKind.RelocateInfluence
                ? activeRequest.PendingActionId
                : 0;
            if (Dispatch(new RelocateInfluenceCommand(seat, moves, pendingId)))
            {
                SetMode(ActionMode.None);
                if (pendingId > 0) CompleteAction();
            }
        }

        /// <summary>
        /// 探索（§6.4c）选好目标点（默认最省路线）后弹确认：确定 → 付路费、翻牌；
        /// 「调整路线」→ 留在探索模式，点航道空格增删路线后再点目标资源点执行；取消 → 退出探索。
        /// </summary>
        private void ShowExploreCommit()
        {
            if (exploreTargetNode < 0) return;

            GameState state = engine.State;
            int toll = TollCalculator.Compute(state, ActionSeat, exploreRoutes, explorePayees).Total;
            string tag = activeRequest != null ? "（卡牌效果，必须执行）" : string.Empty;

            var options = new[] { "确定探索", "调整路线" };
            string prompt = $"探索 {state.Map.Node(exploreTargetNode).Code}：走 {exploreRoutes.Count} 条航道、路费 {toll} 金券。{tag}\n确定现在就探索吗？";

            Action<int> picked = choice =>
            {
                if (choice == 0)
                {
                    CommitExplore();
                    return;
                }

                if (choice == 1)
                {
                    SetStatus("点航道空格增删路线；选好后点目标资源点再次弹出确认。");
                    UpdateHint();
                    return;
                }

                // 卡牌效果授予的探索不能取消：关掉确认窗就退回到「调整路线」，再来一次。
                if (activeRequest != null)
                {
                    SetStatus("这次探索是卡牌效果授予的，必须执行。点资源点再次弹出确认。");
                    return;
                }

                SetMode(ActionMode.None);
                SetStatus("已取消探索。");
            };

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show("确认探索", prompt, options, picked);
                return;
            }

            if (choicePopup == null)
            {
                CommitExplore();
                return;
            }

            choicePopup.Show(prompt, options, picked);
        }

        /// <summary>
        /// 统一的「执行前确认」窗：点确定才真的落地，点取消什么都不做。
        /// 行动区顶部的确定 / 取消已经去掉，部署 / 城市移动 / 宣告 / 特殊行动 / 角色牌都改由这里问一句。
        /// </summary>
        private void ConfirmAction(string title, Action onConfirm, string confirmLabel = "确定")
        {
            if (onConfirm == null) return;

            string action = string.IsNullOrEmpty(confirmLabel) ? "确定" : confirmLabel;

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                // 窗口标题用「确认 XX」，整句话放进正文：行动确认的句子常常两三行，
                // 塞进标题行会被截断或压到按钮上。
                string head = action.Length > 2 && action.StartsWith("确定", StringComparison.Ordinal)
                    ? "确认" + action.Substring(2)
                    : "请确认";
                window.Show(head, title, new[] { action }, choice =>
                {
                    if (choice >= 0) onConfirm();
                    else SetStatus("已取消。");
                });
                return;
            }

            if (choicePopup == null)
            {
                onConfirm();
                return;
            }

            choicePopup.Show(title, new[] { action }, choice =>
            {
                if (choice >= 0) onConfirm();
                else SetStatus("已取消。");
            });
        }

        /// <summary>
        /// 点「结束回合」（§6.1）：主要行动做完后不再自动交出行动权，玩家还能在回合内任意次执行快速行动，
        /// 直到主动点这颗按钮。先挡掉还没走完的多步流程（那样会把这次行动作废），再弹一次确认。
        /// </summary>
        private void OnEndTurnRequested()
        {
            if (engine == null) return;
            GameState state = engine.State;

            if (HasPromptOpen)
            {
                SetStatus("请先答完当前弹出的选择。");
                return;
            }

            if (eventCardPanel != null && eventCardPanel.IsVisible)
            {
                SetStatus("事件牌还没结算完，先在上面选一个事件选项。");
                return;
            }

            // 建设 / 探索 / 特殊行动走到一半就结束回合，会把这次行动已经付过的东西白扔。
            if (mode != ActionMode.None || buildStep != BuildStep.NotBuilding
                || characterStep != CharacterStep.None || specialStep != SpecialStep.None)
            {
                SetStatus("当前行动还没走完：先点「关闭」退出当前行动，再结束回合。");
                return;
            }

            // 卡牌效果授予的行动还没做完（效果给的行动必须执行，引擎那边也会拒绝结束回合）。
            if (activeRequest != null)
            {
                SetStatus($"「{PendingAction.LabelOf(activeRequest.Kind)}」是卡牌效果授予的，必须做完才能结束回合。");
                return;
            }

            if (state.Phase != TurnPhase.PlayerActions)
            {
                SetStatus("现在不是玩家行动阶段，不能结束回合。");
                return;
            }

            int seat = state.ActiveSeat;
            if (lockToLocalSeat && seat != localSeat)
            {
                SetStatus($"现在轮到 {SeatLabel(seat)} 行动，请等待。");
                return;
            }

            if (!state.MainActionTakenThisRound)
            {
                SetStatus("本回合还没有执行主要行动：必须先做 1 次主要行动，才能结束回合（§6.1）。");
                return;
            }

            ConfirmAction(
                "结束本回合？\n结束前还可以继续执行任意次快速行动；结束后行动权立刻交给下一位。",
                () =>
                {
                    if (Dispatch(new EndTurnCommand(seat))) SetMode(ActionMode.None);
                }, "确定结束回合");
        }

        /// <summary>
        /// 返回 / 退出：行动区右上角「关闭」键在选着行动（或多步流程锁住面板）时走到这里。
        /// 逐层往回退一步（建设 / 事件选项 / 角色牌 / 特殊行动），退无可退就退出当前行动并丢弃已做的选点。
        /// </summary>
        private void OnCancelRequested()
        {
            // 小选择窗开着时，「取消」只作用于那个窗口：否则会把正在进行的探索整段作废，
            // 而窗口还留在屏幕上，看上去像「点了取消却什么都没发生」。
            if (HasPromptOpen)
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

            // 收尾持续计谋的「取消」= 返回键：退掉最近一个答案；退无可退就回到「选哪张计谋」。
            if (cleanupSession != null)
            {
                if (cleanupSession.Back())
                {
                    ContinuePersistentCleanupChoices();
                    return;
                }

                cleanupSession = null;
                cleanupTactic = null;
                persistentCleanupPrompting = false;
                SetStatus("已取消这次收尾发动，可以重新选择。");
                if (engine != null) TryBeginPersistentCleanup(engine.State);
                return;
            }

            if (mode == ActionMode.None) return;

            // 卡牌效果授予的行动退不出去：关掉只会回到「重新选点」，效果必须执行完。
            if (activeRequest != null)
            {
                SetStatus($"「{PendingAction.LabelOf(activeRequest.Kind)}」是卡牌效果授予的，必须做完。");
                SetMode(ModeOf(activeRequest.Kind));
                return;
            }

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

            // 企业特效里的「点地图」选择（放置 / 移动 / 替换影响力等）。
            if (enterpriseSession != null && HandleEnterpriseMapPick(hit)) return;

            // 城市样式特殊行动效果里的「点地图」选择（与部署 / 调度 / 城市移动同源）。
            if (specialSession != null && HandleSpecialMapPick(hit)) return;

            // 收尾阶段持续计谋的「点地图」选择（放置影响力，与部署同源）。
            if (cleanupSession != null && HandlePersistentCleanupMapPick(hit)) return;

            // 事件卡弹窗还开着（展开或最小化）时，地图点击一律忽略 ——
            // 事件还没结算完就不该去点资源点（最小化只是为了方便看地图，不是放弃结算）。
            // 但别静默忽略：玩家会以为点击没生效，把提示写进状态行。
            if (eventCardPanel != null && eventCardPanel.IsVisible)
            {
                SetStatus("事件牌还没结算完，请先在事件卡里选一项奖励。");
                return;
            }

            // 小选择弹窗（如「路费付给谁」）开着时同理，先把它答完。
            if (choicePopup != null && choicePopup.IsVisible)
            {
                SetStatus("还有一个小选择没答完，请先在弹窗里选一项。");
                return;
            }

            // 设施入场效果里的「点地图」选择：与部署 / 调度 / 城市移动同源，直接点地图上的合法位置作答。
            // 不要求 mode == Build —— 开发者模式指定建设落地后也会问入场效果，此时并不在建设模式里。
            if (buildStep == BuildStep.Effect && HandleBuildMapPick(hit)) return;

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
                case ActionMode.RemoveInfluence:
                    HandleRemoveInfluence(hit, state);
                    break;
            }
        }

        /// <summary>是不是「第 1 回合的角色牌盖放阶段」——它兼作开局入场，用来决定初始资源点（§4.3）。</summary>
        private static bool IsSetupPhase(GameState state) =>
            state.TurnNumber == 1 && state.Phase == TurnPhase.CharacterPlacement;

        /// <summary>
        /// 开局入场选初始资源点：点绿色资源点本体，把当前该选的玩家的移动城市停靠上去。
        /// 「轮到谁」由引擎按「起始玩家起顺时针」决定，不是按谁点的算（同屏热座）。
        /// 点中后先高亮并弹「确定停靠 / 取消」，确认才真正提交 —— 停靠位置一旦定下就会翻事件牌。
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

            // 联机：不是本机座位在选点，点地图就该完全没反应 —— 更不能弹出确认框，
            // 否则别人操作时你这边会看到「××的移动城市停靠在…？」这种本不该出现的东西。
            if (lockToLocalSeat && seat != localSeat)
            {
                SetStatus($"现在轮到 {SeatLabel(seat)} 决定初始资源点。");
                return;
            }

            // 资源点本体与它自己的影响力空格都算选中这个资源点：点选本来就该宽容一点。
            // 只认 NodeBody 时，点在绿色圆圈左半边会落到空格环（NodeSlot）上，表现为「点了没反应」。
            if (hit.Kind == MapHitKind.RouteSlot || hit.NodeId < 0)
            {
                SetStatus($"轮到 {SeatLabel(seat)}：请点击一个绿色资源点作为初始资源点。");
                return;
            }

            // 点下去就先按规则校验：2 人局禁选 B-01（§12 变体 2）、别人城市已停靠的点、非绿色点、
            // 事件牌堆已空……这些都不该先弹「停靠在这里？」再报错。点错了直接说原因，换个点就行，
            // 也不会留下一次「待确认 → 提交 → 被拒」的往返。
            CommandResult startCheck = new ChooseStartNodeCommand(seat, hit.NodeId).Validate(state);
            if (!startCheck.Ok)
            {
                SetStatus(startCheck.Error);
                return;
            }

            string code = state.Map.Node(hit.NodeId)?.Code ?? $"#{hit.NodeId}";
            Debug.Log($"[开局选点] 命中 {hit.Kind} 资源点 #{hit.NodeId}（{code}），" +
                      $"由 {SeatLabel(seat)} 决定，先等玩家确认。");

            // 先记下待确认的停靠并高亮，再弹确认框：点错了可以取消重选。
            pendingStartNodeId = hit.NodeId;
            RefreshStartNodeHighlight();

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show(
                    "初始停靠",
                    $"把 {SeatLabel(seat)} 的移动城市停靠到 {code}？\n确认后才会翻出落脚点的事件牌；可以先把窗口最小化，对照地图看清周围再决定。",
                    "确定停靠",
                    () =>
                    {
                        int confirmedNode = pendingStartNodeId;
                        pendingStartNodeId = -1;
                        RefreshStartNodeHighlight();
                        CommitStartNode(seat, confirmedNode);
                    },
                    () =>
                    {
                        pendingStartNodeId = -1;
                        RefreshStartNodeHighlight();
                        SetStatus($"已取消：{SeatLabel(seat)} 请重新点一个绿色资源点作为初始资源点。");
                    });
                return;
            }

            choicePopup.Show(
                $"把{SeatLabel(seat)} 的移动城市停靠到 {code}？",
                new[] { "确定停靠" },
                choice =>
                {
                    if (choice != 0)
                    {
                        pendingStartNodeId = -1;
                        RefreshStartNodeHighlight();
                        SetStatus($"已取消：{SeatLabel(seat)} 请重新点一个绿色资源点作为初始资源点。");
                        return;
                    }

                    int confirmedNode = pendingStartNodeId;
                    pendingStartNodeId = -1;
                    RefreshStartNodeHighlight();
                    CommitStartNode(seat, confirmedNode);
                });
        }

        /// <summary>确认后的真正提交（原「点一下就派发」的逻辑，只是晚了一步）。</summary>
        private void CommitStartNode(int seat, int nodeId)
        {
            if (engine == null || nodeId < 0) return;

            GameState state = engine.State;

            // 确认框开着的这段时间里，联机回放可能已经把该选的座位推给别人了。
            int now = ChooseStartNodeCommand.NextChooser(state);
            if (now != seat)
            {
                SetStatus(now >= 0
                    ? $"现在轮到 {SeatLabel(now)} 决定初始资源点，请重新点一个绿色资源点。"
                    : "所有玩家都已经决定了初始资源点。");
                return;
            }

            // 本地先按同一套规则校验一遍（客户端的规则引擎与主机完全一致）：
            // 非法选择当场给出原因，不必「提交 → 主机拒绝」跑一趟，也就不会让界面停在等确认上。
            CommandResult localCheck = new ChooseStartNodeCommand(seat, nodeId).Validate(state);
            if (!localCheck.Ok)
            {
                SetStatus(localCheck.Error);
                return;
            }

            if (!Dispatch(new ChooseStartNodeCommand(seat, nodeId))) return;

            // 联机客户端不能本地翻牌：事件牌是主机执行时从「绿色事件牌堆」顶抽出来的
            // （ChooseStartNodeCommand.Apply 里 state.EventDeckGreen[0]），客户端此刻还不知道是哪张。
            // 所以这里先等回放；回放（或任何一次状态刷新）到达后由 SyncEventCardFromState 按状态把牌翻开。
            if (onlineClient)
                SetStatus($"[联机] 已提交「{SeatLabel(seat)} 决定初始资源点」，等主机确认后自动翻牌…");
            else
                ShowEventCard(seat, nodeId);

            // 最后一名玩家定完初始资源点后，2 人局（§12 变体 4）的弃设施牌窗本该弹出 —— 但事件卡
            //   正开着时这里不会弹；等事件结算完 <see cref="ResolveEventOption"/> 会再刷一次把它放出来。
            RefreshFacilityViews();
        }

        /// <summary>开局选点：把「待确认」的那个资源点高亮出来；没有待确认时清空高亮。</summary>
        private void RefreshStartNodeHighlight()
        {
            if (renderer == null) return;
            renderer.SetHighlightedNodes(pendingStartNodeId >= 0 ? new[] { pendingStartNodeId } : null);
        }

        /// <summary>
        /// 点在地图上却没命中任何元素：多半是点到资源点左上角那个印刷的指示物框了。
        /// 开局选点阶段必须说清楚「该点哪儿」，否则玩家只会看到「点了没反应」。
        /// </summary>
        private void OnMapMissed(Vector2 world)
        {
            if (engine == null) return;
            if (eventCardPanel != null && eventCardPanel.IsVisible) return;
            if (choicePopup != null && choicePopup.IsVisible) return;

            GameState state = engine.State;
            if (mode != ActionMode.None || !IsSetupPhase(state))
            {
                // 行动里点到海面 / 空地也算「点了没反应」，按当前模式说一句该点什么。
                string hint = mode switch
                {
                    ActionMode.Explore => "请点一个高亮的未探索资源点本体作为目标地点（点海面或已探索区域不算）。",
                    ActionMode.MoveCity => "请点一个高亮的相邻资源点本体（点海面不算）。",
                    ActionMode.Place => "请点地图上高亮的影响力空格。",
                    ActionMode.Relocate => "请点自己的一个影响力，再点目标空格。",
                    ActionMode.RemoveInfluence => "请点高亮的影响力格。",
                    ActionMode.Build => buildStep == BuildStep.Effect
                        ? "请按提示点地图上的合法位置；没有高亮位置时点「取消」换一个选项。"
                        : "这一步在右侧面板里选，地图上暂时不用点。",
                    _ => string.Empty,
                };

                if (hint.Length > 0) SetStatus(hint);
                return;
            }

            // 开局选点：底图上印的资源点圆圈比命中圆大得多，点在圈里却没命中时按「最近的绿色资源点」算，
            // 别让玩家觉得「明明点到绿色区域了却毫无反应」。其余情况才提示该点哪儿。
            const float nearNodeRadius = 0.8f;
            int nodeId = renderer != null ? renderer.NearestNodeId(world, nearNodeRadius) : -1;
            MapNodeState node = nodeId >= 0 ? state.Map.Node(nodeId) : null;
            if (node != null && node.Color == NodeColor.Green)
            {
                Debug.Log($"[开局选点] 未命中但离 {node.Code} 很近，按这个资源点处理。");
                HandleChooseStartNode(new MapHit(MapHitKind.NodeBody, default, nodeId, world), state);
                return;
            }

            int seat = ChooseStartNodeCommand.NextChooser(state);
            string who = seat >= 0 ? SeatLabel(seat) : "当前玩家";
            SetStatus($"{who}：这一点没点到资源点。请点绿色资源点（绿色事件牌对应的资源点）。");
        }

        // —— 事件卡（开局翻出的牌） ——

        /// <summary>
        /// 把引擎刚翻开的那张事件卡弹出来让玩家选选项（开局入场与探索都会翻牌）。
        /// 卡面与选项都来自引擎记在资源点上的 <see cref="MapNodeState.EventCardId"/>。
        /// 返回是否真的弹出来了 —— 没有可用的卡面素材时返回 false，调用方据此提示玩家。
        /// </summary>
        /// <summary>
        /// 联机客户端：按「状态」把本机还没结算的事件牌补开。
        ///
        /// 事件牌是主机执行命令时从牌堆顶抽的（客户端只能等回放），但回放不一定可靠：
        /// 可能被拒绝、可能被随后的快照重放顶掉、客户端重连后也只收到快照。
        /// 所以补开不能只绑在「某条命令回放」上，而是每次状态刷新都检查一遍：
        /// 只要本机城市停在一个「有事件牌、牌还没结算」的资源点上，就把牌翻出来。
        /// </summary>
        private void SyncEventCardFromState(GameState state)
        {
            if (!onlineClient || state == null || eventCardPanel == null) return;
            if (awaitingResolveConfirm) return;                              // 选项已提交，等主机确认
            if (eventCardPanel.IsVisible || pendingEventNodeId >= 0) return;  // 已经有一张牌在处理
            if (localSeat < 0 || localSeat >= state.Players.Count) return;

            int nodeId = state.Player(localSeat).MobileCityNodeId;
            if (nodeId < 0) return;

            MapNodeState node = state.Map.Node(nodeId);
            if (node == null || !node.HasResourceToken || node.EventCardId < 0 || node.EventResolved) return;

            if (!ShowEventCard(localSeat, nodeId)) return;

            // 这张牌可能正是「城市移动」步 2 翻出来的：本方法跑在 SyncPendingEventCard 之前，
            // 牌一弹出来，那边就因为「已经有一张牌在结算」直接返回了。不在这里补记「结算完还要补步 3」，
            // 玩家结算完事件后这次城市移动就永远收不了尾 —— 出发点的影响力会丢，座位也不会推进。
            if (state.CityMoveOriginNodeId >= 0)
            {
                pendingEventFromCityMove = true;
                pendingEventOriginNode = state.CityMoveOriginNodeId;
            }
        }

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
            if (pendingEventNodeId >= 0 || eventCardPanel.IsVisible)
            {
                // 本地已经有一张牌在结算（正常）；联机排障时这条日志能区分「没补弹」和「补弹失败」。
                Debug.Log($"[联机] 已有事件牌在结算（pending=#{pendingEventNodeId}，" +
                          $"visible={eventCardPanel.IsVisible}），不再重复补弹。");
                return;
            }

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
            if (seat != localSeat)
            {
                Debug.Log($"[联机] 事件牌属于座位 {SeatLabel(seat)}，本机是 {SeatLabel(localSeat)}，不由本机补弹。");
                return;
            }

            MapNodeState pendingNode = state.Map.Node(nodeId);
            Debug.Log($"[联机] 补弹事件牌：资源点 #{nodeId}（{pendingNode?.Code}）" +
                      $"有指示物={pendingNode?.HasResourceToken}，牌号={pendingNode?.EventCardId}，" +
                      $"已结算={pendingNode?.EventResolved}，卡面下标={EventCardPanel.SpriteIndexOf(pendingNode?.EventCardId ?? -1)}。");

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

            // 没有条件段（或两种弹窗都没接线）就直接执行结果段。
            if (!option.HasCost || (choicePopup == null && EnsureConfirmWindow() == null))
            {
                BeginEventEffects(option, acceptCost: true);
                return;
            }

            // 条件式选项：先让玩家决定要不要做「→」前那一段；返回则什么都不做，可以改选别的选项。
            ShowChoiceWindow("事件选项",
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
            if (choicePopup == null && EnsureConfirmWindow() == null)
            {
                ResolveEventOption(acceptCost, answers: null);
                return;
            }

            GameState state = engine.State;
            int nodeId = pendingEventNodeId;
            int seat = pendingEventSeat;

            eventSession = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.ExecuteOption(sandbox, seat, option, acceptCost,
                    choices, EnterpriseEffectExtension.Instance, anchorNodeId: nodeId));
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
                if (panel != null) panel.SetLocked(true); // 其余行动按钮锁死，「关闭」当返回键
                SetStatus("请直接点地图上列出的位置作答；点行动区右上角「关闭」可退回重选选项。");
                return;
            }

            EffectChoiceSession session = eventSession;
            ShowChoiceRequest(request, choice =>
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

            CommandResult dispatched = DispatchToEngine(command);
            if (!dispatched.Ok)
            {
                // 没发出去（典型：上一条操作还在等主机确认）。选点时卡面会先收起，
                // 这里把它放回来并保留失败原因，玩家可以直接改选重试，而不是对着收起的面板干等。
                BackToEventCard();
                SetStatus(dispatched.Error);
                return;
            }

            SetStatus($"已执行：{command.Name}");

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
                if (panel != null) panel.SetLocked(true);   // 「关闭」当返回键
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

            // 收尾按**状态**判断，而不是只看刚才记下的标记：客户端补开事件牌、重连后收到快照
            // 都不经过 SyncPendingEventCard，标记可能是空的，但引擎里「还等着在出发点放影响力」的状态一定在。
            int pendingOrigin = engine.State.CityMoveOriginNodeId;
            if (fromCityMove
                || (pendingOrigin >= 0 && engine.State.Player(seat).MobileCityNodeId == nodeId))
            {
                // 城市移动·步 3：事件结算完，在出发点放 1 个自己的影响力（走完这步才占用主要行动名额）。
                FinishCityMove(seat, pendingOrigin >= 0 ? pendingOrigin : cityMoveOrigin,
                    DescribeEventStep(command));
                return;
            }

            if (!fromExplore)
            {
                SetStatus(DescribeEventStep(command));
                return;
            }

            // 探索·步 3：事件结算完，在目标资源点放 1 个自己的影响力（走完这步才占用主要行动名额）。
            int exploreId = explorePendingId;
            var place = new ExploreInfluenceCommand(seat, nodeId, exploreId);
            CommandResult result = DispatchToEngine(place);
            SetMode(ActionMode.None);
            SetStatus(result.Ok
                ? $"{DescribeEventStep(command)}；[探索] 已在目标资源点放置 1 个影响力。"
                : $"{DescribeEventStep(command)}；[探索] 放置影响力失败：{result.Error}");

            if (exploreId > 0) CompleteAction();
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

            int pendingOrigin = engine.State.CityMoveOriginNodeId;
            if (fromCityMove
                || (pendingOrigin >= 0 && engine.State.Player(applied.ActorSeat).MobileCityNodeId == applied.NodeId))
            {
                FinishCityMove(applied.ActorSeat, pendingOrigin >= 0 ? pendingOrigin : cityMoveOrigin,
                    DescribeEventStep(applied));
                return;
            }

            if (!fromExplore)
            {
                SetStatus(DescribeEventStep(applied));
                return;
            }

            // 探索·步 3：目标资源点放 1 个影响力。客户端同样只是提交，结果等主机再回放。
            int exploreId = explorePendingId;
            CommandResult result = DispatchToEngine(new ExploreInfluenceCommand(applied.ActorSeat, applied.NodeId, exploreId));
            SetMode(ActionMode.None);
            SetStatus(result.Ok
                ? $"{DescribeEventStep(applied)}；[探索] 已提交在目标资源点放置 1 个影响力。"
                : $"{DescribeEventStep(applied)}；[探索] 放置影响力失败：{result.Error}");

            if (exploreId > 0) CompleteAction();
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
            // 再次点同一个目标点 = 「执行」（会先弹确认窗）；这样调整过路线后不用另找一颗按钮。
            if (nodeId == exploreTargetNode && exploreRoutes.Count > 0)
            {
                ShowExploreCommit();
                return;
            }

            int seat = ActionSeat;
            PendingActionLimits limits = ExploreLimits();

            var probe = new ExploreCommand(seat, nodeId, null, null, explorePendingId);
            CommandResult check = probe.Validate(state);
            if (!check.Ok)
            {
                SetStatus(check.Error);
                return;
            }

            if (explorePendingId > 0 && !PendingActionRules.CanExplore(state, seat, nodeId, limits))
            {
                SetStatus("这个资源点不在本次效果允许探索的范围里（路费上限 / 距离限制）。");
                return;
            }

            exploreTargetNode = nodeId;
            explorePayees.Clear();

            // 先给一套默认最省方案，玩家再按需点航道空格增删。
            exploreRoutes.Clear();
            IReadOnlyList<int> defaultPath = TollCalculator.FindCheapestPath(
                state, seat, state.Player(seat).MobileCityNodeId, nodeId);
            if (defaultPath != null) exploreRoutes.AddRange(defaultPath);

            RefreshExploreHighlight();
            SetStatus($"[探索] 目标 {state.Map.Node(nodeId).Code}，默认路线 {exploreRoutes.Count} 条航道。");
            ShowExploreCommit();
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

            var probe = new ExploreCommand(ActionSeat, exploreTargetNode, exploreRoutes, explorePayees,
                explorePendingId);
            CommandResult check = probe.Validate(state);
            if (!check.Ok)
            {
                SetStatus(check.Error);
                return;
            }

            payeeQueue.Clear();
            payeePopupMissing = false;

            // 正式规则：一条航道上要**同时**站着 ≥2 位不同对手才需要玩家挑收款方（§6.6）。
            int minimumOpponents = 2;

            foreach (int routeId in probe.PlanRoutes(state))
            {
                int cost = TollCalculator.RouteCost(state, ActionSeat, routeId);

                // 参数顺序是 (routeId, seat) —— 反着写编译器不会报错（两个都是 int），
                // 引擎却会去查「第 ActiveSeat 条航道」并把 routeId 当成座位号，
                // 于是这条航道永远算出 0～1 位对手，收款方窗口一次也弹不出来。
                IReadOnlyList<int> opponents = TollCalculator.OpponentsOnRoute(state, routeId, ActionSeat);
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

            if (choicePopup == null && EnsureConfirmWindow() == null)
            {
                // 需要挑收款方却没接弹窗：退回默认（该航道第一位对手），并在状态行 + Console 里点名，避免静默失效。
                payeePopupMissing = true;
                Debug.LogError($"[探索] 航道 {RouteLabel(routeId)} 需要选择收款方，" +
                               "但 GameScreenController 没有拿到任何选择弹窗。请检查 GameBootstrap 物体上的 " +
                               "「Choice Popup」字段是否拖入了场景里的 ChoicePopup 物体（且该物体是激活状态）。", this);
                AdvancePayeeChoice();
                return;
            }

            var labels = new List<string>(opponents.Count);
            for (int i = 0; i < opponents.Count; i++) labels.Add($"付给 {SeatLabel(opponents[i])}");

            ShowChoiceWindow("探索：选择收款方",
                $"航道 {RouteLabel(routeId)} 上有 {opponents.Count} 位对手，路费付给谁？", labels, index =>
                {
                if (index < 0)
                {
                    // 卡牌效果授予的探索不能取消：把这条选择放回队列重新问，直到选出一个收款方。
                    if (activeRequest != null)
                    {
                        SetStatus("这次探索是卡牌效果授予的，必须执行：请选一个路费收款方。");
                        payeeQueue.Enqueue(new PayeeChoice(routeId, opponents));
                        AdvancePayeeChoice();
                        return;
                    }

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
            int seat = ActionSeat;
            int nodeId = exploreTargetNode;
            int pendingId = explorePendingId;

            var command = new ExploreCommand(seat, nodeId, new List<int>(exploreRoutes),
                new Dictionary<int, int>(explorePayees), pendingId);
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
            explorePendingId = 0;
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

            InfluenceSlotRef slot = hit.Slot;

            // 先校验这个空格能不能放（格满 / 已被占 / 标记用完 / 主要行动已做过…），
            // 不合法就直接说原因，别先弹「在这里放置影响力？」再报错。
            CommandResult placeCheck = new PlaceInfluenceCommand(state.ActiveSeat, slot).Validate(state);
            if (!placeCheck.Ok)
            {
                SetStatus(placeCheck.Error);
                return;
            }

            ConfirmAction($"在{SlotName(slot)}放置 1 个影响力？", () =>
            {
                if (engine == null) return;
                if (Dispatch(new PlaceInfluenceCommand(engine.State.ActiveSeat, slot)))
                    SetMode(ActionMode.None);
            }, "确定放置");
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
                SetStatus("一次调度至多 2 步，请先在弹出的确认窗里执行，或点「关闭」退出。");
                return;
            }

            pendingMoves.Add(new InfluenceMove(relocateFrom.Value, hit.Slot));
            relocateFrom = null;
            UpdateHint();

            // 排好这一步就把「确认执行」弹出来：这正是原先行动区顶部那颗「确认」的时机。
            ShowRelocateCommit();
        }

        private void HandleMoveCity(MapHit hit, GameState state)
        {
            if (hit.Kind != MapHitKind.NodeBody)
            {
                SetHint("请点击一个资源点本体（移动城市要停靠的目标）。");
                return;
            }

            int seat = ActionSeat;
            int nodeId = hit.NodeId;
            int pendingId = activeRequest != null && activeRequest.Kind == PendingActionKind.CityMove
                ? activeRequest.PendingActionId
                : 0;
            bool free = pendingId > 0 && cityMoveFree;

            // 和目标点选择同理：先按规则校验（红色区域未开放、源石不足、不相邻、对手城市占着…），
            // 不合法就直接说原因，别先弹「停靠到这里？」再报错。
            CommandResult moveCheck = new MoveMobileCityCommand(seat, nodeId, pendingId).Validate(state);
            if (!moveCheck.Ok)
            {
                SetStatus(moveCheck.Error);
                return;
            }

            // 出发点：派发前先记下来 —— 联机客户端在命令回放之前，state.CityMoveOriginNodeId 还是旧值。
            int originNode = state.Player(seat).MobileCityNodeId;
            string target = state.Map.Node(nodeId).Code;
            string cost = free ? "本次免费，不用付源石" : "需付 3 源石";

            ConfirmAction(
                $"把移动城市停靠到 {target}？\n{cost}；目标点若没有指示物会先翻出事件牌。",
                () => DoMoveCity(seat, nodeId, originNode), "确定移动");
        }

        /// <summary>真正派发城市移动（步 1–2）：付 3 源石、移动、清对手影响力，目标点没指示物就翻牌补上。</summary>
        private void DoMoveCity(int seat, int nodeId, int originNode)
        {
            if (engine == null) return;

            // 步 1–2（§6.4d）：付 3 源石、移动、清对手影响力，目标点没有指示物就翻牌补上。它不占行动名额。
            int pendingId = activeRequest != null && activeRequest.Kind == PendingActionKind.CityMove
                ? activeRequest.PendingActionId
                : 0;
            if (!Dispatch(new MoveMobileCityCommand(seat, nodeId, pendingId))) return;

            SetMode(ActionMode.None);

            // 联机客户端：状态还没回放，牌有没有翻出来还不知道；「先弹牌还是补步 3」交给回放后的
            //   SyncPendingEventCard 统一处理（否则会读着旧状态直接补步 3，与事件流程重复）。
            if (onlineClient)
            {
                SetStatus("[城市移动] 已提交给主机，等待判定…");
                return;
            }

            // 步 2 翻出了事件牌：先让玩家结算选项，结算完在 ResolveEventOption 里补上步 3。
            if (ShowEventCard(seat, nodeId))
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
            int pendingId = cityMovePendingId;
            CommandResult result = DispatchToEngine(new MoveCityInfluenceCommand(seat, originNodeId, pendingId));
            SetMode(ActionMode.None);

            string lead = string.IsNullOrEmpty(prefix) ? "[城市移动]" : prefix + "；[城市移动]";
            SetStatus(result.Ok
                ? $"{lead} 已在出发点放置 1 个影响力。"
                : $"{lead} 放置影响力失败：{result.Error}");

            if (result.Ok && pendingId > 0) CompleteAction();
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

            // 卡牌效果授予的建设：只接受这次限制之内的牌（金券上限 / 供应区 / 面板空位）。
            if (activeRequest != null && activeRequest.Kind == PendingActionKind.Build)
            {
                if (!PendingActionRules.BuildableFacilityIds(state, activeRequest.Seat, activeRequest.Limits)
                        .Contains(cardId))
                {
                    SetStatus($"这张牌不符合本次效果的建设限制（金券费用上限 {activeRequest.Limits.MaxVoucherSpend}）。请换一张。");
                    return;
                }

                buildSeat = activeRequest.Seat;
            }
            else
            {
                buildSeat = ViewSeat(state);
            }

            buildCardId = cardId;

            // 选牌窗的收起交给 EnterBuildPay：付得起的牌才收起面板，
            // 两张费用都付不起时直接报「资源不足」并留在选牌窗，不让人再往里走。
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

            // 卡牌效果给的建设可能免费，也可能限了金券费用上限（见 PendingActionLimits）。
            PendingActionLimits limits = BuildLimits();

            // 上限是「这次效果只能建多贵的设施」，与用哪种方式付款无关：超了直接换牌。
            if (limits.MaxVoucherSpend >= 0 && card.VoucherCost > limits.MaxVoucherSpend)
            {
                SetStatus($"这次效果只能建设金券费用不超过 {limits.MaxVoucherSpend} 的设施，请换一张。");
                return;
            }

            if (limits.Free || card.IsFree || (!hasResources && !hasVouchers))
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

            bool canPayResources = hasResources
                                   && FacilityCardDef.CanPayWithResources(resourceCost, player.Resources, player.Vouchers);
            bool voucherWithinLimit = limits.MaxVoucherSpend < 0 || card.VoucherCost <= limits.MaxVoucherSpend;
            bool canPayVouchers = hasVouchers && voucherWithinLimit && player.Vouchers >= card.VoucherCost;

            // 两种付费方式都付不起：直接停在这里报「资源不足」，不要把人放进选位置那一步 ——
            // 否则要一路点到最后确认时才被拒，还以为是自己选错了格子。
            if (!canPayResources && !canPayVouchers)
            {
                buildStep = BuildStep.PickCard;
                buildCardId = -1;
                buildPayment = FacilityPayment.None;
                if (supplyPanel != null) supplyPanel.SetOpen(true);

                var needParts = new List<string>();
                if (hasResources) needParts.Add($"资源：{DescribeCost(resourceCost)}");
                if (hasVouchers) needParts.Add($"金券：{card.VoucherCost} 张");
                string need = string.Join(" / ", needParts);

                SetStatus($"资源不足，无法建造《{card.Name}》（需要 {need}）。请换一张牌，或点右上角「关闭」退出建设。");
                return;
            }

            string resourceText = hasResources ? $"选项一 · 资源：{DescribeCost(resourceCost)}" : string.Empty;
            string voucherText = hasVouchers
                ? $"选项二 · 金券：{card.VoucherCost} 张" +
                  (limits.MaxVoucherSpend >= 0 ? $"（本次效果上限 {limits.MaxVoucherSpend}）" : string.Empty)
                : string.Empty;

            // 付得起才收起选牌窗，把位置让给「放大卡面 + 选费用」。
            if (supplyPanel != null) supplyPanel.SetOpen(false);

            cardPreview.ShowForBuild(state, buildCardId,
                resourceText,
                canPayResources,
                () =>
                {
                    buildPayment = FacilityPayment.Resources;
                    FinishBuildPay();
                },
                voucherText, canPayVouchers,
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

            string title = $"在此处建造《{card.Name}》？\n得分 {card.Score:+0;-0;0}；获得：{DescribeEntryEffects(card)}";

            Action<int> picked = choice =>
            {
                if (engine == null) return;

                if (choice == 0) BeginBuildEffects();
                else EnterBuildSlot();
            };

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show("确认建造", title, new[] { "确定建造" }, picked);
                return;
            }

            if (choicePopup == null)
            {
                SetStatus("建设确认弹窗没接线，直接按「确定」处理。");
                BeginBuildEffects();
                return;
            }

            choicePopup.Show(title, new[] { "确定建造" }, picked);
        }

        /// <summary>
        /// L4：确认「在此处建造」后，**先让设施落地**（扣费 + 上板 + 得分），
        /// 再弹窗外结算入场效果（有选项就给选项，没有就给一个「确定」）。
        /// 这样玩家能先看到设施进入城市面板，再处理入场效果，顺序与桌游一致。
        /// </summary>
        private void BeginBuildEffects()
        {
            if (engine == null) return;

            // 步 1–2：只做扣费 + 上板，不结算入场效果。
            var buildCommand = new BuildFacilityCommand(buildSeat, buildCardId, buildSlot, buildPayment,
                new RecordedChoices(Array.Empty<int>(), strict: false),
                pendingActionId: buildPendingId, runEntryEffects: false);

            if (!Dispatch(buildCommand))
            {
                // 引擎拒绝（费用不够、与「唯一」冲突、格子被占……）：原因已写进状态行，退回 L3 让玩家改。
                BackToBuildConfirm();
                return;
            }

            // 设施已落地：接着结算入场效果。
            BeginFacilityEntryEffect(buildSeat, buildCardId, buildSlot);
        }

        /// <summary>
        /// 设施落地后结算入场效果：有需要点选的内容就开会话（弹窗 / 点地图），
        /// 没有就弹一个「确定」窗让玩家确认结算。供手动建设与开发者建设共用。
        /// </summary>
        private void BeginFacilityEntryEffect(int seat, int facilityId, int panelSlot)
        {
            if (engine == null) return;

            GameState state = engine.State;
            FacilityCardDef card = state.Catalog.Facility(facilityId);
            IReadOnlyList<RewardOp> effects = card != null ? card.EntryEffects : null;

            buildStep = BuildStep.Effect;
            buildSeat = seat;
            buildCardId = facilityId;
            buildSlot = panelSlot;

            // 没有入场效果：也弹一个窗，点「确定」才结算（给玩家一个明确的「入场效果结算完毕」反馈）。
            if (effects == null || effects.Count == 0)
            {
                string name = card != null ? card.Name : "设施";
                if (!ShowChoiceWindow($"《{name}》入场效果", "这张设施没有入场效果，点「确定」结束建设。",
                        new[] { "确定" }, _ => FinishBuild(), allowCancel: false))
                    FinishBuild();
                return;
            }

            // 设施已经在板上，在状态副本上试跑整串入场效果：跑到需要选择处就停下，等玩家点完再继续。
            buildSession = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, seat, effects, choices,
                    EnterpriseEffectExtension.Instance));
            buildSession.Advance();
            ContinueBuildChoices();
        }

        /// <summary>
        /// 抛出 / 点选入场效果的下一个待答选择：
        /// 候选是地图位置（放 / 移 / 拿走影响力、选资源点）的走「点地图」，与主要行动同源；
        /// 其余抽象选项走弹窗。设施已经落地，所以这里不能再退回建设确认，只能在入场效果的答案之间回退。
        /// </summary>
        private void ContinueBuildChoices()
        {
            if (buildSession == null) return;

            if (buildSession.IsComplete)
            {
                IReadOnlyList<int> answers = buildSession.Answers;
                buildSession = null;
                DispatchEntryEffect(answers);
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

            EffectChoiceSession session = buildSession;
            ShowChoiceRequest(request, choice =>
            {
                if (session != buildSession) return; // 会话已被作废（例如中途换了行动）

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinueBuildChoices();
                    return;
                }

                // 取消键：只能在入场效果的答案之间回退；设施已落地，不能再退回建设确认。
                if (session.Back()) ContinueBuildChoices();
                else ContinueBuildChoices();
            });
        }

        /// <summary>入场效果的答案收齐：派发「结算设施入场效果」命令，结束本次建设。</summary>
        private void DispatchEntryEffect(IReadOnlyList<int> answers)
        {
            if (engine == null) return;

            var command = new ResolveFacilityEntryEffectCommand(buildSeat, buildSlot,
                new RecordedChoices(answers, strict: false));

            if (!Dispatch(command))
            {
                // 极少失败（格子被改掉了）：原因写进状态行，直接结束建设流程。
                FinishBuild();
                return;
            }

            FinishBuild(command.Report);
        }

        /// <summary>一次建设（落地 + 入场效果）全部走完：退出建设模式、交还控制权。</summary>
        private void FinishBuild(EffectReport entryReport = null)
        {
            int seat = buildSeat;
            int cardId = buildCardId;
            int slot = buildSlot;
            buildStep = BuildStep.NotBuilding;
            SetMode(ActionMode.None);

            // 把入场效果的结果也念一遍：效果里「没有合法目标、已自动跳过」这类信息原先只留在报告里，
            // 界面看起来就是「选完之后什么都没发生」，难以判断是卡住还是跳过。
            string detail = entryReport != null ? entryReport.Describe() : string.Empty;
            if (!string.IsNullOrEmpty(detail)) detail = "；入场效果：" + detail;

            SetStatus($"[建设] 玩家{seat + 1} 建造了《{FacilityName(engine.State, cardId)}》" +
                      $"，放在城市面板第 {slot + 1} 格，入场效果已结算{detail}");

            // 卡牌效果授予的建设走完：交还控制权，下一条挂起行动（例如载具仓库给的探索）下一帧接管。
            CompleteAction();
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
            if (matches.Count <= 1 || (choicePopup == null && EnsureConfirmWindow() == null))
            {
                ConfirmDeclare(cardId, matches.Count > 1 ? 0 : -1);
                return;
            }

            var labels = new List<string>(matches.Count);
            for (int i = 0; i < matches.Count; i++)
                labels.Add($"摆法 {i + 1}：{DescribePatternSlots(matches[i])}（旋转 {matches[i].RotationDegrees}°）");

            ShowChoiceWindow($"《{PatternName(state, cardId)}》选摆法",
                $"这张牌有 {matches.Count} 组摆法，你想用哪几格？\n（用过的设施不能再用于下一次宣告）",
                labels, choice =>
                {
                    if (choice >= 0) ConfirmDeclare(cardId, choice);
                    else SetStatus("已返回，可以重新选择要宣告的城市样式牌。");
                });
        }

        /// <summary>执行前的确认窗：点「确定宣告」才真的宣告，取消则留在样式牌弹窗里换一张。</summary>
        private void ConfirmDeclare(int cardId, int placementIndex)
        {
            if (engine == null) return;

            string name = PatternName(engine.State, cardId);
            ConfirmAction($"宣告城市样式《{name}》？",
                () => DeclarePatternWithPlacement(cardId, placementIndex), "确定宣告");
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
        /// 收集期间锁住其他行动按钮，行动区右上角「关闭」当返回键。
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

            ConfirmAction($"使用《{PatternName(state, cardId)}》的特殊行动？\n需要支付：{DescribeOps(card.SpecialActionCost)}",
                () => StartSpecialAction(cardId), "确定使用");
        }

        /// <summary>确认之后真正开始：收费用、结算效果，效果里要玩家点选的逐步问。</summary>
        private void StartSpecialAction(int cardId)
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

            // 没有要玩家点选的选择（或两种弹窗都没接线）就直接派发，省掉一轮无意义的会话。
            if (choicePopup == null && EnsureConfirmWindow() == null)
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
                EffectResolver.Execute(sandbox, seat, cost, choices, EnterpriseEffectExtension.Instance);
                EffectReport report = EffectResolver.Execute(sandbox, seat, effects, choices,
                    EnterpriseEffectExtension.Instance);
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

            EffectChoiceSession session = specialSession;
            ShowChoiceRequest(request, choice =>
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
            });
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
            // 企业扩展：开局入场要先从 7 张手牌里选 2 张放回游戏盒。
            if (!player.SetupDiscardDone) return false;
            return player.PlacedCharacterCard < 0;
        }

        /// <summary>
        /// 企业扩展开局入场第一步：每名玩家先决定初始企业（EnterpriseExpansion.md §4.1）。
        /// 时机在企业家选完、开局之后，且必须先于「决定初始资源点」与「7 选 5」。
        /// 3／4 人局抽 2 选 1；2 人局抽 4 选 2（同一位玩家会连续弹两次）。
        /// </summary>
        private void TryBeginEnterpriseDraft(GameState state)
        {
            if (engine == null) return;
            if (state.Setup == null || !state.Setup.IsEnterpriseEnabled) return;
            if (state.TurnNumber != 1 || state.Phase != TurnPhase.CharacterPlacement) return;

            EnterpriseState enterprises = state.Enterprises;
            if (enterprises.DraftDone) return;
            if (state.DeveloperMode) return;       // 开发者模式可以跳过企业抽选，别弹窗挡住测试
            if (pendingEventNodeId >= 0) return;   // 事件卡开着时让位
            if (onlineSubmitPending) return;       // 联机：上一条命令还在等主机回放

            int seat = FindSeatNeedingEnterprisePick();
            if (seat < 0) return;

            // 已经替这位玩家把牌摆出来了：别每次刷新都重开一遍（会把正在放大的那张合上）。
            if (enterpriseBrowser != null && enterpriseBrowser.IsOpen
                && enterpriseBrowser.CurrentMode == EnterpriseBoardPanel.Mode.Draft
                && enterpriseBrowser.Seat == seat)
            {
                return;
            }

            BeginEnterprisePick(seat);
        }

        /// <summary>
        /// 企业扩展开局入场：每名玩家从 7 张手牌里选 2 张放回游戏盒（EnterpriseExpansion.md §4.2）。
        /// 开局入场（选点 + 结算事件牌）走完后，用统一窗口弹窗连问两次并要求确认；
        /// 完成后才能盖放角色牌。
        /// </summary>
        private void TryBeginSetupDiscard(GameState state)
        {
            if (engine == null) return;
            if (!state.Setup.IsEnterpriseEnabled) return;
            if (state.TurnNumber != 1 || state.Phase != TurnPhase.CharacterPlacement) return;
            // 先决定初始企业；开发者模式可以跳过企业抽选，直接做「7 选 5」。
            if (!state.Enterprises.DraftDone && !state.DeveloperMode) return;
            if (ChooseStartNodeCommand.CurrentOperator(state) >= 0) return; // 开局入场还没走完
            if (pendingEventNodeId >= 0) return;                             // 事件卡开着时让位
            if (IsChoicePromptOpen()) return;                                // 已有别的选择

            int seat = ViewSeat(state);
            if (seat < 0 || seat >= state.Players.Count) return;
            if (!CanOperateSeat(seat)) return;

            PlayerState player = state.Players[seat];
            if (player.SetupDiscardDone)
            {
                setupDiscardSeat = -1;
                setupDiscardFirst = -1;
                setupDiscardSecond = -1;
                return;
            }
            if (player.HandCharacterCards.Count <= 2) return;

            if (setupDiscardSeat != seat)
            {
                setupDiscardSeat = seat;
                setupDiscardFirst = -1;
                setupDiscardSecond = -1;
            }

            ShowSetupDiscardPrompt(state, player, seat);
        }

        /// <summary>「7 选 5」：选第 1 张 → 选第 2 张 → 确认放回（可重新选择）。</summary>
        private void ShowSetupDiscardPrompt(GameState state, PlayerState player, int seat)
        {
            // 选完第 2 张后先进确认窗；确认窗里选「重新选择」会退回这一步。
            if (setupDiscardFirst >= 0 && setupDiscardSecond >= 0)
            {
                ShowSetupDiscardConfirm(state, player, seat);
                return;
            }

            bool firstPick = setupDiscardFirst < 0;
            var ids = new List<int>();
            var labels = new List<string>();
            foreach (int cardId in player.HandCharacterCards)
            {
                if (cardId == setupDiscardFirst || cardId == setupDiscardSecond) continue;
                ids.Add(cardId);
                CharacterCardDef def = state.Catalog != null ? state.Catalog.Character(cardId) : null;
                labels.Add(DescribeSetupCard(def, cardId));
            }

            if (ids.Count == 0) return;

            string title = firstPick ? "开局选牌：放回第 1 张" : "开局选牌：放回第 2 张";
            string prompt = firstPick
                ? $"{SeatLabel(seat)}：开局拿到 {player.HandCharacterCards.Count} 张角色牌，本局只用 5 张。\n"
                  + "请选 2 张放回游戏盒 —— 选中的牌本局不会再出现。"
                : $"{SeatLabel(seat)}：第 1 张选的是《{SetupCardName(state, setupDiscardFirst)}》，再选 1 张。";

            Action<int> picked = choice =>
            {
                if (choice < 0 || choice >= ids.Count) return;
                if (setupDiscardFirst < 0) setupDiscardFirst = ids[choice];
                else setupDiscardSecond = ids[choice];
                ShowSetupDiscardPrompt(state, player, seat);
            };

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show(title, prompt, labels, picked, allowCancel: false);
                return;
            }

            if (choicePopup != null)
                choicePopup.Show(title, labels, picked, allowCancel: false);
        }

        /// <summary>「7 选 5」最后一步：确认把这两张放回游戏盒，或退回去重选。</summary>
        private void ShowSetupDiscardConfirm(GameState state, PlayerState player, int seat)
        {
            string a = SetupCardName(state, setupDiscardFirst);
            string b = SetupCardName(state, setupDiscardSecond);
            string title = "确认开局选牌";
            string prompt = $"{SeatLabel(seat)}：把《{a}》《{b}》放回游戏盒，本局不再出现。\n"
                            + $"确认后手牌只剩 {player.HandCharacterCards.Count - 2} 张。";
            var options = new[] { "确定放回", "重新选择" };

            Action<int> picked = choice =>
            {
                if (choice == 0)
                {
                    int first = setupDiscardFirst;
                    int second = setupDiscardSecond;

                    // 先把本地步骤清掉：Dispatch 会立刻刷新，下一次「7 选 5」不能被当成还没选。
                    setupDiscardSeat = -1;
                    setupDiscardFirst = -1;
                    setupDiscardSecond = -1;

                    var command = new DiscardSetupHandCardsCommand(seat, first, second);
                    if (Dispatch(command))
                    {
                        SetStatus($"{SeatLabel(seat)} 已把《{a}》《{b}》放回游戏盒，可以盖放角色牌了。");
                        return;
                    }

                    // 罕见情况（状态刚被别处改动）：把选择放回去，让玩家再确认一次。
                    setupDiscardSeat = seat;
                    setupDiscardFirst = first;
                    setupDiscardSecond = second;
                    ShowSetupDiscardConfirm(state, player, seat);
                    return;
                }

                // 重新选择：从第 1 张开始。
                setupDiscardFirst = -1;
                setupDiscardSecond = -1;
                ShowSetupDiscardPrompt(state, player, seat);
            };

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show(title, prompt, options, picked, allowCancel: false);
                return;
            }

            if (choicePopup != null)
                choicePopup.Show(prompt, options, picked, allowCancel: false);
        }

        /// <summary>开局选牌列表里的「卡名 + 策略 / 计谋」，长文本交给窗口自动折行。</summary>
        private static string DescribeSetupCard(CharacterCardDef def, int cardId)
        {
            if (def == null) return $"角色牌 {cardId}";
            string strategy = string.IsNullOrEmpty(def.StrategyName) ? "—" : def.StrategyName;
            string tactics = string.IsNullOrEmpty(def.TacticsName) ? "—" : def.TacticsName;
            return $"《{def.Name}》策略：{strategy} ｜ 计谋：{tactics}";
        }

        private static string SetupCardName(GameState state, int cardId)
        {
            CharacterCardDef def = state.Catalog != null ? state.Catalog.Character(cardId) : null;
            return def != null ? def.Name : $"角色牌 {cardId}";
        }

        /// <summary>小选择弹窗或窗口弹窗是否已经开着（开局步骤不要抢别人的弹窗）。</summary>
        private bool IsChoicePromptOpen()
        {
            if (choicePopup != null && choicePopup.IsVisible) return true;
            return confirmWindow != null && confirmWindow.IsVisible;
        }

        /// <summary>手牌区「已盖放」：看所有玩家当前盖放在场上的角色牌。</summary>
        private void OnPlacedCardsClicked() => ShowCharacterPiles(placed: true);

        /// <summary>手牌区「弃牌区」：看所有玩家的角色牌弃牌区（按进入弃牌区的顺序）。</summary>
        private void OnDiscardPileClicked() => ShowCharacterPiles(placed: false);

        private void ShowCharacterPiles(bool placed)
        {
            if (engine == null || engine.State == null) return;

            if (pilePanel == null)
            {
                Canvas canvas = ResolveCanvas();
                if (canvas == null) return;
                pilePanel = CharacterPilePanel.Create(canvas.transform,
                    hud != null ? hud.UiFont : null,
                    handPanel != null ? handPanel.CardFaces : null);
            }

            if (placed) pilePanel.ShowPlaced(engine.State);
            else pilePanel.ShowDiscards(engine.State);
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

            // 盖放阶段也不再「点一下就盖」：先放大卡面看清楚，玩家点「确定盖放」才真的盖下去，也可取消退回。
            if (cardPreview != null)
            {
                cardPreview.ShowForCharacterPlacement(state, cardId, () => ConfirmPlaceCharacter(cardId));
                return;
            }

            PlaceCharacterCard(state, seat, cardId);
        }

        /// <summary>确认弹窗点了「确定盖放」：收起弹窗，重新校验一次当前状态，再真正盖放。</summary>
        private void ConfirmPlaceCharacter(int cardId)
        {
            if (cardPreview != null) cardPreview.Hide();
            if (engine == null) return;

            GameState state = engine.State;
            int seat = ViewSeat(state);
            PlayerState player = seat >= 0 && seat < state.Players.Count ? state.Players[seat] : null;
            if (player == null) return;

            // 弹窗开着的这段时间状态可能被推着走了（联机回放），盖放前再确认一次。
            if (!CanPlaceCharacter(state, player))
            {
                SetStatus("现在不能盖放角色牌了（阶段或前置条件已变）。");
                return;
            }

            PlaceCharacterCard(state, seat, cardId);
        }

        /// <summary>真正把牌盖下去，并交代结果。</summary>
        private void PlaceCharacterCard(GameState state, int seat, int cardId)
        {
            if (!Dispatch(new PlaceCharacterCardCommand(seat, cardId))) return;

            // 最后一名玩家盖放完，引擎已经自动进入玩家行动；状态行给一句明确交代。
            if (state.Phase == TurnPhase.PlayerActions)
            {
                SetStatus("所有人已盖放完毕——自动进入玩家行动第 1 轮。");
                return;
            }

            CharacterCardDef def = state.Catalog.Character(cardId);
            SetStatus($"[盖放] 玩家{seat + 1} 盖放了《{(def != null ? def.Name : cardId.ToString())}》。");
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

            if (confirmWindow != null && confirmWindow.IsVisible)
            {
                SetStatus("请先答完当前弹出的选择。");
                return;
            }

            GameState state = engine.State;

            // 「使用角色牌」是玩家行动阶段的快速行动。
            if (state.Phase != TurnPhase.PlayerActions)
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

            characterSeat = seat;
            characterCardId = player.PlacedCharacterCard;

            if (choicePopup == null && EnsureConfirmWindow() == null)
            {
                SetStatus("使用角色牌的弹窗没接线，无法选策略 / 计谋。");
                return;
            }

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

            // 起始玩家（或企业扩展放开限制的玩家）两种效果都能发；普通玩家二选一 —— 文案与引擎判定保持一致。
            bool bothEffects = state.FirstPlayerSeat == seat || player.BothCharacterEffectsThisTurn;
            string prompt = bothEffects
                ? $"《{def.Name}》：本回合你是起始玩家，策略与计谋可以各发动一次。\n请选择先发动哪一种。"
                : $"《{def.Name}》：本回合你只能发动其中一种效果，请选择。";

            Action<int> picked = choice =>
            {
                if (choice < 0 || choice >= kinds.Count)
                {
                    SetStatus("已取消使用角色牌。");
                    return;
                }

                BeginCharacterEffect(kinds[choice]);
            };

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show("使用角色牌", prompt, options, picked);
                return;
            }

            choicePopup.Show(prompt, options, picked);
        }

        /// <summary>
        /// 发动已选定的那一种效果：在状态副本上试跑整串效果，跑到需要选择处就停下，
        /// 等玩家点完再继续（与建设 L4 / 事件选项共用同一套 <see cref="EffectChoiceSession"/>）。
        /// 收集期间锁住其他行动按钮，行动区右上角「关闭」当返回键。
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

            if (ops == null || ops.Count == 0 || (choicePopup == null && EnsureConfirmWindow() == null))
            {
                DispatchCharacterEffect(Array.Empty<int>());
                return;
            }

            characterSession = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, characterSeat, ops, choices,
                    EnterpriseEffectExtension.Instance));
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

            EffectChoiceSession session = characterSession;
            ShowChoiceRequest(request, choice =>
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
            });
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

        /// <summary>起始玩家用掉一种效果后：问「继续发动另一种 / 结束使用」，期间不得插入其他行动（§10.1.2）。</summary>
        private void BeginSecondEffectPrompt()
        {
            if (engine == null) return;

            characterStep = CharacterStep.Second;

            PlayerState player = engine.State.Player(characterSeat);
            CharacterCardDef def = engine.State.Catalog.Character(characterCardId);
            string cardName = def != null ? def.Name : "这张角色牌";

            // 还没用的那一种（与引擎「另一种是否还在」的判定同源）。
            bool nextIsTactics = player.UsedCharacterStrategyThisTurn;
            string nextName = def == null
                ? (nextIsTactics ? "计谋" : "策略")
                : $"{(nextIsTactics ? "计谋" : "策略")} · {(nextIsTactics ? def.TacticsName : def.StrategyName)}";

            string prompt =
                $"《{cardName}》还可以按任意顺序发动另一种效果：{nextName}。\n" +
                "两种效果之间不能插入别的行动；也可以就此结束使用，把牌移入弃牌区。";

            var options = new[] { "继续发动另一种", "结束使用" };
            Action<int> picked = choice =>
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
            };

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show("角色牌：还可以发动另一种", prompt, options, picked, allowCancel: false);
                return;
            }

            if (choicePopup == null)
            {
                characterStep = CharacterStep.None;
                SetStatus("本回合可连发两种效果：再点「使用角色牌」可发动另一种。");
                return;
            }

            choicePopup.Show(prompt, options, picked);
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
                // 报的是「这条命令想让谁行动」，而不是 ActiveSeat —— 开局入场这类阶段 ActiveSeat
                // 固定在起始玩家身上，用它拼出来会变成「轮到我自己行动，请等待」，正好反了。
                string waiting = $"现在轮到 {SeatLabel(command.ActorSeat)} 行动，请等待。";
                SetStatus(waiting);
                return CommandResult.Fail(waiting);
            }

            // 联机客户端：上一条命令还在路上就不要再发。连点 / 重复触发会让主机执行一条、拒绝一条，
            // 那条拒绝回放回来会把正在等待的流程（典型是事件卡确认）当成失败处理。
            if (onlineClient && onlineSubmitPending && Time.unscaledTime < onlineSubmitDeadline)
            {
                const string pendingText = "上一条操作还在等主机确认，请稍候…";
                SetStatus($"[联机] {pendingText}");
                return CommandResult.Fail(pendingText);
            }

            // 联机客户端：发出去之前先在本机按同一套规则校验一遍。客户端的引擎与主机用同一份状态、
            // 同一份规则代码，所以「2 人局不能选 B-01」「红色区域还没开放」「费用不够」这类非法操作
            // 在这里就当场给出原因，不占一次「提交 → 主机拒绝」的往返；上层多步流程也能照常走自己的
            // 失败回退（建设退回确认、特殊行动退回选牌、探索清掉选点…），不会停在一个没人确认的状态上。
            // 主机仍是最终裁决者：这里只挡「本机就能确定非法」的操作。
            if (onlineClient && commandGateway != null && engine != null
                && command != null && command.ActorSeat >= 0)
            {
                CommandResult localCheck = command.Validate(engine.State);
                if (!localCheck.Ok) return localCheck;
            }

            // 兜住「命令执行时抛异常」：异常在这里冒出去就没人接，玩家看到的现象只会是
            // 「点了没反应 / 流程卡住」，Console 里也只是一条不容易对上号的堆栈。
            try
            {
                CommandResult result = commandGateway != null ? commandGateway(command) : engine.Dispatch(command);

                if (onlineClient && commandGateway != null && result.Ok && command != null && command.ActorSeat >= 0)
                {
                    onlineSubmitPending = true;
                    onlineSubmitAt = Time.unscaledTime;
                    onlineSubmitDeadline = Time.unscaledTime + OnlineSubmitTimeoutSeconds;
                }

                return result;
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

            // 退出行动时把「移除影响力」的候选也丢掉；留在该模式则按当前限制重新填一遍。
            removeCandidates.Clear();
            if (engine != null)
            {
                if (next == ActionMode.RemoveInfluence)
                {
                    int removeSeat = activeRequest != null ? activeRequest.Seat : engine.State.ActiveSeat;
                    PendingInfluenceScope scope = activeRequest != null
                        ? activeRequest.Limits.Scope
                        : PendingInfluenceScope.Any;
                    removeCandidates.AddRange(PendingActionRules.RemoveCandidates(engine.State, removeSeat, scope));
                }

                if (renderer != null)
                    renderer.SetHighlightedSlots(next == ActionMode.RemoveInfluence ? removeCandidates : null);
            }

            buildPendingId = next == ActionMode.Build ? buildPendingId : 0;

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

            // 行动确认窗同理：换了行动还留着旧确认，点下去会把过期答案交给已经换掉的流程。
            if (confirmWindow != null && confirmWindow.IsVisible) confirmWindow.Hide();

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
                    // 企业扩展：开局入场后先逐位做「7 选 5」，完成后才允许盖放（§4.2）。
                    int setupDiscardSeat = FirstSeatNeedingSetupDiscard(state);
                    if (setupDiscardSeat >= 0)
                    {
                        SetHint($"{SeatLabel(setupDiscardSeat)} 开局拿到 7 张角色牌：请先在弹窗里选 2 张放回游戏盒，本局只用 5 张。");
                        return;
                    }

                    // 企业扩展：红区开放回合先让每名玩家提升 1 级，再开始盖放（§3.2）。
                    if (state.Enterprises != null && state.Enterprises.RedZoneAdvanceSeat >= 0)
                    {
                        SetHint($"{SeatLabel(state.Enterprises.RedZoneAdvanceSeat)} 红色区域已开放：点「企业」按钮，选择 1 家企业把自己的合作等级提升 1 级。");
                        return;
                    }

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
                    panel.SetHint("角色牌：请在弹出的窗口里选「继续发动另一种」或「结束使用」（§10.1.2）。");
                    return;
                }

                // 采集阶段：还在选点（走下面的 Harvest 分支）或自己已提交、等其他人提交（引擎收齐后自动结算并进入下一回合）。
                if (state.Phase == TurnPhase.Harvest && state.HarvestPending
                    && lockToLocalSeat && state.HarvestChoices.ContainsKey(localSeat))
                {
                    panel.SetHint("你已提交本回合的采集选择，等待其他玩家提交…");
                    return;
                }

                // 收尾阶段：场上有持续计谋时引擎会停下来等玩家决定（§9.9）。
                if (mode == ActionMode.None && state.Phase == TurnPhase.Cleanup)
                {
                    if (cleanupSession != null)
                    {
                        SetHint(CleanupEffectHint());
                        return;
                    }

                    SetHint("收尾阶段：请在弹出的窗口里选择要发动的持续计谋；不想发动就点「跳过收尾」进入下一回合。");
                    return;
                }
            }

            switch (mode)
            {
                case ActionMode.None:
                {
                    // 企业牌面要求「选择 1 家企业提升 1 级」时，结束回合会被引擎拦下。
                    // 先把原因和入口写进提示，别让玩家对着点不动的按钮猜。
                    GameState pendingState = engine != null ? engine.State : null;
                    if (pendingState != null && pendingState.Enterprises != null
                        && pendingState.Enterprises.PendingAdvanceSeat >= 0)
                    {
                        string advanceReason = pendingState.Enterprises.PendingAdvanceReason;
                        panel.SetHint($"{SeatLabel(pendingState.Enterprises.PendingAdvanceSeat)} "
                                      + (string.IsNullOrEmpty(advanceReason) ? "还有企业要提升合作等级：" : advanceReason + " ")
                                      + "点「企业」按钮选择 1 家企业提升 1 级。");
                        break;
                    }

                    // 主要行动做完后回合**不会**自动结束：玩家还能继续做快速行动，或点「结束回合」。
                    string baseHint = engine != null && engine.State.Phase == TurnPhase.PlayerActions
                                      && engine.State.MainActionTakenThisRound
                        ? "主要行动已完成：可以继续执行任意次快速行动；都做完了点「结束回合」交给下一位（§6.1）。"
                        : "选择一个行动。";
                    panel.SetHint(baseHint + ExpansionStatusHint(engine != null ? engine.State : null));
                    break;
                }
                case ActionMode.Place:
                    panel.SetHint("请点击一个空的影响力空格。");
                    break;
                case ActionMode.Relocate:
                    if (relocateFrom.HasValue)
                        panel.SetHint($"起点已选：{relocateFrom.Value}。请点击目标空格。");
                    else if (pendingMoves.Count == 0)
                        panel.SetHint("先点击你自己的一个影响力。");
                    else
                        panel.SetHint($"已选 {pendingMoves.Count}/2 步。继续点起点与目标空格排下一步；执行时会弹确认窗。");
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
                            panel.SetHint("建设：请点击你城市面板上亮起来的一个空位（点右上角「关闭」返回上一步）。");
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
                        panel.SetHint("探索第 1 步：请点击一个没有资源点指示物、且已开放探索的资源点本体，作为目标地点。");
                        break;
                    }

                    int toll = TollCalculator.Compute(exploreState, ActionSeat, exploreRoutes, explorePayees).Total;
                    panel.SetHint($"探索第 2 步：目标 {exploreState.Map.Node(exploreTargetNode).Code}，默认 {exploreRoutes.Count} 条航道、路费 {toll} 券。" +
                                  "点航道空格可增删路线；第 3 步再点目标资源点，在确认窗里执行。");
                    break;
                }
            }
        }

        /// <summary>
        /// 企业扩展 554/555 带来的回合内状态，直接附在行动提示后面：
        /// 禁用角色牌 / 额外主要行动 / 可连发两种效果 —— 否则玩家只会觉得按钮「莫名点不动」。
        /// </summary>
        private static string ExpansionStatusHint(GameState state)
        {
            if (state == null || state.Phase != TurnPhase.PlayerActions) return string.Empty;

            PlayerState player = state.Player(state.ActiveSeat);
            var notes = new List<string>();

            if (player.CharacterCardBanRound == state.ActionRound)
                notes.Add("本回合不能使用角色牌");

            if (player.ExtraMainActionsThisTurn > 0)
                notes.Add($"本回合还可再执行 {player.ExtraMainActionsThisTurn} 次主要行动");
            if (player.ExtraMainActionsExcludingSpecialThisTurn > 0)
                notes.Add($"本回合还可再执行 {player.ExtraMainActionsExcludingSpecialThisTurn} 次主要行动（特殊行动除外）");

            if (player.BothCharacterEffectsThisTurn)
                notes.Add("本回合可连发角色牌的策略与计谋");

            return notes.Count == 0 ? string.Empty : "｜" + string.Join("；", notes) + "。";
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

        /// <summary>收尾能力效果的引导：候选要「点地图」就把位置念出来，否则提示在弹窗里选。</summary>
        private string CleanupEffectHint()
        {
            ChoiceRequest request = cleanupSession != null ? cleanupSession.Pending : null;
            if (request == null) return "收尾能力：正在结算效果……";

            if (request.Pick == null || request.Pick.Kind == ChoicePickKind.None)
                return $"收尾能力：{request.Prompt}";

            var where = new List<string>();
            if (request.Pick.Kind == ChoicePickKind.InfluenceSlot)
            {
                foreach (InfluenceSlotRef slot in request.Pick.Slots) where.Add(SlotName(slot));
                return $"收尾能力：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
            }

            foreach (int nodeId in request.Pick.NodeIds) where.Add($"资源点 {engine.State.Map.Node(nodeId).Code}");
            return $"收尾能力：{request.Prompt} —— 请直接点地图上的：{string.Join("、", where)}";
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
                SetStatus("进入采集阶段：没有可采集的资源点。");
            else if (lockToLocalSeat)
                SetStatus("进入采集阶段：请选择你要采的资源点（默认全选，可点地图上的资源点增删），提交后等待其他人。");
            else
                SetStatus($"进入采集阶段：轮到玩家 {current + 1} 选择要采的资源点（默认全选，可点地图上的资源点增删）。");

            // 行动区顶部不再有「确认 / 取消」，采集靠每次选完点弹出的确认窗推进。
            ShowHarvestCommit();
        }

        /// <summary>
        /// 采集阶段没有行动区按钮可用，改成「选完一次就弹一次确认窗」：
        /// 确定 = 提交这份选择（单机 / 联机都走 <see cref="ConfirmHarvest"/>），取消 = 继续在地图上增删。
        /// </summary>
        private void ShowHarvestCommit()
        {
            ConfirmWindow window = EnsureConfirmWindow();
            if (window == null && choicePopup == null) return;

            int seat = HarvestSeat;
            string title;

            if (seat < 0)
            {
                title = "采集阶段：没有可选的资源点。\n确定继续吗？";
            }
            else
            {
                HarvestPlan plan = HarvestResolver.Plan(engine.State, seat, harvestCurrent);

                var codes = new List<string>();
                foreach (int nodeId in plan.Nodes) codes.Add(engine.State.Map.Node(nodeId).Code);
                string list = codes.Count == 0 ? "无" : string.Join("、", codes);
                string who = lockToLocalSeat ? "你" : SeatLabel(seat);

                title = $"采集（{who}）：已选 {plan.Nodes.Count} 个点〔{list}〕，路费 {plan.Toll.Total} / 手上 {plan.Budget} 金券。\n确定提交这份选择吗？";
            }

            Action<int> picked = choice =>
            {
                if (choice >= 0)
                {
                    ConfirmHarvest();
                    return;
                }

                SetStatus("继续选点：点地图上的资源点加入或移除，再决定何时提交。");
            };

            if (window != null)
            {
                window.Show("确认采集", title, new[] { "确定提交" }, picked);
                return;
            }

            choicePopup.Show(title, new[] { "确定提交" }, picked);
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
            if (renderer == null) return;

            if (mode == ActionMode.Harvest)
            {
                renderer.SetHighlightedNodes(harvestCurrent);
                return;
            }

            // 卡牌效果授予的探索：高亮它允许的那几个资源点，让玩家一眼看出能点哪里。
            renderer.SetHighlightedNodes(mode == ActionMode.Explore && exploreGrantedCandidates.Count > 0
                ? exploreGrantedCandidates
                : null);
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

            // 每次增删完就把「提交这份选择」的确认窗弹出来，取消即回来继续点。
            ShowHarvestCommit();
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
                ShowHarvestCommit();
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
            const string tail = "点地图上的资源点可加入或移除；每次选完会弹提交确认窗，「关闭」恢复默认全选。";

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
        ///
        /// 这里**不**动事件卡：错误可能来自任何一条命令（重复点击、别人的流程推进等），
        /// 该不该还原卡面交给 <see cref="ShowCommandRejected"/> 按命令类型判断。
        /// </summary>
        public void ShowOnlineError(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            SetStatus($"[联机] {message}");
        }

        /// <summary>
        /// 本机座位的一条命令被主机拒绝（带命令本身）。只有「正在等确认的事件选项被拒」才需要
        /// 还原事件卡让玩家改选重试；其它命令的拒绝只写状态行，不打断当前流程。
        /// —— 之前不区分命令，任意一条联机错误都会把事件卡还原并取消确认，
        /// 于是紧接着到达的「事件结算成功」回放被当成「已有牌在结算」跳过，整局永久卡在选事件这一步。
        /// </summary>
        public void ShowCommandRejected(ICommand command, string message)
        {
            if (command == null) return;

            onlineSubmitPending = false;

            if (command is ChooseStartNodeCommand rejectedStart && rejectedStart.ActorSeat == localSeat)
            {
                // 初始资源点被主机拒绝（典型：2 人局点了 B-01）：清掉这次选中并放开高亮，
                // 玩家可以立刻重选；不清理会留下一颗永远「待确认」的选中点。
                pendingStartNodeId = -1;
                RefreshStartNodeHighlight();
                SetStatus($"[联机] {message}（请重新点一个绿色资源点）");
                return;
            }

            if (awaitingResolveConfirm && command is ResolveEventOptionCommand rejected
                && rejected.ActorSeat == localSeat && rejected.NodeId == pendingEventNodeId)
            {
                awaitingResolveConfirm = false;
                pendingResolveFromExplore = false;
                pendingResolveFromCityMove = false;
                pendingResolveOriginNode = -1;

                // 卡面还原，玩家可以改选别的选项再提交；不还原就只能干等一条永远不会来的确认。
                BackToEventCard();
                SetStatus($"[联机] {message}（可以重新选择这张事件卡的选项）");
                return;
            }

            if (!string.IsNullOrEmpty(message)) SetStatus($"[联机] {message}");
        }

        /// <summary>
        /// 联机客户端：命令交出去后主机迟迟没有回音（丢包 / 主机卡住），到兜底时间就把「等确认」解开，
        /// 否则界面会一直停在「已提交，等主机确认…」，看起来就是卡死。真正的拒绝/回放仍会照常收尾。
        /// </summary>
        private void Update()
        {
            // 卡牌效果授予的行动：等界面空下来后（延后一帧）自动接管，走完整流程。
            TryBeginPendingAction();

            // 终局结算窗同样延后一帧：结束回合的调用方事后还会补一次 SetMode，会把当场弹出的窗收掉。
            TryShowFinalPrompt();

            if (!onlineClient || !onlineSubmitPending) return;
            if (Time.unscaledTime < onlineSubmitDeadline) return;

            onlineSubmitPending = false;
            SetStatus("[联机] 主机没有在预期时间内回应，请再试一次这一步。");
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

        // —— 企业扩展（合作企业板 / 企业特效） ——

        /// <summary>
        /// 操作区的「企业」按钮：按当前进度依次处理
        /// ①开局企业抽选（3／4 人抽 2 选 1，2 人抽 4 选 2）→ ②待提升合作等级（牌面要求选 1 家其他企业）
        /// → ③打开企业板总览（总览里再进「触发企业特效」）。
        /// 规则判断仍全部交给引擎，这里只负责把可选内容摆出来。
        /// </summary>
        private void OnCompanyButtonClicked()
        {
            if (engine == null) return;

            GameState state = engine.State;
            if (state.Setup == null || !state.Setup.IsEnterpriseEnabled)
            {
                SetStatus("本局没有启用企业扩展。");
                return;
            }

            if (state.IsGameOver)
            {
                SetStatus("本局已经结束。");
                return;
            }

            EnterpriseState enterprises = state.Enterprises;

            if (enterprises.PendingAdvanceSeat >= 0)
            {
                if (!CanOperateSeat(enterprises.PendingAdvanceSeat))
                {
                    SetStatus($"正在等 {SeatLabel(enterprises.PendingAdvanceSeat)} 选择要提升的企业板。");
                    return;
                }

                BeginEnterpriseAdvance(enterprises.PendingAdvanceSeat);
                return;
            }

            if (!enterprises.DraftDone)
            {
                int seat = FindSeatNeedingEnterprisePick();
                if (seat < 0)
                {
                    SetStatus("企业抽选已完成，可以决定初始资源点了。");
                    return;
                }

                BeginEnterprisePick(seat);
                return;
            }

            // 开局抽选已完成：打开企业板总览（所有场上企业 + 各玩家的合作等级标记）。
            OpenEnterpriseBrowser();
        }

        /// <summary>
        /// 操作区的「企业」按钮：开局抽选完成后打开企业板总览；
        /// 总览里的「触发企业特效」再转给 <see cref="BeginEnterpriseEffectMenu"/>。
        /// </summary>
        private void OpenEnterpriseBrowser()
        {
            if (engine == null) return;

            EnterpriseBoardPanel browser = EnsureEnterprisePanel();
            if (browser == null)
            {
                SetStatus("找不到 Canvas，企业板总览打不开。");
                return;
            }

            browser.OpenView(engine.State);
        }

        /// <summary>
        /// 拿到（首次则创建）企业板面板。控制器挂在哪一层不一定，所以按「自己 → 行动面板 → HUD → 场景里的 Canvas」
        /// 依次往上找，全都找不到才放弃 —— 找不到 Canvas 就等于弹窗建不出来，只能退回状态行提示。
        /// </summary>
        private EnterpriseBoardPanel EnsureEnterprisePanel()
        {
            if (enterpriseBrowser != null) return enterpriseBrowser;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null && panel != null) canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null && hud != null) canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return null;

            enterpriseBrowser = EnterpriseBoardPanel.Create(canvas.transform, hud != null ? hud.UiFont : null);
            enterpriseBrowser.BoardPicked = OnEnterpriseBoardPicked;
            enterpriseBrowser.AdvancePicked = OnEnterpriseAdvancePicked;
            enterpriseBrowser.RhineDepartmentPicked = OnRhineDepartmentPicked;
            return enterpriseBrowser;
        }

        /// <summary>抽选里确认了某块企业板：派发命令，并把下一位（2 人局是同一位的第 2 张）接着摆出来。</summary>
        private void OnEnterpriseBoardPicked(int boardId)
        {
            if (engine == null || enterpriseBrowser == null) return;

            int seat = enterpriseBrowser.Seat;
            if (seat < 0) return;

            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);
            string boardName = def != null ? def.Name : boardId.ToString();

            if (!Dispatch(new ChooseInitialEnterpriseCommand(seat, boardId))) return;

            // 莱茵生命：派发完选板命令后，立刻弹三张科室板让玩家挑一张。
            // 等玩家选完（OnRhineDepartmentPicked）再回到正常 Draft 流程（继续选第 2 张 或 下一位）。
            if (boardId == (int)EnterpriseId.RhineLab)
            {
                BeginRhineDepartmentPick(seat);
                return;
            }

            ContinueEnterpriseDraftFlow(seat, boardName);
        }

        /// <summary>派发完选板命令后，继续走正常的 Draft 流程（下一位 或 2 人局第 2 张）。</summary>
        private void ContinueEnterpriseDraftFlow(int seat, string boardName)
        {
            EnterpriseState enterprises = engine.State.Enterprises;

            if (!enterprises.SeatDraftDone(seat))
            {
                SetStatus($"{SeatLabel(seat)} 选择了「{boardName}」，还要再选 1 张。");
                BeginEnterprisePick(seat);
                return;
            }

            if (!enterprises.DraftDone)
            {
                SetStatus($"{SeatLabel(seat)} 选择了「{boardName}」。请继续把「企业」按钮交给下一位玩家选择。");
                int next = FindSeatNeedingEnterprisePick();
                if (next >= 0) BeginEnterprisePick(next);
                else enterpriseBrowser.Close();
                return;
            }

            enterpriseBrowser.Close();
            SetStatus($"{SeatLabel(seat)} 选择了「{boardName}」。全部玩家已选定企业，接着决定初始资源点。");
        }

        /// <summary>莱茵生命选完企业后，弹三张科室板让玩家挑一张初始科室板。</summary>
        private void BeginRhineDepartmentPick(int seat)
        {
            if (engine == null) return;
            EnterpriseBoardPanel browser = EnsureEnterprisePanel();
            if (browser == null) return;
            browser.OpenRhineDepartment(engine.State, seat,
                $"{SeatLabel(seat)}：莱茵生命选一张科室板",
                "3 张里挑 1 张作为你的初始科室板。选好后企业板 L2–L5 的效果就按这块科室板结算。");
        }

        /// <summary>玩家在三张科室板里点了一张：派发命令，然后回到正常 Draft 流程。</summary>
        private void OnRhineDepartmentPicked(int depId)
        {
            if (engine == null || enterpriseBrowser == null) return;

            int seat = enterpriseBrowser.Seat;
            if (seat < 0) return;

            string depName = EnterpriseCatalog.Department(depId)?.Name ?? $"科室板 {depId + 1}";

            if (!Dispatch(new PickRhineDepartmentCommand(seat, depId))) return;

            SetStatus($"{SeatLabel(seat)} 为莱茵生命选择了「{depName}」科室板。");
            enterpriseBrowser.Close();

            // 回到正常 Draft 流程
            EnterpriseState enterprises = engine.State.Enterprises;
            if (!enterprises.SeatDraftDone(seat))
            {
                SetStatus($"{SeatLabel(seat)} 为莱茵生命选好科室板，还要再选 1 张企业板。");
                BeginEnterprisePick(seat);
                return;
            }

            if (!enterprises.DraftDone)
            {
                int next = FindSeatNeedingEnterprisePick();
                if (next >= 0) BeginEnterprisePick(next);
            }
            else
            {
                SetStatus($"{SeatLabel(seat)} 选择了莱茵生命（{depName}）。全部玩家已选定企业，接着决定初始资源点。");
            }
        }

        /// <summary>提升等级里确认了某块企业板。</summary>
        private void OnEnterpriseAdvancePicked(int boardId)
        {
            if (engine == null || enterpriseBrowser == null) return;

            int seat = enterpriseBrowser.Seat;
            if (seat < 0) return;

            enterpriseBrowser.Close();
            BeginEnterpriseAdvanceSettle(seat, boardId);
        }

        /// <summary>
        /// 提升某块企业板的合作等级：先走点选收集会话（莱茵生命的科室板奖励可能要选影响力 / 建造），
        /// 收集完再派发命令 —— 界面点的与主机执行的是同一串答案。
        /// </summary>
        private void BeginEnterpriseAdvanceSettle(int seat, int boardId)
        {
            if (engine == null) return;

            enterpriseSeat = seat;
            enterpriseBoardId = boardId;
            enterpriseLevel = -1;
            enterpriseAdvance = true;

            if (choicePopup == null && EnsureConfirmWindow() == null)
            {
                DispatchEnterpriseAdvance(Array.Empty<int>());
                return;
            }

            if (panel != null) panel.SetLocked(true);

            enterpriseSession = new EffectChoiceSession(engine.State, (sandbox, choices) =>
            {
                var command = new AdvanceEnterpriseLevelCommand(seat, boardId, choices);
                command.Apply(sandbox);
                return command.Report;
            });
            enterpriseSession.Advance();
            ContinueEnterpriseChoices();
        }

        /// <summary>照收集到的答案派发「提升企业合作等级」命令。</summary>
        private void DispatchEnterpriseAdvance(IReadOnlyList<int> answers)
        {
            enterpriseAdvance = false;
            if (engine == null) return;

            int boardId = enterpriseBoardId;
            var command = new AdvanceEnterpriseLevelCommand(enterpriseSeat, boardId,
                new RecordedChoices(answers, strict: false));

            bool dispatched = Dispatch(command);
            enterpriseSession = null;
            if (panel != null) panel.SetLocked(false);

            if (!dispatched)
            {
                enterpriseSeat = -1;
                return;
            }

            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);
            int level = engine.State.Enterprises.LevelOf(boardId, enterpriseSeat);
            string suffix = command.Report != null && !string.IsNullOrEmpty(command.Report.Note)
                ? $"；效果：{command.Report.Describe()}"
                : string.Empty;
            SetStatus($"{SeatLabel(enterpriseSeat)} 把「{(def != null ? def.Name : boardId.ToString())}」提升到 {level} 级{suffix}");

            // 红区开放：一位选完，紧接着把下一位的「选企业提升」窗口接上。
            EnterpriseState redZone = engine.State.Enterprises;
            if (redZone.RedZoneAdvanceStarted && redZone.RedZoneAdvanceSeat >= 0
                && CanOperateSeat(redZone.RedZoneAdvanceSeat))
                BeginEnterpriseAdvance(redZone.RedZoneAdvanceSeat);

            enterpriseSeat = -1;
        }

        /// <summary>这个座位现在能不能由本机操作（联机只看本机座位；单机热座都能操作）。</summary>
        private bool CanOperateSeat(int seat)
        {
            if (engine == null || seat < 0 || seat >= engine.State.Players.Count) return false;
            return !lockToLocalSeat || seat == localSeat;
        }

        /// <summary>
        /// 开局企业抽选里「下一位还没选满」的座位（不看本机权限，用于单机热座切换界面）。
        /// 顺序从起始玩家起顺时针；2 人局同一位玩家会连续返回两次。
        /// </summary>
        private static int FirstSeatNeedingEnterpriseDraft(GameState state)
        {
            if (state == null || state.Setup == null || !state.Setup.IsEnterpriseEnabled) return -1;

            EnterpriseState enterprises = state.Enterprises;
            int count = enterprises.ChosenBoards.Count;
            if (count <= 0) return -1;

            int start = state.FirstPlayerSeat;
            if (start < 0 || start >= count) start = 0;

            for (int i = 0; i < count; i++)
            {
                int seat = (start + i) % count;
                if (!enterprises.SeatDraftDone(seat) && enterprises.Offers[seat].Count > 0) return seat;
            }

            return -1;
        }

        /// <summary>
        /// 找出下一位还没选满企业板、且本机可以替他操作的座位；-1 = 都选完了。
        /// 顺序从起始玩家起顺时针（2 人局每人要选 2 张，同一个人会被返回两次）。
        /// </summary>
        private int FindSeatNeedingEnterprisePick()
        {
            if (engine == null) return -1;

            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int count = enterprises.ChosenBoards.Count;
            if (count <= 0) return -1;

            int start = state.FirstPlayerSeat;
            if (start < 0 || start >= count) start = 0;

            for (int i = 0; i < count; i++)
            {
                int seat = (start + i) % count;
                if (enterprises.SeatDraftDone(seat)) continue;
                if (enterprises.Offers[seat].Count == 0) continue;
                if (!CanOperateSeat(seat)) continue;
                return seat;
            }

            return -1;
        }

        /// <summary>
        /// 开局：把这位玩家抽到的企业板卡面摆出来选 1 张（§ 企业扩展设置）。
        /// 3／4 人局抽 2 选 1；2 人局抽 4 选 2（连续选两次）。
        /// </summary>
        private void BeginEnterprisePick(int seat)
        {
            if (engine == null) return;

            EnterpriseState enterprises = engine.State.Enterprises;
            List<int> offers = enterprises.Offers[seat];
            if (offers.Count == 0)
            {
                SetStatus($"{SeatLabel(seat)} 没有待选的企业板。");
                return;
            }

            EnterpriseBoardPanel browser = EnsureEnterprisePanel();
            if (browser == null)
            {
                SetStatus("找不到 Canvas，企业板选不了。");
                return;
            }

            int pickIndex = enterprises.ChosenCount(seat) + 1;
            string progress = enterprises.PicksPerPlayer > 1
                ? $"（第 {pickIndex}/{enterprises.PicksPerPlayer} 张）"
                : string.Empty;

            browser.OpenDraft(engine.State, seat,
                $"{SeatLabel(seat)}：选 1 张合作企业板{progress}",
                $"抽到 {offers.Count} 张。点一张放大看牌面，确认后立即 +1 级。");
        }

        /// <summary>牌面要求「选择 1 家其他企业提升 1 级」时，把合法的企业板卡面摆出来。</summary>
        private void BeginEnterpriseAdvance(int seat)
        {
            if (engine == null) return;

            EnterpriseState enterprises = engine.State.Enterprises;
            List<int> boardIds = EnterpriseBoardPanel.AdvanceCandidates(engine.State, seat);

            if (boardIds.Count == 0)
            {
                SetStatus("场上没有还能提升合作等级的企业板。");
                return;
            }

            EnterpriseBoardPanel browser = EnsureEnterprisePanel();
            if (browser == null)
            {
                SetStatus("找不到 Canvas，企业板选不了。");
                return;
            }

            // 标题只写「要做什么」，把「为什么（哪张牌 / 红区要求）」放进副标题，
            // 免得标题里套两三层冒号（例：「玩家1：罗德岛 1 级：选择 1 家其他企业…」）。
            string reason = string.IsNullOrEmpty(enterprises.PendingAdvanceReason)
                ? string.Empty
                : enterprises.PendingAdvanceReason + " ";

            browser.OpenAdvance(engine.State, seat,
                $"{SeatLabel(seat)}：选择 1 家企业提升合作等级",
                reason + "点一张放大看牌面，确认后合作等级 +1。");
        }

        /// <summary>把这名玩家当前可触发（已解锁、本回合没用过）的企业特效列出来。</summary>
        private void BeginEnterpriseEffectMenu(int seat)
        {
            if (engine == null) return;

            GameState state = engine.State;
            if (state.Phase != TurnPhase.PlayerActions)
            {
                SetStatus("企业特效只能在玩家行动阶段触发。");
                return;
            }

            if (seat < 0 || state.ActiveSeat != seat)
            {
                SetStatus("企业特效只能在自己行动时触发。");
                return;
            }

            EnterpriseState enterprises = state.Enterprises;
            var boardIds = new List<int>();
            var levels = new List<int>();
            var labels = new List<string>();
            var summary = new List<string>();

            foreach (int boardId in enterprises.ActiveBoards)
            {
                EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);
                int current = enterprises.LevelOf(boardId, seat);
                if (def == null || current < 0) continue;

                summary.Add($"{def.Name} {current} 级");

                for (int level = 1; level <= current; level++)
                {
                    EnterpriseLevel entry = def.Level(level);
                    if (entry == null || !entry.IsEffect) continue;
                    if (enterprises.HasUsedEffectThisTurn(boardId, seat, level)) continue;

                    boardIds.Add(boardId);
                    levels.Add(level);
                    labels.Add($"{def.Name} {level} 级：{entry.Text}");
                }
            }

            string title = summary.Count > 0
                ? $"合作等级：{string.Join("、", summary)}。选择要触发的企业特效。"
                : "还没有任何企业合作等级。";

            if (labels.Count == 0)
            {
                SetStatus($"{title} 当前没有可触发的企业特效（同一特效一回合只能触发 1 次）。");
                return;
            }

            Action<int> picked = choice =>
            {
                if (choice < 0 || choice >= boardIds.Count) return;
                BeginEnterpriseTrigger(seat, boardIds[choice], levels[choice]);
            };

            string prompt = $"{SeatLabel(seat)}：{title}\n同一特效一回合只能触发 1 次。";

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show("触发企业特效", prompt, labels, picked);
                return;
            }

            if (choicePopup == null)
            {
                SetStatus("没接小选择弹窗，暂时没法触发企业特效。");
                return;
            }

            choicePopup.Show(prompt, labels, picked);
        }

        /// <summary>
        /// 开发者面板的「触发 Lx」：跳过时机校验，但和正常触发走同一套点选会话 ——
        /// 需要选目标 / 选牌的特效会正常弹窗，不会用默认答案静默结算。
        /// </summary>
        private void BeginDeveloperEnterpriseTrigger(int seat, int boardId, int level)
        {
            if (engine == null) return;

            enterpriseIgnoreTiming = true;
            BeginEnterpriseTrigger(seat, boardId, level);
        }

        /// <summary>触发某个企业特效：带点选的效果先走收集会话，收集完再派发命令。</summary>
        private void BeginEnterpriseTrigger(int seat, int boardId, int level)
        {
            if (engine == null) return;

            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);
            EnterpriseLevel entry = def?.Level(level);
            if (entry == null)
            {
                enterpriseIgnoreTiming = false;
                return;
            }

            enterpriseSeat = seat;
            enterpriseBoardId = boardId;
            enterpriseLevel = level;
            enterpriseAdvance = false;

            // 一律走收集会话：即使这一级没有可执行的效果段，会话也会立刻「跑完」再派发命令；
            // 而带点选的特效（如罗德岛 4 级从弃牌区选牌）只有试跑同一条结算路径才会弹出选择窗。
            if (choicePopup == null && EnsureConfirmWindow() == null)
            {
                DispatchEnterpriseEffect(Array.Empty<int>());
                return;
            }

            if (panel != null) panel.SetLocked(true);

            enterpriseSession = new EffectChoiceSession(engine.State,
                (sandbox, choices) =>
                    EnterpriseEffectResolver.Resolve(sandbox, seat, boardId, level, choices));
            enterpriseSession.Advance();
            ContinueEnterpriseChoices();
        }

        /// <summary>抛出企业特效的下一个待答选择：点地图的走地图，其余走弹窗。</summary>
        private void ContinueEnterpriseChoices()
        {
            if (enterpriseSession == null) return;

            if (enterpriseSession.IsComplete)
            {
                IReadOnlyList<int> answers = enterpriseSession.Answers;
                enterpriseSession = null;
                if (enterpriseAdvance) DispatchEnterpriseAdvance(answers);
                else DispatchEnterpriseEffect(answers);
                return;
            }

            ChoiceRequest request = enterpriseSession.Pending;

            if (request.Pick != null && request.Pick.Kind != ChoicePickKind.None)
            {
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                if (confirmWindow != null && confirmWindow.IsVisible) confirmWindow.Hide();
                SetStatus(enterpriseAdvance
                    ? "提升合作等级：请在地图上点击要选择的目标。"
                    : "企业特效：请在地图上点击要选择的目标。");
                UpdateHint();
                return;
            }

            var options = new List<string>(request.Options.Count);
            foreach (ChoiceOption option in request.Options) options.Add(option.Label);

            EffectChoiceSession session = enterpriseSession;
            Action<int> picked = choice =>
            {
                if (session != enterpriseSession) return;

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinueEnterpriseChoices();
                    return;
                }

                if (session.Back()) ContinueEnterpriseChoices();
                else ExitEnterpriseFlow(enterpriseAdvance
                    ? "已取消这次企业升级：合作等级没变，需要时再点「企业」重新选择。"
                    : "已取消这次企业特效（没有结算）。");
            };

            // 企业特效 / 升级奖励的选项常带整段效果说明，统一用能折行、可最小化的窗口弹窗。
            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show(enterpriseAdvance ? "提升合作等级" : "触发企业特效",
                    request.Prompt, options, picked, request.AllowCancel);
                return;
            }

            if (choicePopup == null)
            {
                ExitEnterpriseFlow("没接选择弹窗，企业效果无法继续结算。");
                return;
            }

            choicePopup.Show(request.Prompt, options, picked, request.AllowCancel);
        }

        /// <summary>企业特效里「点地图」的那一次选择：照候选清单把点击翻译成选项下标。</summary>
        private bool HandleEnterpriseMapPick(MapHit hit)
        {
            if (enterpriseSession == null || enterpriseSession.IsComplete) return false;

            ChoicePick pick = enterpriseSession.Pending != null ? enterpriseSession.Pending.Pick : null;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (!TryTranslateMapPick(pick, hit, out int index)) return true;

            enterpriseSession.Answer(index);
            ContinueEnterpriseChoices();
            return true;
        }

        /// <summary>照收集到的答案派发「触发企业特效」命令。</summary>
        private void DispatchEnterpriseEffect(IReadOnlyList<int> answers)
        {
            if (engine == null) return;

            EnterpriseBoardDef def = EnterpriseCatalog.Board(enterpriseBoardId);
            var command = new TriggerEnterpriseEffectCommand(enterpriseSeat, enterpriseBoardId, enterpriseLevel,
                new RecordedChoices(answers, strict: false), enterpriseIgnoreTiming);
            enterpriseIgnoreTiming = false;

            bool dispatched = Dispatch(command);
            enterpriseSession = null;
            if (panel != null) panel.SetLocked(false);

            if (!dispatched)
            {
                enterpriseSeat = -1;
                return;
            }

            string name = def != null ? def.Name : enterpriseBoardId.ToString();
            string suffix = command.Report != null && command.Report.Entries.Count > 0
                ? $"；效果：{command.Report.Describe()}"
                : string.Empty;
            SetStatus($"{SeatLabel(enterpriseSeat)} 触发「{name}」{enterpriseLevel} 级企业特效{suffix}");

            // 罗德岛 1 级：触发后立刻要求选择 1 家其他企业提升 1 级，别让玩家再点一次按钮。
            EnterpriseState enterprises = engine.State.Enterprises;
            if (enterprises.PendingAdvanceSeat == enterpriseSeat && CanOperateSeat(enterpriseSeat))
                BeginEnterpriseAdvance(enterpriseSeat);

            enterpriseSeat = -1;
        }

        /// <summary>中途放弃企业特效：解锁与使用次数都不变（命令没派发）。</summary>
        private void ExitEnterpriseFlow(string status)
        {
            enterpriseSession = null;
            enterpriseSeat = -1;
            enterpriseAdvance = false;
            enterpriseIgnoreTiming = false;
            if (panel != null) panel.SetLocked(false);
            if (!string.IsNullOrEmpty(status)) SetStatus(status);
            UpdateHint();
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

            // 企业板总览开着时跟着状态刷新（标记位置 / 场上企业可能被命令改变）。
            if (enterpriseBrowser != null && enterpriseBrowser.IsOpen && engine != null)
                enterpriseBrowser.Refresh(engine.State);

            // 开发者面板开着时跟着状态刷新（轮次 / 玩家数值 / 牌堆都可能被开发者命令改变）。
            if (developerPanel != null && developerPanel.IsOpen && engine != null)
                developerPanel.Refresh(engine.State);

            // 引导文字也跟着状态走：开局选点轮次、本局结束这些都不经过 SetMode，
            // 只靠命令/阶段事件刷新，所以并在这里一起更新。
            UpdateHint();

            // 「结束回合」只在玩家行动阶段、轮到自己时可点；还没做主要行动时也允许点 ——
            // 点了会明确告诉你「必须先做 1 次主要行动」，比一颗默默变灰的按钮更好懂（§6.1）。
            if (panel != null && engine != null)
            {
                GameState state = engine.State;
                bool mine = !lockToLocalSeat || state.ActiveSeat == localSeat;
                panel.SetEndTurnAvailable(state.Phase == TurnPhase.PlayerActions && mine);
            }

            // 联机客户端：事件牌按状态补开（见 SyncEventCardFromState），不能只依赖命令回放那一下。
            if (onlineClient && engine != null) SyncEventCardFromState(engine.State);

            // 企业扩展：「向所有玩家展示」的牌（罗德岛干员特派 / 4 级回收）必须让所有人看到牌面。
            ShowPendingPublicReveals();
        }

        /// <summary>
        /// 把新出现的「向所有玩家展示」记录放大成卡面：热座下等同把牌亮给所有人看，看完点关闭。
        /// 只看新增的部分；一次新增多条时亮出最新一条，其余已在行动记录里有文字。
        /// </summary>
        private void ShowPendingPublicReveals()
        {
            if (engine == null || engine.State == null) return;

            GameState state = engine.State;
            if (state.PublicReveals.Count <= revealedSeen)
            {
                revealedSeen = state.PublicReveals.Count;
                return;
            }

            PublicReveal reveal = state.PublicReveals[state.PublicReveals.Count - 1];
            revealedSeen = state.PublicReveals.Count;

            string name = state.Catalog != null ? state.Catalog.Character(reveal.CardId)?.Name ?? $"角色牌 {reveal.CardId}" : $"角色牌 {reveal.CardId}";
            SetStatus($"玩家 {reveal.Seat + 1} 向所有玩家展示《{name}》（{reveal.Reason}）。");
            if (cardPreview != null) cardPreview.ShowCharacter(state, reveal.CardId);
            else SetStatus($"玩家 {reveal.Seat + 1} 展示《{name}》（{reveal.Reason}）；没接「卡面放大」弹窗，看不到牌面。");
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
            // 企业扩展：所有玩家先完成「7 选 5」，2 人局的弃设施牌窗才打开。
            // 没启用企业扩展时这条不适用（弃设施牌是 2 人局的基础变体规则）。
            bool enterprisesReady = state.Setup == null || !state.Setup.IsEnterpriseEnabled
                                    || state.Enterprises.DraftDone || state.DeveloperMode;
            bool allSetupDiscardDone = enterprisesReady && FirstSeatNeedingSetupDiscard(state) < 0;
            bool discard = state.Phase == TurnPhase.CharacterPlacement
                           && state.SupplyDiscardRemaining > 0 && setupDone && allSetupDiscardDone
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

            // 企业扩展：开局入场第一步是「决定初始企业」（企业家已经选完）。
            TryBeginEnterpriseDraft(state);

            // 企业扩展：开局入场走完后，逐位把「7 选 5」问完才能盖放。
            TryBeginSetupDiscard(state);

            // 收尾阶段：场上有可持续发动的计谋时，引擎会停在这里等玩家决定（§9.9）。
            TryBeginPersistentCleanup(state);
        }

        /// <summary>
        /// 收尾阶段的提示：列出本机（单机热座则全部座位）还能发动的持续计谋，
        /// 让玩家选一张发动，或者直接跳过收尾进入下一回合。
        /// 发动后的落点等选择与建设 / 角色牌 / 特殊行动同一套：先收集点选，再派发命令。
        /// </summary>
        private void TryBeginPersistentCleanup(GameState state)
        {
            if (state.Phase != TurnPhase.Cleanup || (choicePopup == null && EnsureConfirmWindow() == null))
            {
                persistentCleanupPrompting = false;
                return;
            }

            if (persistentCleanupPrompting || cleanupSession != null || IsChoicePromptOpen()) return;

            var candidates = new List<PersistentTacticState>();
            foreach (PersistentTacticState tactic in state.PersistentTactics)
            {
                if (!CanOperateSeat(tactic.Seat)) continue;
                if (!PersistentTacticRules.CanUseCleanup(state, tactic)) continue;
                candidates.Add(tactic);
            }
            if (candidates.Count == 0) return;

            var labels = new List<string>();
            foreach (PersistentTacticState tactic in candidates)
            {
                CharacterCardDef def = state.Catalog.Character(tactic.CardId);
                string name = def != null
                    ? def.NameOf(CharacterEffectKind.Tactics)
                    : $"卡牌 {tactic.CardId}";
                labels.Add(lockToLocalSeat
                    ? $"发动「{name}」（还剩 {tactic.Markers} 个标记）"
                    : $"{SeatLabel(tactic.Seat)} 发动「{name}」（还剩 {tactic.Markers} 个标记）");
            }
            labels.Add("跳过收尾");

            persistentCleanupPrompting = true;
            ShowChoiceWindow("收尾阶段：发动持续计谋",
                "场上的持续计谋可以各发动 1 次；不发动就点「跳过收尾」，直接进入下一回合。",
                labels, index =>
                {
                    persistentCleanupPrompting = false;
                    if (index < 0 || index >= candidates.Count)
                    {
                        // 跳过：引擎在确认没有别的收尾能力后会自动进入下一回合。
                        if (engine != null && engine.State.Phase == TurnPhase.Cleanup)
                            Dispatch(new AdvancePhaseCommand());
                        return;
                    }

                    PersistentTacticState picked = candidates[index];
                    BeginPersistentCleanup(picked);
                }, allowCancel: false);
        }

        /// <summary>
        /// 发动 1 张持续计谋的收尾能力：落点等选择与建设 / 角色牌同一套「试跑 + 收集答案」，
        /// 答完再把答案交给 <see cref="UsePersistentTacticCleanupCommand"/> 正式执行。
        /// </summary>
        private void BeginPersistentCleanup(PersistentTacticState tactic)
        {
            if (engine == null || tactic == null) return;

            // 没接弹窗就退回引擎默认答案，别把玩家卡在流程里。
            if (choicePopup == null && EnsureConfirmWindow() == null)
            {
                Dispatch(new UsePersistentTacticCleanupCommand(tactic.Seat, tactic.Kind));
                return;
            }

            cleanupTactic = tactic;
            cleanupSession = new EffectChoiceSession(engine.State, (sandbox, choices) =>
                EffectResolver.Execute(sandbox, tactic.Seat,
                    new[] { RewardOp.Influence(InfluencePlacementScope.DeployRules, 1) },
                    choices, EnterpriseEffectExtension.Instance));
            cleanupSession.Advance();
            ContinuePersistentCleanupChoices();
        }

        /// <summary>抛出 / 点选收尾能力的下一个待答选择：点地图的候选走「点地图」，其余走弹窗（带返回键）。</summary>
        private void ContinuePersistentCleanupChoices()
        {
            if (cleanupSession == null) return;

            if (cleanupSession.IsComplete)
            {
                IReadOnlyList<int> answers = cleanupSession.Answers;
                PersistentTacticState tactic = cleanupTactic;
                cleanupSession = null;
                cleanupTactic = null;

                if (tactic != null)
                    Dispatch(new UsePersistentTacticCleanupCommand(tactic.Seat, tactic.Kind,
                        new RecordedChoices(answers, strict: false)));
                return;
            }

            ChoiceRequest request = cleanupSession.Pending;

            if (request.Pick != null && request.Pick.Kind != ChoicePickKind.None)
            {
                // 与部署同源：直接点地图上的合法空格，不再列一长串按钮。
                if (choicePopup != null && choicePopup.IsVisible) choicePopup.Hide();
                UpdateHint();
                return;
            }

            EffectChoiceSession session = cleanupSession;
            ShowChoiceRequest(request, choice =>
            {
                if (session != cleanupSession) return;

                if (choice >= 0)
                {
                    session.Answer(choice);
                    ContinuePersistentCleanupChoices();
                    return;
                }

                // 返回键：退掉最近一个答案；退无可退就回到「选哪张计谋」的窗口。
                if (session.Back())
                {
                    ContinuePersistentCleanupChoices();
                    return;
                }

                cleanupSession = null;
                cleanupTactic = null;
                persistentCleanupPrompting = false;
                SetStatus("已取消这次收尾发动，可以重新选择。");
                if (engine != null) TryBeginPersistentCleanup(engine.State);
            });
        }

        /// <summary>收尾能力里「点地图」的那一次选择：照候选清单把点击翻译成选项下标。</summary>
        private bool HandlePersistentCleanupMapPick(MapHit hit)
        {
            if (cleanupSession == null || cleanupSession.IsComplete) return false;

            ChoicePick pick = cleanupSession.Pending != null ? cleanupSession.Pending.Pick : null;
            if (pick == null || pick.Kind == ChoicePickKind.None) return false;

            if (!TryTranslateMapPick(pick, hit, out int index)) return true; // 不在候选之列，已提示

            cleanupSession.Answer(index);
            ContinuePersistentCleanupChoices();
            return true;
        }

        /// <summary>
        /// 企业扩展开局入场：从起始玩家起顺时针找第一个还没做「7 选 5」的人；不需要这一步时返回 -1。
        /// </summary>
        private static int FirstSeatNeedingSetupDiscard(GameState state)
        {
            if (state == null || !state.Setup.IsEnterpriseEnabled) return -1;
            if (state.TurnNumber != 1 || state.Phase != TurnPhase.CharacterPlacement) return -1;
            // 先决定初始企业；开发者模式可以跳过企业抽选，别把它当成「7 选 5 还没开始」。
            if (!state.Enterprises.DraftDone && !state.DeveloperMode) return -1;
            if (ChooseStartNodeCommand.CurrentOperator(state) >= 0) return -1; // 开局入场还没走完

            int count = state.Players.Count;
            int start = state.FirstPlayerSeat;
            if (start < 0 || start >= count) start = 0;
            for (int i = 0; i < count; i++)
            {
                int seat = (start + i) % count;
                if (!state.Players[seat].SetupDiscardDone) return seat;
            }
            return -1;
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
                // 座位号缺失时也不跟当前行动座位走：那等于退回热座，会显示别人的城市 / 手牌。
                if (seat < 0 || seat >= state.Players.Count) seat = 0;
                return seat;
            }

            // 单机热座：盖放阶段没有座位轮转（ActiveSeat 一直停在起始玩家），
            // 谁还没盖放就把界面切给谁，否则盖完 1 号以后 2–4 号永远轮不到。
            if (state.Phase == TurnPhase.CharacterPlacement)
            {
                // 企业扩展：开局决定初始企业在选点之前，界面先切给正在选企业的那位。
                int draftSeat = FirstSeatNeedingEnterpriseDraft(state);
                if (draftSeat >= 0) return draftSeat;

                // 第 1 回合的「开局入场」：选初始资源点与结算事件牌都算同一个人的操作，
                // 事件没结算完之前不要把界面切给下一位（否则玩家 1 还在选奖励，顶栏已经变成玩家 2）。
                int chooser = ChooseStartNodeCommand.CurrentOperator(state);
                if (chooser >= 0) return chooser;

                // 企业扩展：开局入场后先做「7 选 5」，显示还没弃牌的那位。
                int discardSeat = FirstSeatNeedingSetupDiscard(state);
                if (discardSeat >= 0) return discardSeat;

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
