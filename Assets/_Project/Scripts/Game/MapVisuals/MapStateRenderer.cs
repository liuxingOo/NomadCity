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

        [Header("自选路线高亮（探索时点选航道）")]
        [Tooltip("玩家在探索时点选的航道会亮起来；这是高亮色。")]
        [SerializeField] private Color routeHighlightColor = new Color(1f, 0.82f, 0.20f, 0.55f);

        [Tooltip("高亮底盘的世界宽度（比空格圈大一些，套在它外面）。")]
        [SerializeField, Min(0.01f)] private float routeHighlightSize = 0.34f;

        // 排序：底图 -10（约定）；往上依次是高亮、空格、指示物、影响力、移动城市、标签。
        private const int SortHighlight = 0;
        private const int SortSlot = 1;
        private const int SortTokenBack = 3;
        private const int SortToken = 4;
        private const int SortInfluence = 5;
        private const int SortCityBack = 9;
        private const int SortCity = 10;
        private const int SortLabel = 20;

        // 拾取优先级：数值大的优先（仅在距离相同时起作用）。
        private const int PriorityNodeBody = 0;
        private const int PriorityEmptySlot = 1;
        private const int PriorityOccupiedSlot = 3;

        private const string MarkersName = "StateMarkers";

        private GameState state;
        private Transform markersRoot;
        private IReadOnlyList<MapNodeView> nodeViews;
        private IReadOnlyList<MapRouteView> routeViews;

        private readonly List<HitTarget> targets = new List<HitTarget>();

        /// <summary>当前高亮的航道 id（探索点选路线用）；空表示没有高亮。</summary>
        private readonly HashSet<int> highlightedRoutes = new HashSet<int>();

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
            markersRoot = CreateMarkersRoot(mapRoot);
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

            for (int slot = 0; slot < node.Slots.Capacity; slot++)
            {
                Vector3 world = origin + view.SlotWorldOffset(slot);
                InfluenceSlotRef slotRef = InfluenceSlotRef.OnNode(node.Id, slot);
                int owner = node.Slots.OwnerOf(slot);

                if (owner >= 0)
                {
                    Spawn(MapSpriteFactory.Disc(), world, influenceSize,
                        MapPalette.Player(state.Player(owner).Color), SortInfluence, $"Influence_{node.Code}_{slot}");
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
                    Spawn(MapSpriteFactory.Disc(), world, influenceSize,
                        MapPalette.Player(state.Player(owner).Color), SortInfluence, $"RouteInfluence_{route.Id}_{slot}");
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
