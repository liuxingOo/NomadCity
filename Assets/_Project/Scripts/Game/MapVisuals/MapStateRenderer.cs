using System.Collections.Generic;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>
    /// 把引擎的 <see cref="MapState"/> 画到地图上：影响力空格、已放置的影响力、资源点指示物、移动城市。
    ///
    /// 设计要点：
    /// - 标记全部是**世界空间 SpriteRenderer**，挂在地图实例根节点下的 StateMarkers 容器里（符合项目约定）。
    /// - StateMarkers 的局部缩放按地图根节点的缩放做反向补偿，使容器内部 1 单位 = 1 世界单位；
    ///   这样地图 Prefab 整体缩放（四人面 0.2）不会把标记缩到看不见。
    /// - 每次 Refresh 全量重建。地图只有 22 个资源点 + 22 条航道，重建成本可忽略，也不会留下脏状态。
    /// - 占位素材由 <see cref="MapSpriteFactory"/> 运行时生成；换成美术素材时只改那一处。
    /// - 资源点编号底图上已经印了，这里默认不重复画（见 showNodeLabels）。
    /// </summary>
    public sealed class MapStateRenderer : MonoBehaviour
    {
        [Header("资源点编号标签")]
        [Tooltip("是否在地图上额外生成资源点编号文字。底图美术上已经印了编号（A-01 之类），" +
                 "所以默认关闭 —— 打开只会在已有编号上再叠一层。仅在换了底图、需要核对 " +
                 "MapNodeView 填的 Code 是否与图一致时临时打开。")]
        [SerializeField] private bool showNodeLabels;

        [Tooltip("标签字体。留空则不生成标签（在 Inspector 里指定 NotoSansSC-Regular SDF）。")]
        [SerializeField] private TMP_FontAsset labelFont;

        [Tooltip("标签的世界高度（地图单位）。字太小或太大就调这个。")]
        [SerializeField, Min(0.01f)] private float labelWorldHeight = 0.10f;

        [Header("标记尺寸（世界单位）")]
        [SerializeField, Min(0.01f)] private float slotRingSize = 0.20f;
        [SerializeField, Min(0.01f)] private float influenceSize = 0.18f;
        [SerializeField, Min(0.01f)] private float citySize = 0.30f;
        [Tooltip("没配图标时用的色盘指示物尺寸（世界单位）。")]
        [SerializeField, Min(0.01f)] private float tokenSize = 0.16f;

        [Header("资源点指示物（图标下标 = ResourceType）")]
        [Tooltip("正常数量的图标：0=源岩、1=源石、2=异铁、3=至纯源石。留空则退回上面的色盘占位画法。")]
        [SerializeField] private Sprite[] resourceTokenIcons = new Sprite[4];

        [Tooltip("数量为 2 时的变体图标，下标同上；没配的会退回对应的正常图标。目前只有源岩会出现 ×2。")]
        [SerializeField] private Sprite[] resourceTokenIconsX2 = new Sprite[4];

        [Tooltip("指示物相对资源点原点的世界空间偏移（世界单位）。底图上资源点左上角那个带准星的框就是指示物的位置，" +
                 "默认值照底图量的；X 为负 = 往左，Y 为正 = 往上，对不齐直接改这两个数。")]
        [SerializeField] private Vector2 tokenOffset = new Vector2(-0.48f, 0.40f);

        [Tooltip("指示物图标的世界宽度（世界单位）—— 调到刚好填满底图上那个框。")]
        [SerializeField, Min(0.01f)] private float tokenIconSize = 0.30f;

        [SerializeField] private float labelOffsetY = -0.34f;

        [Header("玩家标记（Art/Tokens 里的 token_*）")]
        [Tooltip("玩家标记素材，下标 = PlayerColor（绿 0 / 黄 1 / 红 2 / 蓝 3）。" +
                 "留空则退回色盘圆片占位。地图上的影响力、计分轨、城市样式牌都用同一套标记。")]
        [SerializeField] private Sprite[] playerMarkerSprites = new Sprite[4];

        [Header("计分轨（底图外圈 -2 ~ 50 的格子）")]
        [Tooltip("是否在计分轨上画每名玩家的分数标记（§ 计分轨：每人一个标记停在当前分数格）。")]
        [SerializeField] private bool showScoreMarkers = true;

        [Tooltip("-2 那一格的格心（地图 Prefab 本地坐标：底图 50×50、原点在正中，所以左下角是 -25,-25）。" +
                 "数值照 5000px 底图量出来的：px(505,4886) → (-19.95,-23.87)。")]
        [SerializeField] private Vector2 scoreTrackBottomStart = new Vector2(-19.95f, -23.865f);

        [Tooltip("底部一排（-2 → 23）每 +1 分的 x 步长（本地单位）。")]
        [SerializeField, Min(0.01f)] private float scoreTrackBottomStep = 1.7548f;

        [Tooltip("右列最下面一格（24 分）的格心（本地坐标）。")]
        [SerializeField] private Vector2 scoreTrackRightStart = new Vector2(23.90f, -22.19f);

        [Tooltip("右列（24 → 50）每 +1 分的 y 步长（向上，本地单位）。")]
        [SerializeField, Min(0.01f)] private float scoreTrackRightStep = 1.7529f;

        [Tooltip("计分轨上玩家标记的直径（**地图本地单位**：一格大约 1.75，所以 0.6 左右刚好塞进四分之一格）。")]
        [SerializeField, Min(0.01f)] private float scoreMarkerSize = 0.60f;

        [Tooltip("同一格里有多个标记时的排布半径（本地单位）：1 个居中、2 个左右、3 个品字、4 个四角。")]
        [SerializeField, Min(0.01f)] private float scoreMarkerSpread = 0.45f;

        [Header("自选路线高亮（探索时点选航道）")]
        [Tooltip("玩家在探索时点选的航道会亮起来；这是高亮色。")]
        [SerializeField] private Color routeHighlightColor = new Color(1f, 0.82f, 0.20f, 0.55f);

        [Tooltip("高亮底盘的世界宽度（比空格圈大一些，套在它外面）。")]
        [SerializeField, Min(0.01f)] private float routeHighlightSize = 0.34f;

        [Header("采集自选高亮（采集阶段点选资源点）")]
        [Tooltip("采集阶段里，当前玩家已选中的资源点会套一个圈亮起来；这是圈的颜色。")]
        [SerializeField] private Color nodeHighlightColor = new Color(1f, 0.82f, 0.20f, 0.45f);

        [Tooltip("资源点高亮圈的世界直径（比资源点本体大一些，套在它外面）。")]
        [SerializeField, Min(0.01f)] private float nodeHighlightSize = 0.50f;

        // 排序：底图 -10（约定）；往上依次是高亮、空格、指示物、影响力、移动城市、标签。
        private const int SortHighlight = 0;
        private const int SortSlot = 1;
        private const int SortTokenBack = 3;
        private const int SortToken = 4;
        private const int SortInfluence = 5;
        private const int SortScore = 6;
        private const int SortCityBack = 9;
        private const int SortCity = 10;
        private const int SortLabel = 20;

        // 拾取优先级：数值大的优先（仅在距离相同时起作用）。
        private const int PriorityNodeBody = 0;
        private const int PriorityEmptySlot = 1;
        private const int PriorityOccupiedSlot = 3;

        private const string MarkersName = "StateMarkers";

        // 计分轨范围：底图外圈印的是 -2 … 50（底部一排 -2 → 23，右列 24 → 50 往上）。
        private const int ScoreTrackMin = -2;
        private const int ScoreTrackBottomLast = 23;
        private const int ScoreTrackRightFirst = 24;
        private const int ScoreTrackMax = 50;

        private GameState state;
        private Transform markersRoot;

        /// <summary>地图实例根节点：计分轨的格子是按底图本地坐标量的，画的时候要靠它换算到世界坐标。</summary>
        private Transform mapRoot;

        private IReadOnlyList<MapNodeView> nodeViews;
        private IReadOnlyList<MapRouteView> routeViews;

        private readonly List<HitTarget> targets = new List<HitTarget>();

        /// <summary>每个分数格里的玩家标记「到达先后」（分数 → 座位列表），用来决定格子内谁排第一。</summary>
        private readonly Dictionary<int, List<int>> scoreArrivals = new Dictionary<int, List<int>>();

        /// <summary>上一次刷新时每名玩家的分数，用来发现「谁换了格子」以维护到达顺序。</summary>
        private readonly Dictionary<int, int> lastScoreBySeat = new Dictionary<int, int>();

        /// <summary>当前高亮的航道 id（探索点选路线用）；空表示没有高亮。</summary>
        private readonly HashSet<int> highlightedRoutes = new HashSet<int>();

        /// <summary>当前高亮的资源点 id（采集阶段点选采集点用）；空表示没有高亮。</summary>
        private readonly HashSet<int> highlightedNodes = new HashSet<int>();

        private struct HitTarget
        {
            public Vector2 World;
            public float Radius;
            public int Priority;
            public MapHitKind Kind;
            public InfluenceSlotRef Slot;
            public int NodeId;
        }

        /// <summary>是否已经完成初始化、可以渲染。</summary>
        public bool IsReady => state != null && markersRoot != null;

        /// <summary>
        /// 绑定引擎状态与地图视图。nodeViews / routeViews 的下标必须与 <c>state.Map.Nodes / Routes</c> 的 id 对齐
        /// （由 <see cref="MapView.Build"/> 保证）。
        /// 这里收的是整个 <see cref="GameState"/>（而不只是 <see cref="MapState"/>），
        /// 因为要画「哪个玩家把移动城市停在哪」——那部分信息记在玩家身上。
        /// </summary>
        public void Initialize(GameState gameState, Transform mapRoot,
            IReadOnlyList<MapNodeView> nodes, IReadOnlyList<MapRouteView> routes)
        {
            state = gameState;
            nodeViews = nodes;
            routeViews = routes;
            this.mapRoot = mapRoot;
            markersRoot = CreateMarkersRoot(mapRoot);

            // 新的一局（或换了地图）到达顺序要重来，否则旧顺序会带到新局。
            scoreArrivals.Clear();
            lastScoreBySeat.Clear();

            Refresh();
        }

        /// <summary>按当前引擎状态重建全部标记。每次命令执行后调用即可。</summary>
        public void Refresh()
        {
            targets.Clear();

            if (!IsReady)
            {
                Debug.LogWarning("[地图] MapStateRenderer 尚未初始化，无法渲染地图状态。", this);
                return;
            }

            ClearMarkers();

            int nodeCount = Mathf.Min(nodeViews.Count, state.Map.Nodes.Count);
            for (int i = 0; i < nodeCount; i++) DrawNode(state.Map.Nodes[i], nodeViews[i]);

            int routeCount = Mathf.Min(routeViews.Count, state.Map.Routes.Count);
            for (int i = 0; i < routeCount; i++) DrawRoute(state.Map.Routes[i], routeViews[i]);

            DrawScoreTrack();
        }

        /// <summary>
        /// 高亮一组航道（探索时玩家点选的路线）。传 null 或空集合即取消全部高亮。
        /// 只影响显示，不改规则状态。
        /// </summary>
        public void SetHighlightedRoutes(IEnumerable<int> routeIds)
        {
            highlightedRoutes.Clear();
            if (routeIds != null)
            {
                foreach (int routeId in routeIds) highlightedRoutes.Add(routeId);
            }

            if (IsReady) Refresh();
        }

        /// <summary>高亮若干资源点（采集阶段把当前玩家已选中的采集点亮起来）；传 null 清空。</summary>
        public void SetHighlightedNodes(IEnumerable<int> nodeIds)
        {
            highlightedNodes.Clear();
            if (nodeIds != null)
            {
                foreach (int nodeId in nodeIds) highlightedNodes.Add(nodeId);
            }

            if (IsReady) Refresh();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器专用：在 Scene 视图里，给每个资源点画出「指示物落点与尺寸」的线框，
        /// 方便对着底图左上角那个准星框对齐。运行期画的是真实物体，这里不重复画。
        /// 与运行期用的是同一组数值，所以 Scene 里对齐了，Play 出来就是一致的
        /// （偏移与尺寸都按世界单位算，跟 MapNodeView 的 Gizmos 一样不受 Prefab 缩放影响）。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            if (tokenIconSize <= 0f) return;

            MapNodeView[] nodes = FindObjectsByType<MapNodeView>(FindObjectsInactive.Exclude);

            Gizmos.color = new Color(0.20f, 0.85f, 1f, 0.9f);
            foreach (MapNodeView node in nodes)
            {
                if (node == null) continue;

                Vector3 center = node.transform.position + new Vector3(tokenOffset.x, tokenOffset.y, 0f);
                Gizmos.DrawWireCube(center, new Vector3(tokenIconSize, tokenIconSize, 0f));
                Gizmos.DrawWireSphere(center, tokenIconSize * 0.08f); // 中心点，方便看偏移方向
            }
        }
#endif

        /// <summary>把世界坐标处的点击解析成地图元素；没有命中返回 false。</summary>
        public bool TryPick(Vector2 world, out MapHit hit)
        {
            hit = default;

            bool found = false;
            float bestDistance = float.MaxValue;
            int bestPriority = int.MinValue;
            HitTarget best = default;

            for (int i = 0; i < targets.Count; i++)
            {
                HitTarget target = targets[i];
                float distance = Vector2.Distance(world, target.World);
                if (distance > target.Radius) continue;

                // 先比距离；距离相同（例如移动城市与它所在资源点本体）再比优先级。
                bool closer = distance < bestDistance - 1e-4f;
                bool tieButStronger = Mathf.Abs(distance - bestDistance) <= 1e-4f && target.Priority > bestPriority;
                if (!closer && !tieButStronger) continue;

                bestDistance = distance;
                bestPriority = target.Priority;
                best = target;
                found = true;
            }

            if (found) hit = new MapHit(best.Kind, best.Slot, best.NodeId, best.World);
            return found;
        }

        // —— 绘制 ——

        private void DrawNode(MapNodeState node, MapNodeView view)
        {
            Vector3 origin = view.transform.position;

            // 采集阶段：当前玩家已选中的采集点套一圈高亮（排在空格与影响力之下，不挡它们）。
            if (highlightedNodes.Contains(node.Id))
            {
                Spawn(MapSpriteFactory.Ring(), origin, nodeHighlightSize, nodeHighlightColor, SortHighlight,
                    $"NodeHighlight_{node.Code}");
            }

            for (int slot = 0; slot < node.Slots.Capacity; slot++)
            {
                Vector3 world = origin + view.SlotWorldOffset(slot);
                InfluenceSlotRef slotRef = InfluenceSlotRef.OnNode(node.Id, slot);
                int owner = node.Slots.OwnerOf(slot);

                if (owner >= 0)
                {
                    // 影响力 = 玩家标记：用 token 素材，没接素材才退回色盘圆片。
                    SpawnPlayerMarker(owner, world, influenceSize, SortInfluence, $"Influence_{node.Code}_{slot}");
                    AddTarget(world, influenceSize * 0.6f, PriorityOccupiedSlot, MapHitKind.NodeSlot, slotRef, node.Id);
                }
                else
                {
                    Spawn(MapSpriteFactory.Ring(), world, slotRingSize, MapPalette.EmptySlot, SortSlot,
                        $"Slot_{node.Code}_{slot}");
                    AddTarget(world, slotRingSize * 0.6f, PriorityEmptySlot, MapHitKind.NodeSlot, slotRef, node.Id);
                }
            }

            // 资源点指示物：底图上每个资源点的左上角都印着一个带准星的框，指示物就落在这个框里。
            if (node.HasResourceToken)
            {
                Vector3 world = origin + new Vector3(tokenOffset.x, tokenOffset.y, 0f);
                Sprite icon = TokenIcon(node.TokenResource, node.TokenAmount);

                if (icon != null)
                {
                    Spawn(icon, world, tokenIconSize, Color.white, SortToken, $"Token_{node.Code}");
                }
                else
                {
                    // 图标还没接上时的占位画法：色盘 + （数量 > 1 时）描边圈。
                    if (node.TokenAmount > 1)
                    {
                        Spawn(MapSpriteFactory.Disc(), world, tokenSize + 0.07f, MapPalette.Outline, SortTokenBack,
                            $"TokenBack_{node.Code}");
                    }
                    Spawn(MapSpriteFactory.Disc(), world, tokenSize, MapPalette.Resource(node.TokenResource), SortToken,
                        $"Token_{node.Code}");
                }
            }

            foreach (PlayerState player in state.Players)
            {
                if (player.MobileCityNodeId != node.Id) continue;
                Spawn(MapSpriteFactory.Disc(), origin, citySize + 0.09f, MapPalette.Outline, SortCityBack,
                    $"CityBack_{node.Code}");
                Spawn(MapSpriteFactory.Disc(), origin, citySize, MapPalette.Player(player.Color), SortCity,
                    $"City_{node.Code}_{player.SeatIndex}");
            }

            if (showNodeLabels && labelFont != null) SpawnLabel(origin + new Vector3(0f, labelOffsetY, 0f), node.Code);

            // 资源点本体：最弱的命中目标，用于「城市移动」选点。
            AddTarget(origin, 0.36f, PriorityNodeBody, MapHitKind.NodeBody, default, node.Id);
        }

        private void DrawRoute(RouteState route, MapRouteView view)
        {
            Vector3 origin = view.transform.position;
            bool highlighted = highlightedRoutes.Contains(route.Id);

            for (int slot = 0; slot < route.Slots.Capacity; slot++)
            {
                Vector3 world = origin + view.SlotWorldOffset(slot);
                InfluenceSlotRef slotRef = InfluenceSlotRef.OnRoute(route.Id, slot);
                int owner = route.Slots.OwnerOf(slot);

                // 玩家在探索里点选的航道：在每个空格下面垫一层半透明高亮盘，让选中的路线一眼可见。
                if (highlighted)
                {
                    Spawn(MapSpriteFactory.Disc(), world, routeHighlightSize, routeHighlightColor, SortHighlight,
                        $"RouteHighlight_{route.Id}_{slot}");
                }

                if (owner >= 0)
                {
                    SpawnPlayerMarker(owner, world, influenceSize, SortInfluence, $"RouteInfluence_{route.Id}_{slot}");
                    AddTarget(world, influenceSize * 0.6f, PriorityOccupiedSlot, MapHitKind.RouteSlot, slotRef, -1);
                }
                else
                {
                    Spawn(MapSpriteFactory.Ring(), world, slotRingSize, MapPalette.EmptySlot, SortSlot,
                        $"RouteSlot_{route.Id}_{slot}");
                    AddTarget(world, slotRingSize * 0.6f, PriorityEmptySlot, MapHitKind.RouteSlot, slotRef, -1);
                }
            }
        }

        /// <summary>
        /// 取某个资源点指示物该用的图标：数量 &gt; 1 时优先用 ×2 变体，
        /// 没配就退回普通图标，两张表都没有则返回 null（由调用方退回色盘占位）。
        /// </summary>
        private Sprite TokenIcon(ResourceType type, int amount)
        {
            int index = (int)type;
            Sprite variant = amount > 1 ? Pick(resourceTokenIconsX2, index) : null;
            return variant != null ? variant : Pick(resourceTokenIcons, index);
        }

        private static Sprite Pick(Sprite[] table, int index)
            => table != null && index >= 0 && index < table.Length ? table[index] : null;

        // —— 玩家标记（Art/Tokens 的 token_*） ——

        /// <summary>在世界坐标处画一枚玩家标记：优先用 token 素材，没接素材才退回玩家色圆片。</summary>
        private void SpawnPlayerMarker(int seat, Vector3 world, float size, int sortingOrder, string name)
        {
            PlayerColor color = state.Player(seat).Color;
            Sprite sprite = Pick(playerMarkerSprites, (int)color);

            if (sprite != null) Spawn(sprite, world, size, Color.white, sortingOrder, name);
            else Spawn(MapSpriteFactory.Disc(), world, size, MapPalette.Player(color), sortingOrder, name);
        }

        // —— 计分轨（底图外圈 -2 … 50） ——

        /// <summary>
        /// 每名玩家的标记停在当前分数格上。底图外圈印的刻度是 -2 … 50：
        /// 底部一排从左到右 -2 → 23，右列自下而上 24 → 50（坐标照 5000px 底图量的本地坐标）。
        ///
        /// 同一格里有多个标记时的排布按「数量」来（1 个居中、2 个左右并排、3 个上 1 下 2、4 个四角），
        /// 谁排第一个按「进入这一格的先后」算（初始按座位顺序）—— 见 <see cref="UpdateScoreArrivals"/>。
        /// </summary>
        private void DrawScoreTrack()
        {
            if (!showScoreMarkers || mapRoot == null || state == null) return;

            UpdateScoreArrivals();

            // 先把所有人按分数归类：一个字典项就是「一格」。
            var byScore = new Dictionary<int, List<int>>();
            foreach (PlayerState player in state.Players)
            {
                int score = Mathf.Clamp(player.Score, ScoreTrackMin, ScoreTrackMax);
                if (!byScore.TryGetValue(score, out List<int> seats))
                {
                    seats = new List<int>();
                    byScore[score] = seats;
                }
                seats.Add(player.SeatIndex);
            }

            // 标记尺寸按地图本地单位配，乘上地图实例的缩放才是世界尺寸（底图整体缩小到 0.2）。
            float mapScale = Mathf.Abs(mapRoot.lossyScale.x);
            if (mapScale <= 1e-4f) mapScale = 1f;
            float worldSize = scoreMarkerSize * mapScale;

            foreach (KeyValuePair<int, List<int>> entry in byScore)
            {
                int score = entry.Key;
                List<int> seats = OrderByArrival(score, entry.Value);
                Vector3 centerLocal = ScoreCellLocal(score);

                for (int i = 0; i < seats.Count; i++)
                {
                    Vector2 offset = ScoreMarkerOffset(i, seats.Count);
                    Vector3 world = mapRoot.TransformPoint(centerLocal + new Vector3(offset.x, offset.y, 0f));
                    SpawnPlayerMarker(seats[i], world, worldSize, SortScore, $"Score_{score}_{seats[i]}");
                }
            }
        }

        /// <summary>分数 → 格心（地图 Prefab 本地坐标）。</summary>
        private Vector3 ScoreCellLocal(int score)
        {
            if (score <= ScoreTrackBottomLast)
            {
                float x = scoreTrackBottomStart.x + (score - ScoreTrackMin) * scoreTrackBottomStep;
                return new Vector3(x, scoreTrackBottomStart.y, 0f);
            }

            float y = scoreTrackRightStart.y + (score - ScoreTrackRightFirst) * scoreTrackRightStep;
            return new Vector3(scoreTrackRightStart.x, y, 0f);
        }

        /// <summary>同一格内第 <paramref name="index"/> 个标记（共 <paramref name="count"/> 个）相对格心的偏移。</summary>
        private Vector2 ScoreMarkerOffset(int index, int count)
        {
            float spread = scoreMarkerSpread;

            switch (count)
            {
                case 1:
                    return Vector2.zero;

                case 2:
                    return new Vector2(index == 0 ? -spread : spread, 0f);

                case 3:
                    // 上排 1 个居中，下排 2 个左右。
                    return index == 0
                        ? new Vector2(0f, spread)
                        : new Vector2(index == 1 ? -spread : spread, -spread);

                default:
                    // 四角：左上 → 右上 → 左下 → 右下。
                    return new Vector2(index % 2 == 0 ? -spread : spread, index < 2 ? spread : -spread);
            }
        }

        /// <summary>
        /// 维护「谁先进入哪个分数格」。分数一变就把该座位从旧格删掉、追加到新格队尾，
        /// 这样同一格内的排布顺序就是真实的到达先后，而不是座位号。
        /// 只在本地视图里记账，不影响规则状态（联机时各端按同样的命令顺序刷新，顺序一致）。
        /// </summary>
        private void UpdateScoreArrivals()
        {
            foreach (PlayerState player in state.Players)
            {
                int seat = player.SeatIndex;
                int score = Mathf.Clamp(player.Score, ScoreTrackMin, ScoreTrackMax);

                if (lastScoreBySeat.TryGetValue(seat, out int previous))
                {
                    if (previous == score) continue;
                    RemoveArrival(previous, seat);
                }

                lastScoreBySeat[seat] = score;
                List<int> list = Arrivals(score);
                if (!list.Contains(seat)) list.Add(seat);
            }
        }

        private List<int> Arrivals(int score)
        {
            if (!scoreArrivals.TryGetValue(score, out List<int> list))
            {
                list = new List<int>();
                scoreArrivals[score] = list;
            }
            return list;
        }

        private void RemoveArrival(int score, int seat)
        {
            if (scoreArrivals.TryGetValue(score, out List<int> list)) list.Remove(seat);
        }

        /// <summary>按到达先后排好这一格里的座位；不在到达表里的（理论上不该有）按座位号补到队尾。</summary>
        private List<int> OrderByArrival(int score, List<int> seats)
        {
            List<int> order = Arrivals(score);
            var ordered = new List<int>(seats.Count);

            foreach (int seat in order)
            {
                if (seats.Contains(seat) && !ordered.Contains(seat)) ordered.Add(seat);
            }

            foreach (int seat in seats)
            {
                if (!ordered.Contains(seat)) ordered.Add(seat);
            }

            return ordered;
        }

        private void Spawn(Sprite sprite, Vector3 world, float size, Color color, int sortingOrder, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(markersRoot, false);
            go.transform.localPosition = markersRoot.InverseTransformPoint(world);

            // size 统一按「世界单位的宽度」解释。程序生成的占位图恰好 1 单位宽，导入的图标则带着
            // 自己的 Pixels Per Unit（比如 100），不做归一会让同一个 size 出现两种大小。
            float width = sprite != null && sprite.bounds.size.x > 1e-4f ? sprite.bounds.size.x : 1f;
            float scale = size / width;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = sortingOrder;
        }

        private void SpawnLabel(Vector3 world, string text)
        {
            var go = new GameObject($"Label_{text}");
            go.transform.SetParent(markersRoot, false);
            go.transform.localPosition = markersRoot.InverseTransformPoint(world);
            go.transform.localScale = Vector3.one;

            var label = go.AddComponent<TextMeshPro>();
            label.text = text;
            label.font = labelFont;
            label.fontSize = 36f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = MapPalette.Label;
            label.rectTransform.sizeDelta = new Vector2(5f, 1f);
            label.ForceMeshUpdate();

            // 用 TMP 自己量出的高度换算缩放，避免依赖「字号 → 世界尺寸」的经验系数。
            float measured = label.bounds.size.y;
            if (measured <= 1e-5f)
            {
                Debug.LogWarning($"[地图] 资源点标签「{text}」无法测量尺寸，已跳过。请检查 MapStateRenderer 的标签字体。", this);
                DestroySafely(go);
                return;
            }

            go.transform.localScale = Vector3.one * (labelWorldHeight / measured);

            MeshRenderer mesh = label.GetComponent<MeshRenderer>();
            if (mesh != null) mesh.sortingOrder = SortLabel;
        }

        private void AddTarget(Vector3 world, float radius, int priority, MapHitKind kind, InfluenceSlotRef slot, int nodeId)
        {
            targets.Add(new HitTarget
            {
                World = world,
                Radius = radius,
                Priority = priority,
                Kind = kind,
                Slot = slot,
                NodeId = nodeId,
            });
        }

        private void ClearMarkers()
        {
            for (int i = markersRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = markersRoot.GetChild(i).gameObject;
                child.SetActive(false); // 先失活，避免本帧与新标记重叠
                DestroySafely(child);
            }
        }

        private static void DestroySafely(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private Transform CreateMarkersRoot(Transform mapRoot)
        {
            if (mapRoot == null)
            {
                Debug.LogError("[地图] MapStateRenderer 没有拿到地图根节点。", this);
                return null;
            }

            Transform existing = mapRoot.Find(MarkersName);
            if (existing != null) DestroySafely(existing.gameObject);

            var go = new GameObject(MarkersName);
            go.transform.SetParent(mapRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // 反向补偿地图根节点的缩放，使容器内部 1 单位 = 1 世界单位。
            Vector3 scale = mapRoot.lossyScale;
            go.transform.localScale = new Vector3(
                1f / Mathf.Max(Mathf.Abs(scale.x), 1e-4f),
                1f / Mathf.Max(Mathf.Abs(scale.y), 1e-4f),
                1f);
            return go.transform;
        }
    }
}
