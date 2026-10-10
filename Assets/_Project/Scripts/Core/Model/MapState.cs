using System;
using System.Collections.Generic;

namespace NomadCity.Core.Model
{
    /// <summary>资源点的颜色，决定探索／城市移动时翻开哪种颜色的事件牌（规则规格 7 / 10.3）。</summary>
    public enum NodeColor
    {
        Green = 0,
        Yellow = 1,
        Red = 2,
    }

    /// <summary>
    /// 一个可放影响力的位置（资源点或航道）的公共占用状态。
    /// 「影响力空格」的数量属于地图布局，由 MapData 定义；运行时谁占哪个槽记在这里。
    /// </summary>
    public sealed class InfluenceSlots
    {
        private readonly int[] owners; // 每个槽位的所有者座位号，-1 = 空

        /// <summary>空格数量（布局定义）。</summary>
        public int Capacity => owners.Length;

        public InfluenceSlots(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            owners = new int[capacity];
            for (int i = 0; i < capacity; i++) owners[i] = -1;
        }

        public int OwnerOf(int slot) => owners[slot];

        public bool IsEmpty(int slot) => owners[slot] < 0;

        public bool HasFreeSlot => Array.IndexOf(owners, -1) >= 0;

        public void Place(int slot, int ownerSeat) => owners[slot] = ownerSeat;

        public void Clear(int slot) => owners[slot] = -1;

        /// <summary>该座位在此位置上的影响力数量。</summary>
        public int CountOf(int seat)
        {
            int count = 0;
            foreach (int owner in owners) if (owner == seat) count++;
            return count;
        }
    }

    /// <summary>资源点的运行时状态。静态属性（编号、颜色、空格数）在建图时确定，之后不变。</summary>
    public sealed class MapNodeState
    {
        public readonly int Id;          // 布局内的唯一编号
        public readonly string Code;     // 图上编号，如 "G-01"
        public readonly District District;
        public readonly NodeColor Color; // 决定事件牌颜色
        public readonly bool IsRedZone;  // 红色区域：开放回合受人数变体控制
        public readonly InfluenceSlots Slots;

        /// <summary>是否已放置资源点指示物。未放置时不能被「部署」占领。</summary>
        public bool HasResourceToken;

        /// <summary>指示物提供的资源种类（放置指示物后有效）。</summary>
        public ResourceType TokenResource;

        /// <summary>指示物提供的数量：1 或 2（红色区域的「2」标记，规则规格 10.3）。</summary>
        public int TokenAmount = 1;

        public MapNodeState(int id, string code, District district, NodeColor color, bool isRedZone, int influenceCapacity)
        {
            Id = id;
            Code = code;
            District = district;
            Color = color;
            IsRedZone = isRedZone;
            Slots = new InfluenceSlots(influenceCapacity);
        }
    }

    /// <summary>
    /// 航道的运行时状态。属于某个区块。
    /// 地图上存在一条航线同时连到多个资源点的情况（例如地图中间那条 R 航道连了 4 个点），
    /// 所以端点是列表而不是固定两个。整条航道只有一份影响力空格，也只计一次路费。
    /// </summary>
    public sealed class RouteState
    {
        public readonly int Id;

        /// <summary>该航道连接到的全部资源点，至少 2 个。</summary>
        public readonly int[] Nodes;

        public readonly District District;
        public readonly InfluenceSlots Slots;

        public RouteState(int id, int[] nodes, District district, int influenceCapacity)
        {
            if (nodes == null || nodes.Length < 2)
                throw new ArgumentException("一条航道至少要连接两个资源点。", nameof(nodes));

            Id = id;
            Nodes = nodes;
            District = district;
            Slots = new InfluenceSlots(influenceCapacity);
        }

        /// <summary>这条航道是否连到了该资源点。</summary>
        public bool Connects(int nodeId)
        {
            foreach (int node in Nodes) if (node == nodeId) return true;
            return false;
        }

        /// <summary>在同一条航道上的其他资源点（也就是与 nodeId 相邻的点）。</summary>
        public IEnumerable<int> Others(int nodeId)
        {
            foreach (int node in Nodes) if (node != nodeId) yield return node;
        }
    }

    /// <summary>
    /// 地图的运行时状态：资源点指示物、影响力占用。
    /// 静态布局（世界坐标、邻接、空格数量、底图）来自 Data 层的 MapData。
    /// 移动城市的位置记在 PlayerState 上，此处不重复保存。
    /// </summary>
    public sealed class MapState
    {
        public readonly List<MapNodeState> Nodes = new();
        public readonly List<RouteState> Routes = new();

        public MapNodeState Node(int id) => Nodes[id];

        public MapNodeState FindNode(string code)
        {
            foreach (var node in Nodes) if (node.Code == code) return node;
            return null;
        }

        /// <summary>找到同时连到这两个资源点的那条航道。两个点可能在同一条多端点航道上。</summary>
        public RouteState FindRoute(int nodeA, int nodeB)
        {
            foreach (var route in Routes)
            {
                if (route.Connects(nodeA) && route.Connects(nodeB)) return route;
            }
            return null;
        }

        /// <summary>与某资源点仅用一条航道相连的资源点（城市移动的「相邻」判定）。</summary>
        public IEnumerable<int> NeighboursOf(int nodeId)
        {
            foreach (var route in Routes)
            {
                if (!route.Connects(nodeId)) continue;
                foreach (int other in route.Others(nodeId)) yield return other;
            }
        }
    }
}
