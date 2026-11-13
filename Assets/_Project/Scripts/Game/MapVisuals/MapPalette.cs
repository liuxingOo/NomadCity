using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>
    /// 地图可视化的统一配色。所有颜色集中在这里，避免散落到各处硬编码。
    /// 玩家色与 <see cref="PlayerColor"/> 枚举同序（绿 / 黄 / 红 / 蓝）。
    /// </summary>
    public static class MapPalette
    {
        public static Color Player(PlayerColor color) => color switch
        {
            PlayerColor.Green => new Color(0.22f, 0.72f, 0.32f),
            PlayerColor.Yellow => new Color(0.95f, 0.78f, 0.15f),
            PlayerColor.Red => new Color(0.85f, 0.26f, 0.24f),
            _ => new Color(0.26f, 0.52f, 0.88f), // Blue
        };

        /// <summary>与 <see cref="MapNodeView"/> 的 Gizmos 配色保持一致，便于对着底图核对。</summary>
        public static Color Node(NodeColor color) => color switch
        {
            NodeColor.Green => new Color(0.20f, 0.90f, 0.20f),
            NodeColor.Yellow => new Color(1.00f, 0.85f, 0.10f),
            _ => new Color(1.00f, 0.25f, 0.25f),
        };

        public static Color Resource(ResourceType type) => type switch
        {
            ResourceType.Orirock => new Color(0.62f, 0.55f, 0.40f),   // 源岩：褐
            ResourceType.Originium => new Color(0.55f, 0.35f, 0.75f), // 源石：紫
            ResourceType.Oriron => new Color(0.45f, 0.62f, 0.70f),    // 异铁：青灰
            _ => new Color(0.90f, 0.86f, 0.96f),                      // 至纯源石：浅紫白
        };

        /// <summary>空影响力空格：半透明灰环，仅作可点击落点的提示。</summary>
        public static readonly Color EmptySlot = new Color(0.86f, 0.87f, 0.90f, 0.50f);

        /// <summary>描边白（移动城市、数量 2 的资源点指示物用它垫底）。</summary>
        public static readonly Color Outline = new Color(1f, 1f, 1f, 0.95f);

        /// <summary>资源点编号标签。</summary>
        public static readonly Color Label = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>企业扩展「公路」：盖住整条航道影响力格的暖褐色。</summary>
        public static readonly Color Highway = new Color(0.72f, 0.55f, 0.32f, 0.92f);
    }
}
