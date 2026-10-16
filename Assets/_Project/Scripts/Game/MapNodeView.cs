using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>
    /// 地图 Prefab 里的一个资源点。把这张图的元数据直接摆在场景里（所见即所得），
    /// 运行时由 MapView 收集，构建出 Core 的 MapState。
    /// 位置就是它自己的 Transform，不需要另填坐标。
    /// </summary>
    public sealed class MapNodeView : MonoBehaviour
    {
        [Tooltip("图上编号，如 G-01")]
        [SerializeField] private string code = "A-01";

        [SerializeField] private District district = District.A;

        [Tooltip("资源点颜色，决定探索时翻哪种颜色的事件牌")]
        [SerializeField] private NodeColor color = NodeColor.Green;

        [Tooltip("红色区域：开放回合受人数变体控制（规则规格 10.3）")]
        [SerializeField] private bool isRedZone;

        [Tooltip("该资源点上的影响力空格数量")]
        [SerializeField, Min(0)] private int influenceCapacity = 2;

        public string Code => code;
        public District District => district;
        public NodeColor Color => color;
        public bool IsRedZone => isRedZone;
        public int InfluenceCapacity => influenceCapacity;

        /// <summary>
        /// 第 index 个影响力空格相对本节点原点的**世界空间偏移**（不是局部坐标）。
        /// 用世界空间是刻意的：底图上影响力空格画在资源点左侧的竖列里，
        /// 地图 Prefab 整体缩放（如四人面 0.2）不该让标记跟着缩到看不见；
        /// 渲染层与 OnDrawGizmos 共用这一份算式，保证所见即所得。
        /// </summary>
        public Vector3 SlotWorldOffset(int index) =>
            new Vector3(-0.40f, 0.22f * ((influenceCapacity - 1) * 0.5f - index), 0f);

#if UNITY_EDITOR
        /// <summary>
        /// 只在编辑器里生效：在 Scene 视图里把填进去的颜色、影响力容量画出来，
        /// 方便对着底图逐点核对有没有点错。Gizmos 用世界空间尺寸，改 Prefab 缩放不影响它。
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = color switch
            {
                NodeColor.Green => new Color(0.20f, 0.90f, 0.20f),
                NodeColor.Yellow => new Color(1.00f, 0.85f, 0.10f),
                _ => new Color(1.00f, 0.25f, 0.25f),
            };
            Gizmos.DrawWireSphere(transform.position, 0.25f);

            // 圆点个数 = Influence Capacity。底图上影响力空格画在资源点左侧的竖列里，这里跟着竖排。
            for (int i = 0; i < influenceCapacity; i++)
            {
                Gizmos.DrawSphere(transform.position + SlotWorldOffset(i), 0.07f);
            }
        }
#endif
    }
}
