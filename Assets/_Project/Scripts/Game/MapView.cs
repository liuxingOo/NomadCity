using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Data;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>
    /// 场景里唯一的地图容器。开局按人数选一份 MapData，实例化对应的地图 Prefab，
    /// 再把 Prefab 里每个 MapNodeView / MapRouteView 收集成 Core 的 MapState。
    /// 之后所有规则逻辑只认 MapState，不关心用的是三人面还是四人面。
    /// </summary>
    public sealed class MapView : MonoBehaviour
    {
        [Tooltip("三人面地图（2 人、3 人局）")]
        [SerializeField] private MapData threePlayerMap;

        [Tooltip("四人面地图（4 人局）")]
        [SerializeField] private MapData fourPlayerMap;

        /// <summary>当前构建出的地图状态；未构建时为 null。</summary>
        public MapState State { get; private set; }

        /// <summary>当前使用的地图数据。</summary>
        public MapData ActiveMap { get; private set; }

        /// <summary>当前地图实例的根节点；未构建时为 null。渲染层把标记挂到它下面。</summary>
        public Transform MapRoot { get; private set; }

        /// <summary>当前地图实例里收集到的资源点视图，下标与 <see cref="MapState.Nodes"/> 的 id 一一对应。</summary>
        public IReadOnlyList<MapNodeView> NodeViews => nodeViews;

        /// <summary>当前地图实例里收集到的航道视图，下标与 <see cref="MapState.Routes"/> 的 id 一一对应。</summary>
        public IReadOnlyList<MapRouteView> RouteViews => routeViews;

        private readonly List<MapNodeView> nodeViews = new List<MapNodeView>();
        private readonly List<MapRouteView> routeViews = new List<MapRouteView>();

        private GameObject instance;

        /// <summary>按人数选地图：4 人用四人面，2 / 3 人用三人面（规则规格 13.2）。</summary>
        public MapData SelectFor(int playerCount) => playerCount >= 4 ? fourPlayerMap : threePlayerMap;

        /// <summary>实例化地图并构建 MapState。重复调用会先清掉上一套。</summary>
        public MapState Build(MapData data)
        {
            Clear();
            State = new MapState();

            if (data == null)
            {
                Debug.LogError("[地图] 没有配置对应的 MapData。", this);
                return State;
            }

            ActiveMap = data;

            if (data.MapPrefab == null)
            {
                Debug.LogError($"[地图]「{data.DisplayName}」没有指定地图 Prefab。", data);
                return State;
            }

            instance = Instantiate(data.MapPrefab, transform);
            instance.name = data.MapPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            MapRoot = instance.transform;

            // 先收集资源点，建立「View → 编号」映射，航道再据此解析两端。
            // nodeViews 的下标必须与 MapState.Nodes 的 id 严格一致，渲染层才能按下标取视图。
            var idOf = new Dictionary<MapNodeView, int>();
            foreach (MapNodeView view in instance.GetComponentsInChildren<MapNodeView>(true))
            {
                int id = State.Nodes.Count;
                idOf[view] = id;
                State.Nodes.Add(new MapNodeState(id, view.Code, view.District, view.Color, view.IsRedZone, view.InfluenceCapacity));
                nodeViews.Add(view);
            }

            foreach (MapRouteView view in instance.GetComponentsInChildren<MapRouteView>(true))
            {
                var ends = new List<int>(view.Ends.Count);
                bool ok = true;

                foreach (MapNodeView end in view.Ends)
                {
                    if (end == null || !idOf.TryGetValue(end, out int nodeId))
                    {
                        Debug.LogError($"[地图] 航道「{view.name}」有端点没有正确指定资源点，已跳过。", view);
                        ok = false;
                        break;
                    }
                    ends.Add(nodeId);
                }

                if (!ok) continue;

                if (ends.Count < 2)
                {
                    Debug.LogError($"[地图] 航道「{view.name}」连到的资源点少于 2 个，已跳过。", view);
                    continue;
                }

                int id = State.Routes.Count;
                State.Routes.Add(new RouteState(id, ends.ToArray(), view.District, view.InfluenceCapacity));
                routeViews.Add(view); // 跳过的航道不进列表，保证下标与 RouteState.Id 对齐
            }

            return State;
        }

        /// <summary>销毁当前地图实例并清空状态。</summary>
        public void Clear()
        {
            if (instance != null)
            {
                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
                instance = null;
            }

            State = null;
            ActiveMap = null;
            MapRoot = null;
            nodeViews.Clear();
            routeViews.Clear();
        }
    }
}
