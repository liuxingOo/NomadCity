using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 采集阶段（§7）的单元测试。全部用 EditMode 断言，不需要预制体。
    /// 测试地图是一条直链：N0—R0—N1—R1—N2—R2—N3，每个航道容量 1。
    /// </summary>
    public sealed class HarvestTests
    {
        private static GameState HarvestState(int playerCount = 4)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Nodes.Add(new MapNodeState(3, "C-01", District.C, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 1));
            map.Routes.Add(new RouteState(2, new[] { 2, 3 }, District.B, 1));

            var state = new GameState(new GameSetup(playerCount)) { Phase = TurnPhase.Harvest };
            for (int seat = 0; seat < playerCount; seat++)
            {
                state.Players.Add(new PlayerState(seat, (PlayerColor)seat) { MarkersInSupply = 30 });
            }

            foreach (MapNodeState node in map.Nodes)
            {
                node.HasResourceToken = true;
                node.TokenResource = ResourceType.Orirock;
                node.TokenAmount = 1;
            }

            state.Map = map;
            return state;
        }

        private static IReadOnlyDictionary<int, IReadOnlyList<int>> Choices(int seat, params int[] nodeIds) =>
            new Dictionary<int, IReadOnlyList<int>> { [seat] = nodeIds };

        private static void AssertNodes(IReadOnlyList<int> actual, params int[] expected)
        {
            CollectionAssert.AreEqual(new List<int>(expected), new List<int>(actual));
        }

        private static void OwnInfluence(GameState state, int nodeId, int seat) =>
            state.Map.Node(nodeId).Slots.Place(0, seat);

        // ————————————————————— 可选资源点 —————————————————————

        [Test]
        public void 可选点_只有自己影响力或自己城市停靠的资源点()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            OwnInfluence(state, 2, 1); // 对手的影响力不算

            AssertNodes(HarvestResolver.EligibleNodes(state, 0), 0, 1);
            AssertNodes(HarvestResolver.EligibleNodes(state, 1), 2);
        }

        // ————————————————————— 路费结算 —————————————————————

        [Test]
        public void 采集_城市就在该点上_路费为0()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 0));

            Assert.IsTrue(report.Ok, report.Error);
            AssertNodes(report.Results[0].Nodes, 0);
            Assert.AreEqual(0, report.Results[0].VouchersPaid);
            Assert.AreEqual(1, state.Players[0].Resources.Orirock);
        }

        [Test]
        public void 采集_无自己影响力的航道_付2金券()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            state.Players[0].Vouchers = 10;

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1));

            Assert.IsTrue(report.Ok, report.Error);
            Assert.AreEqual(2, report.Results[0].VouchersPaid);
            Assert.AreEqual(8, state.Players[0].Vouchers);
            Assert.AreEqual(1, state.Players[0].Resources.Orirock);
        }

        [Test]
        public void 采集_航道上已有自己影响力_免费()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            state.Map.Route(0).Slots.Place(0, 0); // 自己在 R0 上的影响力
            state.Players[0].Vouchers = 10;

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1));

            Assert.AreEqual(0, report.Results[0].VouchersPaid);
            Assert.AreEqual(10, state.Players[0].Vouchers);
        }

        [Test]
        public void 采集_航道上若有对手影响力_路费付给该对手()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            state.Map.Route(0).Slots.Place(0, 1); // 座位 1 在 R0 上的影响力
            state.Players[0].Vouchers = 10;
            state.Players[1].Vouchers = 0;

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1));

            Assert.AreEqual(2, report.Results[0].VouchersPaid);
            Assert.AreEqual(8, state.Players[0].Vouchers);
            Assert.AreEqual(2, state.Players[1].Vouchers);
        }

        [Test]
        public void 采集_同一条航道复用_只计一次路费()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            OwnInfluence(state, 2, 0);
            state.Players[0].Vouchers = 20;

            // 到 N1 要 R0，到 N2 要 R0+R1；并集 {R0,R1} 共 4，而不是 2+4=6。
            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1, 2));

            AssertNodes(report.Results[0].Nodes, 1, 2);
            Assert.AreEqual(4, report.Results[0].VouchersPaid);
            Assert.AreEqual(16, state.Players[0].Vouchers);
            Assert.AreEqual(2, state.Players[0].Resources.Orirock); // 每点 1 个，共 2 个
        }

        [Test]
        public void 采集_重复选择同一个点_只算一次()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            state.Players[0].Vouchers = 20;

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1, 1, 1));

            AssertNodes(report.Results[0].Nodes, 1);
            Assert.AreEqual(2, report.Results[0].VouchersPaid); // 只付一次
            Assert.AreEqual(1, state.Players[0].Resources.Orirock);
        }

        [Test]
        public void 采集_金券不足_该点拿不到_不扣费也不发资源()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 0);
            state.Players[0].Vouchers = 1; // 不够 2

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1));

            Assert.IsTrue(report.Ok, report.Error);
            Assert.IsEmpty(report.Results[0].Nodes);
            AssertNodes(report.Results[0].SkippedNodes, 1);
            Assert.AreEqual(1, state.Players[0].Vouchers);
            Assert.AreEqual(0, state.Players[0].Resources.Orirock);
        }

        [Test]
        public void 采集_到不了的资源点被跳过()
        {
            GameState state = HarvestState();
            // 另加一个不与任何航道相连的孤岛资源点。
            state.Map.Nodes.Add(new MapNodeState(4, "Z-01", District.R, NodeColor.Green, false, 1));
            state.Map.Node(4).HasResourceToken = true;
            state.Map.Node(4).TokenResource = ResourceType.Orirock;

            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 4, 0);
            state.Players[0].Vouchers = 10; // 钱够，但根本没有航道能过去

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 4));

            Assert.IsEmpty(report.Results[0].Nodes);
            AssertNodes(report.Results[0].SkippedNodes, 4);
            Assert.AreEqual(10, state.Players[0].Vouchers);
        }

        [Test]
        public void 采集_没有移动城市停靠时不能采集()
        {
            GameState state = HarvestState();
            state.Players[0].MobileCityNodeId = -1;
            OwnInfluence(state, 1, 0);

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 1));

            Assert.IsEmpty(report.Results[0].Nodes);
            AssertNodes(report.Results[0].SkippedNodes, 1);
        }

        [Test]
        public void 采集_指示物是2个时获得2个资源()
        {
            GameState state = HarvestState();
            state.Map.Node(0).TokenAmount = 2;
            state.Players[0].MobileCityNodeId = 0;

            HarvestResolver.Resolve(state, Choices(0, 0));

            Assert.AreEqual(2, state.Players[0].Resources.Orirock);
        }

        // ————————————————————— 同时结算 —————————————————————

        [Test]
        public void 采集_不能用本阶段收到的金券付本阶段路费()
        {
            GameState state = HarvestState();

            // 座位 0：城市在 N0，影响力在 N2，要经过 R0+R1 才能到达；手里 0 金券。
            // R0 上有座位 0 的影响力，因此座位 1 经过 R0 时要把路费付给座位 0。
            state.Players[0].MobileCityNodeId = 0;
            OwnInfluence(state, 2, 0);
            state.Map.Route(0).Slots.Place(0, 0); // 座位 0 在 R0 上
            state.Players[0].Vouchers = 0;

            // 座位 1：城市在 N0，影响力在 N1，经过 R0（要付给座位 0）拿 N1。
            state.Players[1].MobileCityNodeId = 0;
            OwnInfluence(state, 1, 1);
            state.Players[1].Vouchers = 10;

            var chosen = new Dictionary<int, IReadOnlyList<int>>
            {
                [0] = new[] { 2 },
                [1] = new[] { 1 },
            };

            HarvestReport report = HarvestResolver.Resolve(state, chosen);

            Assert.IsTrue(report.Ok, report.Error);
            // 座位 0 结算前是 0 金券，所以 R1 的 2 金券付不起，N2 拿不到。
            Assert.IsEmpty(report.Results[0].Nodes);
            AssertNodes(report.Results[0].SkippedNodes, 2);
            Assert.AreEqual(0, state.Players[0].Resources.Orirock);
            // 座位 1 的路费 2 付给了座位 0：收到的这 2 金券没有被座位 0 花掉。
            Assert.AreEqual(2, state.Players[0].Vouchers);
            // 座位 1 正常拿到 N1。
            AssertNodes(report.Results[1].Nodes, 1);
            Assert.AreEqual(8, state.Players[1].Vouchers);
            Assert.AreEqual(1, state.Players[1].Resources.Orirock);
        }

        [Test]
        public void 采集_不是采集阶段时报告失败()
        {
            GameState state = HarvestState();
            state.Phase = TurnPhase.Cleanup;

            HarvestReport report = HarvestResolver.Resolve(state, Choices(0, 0));

            Assert.IsFalse(report.Ok);
            Assert.IsEmpty(report.Results);
        }
    }
}