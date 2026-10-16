using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>点击命中的地图元素类型。</summary>
    public enum MapHitKind
    {
        /// <summary>资源点上的影响力空格（含已被占据的）。</summary>
        NodeSlot = 0,

        /// <summary>航道上的影响力空格（含已被占据的）。</summary>
        RouteSlot = 1,

        /// <summary>资源点本体（城市移动的选点目标）。</summary>
        NodeBody = 2,
    }

    /// <summary>一次地图点击的命中结果。</summary>
    public readonly struct MapHit
    {
        public readonly MapHitKind Kind;

        /// <summary>Kind 为 NodeSlot / RouteSlot 时有效。</summary>
        public readonly InfluenceSlotRef Slot;

        /// <summary>Kind 为 NodeBody 时有效（资源点 id）。</summary>
        public readonly int NodeId;

        /// <summary>命中点的世界坐标。</summary>
        public readonly Vector2 World;

        public MapHit(MapHitKind kind, InfluenceSlotRef slot, int nodeId, Vector2 world)
        {
            Kind = kind;
            Slot = slot;
            NodeId = nodeId;
            World = world;
        }

        public bool IsSlot => Kind != MapHitKind.NodeBody;

        public override string ToString() => Kind == MapHitKind.NodeBody
            ? $"资源点#{NodeId}"
            : $"{Kind}({Slot})";
    }
}
