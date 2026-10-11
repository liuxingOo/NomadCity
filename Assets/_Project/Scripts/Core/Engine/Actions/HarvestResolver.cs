using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 采集阶段（§7）。两轮玩家行动结束后，每位玩家**同时**从资源点获取资源。
    ///
    /// 与探索的关键差异（必须分别实现，不能共用一套结算）：
    /// - 航道计费次数：采集时**每条航道只计一次**，即使被多条路径复用；
    /// - 可用资金：**不能使用本阶段获得的「金券」**付路费（本阶段收到的金券要到下阶段才算自己的）；
    /// - 目标数量：任意数量资源点（探索只针对 1 个点）。
    ///
    /// 「不能用本阶段收到的金券」靠**先算后付**实现：先把每个座位的计划一次性算完，
    /// 此时谁都没付款，各人的预算就是本阶段可动用的上限；之后再统一付款、发资源。
    /// 付款对象只能来自路费结算（影响力所有者），因此付款不会改变已算好的计划。
    ///
    /// 采集不是「轮到某人」的主要行动（没有 ActiveSeat、不占用行动名额），
    /// 所以不像部署／调度那样走单条命令通道，而是像 <see cref="TurnFlow"/> 一样直接作用于 state。
    /// </summary>
    public static class HarvestResolver
    {
        /// <summary>某个座位上、本阶段可以选择的资源点：有自己影响力，或自己的移动城市停靠在此（§7 步 1）。</summary>
        public static IReadOnlyList<int> EligibleNodes(GameState state, int seat)
        {
            var list = new List<int>();
            foreach (MapNodeState node in state.Map.Nodes)
            {
                if (IsEligible(state, seat, node)) list.Add(node.Id);
            }
            return list;
        }

        /// <summary>该资源点是否满足「有自己影响力或自己移动城市停靠」。</summary>
        public static bool IsEligible(GameState state, int seat, MapNodeState node) =>
            node.Slots.CountOf(seat) > 0 || state.Player(seat).MobileCityNodeId == node.Id;

        /// <summary>
        /// 算出一名玩家在采集阶段的计划（只读，不改 state）：能采到哪些点、要付多少路费。
        /// 传入的 chosenNodeIds 是玩家选的资源点（可含重复与非法项，会去重并剔除）。
        /// 顺序即玩家的取舍顺序：一旦把某点并入后总路费超过预算，该点及之后付不起的点都会被跳过。
        /// 供 UI 预览花费，也被 <see cref="Resolve"/> 复用。
        /// </summary>
        public static HarvestPlan Plan(GameState state, int seat, IReadOnlyList<int> chosenNodeIds)
        {
            PlayerState player = state.Player(seat);
            int budget = player.Vouchers;      // 本阶段可动用的上限（结算前手里已有的金券）
            int city = player.MobileCityNodeId;

            var accepted = new List<int>();
            var skipped = new List<int>();
            var routeSet = new HashSet<int>(); // 本次采集要经过的全部航道，同一航道只记一次
            var seen = new HashSet<int>();

            if (chosenNodeIds != null)
            {
                foreach (int nodeId in chosenNodeIds)
                {
                    if (!seen.Add(nodeId)) continue; // 重复选择：每个资源点每次只算一次
                    if (nodeId < 0 || nodeId >= state.Map.Nodes.Count) { skipped.Add(nodeId); continue; }

                    MapNodeState node = state.Map.Node(nodeId);
                    if (city < 0 || !IsEligible(state, seat, node) || !node.HasResourceToken || node.TokenAmount <= 0)
                    {
                        skipped.Add(nodeId);
                        continue;
                    }

                    IReadOnlyList<int> path = TollCalculator.FindCheapestPath(state, seat, city, nodeId);
                    if (path == null) { skipped.Add(nodeId); continue; } // 到不了

                    var trial = new HashSet<int>(routeSet);
                    foreach (int routeId in path) trial.Add(routeId);

                    if (TollCalculator.Compute(state, seat, Sorted(trial)).Total > budget)
                    {
                        skipped.Add(nodeId); // 付不起，拿不到这个点的资源
                        continue;
                    }

                    routeSet = trial;
                    accepted.Add(nodeId);
                }
            }

            TollCalculator.Toll toll = TollCalculator.Compute(state, seat, Sorted(routeSet));
            return new HarvestPlan(seat, accepted, skipped, toll, budget);
        }

        /// <summary>
        /// 结算整个采集阶段。chosenBySeat 是每个座位选择的资源点（座位号 → 资源点 id 列表）。
        /// 先为所有座位算出计划，再统一付款、发资源，保证「同时」与「不能用本阶段收到的金券付路费」。
        /// </summary>
        public static HarvestReport Resolve(GameState state, IReadOnlyDictionary<int, IReadOnlyList<int>> chosenBySeat)
        {
            if (state == null) return HarvestReport.Fail("引擎没有游戏状态。");
            if (state.Phase != TurnPhase.Harvest) return HarvestReport.Fail("当前不是采集阶段。");

            int count = state.Players.Count;
            var plans = new HarvestPlan[count];

            // 第一步：先算完所有人的计划 —— 此刻谁都没付款，预算天然不含本阶段将要收到的金券。
            for (int seat = 0; seat < count; seat++)
            {
                IReadOnlyList<int> chosen = null;
                chosenBySeat?.TryGetValue(seat, out chosen);
                plans[seat] = Plan(state, seat, chosen);
            }

            // 第二步：统一付款（付给供应堆或对手）并发资源。
            var results = new List<HarvestResult>(count);
            for (int seat = 0; seat < count; seat++)
            {
                HarvestPlan plan = plans[seat];
                PlayerState player = state.Player(seat);

                foreach (TollCalculator.Payment payment in plan.Toll.Payments)
                {
                    player.Vouchers -= payment.Amount;
                    if (payment.PayeeSeat == TollCalculator.SupplyPayee) state.SupplyVouchers += payment.Amount;
                    else state.Player(payment.PayeeSeat).Vouchers += payment.Amount;
                }

                foreach (int nodeId in plan.Nodes)
                {
                    MapNodeState node = state.Map.Node(nodeId);
                    player.Resources.Add(node.TokenResource, node.TokenAmount);
                }

                results.Add(new HarvestResult(seat, plan));
            }

            return new HarvestReport(results);
        }

        /// <summary>把航道集合转成升序列表，让路费拆解（付款顺序）可复现。</summary>
        private static List<int> Sorted(HashSet<int> routeIds)
        {
            var list = new List<int>(routeIds);
            list.Sort();
            return list;
        }
    }

    /// <summary>一名玩家的采集计划（§7）。只读。</summary>
    public sealed class HarvestPlan
    {
        public readonly int Seat;

        /// <summary>实际能采集到的资源点（按玩家选择的顺序）。</summary>
        public readonly IReadOnlyList<int> Nodes;

        /// <summary>选了但拿不到的资源点：没资格／没有指示物／到不了／付不起。</summary>
        public readonly IReadOnlyList<int> SkippedNodes;

        /// <summary>连接这些点要付的路费（同一航道只计一次）。</summary>
        public readonly TollCalculator.Toll Toll;

        /// <summary>结算前该玩家手里的金券，即本阶段可动用的上限。</summary>
        public readonly int Budget;

        internal HarvestPlan(int seat, IReadOnlyList<int> nodes, IReadOnlyList<int> skippedNodes,
            TollCalculator.Toll toll, int budget)
        {
            Seat = seat;
            Nodes = nodes;
            SkippedNodes = skippedNodes;
            Toll = toll;
            Budget = budget;
        }
    }

    /// <summary>一名玩家的采集结果。</summary>
    public sealed class HarvestResult
    {
        public readonly int Seat;

        /// <summary>采集到的资源点。</summary>
        public readonly IReadOnlyList<int> Nodes;

        /// <summary>选了但没拿到的资源点。</summary>
        public readonly IReadOnlyList<int> SkippedNodes;

        /// <summary>本阶段支付的路费总额。</summary>
        public readonly int VouchersPaid;

        /// <summary>路费拆解（付给谁、各多少）。</summary>
        public readonly TollCalculator.Toll Toll;

        internal HarvestResult(int seat, HarvestPlan plan)
        {
            Seat = seat;
            Nodes = plan.Nodes;
            SkippedNodes = plan.SkippedNodes;
            VouchersPaid = plan.Toll.Total;
            Toll = plan.Toll;
        }
    }

    /// <summary>整个采集阶段的结算报告：Ok=false 表示这次调用根本没进行（例如阶段不对）。</summary>
    public sealed class HarvestReport
    {
        public readonly bool Ok;
        public readonly string Error;

        /// <summary>每名玩家一条结果，下标即座位号；Ok=false 时为空。</summary>
        public readonly IReadOnlyList<HarvestResult> Results;

        private HarvestReport(bool ok, string error, IReadOnlyList<HarvestResult> results)
        {
            Ok = ok;
            Error = error;
            Results = results;
        }

        internal HarvestReport(IReadOnlyList<HarvestResult> results) : this(true, null, results) { }

        public static HarvestReport Fail(string reason) => new HarvestReport(false, reason, new List<HarvestResult>());
    }
}