using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NomadCity.Data;
using NomadCity.Game;
using NomadCity.UI;
using UnityEngine;

namespace NomadCity.App
{
    /// <summary>
    /// Game 场景的入口。开局读主菜单写入的 GameSession，选地图、构建地图状态，并建立规则引擎。
    /// 后续的具体行动规则（部署 / 调度 / 探索 / 路费 …）都会挂在引擎的命令通道上。
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("主菜单写入的跨场景配置")]
        [SerializeField] private GameSession session;

        [Tooltip("场景里唯一的地图容器")]
        [SerializeField] private MapView mapView;

        [Header("地图渲染与交互（挂在 MapRoot 上）")]
        [Tooltip("把引擎状态画到地图上")]
        [SerializeField] private MapStateRenderer mapStateRenderer;

        [Tooltip("把鼠标点击解析成地图命中")]
        [SerializeField] private MapPointerInput mapPointerInput;

        [Header("界面（挂在 Canvas 上）")]
        [SerializeField] private GameHud gameHud;
        [SerializeField] private ActionPanel actionPanel;

        [Tooltip("开局翻出事件卡时弹出的选项面板")]
        [SerializeField] private EventCardPanel eventCardPanel;

        [Tooltip("通用小选择弹窗（如探索路费「付给谁」、建设的付费方式）")]
        [SerializeField] private ChoicePopup choicePopup;

        [Tooltip("设施供应区：公开的 6 张设施牌（建设用）")]
        [SerializeField] private FacilitySupplyPanel facilitySupplyPanel;

        [Tooltip("城市面板视图：本机玩家的 12 个设施格（建设用；非建设模式点已建成的格子可看放大卡面）")]
        [SerializeField] private CityPanelView cityPanelView;

        [Tooltip("本机玩家座位（0 起）：城市面板固定显示这名玩家的城市，建设也建在这名玩家名下。")]
        [SerializeField] private int localSeat;

        [Tooltip("城市样式牌弹窗：场上的 6 张城市样式牌（宣告用，§6.3b）")]
        [SerializeField] private CityPatternPanel cityPatternPanel;

        [Tooltip("卡面放大弹窗：点已建成的设施时弹出放大卡面")]
        [SerializeField] private CardPreviewPopup cardPreviewPopup;

        [Tooltip("角色牌手牌托盘：盖放阶段点一张牌即盖放（§4.2 阶段 1）")]
        [SerializeField] private CharacterHandPanel characterHandPanel;

        [Tooltip("行动记录：右上角按钮 + 聊天框式面板")]
        [SerializeField] private ActionLogPanel actionLogPanel;

        [Tooltip("把点击/按钮与引擎连起来的状态机")]
        [SerializeField] private GameScreenController screenController;

        [Header("调试")]
        [Tooltip("开发者模式（仅调试用）：放开「轮次 / 阶段 / 红色区域开放」等流程闸门，" +
                 "让任何玩家在任何阶段都能直接执行行动，方便单独测各个按钮。正式游戏请务必取消勾选。" +
                 "注意：它只放开流程限制，不改变规则效果（金券 / 资源 / 标记照常扣除）。")]
        [SerializeField] private bool developerMode;

        [Header("联机（阶段 3）")]
        private NetworkGameSession onlineSession;

        [Tooltip("开局前的企业家挑选弹窗（单机与联机共用）；漏接时按座位自然顺序与默认颜色直接开局")]
        [SerializeField] private EntrepreneurChoicePanel entrepreneurPanel;

        /// <summary>本局的规则引擎；企业家挑选完成后建立。UI 通过它提交命令、订阅事件。</summary>
        public GameEngine Engine { get; private set; }

        private MapData mapData;
        private MapState mapState;
        private OnlineRole role;

        /// <summary>随机排出的行动顺序（座位号，下标 0 = 起始玩家）。</summary>
        private int[] turnOrder = System.Array.Empty<int>();

        /// <summary>各座位已挑中的企业家颜色（-1 = 还没挑）。</summary>
        private int[] pickedColors = System.Array.Empty<int>();

        /// <summary>本机玩家已提交的企业家颜色；-1 = 还没挑。</summary>
        private int localColor = -1;

        private bool draftDone;

        private void Awake()
        {
            if (session == null || mapView == null)
            {
                Debug.LogError("[开局] GameBootstrap 的 session / mapView 未指定。", this);
                return;
            }

            int playerCount = session.PlayerCount;
            mapData = mapView.SelectFor(playerCount);

            if (mapData == null)
            {
                Debug.LogError($"[开局] {playerCount} 人局没有对应的地图数据。", this);
                return;
            }

            mapState = mapView.Build(mapData);

            // 联机身份由准备房间写入：Host = 本机房主，Client = 加入者，None = 单机热座。
            role = session.Role;

            // 行动顺序：联机时由房主开局随机排出并写进 Lobby，所有人读同一份；单机在本机随机排。
            turnOrder = ResolveTurnOrder(playerCount);

            // 客户端：地图只用来画视觉，状态以主机快照为准，引擎与界面等快照到了再建立。
            if (role == OnlineRole.Client)
            {
                StartOnlineClient();
                return;
            }

            if (role == OnlineRole.Host)
            {
                var host = OnlineSessionHost.Instance;
                if (host == null || host.Session == null)
                {
                    Debug.LogError("[联机] 找不到 OnlineSessionHost 或联机会话，无法以主机身份开局。", this);
                    return;
                }

                onlineSession = host.Session;
                onlineSession.ErrorArrived += OnOnlineError;
                localSeat = 0;
                host.RosterChanged += OnHostRosterChanged;
            }

            // 开发者模式：跳过企业家挑选直接按座位默认颜色开局，免得每次调试都要先挑企业家。
            if (role == OnlineRole.None && developerMode)
            {
                CreateAndStartGame(DefaultColors(playerCount), turnOrder);
                return;
            }

            if (role == OnlineRole.None) BeginOfflineDraft(playerCount);
            else BeginOnlineDraft();
        }

        // —— 开局：随机行动顺序 + 倒序挑选企业家 ——

        /// <summary>
        /// 联机读房主排好、随 Started 发布的顺序；单机在本机随机排一份。
        /// 联机读不到（旧房主 / 字段缺失）时退回按座位的自然顺序，保证两端算出同一份。
        /// </summary>
        private int[] ResolveTurnOrder(int playerCount)
        {
            if (role != OnlineRole.None)
            {
                var host = OnlineSessionHost.Instance;
                int[] online = host != null ? host.SeatOrder : System.Array.Empty<int>();
                return IsPermutation(online, playerCount) ? (int[])online.Clone() : NaturalOrder(playerCount);
            }

            return ShuffledOrder(playerCount);
        }

        private static bool IsPermutation(int[] order, int count)
        {
            if (order == null || order.Length != count) return false;

            var seen = new bool[count];
            for (int i = 0; i < order.Length; i++)
            {
                int seat = order[i];
                if (seat < 0 || seat >= count || seen[seat]) return false;
                seen[seat] = true;
            }

            return true;
        }

        private static int[] NaturalOrder(int count)
        {
            var order = new int[Mathf.Max(0, count)];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            return order;
        }

        private static int[] ShuffledOrder(int count)
        {
            int[] order = NaturalOrder(count);
            var rng = new System.Random(System.Environment.TickCount);

            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int temp = order[i];
                order[i] = order[j];
                order[j] = temp;
            }

            return order;
        }

        private static int[] Filled(int count, int value)
        {
            var array = new int[Mathf.Max(0, count)];
            for (int i = 0; i < array.Length; i++) array[i] = value;
            return array;
        }

        /// <summary>现在该挑企业家的座位：按行动顺序**倒序**找第一个还没挑的人。</summary>
        private int DraftSeat
        {
            get
            {
                for (int i = turnOrder.Length - 1; i >= 0; i--)
                {
                    int seat = turnOrder[i];
                    if (seat >= 0 && seat < pickedColors.Length && pickedColors[seat] < 0) return seat;
                }

                return -1;
            }
        }

        /// <summary>单机热座：按倒序一名玩家挑一次，挑完即开局。</summary>
        private void BeginOfflineDraft(int playerCount)
        {
            pickedColors = Filled(playerCount, -1);

            if (entrepreneurPanel == null)
            {
                Debug.LogWarning("[开局] 未指定 EntrepreneurChoicePanel：跳过企业家挑选，" +
                                 "按座位自然顺序与默认颜色直接开局。", this);
                CreateAndStartGame(DefaultColors(playerCount), turnOrder);
                return;
            }

            entrepreneurPanel.Chosen += OnEntrepreneurChosen;
            entrepreneurPanel.Show(string.Empty, string.Empty, OrderText());
            RefreshDraftUI();
        }

        /// <summary>房主：等所有人把企业家写进 Lobby 后建局。</summary>
        private void BeginOnlineDraft()
        {
            pickedColors = Filled(session.PlayerCount, -1);
            SyncPickedFromLobby();

            if (entrepreneurPanel == null)
            {
                Debug.LogWarning("[联机] 未指定 EntrepreneurChoicePanel：房主无法等待其他人挑选，" +
                                 "已按座位默认颜色直接开局。", this);
                CreateAndStartGame(DefaultColors(session.PlayerCount), turnOrder);
                return;
            }

            entrepreneurPanel.Chosen += OnEntrepreneurChosen;
            entrepreneurPanel.Show(string.Empty, string.Empty, OrderText());
            RefreshDraftUI();
            TryStartHostGame();
        }

        private void OnEntrepreneurChosen(PlayerColor color)
        {
            int picked = (int)color;
            int seat = DraftSeat;

            if (seat < 0) return;
            if (System.Array.IndexOf(pickedColors, picked) >= 0) return; // 这张已经被别人挑走了

            localColor = picked;
            pickedColors[seat] = picked; // 先乐观更新，Lobby 刷新回来会覆盖

            if (role == OnlineRole.None)
            {
                if (DraftSeat < 0) FinishDraft(ColorsFromPicks());
                else RefreshDraftUI();
                return;
            }

            OnlineSessionHost.Instance?.SetColor(picked);
            RefreshDraftUI();
            if (role == OnlineRole.Host) TryStartHostGame();
        }

        /// <summary>挑选完成：收起弹窗、按最终颜色与行动顺序建局。</summary>
        private void FinishDraft(PlayerColor[] seatColors)
        {
            draftDone = true;

            if (entrepreneurPanel != null)
            {
                entrepreneurPanel.Chosen -= OnEntrepreneurChosen;
                entrepreneurPanel.Hide();
            }

            CreateAndStartGame(seatColors, turnOrder);
        }

        private PlayerColor[] ColorsFromPicks()
        {
            var colors = new PlayerColor[pickedColors.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = pickedColors[i] >= 0 ? (PlayerColor)pickedColors[i] : (PlayerColor)(i % 4);
            return colors;
        }

        /// <summary>房主：名单刷新后同步「谁挑了哪张」，收齐且不重复就开局。</summary>
        private void OnHostRosterChanged(System.Collections.Generic.IReadOnlyList<NomadCity.Core.Net.LobbyMemberInfo> members)
        {
            if (draftDone) return;

            SyncPickedFromLobby();
            RefreshDraftUI();
            TryStartHostGame();
        }

        private void TryStartHostGame()
        {
            var host = OnlineSessionHost.Instance;
            if (host == null || host.Lobby == null || draftDone) return;

            int playerCount = session.PlayerCount;
            int[] colors = host.SeatColors;
            if (colors.Length != playerCount) return;

            var seatColors = new PlayerColor[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                if (colors[i] < 0 || colors[i] >= 4) return; // 还有人没挑
                seatColors[i] = (PlayerColor)colors[i];

                for (int j = 0; j < i; j++)
                    if (seatColors[j] == seatColors[i]) return; // 撞色（正常流程不会发生）
            }

            FinishDraft(seatColors);
        }

        private int LocalSeat() =>
            onlineSession != null && onlineSession.LocalSeat >= 0 ? onlineSession.LocalSeat : localSeat;

        /// <summary>把各座位已挑中的企业家颜色同步到本地；保留本机刚提交但还没回写的选择。</summary>
        private void SyncPickedFromLobby()
        {
            var host = OnlineSessionHost.Instance;
            int[] colors = host != null ? host.SeatColors : System.Array.Empty<int>();

            for (int i = 0; i < pickedColors.Length; i++)
                pickedColors[i] = i < colors.Length ? colors[i] : -1;

            int seat = LocalSeat();
            if (localColor >= 0 && seat >= 0 && seat < pickedColors.Length && pickedColors[seat] < 0)
                pickedColors[seat] = localColor;
        }

        /// <summary>刷新企业家弹窗：顺序文本、占用者、以及现在能不能点。</summary>
        private void RefreshDraftUI()
        {
            if (entrepreneurPanel == null || draftDone) return;

            int drafter = DraftSeat;
            bool online = role != OnlineRole.None;
            bool canPick = drafter >= 0 && (!online || drafter == LocalSeat());

            entrepreneurPanel.SetOrder(OrderText());
            entrepreneurPanel.Refresh(pickedColors,
                session != null ? session.PlayerNicknames : null, canPick);

            if (!online)
            {
                entrepreneurPanel.SetTitle(drafter >= 0 ? $"{SeatLabel(drafter)} 请挑选企业家" : "挑选完毕");
                entrepreneurPanel.SetHint(drafter >= 0
                    ? "按行动顺序的倒序挑选：挑好后轮到下一名玩家。"
                    : string.Empty);
                return;
            }

            entrepreneurPanel.SetTitle("选择你的企业家");

            if (drafter < 0) entrepreneurPanel.SetHint("所有人已挑选，等待开局…");
            else if (canPick) entrepreneurPanel.SetHint($"轮到你挑选（{SeatLabel(drafter)}）。");
            else entrepreneurPanel.SetHint($"等待 {SeatLabel(drafter)} 挑选…");
        }

        private string OrderText()
        {
            var play = new System.Collections.Generic.List<string>();
            for (int i = 0; i < turnOrder.Length; i++) play.Add($"{i + 1}. {SeatLabel(turnOrder[i])}");

            var draft = new System.Collections.Generic.List<string>();
            for (int i = turnOrder.Length - 1; i >= 0; i--) draft.Add(SeatLabel(turnOrder[i]));

            return "行动顺序：" + string.Join(" → ", play) + "\n" +
                   "挑选企业家（倒序）：" + string.Join(" → ", draft);
        }

        private string SeatLabel(int seat)
        {
            string[] names = session != null ? session.PlayerNicknames : null;
            string name = names != null && seat >= 0 && seat < names.Length && !string.IsNullOrWhiteSpace(names[seat])
                ? names[seat]
                : $"玩家 {seat + 1}";

            if (role != OnlineRole.None && seat == LocalSeat()) name += "（你）";
            return name;
        }

        private static PlayerColor[] DefaultColors(int playerCount)
        {
            var colors = new PlayerColor[playerCount];
            for (int i = 0; i < playerCount; i++) colors[i] = (PlayerColor)i;
            return colors;
        }

        /// <summary>建引擎并开局。选色完成后调用；单机 / 主机在本地立即执行，客户端等快照。</summary>
        private void CreateAndStartGame(PlayerColor[] seatColors, int[] order)
        {
            // 起始玩家 = 随机顺序的第一位，起始玩家标记也记在他头上（§3.1）。
            int firstSeat = order != null && order.Length > 0 ? order[0] : 0;

            Engine = GameEngine.Create(session.ToGameSetup(), firstSeat, mapState, null, null, seatColors, order);
            // 联机强制关闭开发者模式；单机时按 Inspector 的调试开关。
            Engine.State.DeveloperMode = role == OnlineRole.None && developerMode;
            ApplyDistrictScores(Engine.State, mapData);

            if (role == OnlineRole.Host && onlineSession != null) onlineSession.BindEngine(Engine);

            InitializePresentation(localSeat);

            if (onlineSession != null && screenController != null)
                screenController.SetCommandGateway(onlineSession.Submit);

            CommandResult start = Engine.Dispatch(new StartGameCommand(firstSeat));
            if (!start.Ok)
            {
                Debug.LogError($"[开局] 无法开始游戏：{start.Error}", this);
                return;
            }

            // 开发者模式：把开局设置自动跑完（认领初始资源点 → 结算绿色事件牌 → 进入玩家行动阶段），
            // 这样一进游戏就有移动城市可用，各行动按钮可以立刻点起来测。
            if (role == OnlineRole.None && developerMode) AutoSetupForDeveloperMode(Engine.State);

            // 主机：开局快照发给所有已接入的客户端；之后才接入的由 PeerJoined 自动补发。
            if (onlineSession != null && onlineSession.IsHost) onlineSession.BroadcastSnapshot();

            var state = Engine.State;
            Debug.Log($"[开局] {state.Players.Count} 人局 → 地图「{mapData.DisplayName}」：" +
                      $"资源点 {mapState.Nodes.Count} 个，航道 {mapState.Routes.Count} 条。\n" +
                      $"[引擎] 回合 {state.TurnNumber}/{TurnFlow.TotalTurns}，" +
                      $"阶段 {state.Phase}，起始座位 {state.FirstPlayerSeat}，行动座位 {state.ActiveSeat}。");
        }

        private void OnDestroy()
        {
            if (entrepreneurPanel != null) entrepreneurPanel.Chosen -= OnEntrepreneurChosen;

            var host = OnlineSessionHost.Instance;
            if (host != null) host.RosterChanged -= OnHostRosterChanged;
            if (host != null) host.RosterChanged -= OnClientRosterChanged;
        }

        /// <summary>
        /// 建立界面与引擎的连接。单机 / 主机在 Awake 里立即调用；客户端等快照到达后调用，
        /// 因为此刻才知道主机裁决出来的状态与自己的座位。
        /// </summary>
        private void InitializePresentation(int seat)
        {
            // 顶栏显示的玩家名来自准备房间的名单；联机时在自己那一行后面标「（你）」。
            if (gameHud != null)
            {
                gameHud.SetPlayerNames(session != null ? session.PlayerNicknames : null);
                gameHud.SetLocalSeat(session != null && session.Role != OnlineRole.None ? seat : -1);
            }

            // 先把引擎状态画出来，再把界面接上引擎 —— 顺序不能反，否则界面第一次刷新时地图还没有标记。
            if (mapStateRenderer != null)
                mapStateRenderer.Initialize(Engine.State, mapView.MapRoot, mapView.NodeViews, mapView.RouteViews);

            if (screenController != null)
                screenController.Initialize(Engine, mapStateRenderer, mapPointerInput, gameHud, actionPanel,
                    eventCardPanel, choicePopup, facilitySupplyPanel, cityPanelView, cityPatternPanel, cardPreviewPopup,
                    characterHandPanel, seat, session != null && session.Role != OnlineRole.None);
            else
                Debug.LogWarning("[开局] 未指定 GameScreenController，界面不会响应键鼠操作。", this);

            // 接线自检：设施供应区 / 城市面板漏接的话，「建设」按钮点下去没有任何可点的地方，
            // 画面上只表现为「点了没反应」，所以这里点名提醒。
            if (facilitySupplyPanel == null)
                Debug.LogWarning("[开局] 未指定 FacilitySupplyPanel：设施供应区的 6 张牌不会显示，" +
                                 "也就没法选要建哪张牌（建设 §6.4e）。", this);

            if (cityPanelView == null)
                Debug.LogWarning("[开局] 未指定 CityPanelView：城市面板的 12 个格子不会显示，" +
                                 "也就没法选把设施建在哪里（建设 §6.4e）。", this);

            // 接线自检：宣告城市样式靠这个弹窗，漏接的话点「宣告城市样式」不会有任何反应。
            if (cityPatternPanel == null)
                Debug.LogWarning("[开局] 未指定 CityPatternPanel：点「宣告城市样式」不会弹出样式牌，" +
                                 "也就无法宣告（§6.3b）。请把场景里的 CityPatternPanel 拖进本字段。", this);

            // 接线自检：点已建成的设施会弹放大卡面，漏接的话点了只会弹一句「没接线」的提示。
            if (cardPreviewPopup == null)
                Debug.LogWarning("[开局] 未指定 CardPreviewPopup：点已建成的设施不会弹出放大卡面。" +
                                 "请把场景里的 CardPreviewPopup 拖进本字段。", this);

            // 接线自检：角色牌盖放靠这个手牌托盘，漏接的话盖放阶段没有可点的牌，也就没法盖放（§4.2 阶段 1）。
            if (characterHandPanel == null)
                Debug.LogWarning("[开局] 未指定 CharacterHandPanel：角色牌盖放阶段没有手牌托盘可点，" +
                                 "也就无法盖放角色牌（§4.2 阶段 1）。请把场景里的 CharacterHandPanel 拖进本字段。", this);

            // 接线自检：探索的「付给谁」小选择窗靠这个字段，漏接的话界面上不会有任何提示，只会在探索时静默跳过。
            if (choicePopup == null)
                Debug.LogWarning("[开局] 未指定 ChoicePopup：探索时「路费付给谁」的小选择窗不会弹出" +
                                 "（会静默按第一位对手结算）。请在 Inspector 里把场景中的 ChoicePopup " +
                                 "拖到本物体的「Choice Popup」字段。", this);

            // 行动记录要在开局命令之前接上引擎，否则连「开局」这一步都记不进去。
            // 漏接这个字段在画面上完全看不出来（面板点开是空的），所以这里再兜一次底：
            // 场景里只有一个 ActionLogPanel，直接找出来用，并把这件事说清楚。
            if (actionLogPanel == null)
            {
                actionLogPanel = FindAnyObjectByType<ActionLogPanel>();
                if (actionLogPanel != null)
                    Debug.LogWarning($"[开局] GameBootstrap 上的「Action Log Panel」字段是空的，" +
                                     $"已自动改用场景里的 {actionLogPanel.name}。建议在 Inspector 里把它拖进该字段，" +
                                     "免得以后场景改名或复制时对不上。", this);
            }

            if (actionLogPanel != null)
                actionLogPanel.Bind(Engine);
            else
                Debug.LogWarning("[开局] 场景里找不到 ActionLogPanel，右上角的「行动记录」不会记录任何内容。", this);
        }

        // —— 联机 ——

        /// <summary>
        /// 客户端：会话由 <see cref="OnlineSessionHost"/> 在准备房间阶段建立并已连上 Relay。
        /// 若快照先到（引擎已在准备房间建好），直接接线；否则等 <see cref="NetworkGameSession.SnapshotApplied"/>。
        /// </summary>
        private void StartOnlineClient()
        {
            var host = OnlineSessionHost.Instance;
            if (host == null || host.Session == null)
            {
                Debug.LogError("[联机] 找不到 OnlineSessionHost 或联机会话，无法以客户端身份开局。", this);
                return;
            }

            onlineSession = host.Session;
            onlineSession.ErrorArrived += OnOnlineError;

            pickedColors = Filled(session.PlayerCount, -1);

            // 先挑企业家：主机要等所有人的颜色都写进 Lobby 才会建局，客户端此时还没有快照。
            if (entrepreneurPanel != null)
            {
                entrepreneurPanel.Chosen += OnEntrepreneurChosen;
                entrepreneurPanel.Show(string.Empty, string.Empty, OrderText());
                host.RosterChanged += OnClientRosterChanged;
                SyncPickedFromLobby();
                RefreshDraftUI();
            }
            else
            {
                Debug.LogWarning("[联机] 未指定 EntrepreneurChoicePanel：客户端无法挑选企业家，" +
                                 "已改为轮到自己时自动挑一张空闲的企业家。", this);
                host.RosterChanged += OnClientRosterChanged;
                OnClientRosterChanged(null);
            }

            if (onlineSession.Engine != null)
            {
                ApplyOnlineSnapshot(onlineSession.Engine.State);
                return;
            }

            onlineSession.SnapshotApplied += ApplyOnlineSnapshot;
        }

        /// <summary>客户端：名单刷新时更新「哪些企业家已被挑走」，轮到自己时刷新可点状态。</summary>
        private void OnClientRosterChanged(System.Collections.Generic.IReadOnlyList<NomadCity.Core.Net.LobbyMemberInfo> members)
        {
            if (draftDone) return;

            SyncPickedFromLobby();

            if (entrepreneurPanel == null)
            {
                if (DraftSeat == LocalSeat())
                {
                    int pick = FirstFreeColor();
                    if (pick >= 0) OnEntrepreneurChosen((PlayerColor)pick);
                }
                return;
            }

            RefreshDraftUI();
        }

        /// <summary>还没被挑走的第一张企业家（面板漏接时的兜底用）。</summary>
        private int FirstFreeColor()
        {
            for (int color = 0; color < 4; color++)
                if (System.Array.IndexOf(pickedColors, color) < 0) return color;
            return -1;
        }

        /// <summary>客户端收齐快照：用快照建立引擎，并补上整套界面接线（此时才知道自己的座位）。</summary>
        private void ApplyOnlineSnapshot(GameState state)
        {
            // 主机已按所有人挑定的企业家建局：收起挑选弹窗，之后颜色以快照为准。
            draftDone = true;
            if (entrepreneurPanel != null)
            {
                entrepreneurPanel.Chosen -= OnEntrepreneurChosen;
                entrepreneurPanel.Hide();
            }

            var host = OnlineSessionHost.Instance;
            if (host != null) host.RosterChanged -= OnClientRosterChanged;

            Engine = onlineSession.Engine;
            Engine.State.DeveloperMode = false; // 联机强制关闭开发者模式

            int seat = onlineSession.LocalSeat >= 0 ? onlineSession.LocalSeat : localSeat;
            localSeat = seat;
            InitializePresentation(seat);

            if (screenController != null)
                screenController.SetCommandGateway(onlineSession.Submit);

            Debug.Log($"[联机] 已同步开局快照：座位 {seat + 1}，行动顺位 " +
                      $"{System.Array.IndexOf(Engine.State.SeatOrder, seat) + 1}/{Engine.State.Players.Count}，" +
                      $"回合 {state.TurnNumber}，阶段 {state.Phase}，行动座位 {state.ActiveSeat + 1}。", this);
        }

        /// <summary>联机错误：控制台报错；准备房间阶段的具体提示由 Lobby 场景负责显示。</summary>
        private void OnOnlineError(string message)
        {
            Debug.LogError("[联机] " + message, this);
        }

        /// <summary>
        /// 把地图上配置的区块控制分（§9.2）覆盖到引擎状态。此处只覆盖地图里**显式列出**的区块，
        /// 没配置的区块保留引擎里的标准表取值（例如三人地图没有 G 区，无需覆盖）。
        /// </summary>
        private static void ApplyDistrictScores(GameState state, MapData mapData)
        {
            foreach (DistrictScore entry in mapData.DistrictScores)
            {
                state.DistrictScores[(int)entry.District] = entry.Score;
            }
        }

        /// <summary>
        /// 开发者模式专用：自动把开局设置跑完 —— 每名玩家依次认领一个绿色资源点、把翻出的绿色事件标记为
        /// 「已结算」（但**不发放选项奖励**，保持开局资源 / 金券可预测），然后推进到玩家行动阶段。
        /// 全部走命令通道，不直接改状态。4 名玩家各占一个绿色资源点后，其余资源点仍可正常探索，够测试用。
        /// </summary>
        private void AutoSetupForDeveloperMode(GameState state)
        {
            int seat = state.FirstPlayerSeat;
            for (int i = 0; i < state.Players.Count; i++)
            {
                int nodeId = FindFreeGreenNode(state, seat);
                if (nodeId >= 0)
                {
                    CommandResult pick = Engine.Dispatch(new ChooseStartNodeCommand(seat, nodeId));
                    if (pick.Ok)
                        // grantRewards: false —— 只标记事件已结算，不替玩家白拿绿色事件选项奖励。
                        Engine.Dispatch(new ResolveEventOptionCommand(seat, nodeId, 0, acceptCost: true,
                            grantRewards: false));
                    else
                        Debug.LogWarning($"[开发者模式] 座位 {seat + 1} 自动选初始资源点失败：{pick.Error}", this);
                }

                seat = state.NextSeat(seat);
            }

            // 角色牌盖放也要跑完 —— 否则开发者模式一进游戏就没盖放牌，「使用角色牌」按钮点不动（§6.3a）。
            // 每人盖放自己手牌里的第一张，让任意座位都能直接测策略 / 计谋。
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerState player = state.Players[i];
                if (player.PlacedCharacterCard < 0 && player.HandCharacterCards.Count > 0)
                    Engine.Dispatch(new PlaceCharacterCardCommand(i, player.HandCharacterCards[0]));
            }

            CommandResult advance = Engine.Dispatch(new AdvancePhaseCommand());
            if (!advance.Ok)
                Debug.LogWarning($"[开发者模式] 自动推进到玩家行动阶段失败：{advance.Error}", this);
        }

        /// <summary>
        /// 找一个还没有停靠别人移动城市的绿色资源点（开发者模式自动开局用）。
        /// 刻意跳过 <c>B-01</c>：选它会按规则白送 2/2/2 资源，会让开发者模式的开局状态显得「莫名多资源」。
        /// </summary>
        private static int FindFreeGreenNode(GameState state, int seat)
        {
            foreach (MapNodeState node in state.Map.Nodes)
            {
                if (node.Color == NodeColor.Green && node.Code != "B-01"
                    && !ActionRules.HasOpponentCity(state, node.Id, seat))
                    return node.Id;
            }
            return -1;
        }
    }
}