using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 路费计算（§6.6）—— 探索与采集共用的核心，也是最容易实现错的一块。
    ///
    /// 规则要点：
    /// - 每经过一条「没有自己影响力」的航道就要付 2 金券；有自己影响力的航道免费。
    /// - 该航道上若有对手影响力，2 金券付给该对手（引擎默认取第一个对手，玩家可另行指定）；
    ///   若没有对手影响力，则付给资源供应堆。
    /// - 引擎默认给一套「按总费用最小、同费用取跳数最少」的方案；玩家可自定义路线，
    ///   届时把自定义的那组航道交给 <see cref="Compute"/> 计费即可。
    /// </summary>
    public static class TollCalculator
    {
        /// <summary>每经过一条需要付费的航道，支付 2 金券。</summary>
        public const int CostPerRoute = 2;

        /// <summary>应付给资源供应堆时，支付对象记为 -1。</summary>
        public const int SupplyPayee = -1;

        /// <summary>一笔路费：付给某条航道上的某个对象。</summary>
        public readonly struct Payment
        {
            public readonly int RouteId;
            public readonly int Amount;

            /// <summary>收款方座位号；<see cref="SupplyPayee"/> 表示付给公共供应堆。</summary>
            public readonly int PayeeSeat;

            public Payment(int routeId, int amount, int payeeSeat)
            {
                RouteId = routeId;
                Amount = amount;
                PayeeSeat = payeeSeat;
            }
        }

        /// <summary>一次路费的完整拆解。</summary>
        public readonly struct Toll
        {
            public readonly IReadOnlyList<int> RouteIds;
            public readonly int Total;
            public readonly IReadOnlyList<Payment> Payments;

            public Toll(IReadOnlyList<int> routeIds, int total, IReadOnlyList<Payment> payments)
            {
                RouteIds = routeIds;
                Total = total;
                Payments = payments;
            }
        }

        /// <summary>这条航道上是否已有该玩家的影响力（有则免费）。</summary>
        public static bool RouteHasOwnInfluence(GameState state, int routeId, int seat) =>
            state.Map.Route(routeId).Slots.CountOf(seat) > 0;

        /// <summary>
        /// 经过这条航道的费用：公路（企业扩展「开辟商路」）免路费；有自己影响力也免费；否则 2。
        /// </summary>
        public static int RouteCost(GameState state, int seat, int routeId)
        {
            RouteState route = state.Map.Route(routeId);
            if (route.IsHighway) return 0;
            return route.Slots.CountOf(seat) > 0 ? 0 : CostPerRoute;
        }

        /// <summary>该航道上所有「对手」的座位号（按槽位顺序，去重）。玩家要在其中选一位支付（§6.6）。</summary>
        public static IReadOnlyList<int> OpponentsOnRoute(GameState state, int routeId, int seat)
        {
            var result = new List<int>();
            InfluenceSlots slots = state.Map.Route(routeId).Slots;

            for (int i = 0; i < slots.Capacity; i++)
            {
                int owner = slots.OwnerOf(i);
                if (owner < 0 || owner == seat) continue;
                if (!result.Contains(owner)) result.Add(owner);
            }

            return result;
        }

        /// <summary>某位玩家是不是该航道上的对手之一（校验「付给谁」是否合法）。</summary>
        public static bool IsOpponentOnRoute(GameState state, int routeId, int seat, int candidate)
        {
            if (candidate == seat) return false;

            IReadOnlyList<int> opponents = OpponentsOnRoute(state, routeId, seat);
            for (int i = 0; i < opponents.Count; i++)
            {
                if (opponents[i] == candidate) return true;
            }
            return false;
        }

        /// <summary>
        /// 按给定的一组航道结算路费（同一航道只计一次，调用方保证不重复）。
        /// <paramref name="payees"/> 是玩家对「付给谁」的选择（航道 id → 座位号）；
        /// 缺项或选得不合法时退回该航道上的第一位对手。
        /// </summary>
        public static Toll Compute(GameState state, int seat, IReadOnlyList<int> routeIds,
            IReadOnlyDictionary<int, int> payees = null)
        {
            var payments = new List<Payment>();
            int total = 0;

            foreach (int routeId in routeIds)
            {
                int cost = RouteCost(state, seat, routeId);
                if (cost == 0) continue;

                int payee = ResolvePayee(state, routeId, seat, payees);
                payments.Add(new Payment(routeId, cost, payee));
                total += cost;
            }

            return new Toll(routeIds, total, payments);
        }

        /// <summary>这条航道的收款方：优先用玩家指定且确实在该航道上的对手，否则取第一位对手。</summary>
        public static int ResolvePayee(GameState state, int routeId, int seat, IReadOnlyDictionary<int, int> payees)
        {
            if (payees != null && payees.TryGetValue(routeId, out int chosen)
                && IsOpponentOnRoute(state, routeId, seat, chosen))
            {
                return chosen;
            }

            return ActionRules.FirstOpponentOnRoute(state, routeId, seat);
        }

        /// <summary>去掉重复的航道 id —— 同一条航道只计一次路费（§6.6）。</summary>
        public static List<int> DistinctRoutes(IReadOnlyList<int> routeIds)
        {
            var result = new List<int>();
            if (routeIds == null) return result;

            foreach (int routeId in routeIds)
            {
                if (!result.Contains(routeId)) result.Add(routeId);
            }
            return result;
        }

        /// <summary>
        /// 这组航道能不能把 fromNode 与 toNode 连起来（§6.6 步 1：玩家自定义路线时用它判定合法性）。
        /// 只要求连通，不要求是最短路 —— 玩家多绕几跳只是多付钱。
        /// </summary>
        public static bool RoutesConnect(MapState map, IReadOnlyList<int> routeIds, int fromNode, int toNode)
        {
            if (fromNode == toNode) return true;
            if (routeIds == null || routeIds.Count == 0) return false;

            int count = map.Nodes.Count;
            var visited = new bool[count];
            var queue = new Queue<int>();

            visited[fromNode] = true;
            queue.Enqueue(fromNode);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                foreach (int routeId in routeIds)
                {
                    if (routeId < 0 || routeId >= map.Routes.Count) continue;

                    RouteState route = map.Route(routeId);
                    if (!route.Connects(current)) continue;

                    foreach (int next in route.Others(current))
                    {
                        if (visited[next]) continue;
                        visited[next] = true;
                        if (next == toNode) return true;
                        queue.Enqueue(next);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 默认方案：求从 fromNode 到 toNode 最省钱的航道组合（总费用最小，同费用取跳数最少）。
        /// 到不了返回 null。自己影响力覆盖的航道视为 0 费用，因此会被优先走。
        /// </summary>
        public static IReadOnlyList<int> FindCheapestPath(GameState state, int seat, int fromNode, int toNode)
        {
            MapState map = state.Map;
            int count = map.Nodes.Count;

            var cost = new int[count];
            var hops = new int[count];
            var prevNode = new int[count];
            var prevRoute = new int[count];
            var visited = new bool[count];

            for (int i = 0; i < count; i++)
            {
                cost[i] = int.MaxValue;
                hops[i] = int.MaxValue;
                prevNode[i] = -1;
                prevRoute[i] = -1;
            }

            cost[fromNode] = 0;
            hops[fromNode] = 0;

            while (true)
            {
                int u = -1;
                for (int i = 0; i < count; i++)
                {
                    if (visited[i] || cost[i] == int.MaxValue) continue;
                    if (u < 0 || cost[i] < cost[u] || (cost[i] == cost[u] && hops[i] < hops[u])) u = i;
                }
                if (u < 0) break;

                visited[u] = true;
                if (u == toNode) break;

                foreach (RouteState route in map.Routes)
                {
                    if (!route.Connects(u)) continue;
                    int step = RouteCost(state, seat, route.Id);

                    foreach (int v in route.Others(u))
                    {
                        if (visited[v]) continue;
                        int nextCost = cost[u] + step;
                        int nextHops = hops[u] + 1;
                        if (nextCost < cost[v] || (nextCost == cost[v] && nextHops < hops[v]))
                        {
                            cost[v] = nextCost;
                            hops[v] = nextHops;
                            prevNode[v] = u;
                            prevRoute[v] = route.Id;
                        }
                    }
                }
            }

            if (cost[toNode] == int.MaxValue) return null;

            var path = new List<int>();
            for (int node = toNode; node != fromNode; node = prevNode[node])
            {
                path.Add(prevRoute[node]);
            }
            path.Reverse();
            return path;
        }
    }
}