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

        /// <summary>经过这条航道的费用：有自己影响力为 0，否则 2。</summary>
        public static int RouteCost(GameState state, int seat, int routeId) =>
            RouteHasOwnInfluence(state, routeId, seat) ? 0 : CostPerRoute;

        /// <summary>按给定的一组航道结算路费（同一航道只计一次，调用方保证不重复）。</summary>
        public static Toll Compute(GameState state, int seat, IReadOnlyList<int> routeIds)
        {
            var payments = new List<Payment>();
            int total = 0;

            foreach (int routeId in routeIds)
            {
                int cost = RouteCost(state, seat, routeId);
                if (cost == 0) continue;

                int payee = ActionRules.FirstOpponentOnRoute(state, routeId, seat);
                payments.Add(new Payment(routeId, cost, payee));
                total += cost;
            }

            return new Toll(routeIds, total, payments);
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