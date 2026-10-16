using System.Collections.Generic;
using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>
    /// 地图 Prefab 里的一条航道。
    /// 地图上有一条航道同时连到多个资源点的情况（比如中间那条 R 航道连到 4 个点），
    /// 所以端点是列表：把这条航道连到的资源点全部拖进来（至少 2 个）。
    /// 整条航道的影响力空格数量由布局决定（1 格或 2 格），但路费只计一次。
    /// 运行时由 MapView 解析成 MapState 里的节点编号。
    /// </summary>
    public sealed class MapRouteView : MonoBehaviour
    {
        [Tooltip("这条航道连到的全部资源点，至少 2 个")]
        [SerializeField] private List<MapNodeView> ends = new();

        [SerializeField] private District district = District.A;

        [Tooltip("该航道上的影响力空格数量")]
        [SerializeField, Min(0)] private int influenceCapacity = 1;

        public IReadOnlyList<MapNodeView> Ends => ends;
        public District District => district;
        public int InfluenceCapacity => influenceCapacity;

        /// <summary>
        /// 第 index 个影响力空格相对本航道物体原点的**世界空间偏移**（不是局部坐标）。
        /// 与 <see cref="MapNodeView.SlotWorldOffset"/> 同理：地图 Prefab 整体缩放不该影响标记大小，
        /// 渲染层与 OnDrawGizmos 共用这一份算式。
        /// </summary>
        public Vector3 SlotWorldOffset(int index) =>
            new Vector3(0.22f * (index - (influenceCapacity - 1) * 0.5f), 0f, 0f);

#if UNITY_EDITOR
        /// <summary>
        /// 只在编辑器里生效：从航道物体的位置向每个端点画一条线，并在物体位置画出影响力容量。
        /// 用来核对这条航道连到了哪几个资源点、有没有漏连或连错、容量对不对。
        /// Gizmos 用世界空间尺寸，改 Prefab 缩放不影响它。
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.30f, 0.80f, 1f, 0.6f);
            foreach (MapNodeView end in ends)
            {
                if (end != null) Gizmos.DrawLine(transform.position, end.transform.position);
            }

            Gizmos.color = new Color(0.30f, 0.80f, 1f);
            for (int i = 0; i < influenceCapacity; i++)
            {
                Gizmos.DrawSphere(transform.position + SlotWorldOffset(i), 0.07f);
            }
        }
#endif
    }
}
