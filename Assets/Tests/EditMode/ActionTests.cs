using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// ③ 不依赖卡面的主要行动：部署 / 调度 / 城市移动 / 路费。
    /// 全部用 EditMode 断言 state 变化，不需要任何预制体或美术。
    /// </summary>
    public sealed class ActionTests
    {
        // 测试地图：N0—R0—N1—R1—N2—R2—N3（N3 属于红色区域）
        private static GameEngine NewActionGame(int playerCount = 4, int firstSeat = 0)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Nodes.Add(new MapNodeState(3, "F-01", District.F, NodeColor.Red, true, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 1));
            map.Routes.Add(new RouteState(2, new[] { 2, 3 }, District.F, 2));

            var engine = GameEngine.Create(new GameSetup(playerCount), firstSeat, map,
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(firstSeat)); // 回合 1，盖放阶段
            engine.Dispatch(new AdvancePhaseCommand());       // → 玩家行动阶段
            return engine;
        }

        private static readonly InfluenceSlotRef Route0Slot0 = InfluenceSlotRef.OnRoute(0, 0);
        private static readonly InfluenceSlotRef Route1Slot0 = InfluenceSlotRef.OnRoute(1, 0);
        private static readonly InfluenceSlotRef Route2Slot0 = InfluenceSlotRef.OnRoute(2, 0);
        private static readonly InfluenceSlotRef Node0Slot0 = InfluenceSlotRef.OnNode(0, 0);

        // ————————————————————— 部署 —————————————————————

        [Test]
        public void 部署_放在空航道上_消耗1个标记并推进到下一顺位()
        {
            GameEngine engine = NewActionGame(4, 0);
            GameState state = engine.State;
            int markers = state.Players[0].MarkersInSupply;

            CommandResult result = engine.Dispatch(new PlaceInfluenceCommand(0, Route0Slot0));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, state.Map.Route(0).Slots.OwnerOf(0));
            Assert.AreEqual(markers - 1, state.Players[0].MarkersInSupply);
            Assert.AreEqual(1, state.ActiveSeat);
        }

        [Test]
        public void 部署_资源点没有指示物时非法()
        {
            GameEngine engine = NewActionGame();
            CommandResult result = engine.Dispatch(new PlaceInfluenceCommand(0, Node0Slot0));
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 部署_资源点有指示物且无对手城市时合法()
        {
            GameEngine engine = NewActionGame();
            engine.State.Map.Node(0).HasResourceToken = true;

            CommandResult result = engine.Dispatch(new PlaceInfluenceCommand(0, Node0Slot0));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, engine.State.Map.Node(0).Slots.OwnerOf(0));
        }

        [Test]
        public void 部署_对手城市停靠在资源点上时非法()
        {
            GameEngine engine = NewActionGame();
            engine.State.Map.Node(0).HasResourceToken = true;
            engine.State.Players[1].MobileCityNodeId = 0;

            Assert.IsFalse(engine.Dispatch(new PlaceInfluenceCommand(0, Node0Slot0)).Ok);
        }

        [Test]
        public void 部署_空格已被占用时非法()
        {
            GameEngine engine = NewActionGame();
            engine.State.Map.Route(0).Slots.Place(0, 1); // 座位 1 先占了

            Assert.IsFalse(engine.Dispatch(new PlaceInfluenceCommand(0, Route0Slot0)).Ok);
        }

        [Test]
        public void 部署_不是当前行动玩家时非法()
        {
            GameEngine engine = NewActionGame(4, 0);
            Assert.IsFalse(engine.Dispatch(new PlaceInfluenceCommand(1, Route0Slot0)).Ok);
        }

        [Test]
        public void 部署_供应堆没有标记时非法()
        {
            GameEngine engine = NewActionGame();
            engine.State.Players[0].MarkersInSupply = 0;

            Assert.IsFalse(engine.Dispatch(new PlaceInfluenceCommand(0, Route0Slot0)).Ok);
        }

        // ————————————————————— 调度 —————————————————————

        [Test]
        public void 调度_移动自己的影响力_不增减标记()
        {
            GameEngine engine = NewActionGame();
            GameState state = engine.State;
            state.Map.Route(0).Slots.Place(0, 0);
            int markers = state.Players[0].MarkersInSupply;

            var moves = new[] { new InfluenceMove(Route0Slot0, Route1Slot0) };
            CommandResult result = engine.Dispatch(new RelocateInfluenceCommand(0, moves));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(state.Map.Route(0).Slots.IsEmpty(0));
            Assert.AreEqual(0, state.Map.Route(1).Slots.OwnerOf(0));
            Assert.AreEqual(markers, state.Players[0].MarkersInSupply);
        }

        [Test]
        public void 调度_一次可以连续移动两步()
        {
            GameEngine engine = NewActionGame();
            GameState state = engine.State;
            state.Map.Route(0).Slots.Place(0, 0);

            var moves = new[]
            {
                new InfluenceMove(Route0Slot0, Route1Slot0),
                new InfluenceMove(Route1Slot0, Route2Slot0),
            };
            CommandResult result = engine.Dispatch(new RelocateInfluenceCommand(0, moves));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(state.Map.Route(0).Slots.IsEmpty(0));
            Assert.IsTrue(state.Map.Route(1).Slots.IsEmpty(0));
            Assert.AreEqual(0, state.Map.Route(2).Slots.OwnerOf(0));
        }

        [Test]
        public void 调度_不能移动对手的影响力()
        {
            GameEngine engine = NewActionGame();
            engine.State.Map.Route(0).Slots.Place(0, 1); // 座位 1 的影响力

            var moves = new[] { new InfluenceMove(Route0Slot0, Route1Slot0) };
            Assert.IsFalse(engine.Dispatch(new RelocateInfluenceCommand(0, moves)).Ok);
        }

        [Test]
        public void 调度_超过两步非法()
        {
            GameEngine engine = NewActionGame();
            var moves = new[]
            {
                new InfluenceMove(Route0Slot0, Route1Slot0),
                new InfluenceMove(Route1Slot0, Route2Slot0),
                new InfluenceMove(Route2Slot0, Route0Slot0),
            };
            Assert.IsFalse(engine.Dispatch(new RelocateInfluenceCommand(0, moves)).Ok);
        }

        // ————————————————————— 城市移动 —————————————————————

        private static GameEngine CityGame(out GameState state)
        {
            GameEngine engine = NewActionGame(4, 0);
            state = engine.State;
            state.Players[0].Resources.Add(ResourceType.Originium, 5);
            state.Players[0].MobileCityNodeId = 0;
            state.Map.Node(0).HasResourceToken = true;
            state.Map.Node(1).HasResourceToken = true; // 目标点已有指示物 → 不触发翻牌，专注测移动本身
            return engine;
        }

        [Test]
        public void 城市移动_支付3源石_移动城市并在出发点放影响力()
        {
            GameEngine engine = CityGame(out GameState state);
            int markers = state.Players[0].MarkersInSupply;

            // 步 1–2：付钱 + 移动（不占名额，见 §6.4d）。
            CommandResult result = engine.Dispatch(new MoveMobileCityCommand(0, 1));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(2, state.Players[0].Resources.Originium);
            Assert.AreEqual(1, state.Players[0].MobileCityNodeId);
            Assert.AreEqual(0, state.CityMoveOriginNodeId); // 出发点：0
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0), "步 3 之前出发点还没有影响力。");

            // 步 3：在出发点放 1 个自己的影响力，走完这步才占主要行动名额。
            CommandResult place = engine.Dispatch(new MoveCityInfluenceCommand(0, 0));
            Assert.IsTrue(place.Ok, place.Error);
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0)); // 出发点
            Assert.AreEqual(markers - 1, state.Players[0].MarkersInSupply);
            Assert.AreEqual(-1, state.CityMoveOriginNodeId, "三步走完应当结清。");
        }

        [Test]
        public void 城市移动_目标点没有指示物_翻开事件牌补上()
        {
            GameEngine engine = CityGame(out GameState state);
            state.Map.Node(1).HasResourceToken = false; // 目标点还没被探索过
            int deckBefore = state.EventDeckYellow.Count + state.EventDeckGreen.Count + state.EventDeckRed.Count;

            CommandResult result = engine.Dispatch(new MoveMobileCityCommand(0, 1));
            Assert.IsTrue(result.Ok, result.Error);

            MapNodeState target = state.Map.Node(1);
            Assert.IsTrue(target.HasResourceToken, "目标点应当补上资源点指示物。");
            Assert.GreaterOrEqual(target.EventCardId, 0, "应当翻开一张事件牌。");
            Assert.IsFalse(target.EventResolved, "翻开的事件牌等着玩家选选项。");

            // 事件没结算之前，不许在出发点放影响力。
            Assert.IsFalse(engine.Dispatch(new MoveCityInfluenceCommand(0, 0)).Ok);

            // 结算事件选项后放行。
            EventCardDef card = state.Catalog.Event(target.EventCardId);
            Assert.IsTrue(engine.Dispatch(new ResolveEventOptionCommand(0, 1, 0)).Ok, "应当能结算事件选项。");
            Assert.IsTrue(engine.Dispatch(new MoveCityInfluenceCommand(0, 0)).Ok);
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0));

            int deckAfter = state.EventDeckYellow.Count + state.EventDeckGreen.Count + state.EventDeckRed.Count;
            Assert.AreEqual(deckBefore - 1, deckAfter, "翻开一张牌，牌堆应当少一张。");
            Assert.IsNotNull(card);
        }

        [Test]
        public void 城市移动_步3的出发点必须与记录一致()
        {
            GameEngine engine = CityGame(out GameState state);
            Assert.IsTrue(engine.Dispatch(new MoveMobileCityCommand(0, 1)).Ok);

            // 出发点记录的是 0，却拿别的点来收尾 → 拒绝。
            Assert.IsFalse(engine.Dispatch(new MoveCityInfluenceCommand(0, 2)).Ok);
        }

        [Test]
        public void 城市移动_源石不足非法()
        {
            GameEngine engine = NewActionGame();
            GameState state = engine.State;
            state.Players[0].MobileCityNodeId = 0;
            state.Players[0].Resources.Add(ResourceType.Originium, 2);

            Assert.IsFalse(engine.Dispatch(new MoveMobileCityCommand(0, 1)).Ok);
        }

        [Test]
        public void 城市移动_目标不相邻非法()
        {
            GameEngine engine = CityGame(out _);
            Assert.IsFalse(engine.Dispatch(new MoveMobileCityCommand(0, 3)).Ok);
        }

        [Test]
        public void 城市移动_目标有对手城市非法()
        {
            GameEngine engine = CityGame(out GameState state);
            state.Players[1].MobileCityNodeId = 1;

            Assert.IsFalse(engine.Dispatch(new MoveMobileCityCommand(0, 1)).Ok);
        }

        [Test]
        public void 城市移动_红色区域未开放时非法()
        {
            GameEngine engine = NewActionGame(4, 0);
            GameState state = engine.State;
            state.Players[0].Resources.Add(ResourceType.Originium, 5);
            state.Players[0].MobileCityNodeId = 2; // 旁边就是红区 N3

            // 4 人局第 1 回合，红色区域要到第 4 回合才开放。
            Assert.IsFalse(engine.Dispatch(new MoveMobileCityCommand(0, 3)).Ok);
        }

        [Test]
        public void 城市移动_清除所用航道与目标点上的对手影响力()
        {
            GameEngine engine = CityGame(out GameState state);
            state.Map.Route(0).Slots.Place(0, 1); // 航道上的对手影响力
            state.Map.Node(1).Slots.Place(0, 2);  // 目标点上的对手影响力
            state.Map.Node(1).Slots.Place(1, 0);  // 目标点上自己的影响力（应保留）

            CommandResult result = engine.Dispatch(new MoveMobileCityCommand(0, 1));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(state.Map.Route(0).Slots.IsEmpty(0));
            Assert.IsTrue(state.Map.Node(1).Slots.IsEmpty(0));
            Assert.AreEqual(0, state.Map.Node(1).Slots.OwnerOf(1));
            Assert.AreEqual(31, state.Players[1].MarkersInSupply); // 标记退回
            Assert.AreEqual(31, state.Players[2].MarkersInSupply);
        }

        // ————————————————————— 路费 —————————————————————

        private static GameState TollState()
        {
            // N0 到 N2 有两条路：R0+R1（经 N1）与 R2（直达）。
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Nodes.Add(new MapNodeState(3, "Z-01", District.R, NodeColor.Green, false, 1)); // 孤岛
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 1));
            map.Routes.Add(new RouteState(2, new[] { 0, 2 }, District.R, 2));

            var state = new GameState(new GameSetup(4));
            for (int seat = 0; seat < 4; seat++) state.Players.Add(new PlayerState(seat, (PlayerColor)seat));
            state.Map = map;
            return state;
        }

        [Test]
        public void 路费_有自己影响力的航道免费()
        {
            GameState state = TollState();
            state.Map.Route(0).Slots.Place(0, 0);

            TollCalculator.Toll toll = TollCalculator.Compute(state, 0, new[] { 0 });

            Assert.AreEqual(0, toll.Total);
            Assert.IsEmpty(toll.Payments);
        }

        [Test]
        public void 路费_无自己影响力时付2金券给供应堆()
        {
            GameState state = TollState();

            TollCalculator.Toll toll = TollCalculator.Compute(state, 0, new[] { 0 });

            Assert.AreEqual(2, toll.Total);
            Assert.AreEqual(1, toll.Payments.Count);
            Assert.AreEqual(TollCalculator.SupplyPayee, toll.Payments[0].PayeeSeat);
        }

        [Test]
        public void 路费_航道上若有对手影响力则付给该对手()
        {
            GameState state = TollState();
            state.Map.Route(0).Slots.Place(0, 2);

            TollCalculator.Toll toll = TollCalculator.Compute(state, 0, new[] { 0 });

            Assert.AreEqual(2, toll.Total);
            Assert.AreEqual(2, toll.Payments[0].PayeeSeat);
        }

        [Test]
        public void 路费_默认走总费用最小的方案()
        {
            GameState state = TollState();

            IReadOnlyList<int> path = TollCalculator.FindCheapestPath(state, 0, 0, 2);

            CollectionAssert.AreEqual(new[] { 2 }, path); // 直达 R2 只要 2，绕 N1 要 4
            Assert.AreEqual(2, TollCalculator.Compute(state, 0, path).Total);
        }

        [Test]
        public void 路费_自己影响力铺满的绕行路线可以更便宜()
        {
            GameState state = TollState();
            state.Map.Route(0).Slots.Place(0, 0); // R0 免费
            state.Map.Route(1).Slots.Place(0, 0); // R1 免费

            IReadOnlyList<int> path = TollCalculator.FindCheapestPath(state, 0, 0, 2);

            CollectionAssert.AreEqual(new[] { 0, 1 }, path); // 绕行全免费（0），优于直达的 2
            Assert.AreEqual(0, TollCalculator.Compute(state, 0, path).Total);
        }

        [Test]
        public void 路费_不可达时返回null()
        {
            GameState state = TollState();
            Assert.IsNull(TollCalculator.FindCheapestPath(state, 0, 0, 3));
        }

        // ————————————————————— 行动轮次推进 —————————————————————

        [Test]
        public void 两轮主要行动结束后进入采集阶段()
        {
            GameEngine engine = NewActionGame(4, 0);
            GameState state = engine.State;
            for (int i = 0; i < state.Map.Nodes.Count; i++) state.Map.Node(i).HasResourceToken = true;

            var order = new[]
            {
                InfluenceSlotRef.OnNode(0, 0), InfluenceSlotRef.OnNode(0, 1),
                InfluenceSlotRef.OnNode(1, 0), InfluenceSlotRef.OnNode(1, 1),
                InfluenceSlotRef.OnNode(2, 0), InfluenceSlotRef.OnNode(2, 1),
                InfluenceSlotRef.OnNode(3, 0), InfluenceSlotRef.OnNode(3, 1),
            };

            for (int i = 0; i < order.Length; i++)
            {
                CommandResult result = engine.Dispatch(new PlaceInfluenceCommand(state.ActiveSeat, order[i]));
                Assert.IsTrue(result.Ok, $"第 {i + 1} 次部署失败：{result.Error}");

                if (i == 3)
                {
                    Assert.AreEqual(2, state.ActionRound, "第一轮走完应进入第 2 轮");
                    Assert.AreEqual(TurnPhase.PlayerActions, state.Phase);
                    Assert.AreEqual(0, state.ActiveSeat);
                }
            }

            Assert.AreEqual(TurnPhase.Harvest, state.Phase, "两轮都走完应进入采集阶段");
        }
    }
}