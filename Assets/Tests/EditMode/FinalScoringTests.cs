using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 终局结算（§9）的单元测试：区块控制分、剩余资源分、平局判定。
    /// 测试地图含 A、B、E、R 四个区块，每个资源点／航道影响力容量 4。
    /// </summary>
    public sealed class FinalScoringTests
    {
        private static GameState ScoringState(int playerCount = 4)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 4));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 4));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Green, false, 4));
            map.Nodes.Add(new MapNodeState(3, "E-01", District.E, NodeColor.Red, true, 4));
            map.Nodes.Add(new MapNodeState(4, "R-01", District.R, NodeColor.Green, false, 4));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 2));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 2));
            map.Routes.Add(new RouteState(2, new[] { 3, 4 }, District.E, 2));

            var state = new GameState(new GameSetup(playerCount)) { Phase = TurnPhase.Cleanup };
            for (int seat = 0; seat < playerCount; seat++)
            {
                state.Players.Add(new PlayerState(seat, (PlayerColor)seat) { MarkersInSupply = 30 });
            }
            state.Map = map;
            return state;
        }

        private static DistrictControl Control(GameState state, District district)
        {
            foreach (DistrictControl control in FinalScoring.EvaluateDistricts(state))
            {
                if (control.District == district) return control;
            }
            Assert.Fail($"没有找到区块 {district}");
            return default;
        }

        // ————————————————————— 区块控制 —————————————————————

        [Test]
        public void 结算_影响力最多者控制区块并得该区块的分()
        {
            GameState state = ScoringState();
            state.Map.Node(0).Slots.Place(0, 0);  // 座位 0
            state.Map.Route(0).Slots.Place(0, 0); // 座位 0 → A 区共 2
            state.Map.Node(1).Slots.Place(0, 1);  // 座位 1 → A 区共 1

            DistrictControl a = Control(state, District.A);

            Assert.AreEqual(2, a.Influence[0]);
            Assert.AreEqual(1, a.Influence[1]);
            Assert.AreEqual(0, a.Controller);
            Assert.AreEqual(3, a.Score);
        }

        [Test]
        public void 结算_区块内并列最多_双方都不拿分()
        {
            GameState state = ScoringState();
            state.Map.Node(0).Slots.Place(0, 0);
            state.Map.Node(1).Slots.Place(0, 1);

            DistrictControl a = Control(state, District.A);

            Assert.AreEqual(-1, a.Controller);
        }

        [Test]
        public void 结算_区块内无人有影响力_无人控制()
        {
            GameState state = ScoringState();
            Assert.AreEqual(-1, Control(state, District.B).Controller);
        }

        [Test]
        public void 结算_区块内的移动城市视为2点影响力()
        {
            GameState state = ScoringState();
            state.Players[0].MobileCityNodeId = 0; // 城市停在 A 区的 N0
            state.Map.Node(0).Slots.Place(0, 0);   // 自己在该点还有 1 个影响力
            state.Map.Node(1).Slots.Place(0, 1);   // 座位 1 有 1 个

            DistrictControl a = Control(state, District.A);

            Assert.AreEqual(3, a.Influence[0]); // 1 影响力 + 城市 2
            Assert.AreEqual(0, a.Controller);
        }

        [Test]
        public void 结算_区块分可被地图数据覆盖()
        {
            GameState state = ScoringState();
            state.DistrictScores[(int)District.A] = 7;
            state.Map.Node(0).Slots.Place(0, 0);

            Assert.AreEqual(7, Control(state, District.A).Score);
        }

        // ————————————————————— 剩余资源分 —————————————————————

        [Test]
        public void 结算_剩余资源分按兑换比向下取整()
        {
            var player = new PlayerState(0, (PlayerColor)0);
            player.Resources.Add(ResourceType.Orirock, 19);        // 19 / 9 = 2
            player.Resources.Add(ResourceType.Originium, 8);       // 8 / 8 = 1
            player.Resources.Add(ResourceType.Oriron, 5);          // 5 / 6 = 0
            player.Resources.Add(ResourceType.OriginitePrime, 3);  // 3 / 1 = 3

            Assert.AreEqual(6, FinalScoring.RemainingResourceScore(player));
        }

        // ————————————————————— 总结算 —————————————————————

        [Test]
        public void 结算_区块分与资源分一起加到分数上()
        {
            GameState state = ScoringState();
            state.Map.Node(0).Slots.Place(0, 0); // A 区 → 座位 0（3 分）
            state.Map.Node(2).Slots.Place(0, 0); // B 区 → 座位 0（3 分）
            state.Map.Node(3).Slots.Place(0, 1); // E 区 → 座位 1（4 分）
            state.Map.Node(4).Slots.Place(0, 1); // R 区 → 座位 1（3 分）

            state.Players[0].Resources.Add(ResourceType.Orirock, 9);        // +1
            state.Players[1].Resources.Add(ResourceType.OriginitePrime, 2); // +2

            FinalScoring.Settle(state);

            Assert.AreEqual(7, state.Players[0].Score); // 3 + 3 + 1
            Assert.AreEqual(9, state.Players[1].Score); // 4 + 3 + 2
        }

        // ————————————————————— 胜负与平局 —————————————————————

        [Test]
        public void 胜负_分相同比剩余金券()
        {
            GameState state = ScoringState(3);
            state.Players[0].Score = 10; state.Players[0].Vouchers = 5;
            state.Players[1].Score = 10; state.Players[1].Vouchers = 8;
            state.Players[2].Score = 5;

            CollectionAssert.AreEqual(new[] { 1 }, new List<int>(FinalScoring.Winners(state)));
        }

        [Test]
        public void 胜负_金券也相同比剩余至纯源石()
        {
            GameState state = ScoringState(3);
            state.Players[0].Score = 10; state.Players[0].Vouchers = 5;
            state.Players[0].Resources.Add(ResourceType.OriginitePrime, 3);
            state.Players[1].Score = 10; state.Players[1].Vouchers = 5;
            state.Players[1].Resources.Add(ResourceType.OriginitePrime, 1);

            CollectionAssert.AreEqual(new[] { 0 }, new List<int>(FinalScoring.Winners(state)));
        }

        [Test]
        public void 胜负_全部相同则共同胜利()
        {
            GameState state = ScoringState(3);
            state.Players[0].Score = 10; state.Players[0].Vouchers = 5;
            state.Players[0].Resources.Add(ResourceType.OriginitePrime, 2);
            state.Players[1].Score = 10; state.Players[1].Vouchers = 5;
            state.Players[1].Resources.Add(ResourceType.OriginitePrime, 2);

            CollectionAssert.AreEquivalent(new[] { 0, 1 }, new List<int>(FinalScoring.Winners(state)));
        }

        // ————————————————————— 与流程的衔接 —————————————————————

        [Test]
        public void 结算_第8回合收尾结束后进入终局并自动结算()
        {
            GameState state = ScoringState();
            state.Map.Node(0).Slots.Place(0, 0); // A 区 → 座位 0
            state.Players[0].Resources.Add(ResourceType.Orirock, 9);
            state.TurnNumber = TurnFlow.TotalTurns; // 第 8 回合
            state.Phase = TurnPhase.Cleanup;

            TurnFlow.AdvancePhase(state);

            Assert.IsTrue(state.IsGameOver);
            Assert.AreEqual(4, state.Players[0].Score); // A 区 3 + 源岩 1
        }
    }
}