using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 主要行动 c：探索（§6.4c）——「支付路费 → 结算事件 → 在目标资源点放置影响力」三步。
    /// 与部署 / 调度一样纯用 state 断言，不依赖任何预制体或美术。
    /// </summary>
    public sealed class ExploreTests
    {
        // 测试地图：N0—R0—N1—R1—N2—R2—N3（N3 属红色区域、N2 为黄色资源点）
        private static GameEngine NewExploreGame(int playerCount = 4, int firstSeat = 0)
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

        /// <summary>把座位 0 的移动城市停在 N0，并把黄色事件牌堆固定成指定牌序。</summary>
        private static GameEngine ExploreFromNode0(params int[] yellowDeck)
        {
            GameEngine engine = NewExploreGame();
            engine.State.Player(0).MobileCityNodeId = 0;
            engine.State.EventDeckYellow.Clear();
            engine.State.EventDeckYellow.AddRange(yellowDeck);
            return engine;
        }

        // ————————————————————— 步 1：支付路费 + 步 2 前半：翻牌放指示物 —————————————————————

        [Test]
        public void 探索_支付路费并翻开对应颜色的事件牌放置指示物()
        {
            GameEngine engine = ExploreFromNode0(406); // 大型源岩场：黄色、指示物=源岩×1
            GameState state = engine.State;
            int vouchersBefore = state.Player(0).Vouchers;

            CommandResult result = engine.Dispatch(new ExploreCommand(0, 2));

            Assert.IsTrue(result.Ok, result.Error);
            // N0→N1→N2 要走 R0、R1 两条航道，自己没有影响力时每条 2 券。
            Assert.AreEqual(4, vouchersBefore - state.Player(0).Vouchers, "路费应为 4 金券。");

            MapNodeState node = state.Map.Node(2);
            Assert.IsTrue(node.HasResourceToken);
            Assert.AreEqual(ResourceType.Orirock, node.TokenResource);
            Assert.AreEqual(1, node.TokenAmount);
            Assert.AreEqual(406, node.EventCardId);
            Assert.IsFalse(node.EventResolved, "刚翻出来的牌还没结算。");
            Assert.IsTrue(node.ExplorePending, "第三步（放影响力）还没走，应留待办标记。");
        }

        [Test]
        public void 探索_航道上若有对手影响力则路费付给该对手()
        {
            GameEngine engine = ExploreFromNode0(406);
            GameState state = engine.State;
            state.Map.Route(0).Slots.Place(0, 2); // 座位 2 占了 R0
            int mine = state.Player(0).Vouchers;
            int theirs = state.Player(2).Vouchers;
            int supply = state.SupplyVouchers;

            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);

            Assert.AreEqual(mine - 4, state.Player(0).Vouchers, "总路费仍是 4 券。");
            Assert.AreEqual(theirs + 2, state.Player(2).Vouchers, "R0 上的 2 券归座位 2。");
            Assert.AreEqual(supply + 2, state.SupplyVouchers, "R1 没有对手影响力，2 券进供应堆。");
        }

        [Test]
        public void 探索_目标点已有资源点指示物时非法()
        {
            GameEngine engine = ExploreFromNode0(406);
            engine.State.Map.Node(2).HasResourceToken = true;

            Assert.IsFalse(engine.Dispatch(new ExploreCommand(0, 2)).Ok);
        }

        [Test]
        public void 探索_红色区域未开放时非法()
        {
            GameEngine engine = ExploreFromNode0(406);
            engine.State.Player(0).MobileCityNodeId = 2; // 红区 N3 就在旁边

            // 4 人局第 1 回合，红色区域要到第 4 回合才开放（§10.3）。
            Assert.IsFalse(engine.Dispatch(new ExploreCommand(0, 3)).Ok);
        }

        [Test]
        public void 探索_路费不足时非法()
        {
            GameEngine engine = ExploreFromNode0(406);
            engine.State.Player(0).Vouchers = 3; // 差 1 券

            Assert.IsFalse(engine.Dispatch(new ExploreCommand(0, 2)).Ok);
        }

        [Test]
        public void 探索_事件牌堆已空时非法()
        {
            GameEngine engine = ExploreFromNode0(); // 黄色牌堆清空
            Assert.IsFalse(engine.Dispatch(new ExploreCommand(0, 2)).Ok);
        }

        [Test]
        public void 探索_不是当前行动玩家时非法()
        {
            GameEngine engine = ExploreFromNode0(406);
            Assert.IsFalse(engine.Dispatch(new ExploreCommand(1, 2)).Ok);
        }

        // ————————————————————— 步 2 后半：结算事件选项 —————————————————————

        [Test]
        public void 探索_翻牌后可由当前行动玩家结算选项()
        {
            GameEngine engine = ExploreFromNode0(406); // 选项 0 = 获得源岩×5
            GameState state = engine.State;
            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);

            CommandResult result = engine.Dispatch(new ResolveEventOptionCommand(0, 2, 0));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(5, state.Player(0).Resources.Orirock);
            Assert.IsTrue(state.Map.Node(2).EventResolved);
        }

        [Test]
        public void 探索_非当前行动玩家不能结算该资源点的事件()
        {
            GameEngine engine = ExploreFromNode0(406);
            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);

            Assert.IsFalse(engine.Dispatch(new ResolveEventOptionCommand(1, 2, 0)).Ok);
        }

        // ————————————————————— 步 3：在目标资源点放置影响力 —————————————————————

        [Test]
        public void 探索_三步走完才放影响力并消耗主要行动名额()
        {
            GameEngine engine = ExploreFromNode0(406);
            GameState state = engine.State;
            int markers = state.Player(0).MarkersInSupply;

            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);
            Assert.AreEqual(0, state.ActiveSeat, "探索还没走完，不该轮到下一位。");

            Assert.IsTrue(engine.Dispatch(new ResolveEventOptionCommand(0, 2, 0)).Ok);

            CommandResult result = engine.Dispatch(new ExploreInfluenceCommand(0, 2));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, state.Map.Node(2).Slots.OwnerOf(0), "目标点第一个空格归探索者。");
            Assert.AreEqual(markers - 1, state.Player(0).MarkersInSupply);
            Assert.IsFalse(state.Map.Node(2).ExplorePending, "三步走完应结清待办标记。");
            Assert.AreEqual(1, state.ActiveSeat, "走完三步才轮到下一位玩家（§6.1）。");
        }

        [Test]
        public void 探索_事件未结算前不能放置影响力()
        {
            GameEngine engine = ExploreFromNode0(406);
            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);

            // 规则要求先结算事件、再放影响力（§6.4c 步 2 → 步 3）。
            Assert.IsFalse(engine.Dispatch(new ExploreInfluenceCommand(0, 2)).Ok);
        }

        [Test]
        public void 探索_不能对不是本次探索目标的资源点放置影响力()
        {
            GameEngine engine = ExploreFromNode0(406);
            GameState state = engine.State;
            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);

            // 让 N1 看上去像一张「已翻牌、已结算」的老资源点（比如别人的初始资源点）。
            state.Map.Node(1).HasResourceToken = true;
            state.Map.Node(1).EventCardId = 401;
            state.Map.Node(1).EventResolved = true;

            Assert.IsFalse(engine.Dispatch(new ExploreInfluenceCommand(0, 1)).Ok);
        }

        [Test]
        public void 收尾阶段作废没有走完第三步的探索()
        {
            GameEngine engine = ExploreFromNode0(406);
            GameState state = engine.State;
            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);
            Assert.IsTrue(state.Map.Node(2).ExplorePending);

            CleanupResolver.Resolve(state);

            Assert.IsFalse(state.Map.Node(2).ExplorePending, "主要行动必须在本回合内走完，跨回合作废。");
        }

        // ————————————————————— 自选路线与「付给谁」（§6.6） —————————————————————

        /// <summary>
        /// 「菱形」测试地图：N0 到 N2（黄色目标点）有两条路 ——
        /// 近路 R2 直达，远路 R0+R1 绕经 N1；另有支线 R3 通往红区 N3。
        /// R2 容量 2 格，用来构造「一条航道上有多位对手」的情形。
        /// </summary>
        private static GameEngine NewDiamondGame(params int[] yellowDeck)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Nodes.Add(new MapNodeState(3, "F-01", District.F, NodeColor.Red, true, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1)); // 远路前半
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 1)); // 远路后半
            map.Routes.Add(new RouteState(2, new[] { 0, 2 }, District.R, 2)); // 近路（可站两位对手）
            map.Routes.Add(new RouteState(3, new[] { 2, 3 }, District.F, 1)); // 通往红区的支线

            var engine = GameEngine.Create(new GameSetup(4), 0, map,
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(0));
            engine.Dispatch(new AdvancePhaseCommand());
            engine.State.Player(0).MobileCityNodeId = 0;
            engine.State.EventDeckYellow.Clear();
            engine.State.EventDeckYellow.AddRange(yellowDeck);
            return engine;
        }

        [Test]
        public void 探索_不指定路线时默认走最省方案()
        {
            GameEngine engine = NewDiamondGame(406);
            GameState state = engine.State;
            int vouchersBefore = state.Player(0).Vouchers;

            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2)).Ok);

            // 近路 R2 只要 2 券；远路 R0+R1 要 4 券，所以默认选近路。
            Assert.AreEqual(2, vouchersBefore - state.Player(0).Vouchers);
        }

        [Test]
        public void 探索_自选绕远路线时按玩家选的航道计费()
        {
            GameEngine engine = NewDiamondGame(406);
            GameState state = engine.State;
            int vouchersBefore = state.Player(0).Vouchers;

            CommandResult result = engine.Dispatch(
                new ExploreCommand(0, 2, routeIds: new[] { 0, 1 }));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(4, vouchersBefore - state.Player(0).Vouchers, "自选远路应走两条航道 = 4 券。");
            Assert.IsTrue(state.Map.Node(2).HasResourceToken, "翻牌与放指示物不受路线选择影响。");
        }

        [Test]
        public void 探索_自选路线不连通时非法()
        {
            GameEngine engine = NewDiamondGame(406);
            // R3 只在 N2 与 N3 之间，从 N0 出发够不到目标点。
            Assert.IsFalse(engine.Dispatch(new ExploreCommand(0, 2, routeIds: new[] { 3 })).Ok);
        }

        [Test]
        public void 探索_自选路线含不存在的航道时非法()
        {
            GameEngine engine = NewDiamondGame(406);
            Assert.IsFalse(engine.Dispatch(new ExploreCommand(0, 2, routeIds: new[] { 99 })).Ok);
        }

        [Test]
        public void 探索_同一航道重复选择时只计一次路费()
        {
            GameEngine engine = NewDiamondGame(406);
            GameState state = engine.State;
            int vouchersBefore = state.Player(0).Vouchers;

            Assert.IsTrue(engine.Dispatch(new ExploreCommand(0, 2, routeIds: new[] { 2, 2 })).Ok);

            Assert.AreEqual(2, vouchersBefore - state.Player(0).Vouchers, "同一条航道只计一次（§6.6）。");
        }

        [Test]
        public void 探索_一条航道上有多位对手时可指定收款方()
        {
            GameEngine engine = NewDiamondGame(406);
            GameState state = engine.State;
            state.Map.Route(2).Slots.Place(0, 1); // 座位 1
            state.Map.Route(2).Slots.Place(1, 3); // 座位 3
            int theirs1 = state.Player(1).Vouchers;
            int theirs3 = state.Player(3).Vouchers;

            CommandResult result = engine.Dispatch(new ExploreCommand(
                0, 2, routeIds: new[] { 2 }, payees: new Dictionary<int, int> { { 2, 3 } }));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(theirs3 + 2, state.Player(3).Vouchers, "玩家指定付给座位 3。");
            Assert.AreEqual(theirs1, state.Player(1).Vouchers, "没被指定的座位 1 收不到钱。");
        }

        [Test]
        public void 探索_指定不在该航道上的对手为收款方时非法()
        {
            GameEngine engine = NewDiamondGame(406);
            // R2 上没有任何对手，却指定付给座位 1。
            Assert.IsFalse(engine.Dispatch(new ExploreCommand(
                0, 2, routeIds: new[] { 2 }, payees: new Dictionary<int, int> { { 2, 1 } })).Ok);
        }
    }
}
