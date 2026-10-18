using NomadCity.Core.Engine;
using NomadCity.Core.Model;
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

        /// <summary>本局的规则引擎；地图构建完成后建立。UI 通过它提交命令、订阅事件。</summary>
        public GameEngine Engine { get; private set; }

        private void Awake()
        {
            if (session == null || mapView == null)
            {
                Debug.LogError("[开局] GameBootstrap 的 session / mapView 未指定。", this);
                return;
            }

            int playerCount = session.PlayerCount;
            MapData mapData = mapView.SelectFor(playerCount);

            if (mapData == null)
            {
                Debug.LogError($"[开局] {playerCount} 人局没有对应的地图数据。", this);
                return;
            }

            var map = mapView.Build(mapData);

            Engine = GameEngine.Create(session.ToGameSetup(), 0, map);
            Engine.State.DeveloperMode = developerMode; // 调试开关：非运行态在 Inspector 上勾选
            ApplyDistrictScores(Engine.State, mapData);

            // 先把引擎状态画出来，再把界面接上引擎 —— 顺序不能反，否则界面第一次刷新时地图还没有标记。
            if (mapStateRenderer != null)
                mapStateRenderer.Initialize(Engine.State, mapView.MapRoot, mapView.NodeViews, mapView.RouteViews);

            if (screenController != null)
                screenController.Initialize(Engine, mapStateRenderer, mapPointerInput, gameHud, actionPanel,
                    eventCardPanel, choicePopup, facilitySupplyPanel, cityPanelView, cityPatternPanel, cardPreviewPopup,
                    characterHandPanel, localSeat);
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

            CommandResult start = Engine.Dispatch(new StartGameCommand(0));
            if (!start.Ok)
            {
                Debug.LogError($"[开局] 无法开始游戏：{start.Error}", this);
                return;
            }

            // 开发者模式：把开局设置自动跑完（认领初始资源点 → 结算绿色事件牌 → 进入玩家行动阶段），
            // 这样一进游戏就有移动城市可用，各行动按钮可以立刻点起来测。
            if (developerMode) AutoSetupForDeveloperMode(Engine.State);

            var state = Engine.State;
            Debug.Log($"[开局] {playerCount} 人局 → 地图「{mapData.DisplayName}」：" +
                      $"资源点 {map.Nodes.Count} 个，航道 {map.Routes.Count} 条。\n" +
                      $"[引擎] 回合 {state.TurnNumber}/{TurnFlow.TotalTurns}，" +
                      $"阶段 {state.Phase}，起始座位 {state.FirstPlayerSeat}，行动座位 {state.ActiveSeat}。");
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