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

    /// <summary>影响力空格挂在哪里：资源点上还是航道上。</summary>
    public enum InfluenceSlotKind
    {
        Node = 0,
        Route = 1,
    }

    /// <summary>
    /// 指向地图上某一个具体影响力空格的引用（值类型，可比较、可作字典键）。
    /// 行动（部署 / 调度 / 城市移动）都用它来指明「把影响力放到哪里」。
    /// </summary>
    public readonly struct InfluenceSlotRef : IEquatable<InfluenceSlotRef>
    {
        public readonly InfluenceSlotKind Kind;

        /// <summary>资源点 id（Kind=Node）或航道 id（Kind=Route）。</summary>
        public readonly int TargetId;

        public readonly int SlotIndex;

        public InfluenceSlotRef(InfluenceSlotKind kind, int targetId, int slotIndex)
        {
            Kind = kind;
            TargetId = targetId;
            SlotIndex = slotIndex;
        }

        public static InfluenceSlotRef OnNode(int nodeId, int slotIndex) =>
            new InfluenceSlotRef(InfluenceSlotKind.Node, nodeId, slotIndex);

        public static InfluenceSlotRef OnRoute(int routeId, int slotIndex) =>
            new InfluenceSlotRef(InfluenceSlotKind.Route, routeId, slotIndex);

        public bool Equals(InfluenceSlotRef other) =>
            Kind == other.Kind && TargetId == other.TargetId && SlotIndex == other.SlotIndex;

        public override bool Equals(object obj) => obj is InfluenceSlotRef other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397 ^ TargetId) * 397 ^ SlotIndex;

        public override string ToString() => $"{Kind}#{TargetId}·槽{SlotIndex}";
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

        /// <summary>放置指示物时翻开的事件牌 id（§4.3 步 2）；尚未放置为 -1。</summary>
        public int EventCardId = -1;

        /// <summary>这张事件牌的选项是否已经结算过（每个资源点只结算一次，避免反复领奖励）。</summary>
        public bool EventResolved;

        /// <summary>
        /// 探索（§6.4c）翻牌后、第三步「在目标资源点放置影响力」尚未执行的标记。
        /// 探索分三步走，牌翻出来之后要等玩家选完事件选项才能放影响力，中间会隔开两条命令，
        /// 靠它把「这次探索还没走完」记在资源点上，避免别处误放影响力。
        /// 回合结束时由收尾阶段作废（主要行动必须在本回合内走完）。
        /// </summary>
        public bool ExplorePending;

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
    /// 所以端点是列表而不是固定两个。整条航道的影响力空格数量由布局决定（1 格或 2 格），
    /// 但路费只计一次。
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

        public RouteState Route(int id) => Routes[id];

        /// <summary>
        /// 航道的显示名：用端点资源点编号拼出来，如「A-02–A-03–B-03」。
        /// 航道本身没有编号，日志与提示里直接打 id（「航道 4」）谁也对不上地图，
        /// 所以凡是给人看的航道名一律走这里，别各写一份。
        /// </summary>
        public string RouteLabel(int routeId)
        {
            if (routeId < 0 || routeId >= Routes.Count) return $"#{routeId}";

            RouteState route = Routes[routeId];
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < route.Nodes.Length; i++)
            {
                if (i > 0) sb.Append('–');
                sb.Append(Nodes[route.Nodes[i]].Code);
            }
            return sb.ToString();
        }

        /// <summary>取某个影响力空格引用对应的槽位集合（资源点或航道）。</summary>
        public InfluenceSlots SlotsOf(InfluenceSlotRef slot) => slot.Kind == InfluenceSlotKind.Node
            ? Nodes[slot.TargetId].Slots
            : Routes[slot.TargetId].Slots;

        /// <summary>该空格当前的所有者座位号；空位返回 -1。</summary>
        public int OccupantOf(InfluenceSlotRef slot) => SlotsOf(slot).OwnerOf(slot.SlotIndex);

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
