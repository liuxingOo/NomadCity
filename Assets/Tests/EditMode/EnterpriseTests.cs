using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>企业扩展：数据目录、开局抽选、合作等级与快照往返。</summary>
    public sealed class EnterpriseTests
    {
        private const int Seed = 20261110;

        private static GameEngine NewEnterpriseGame(int playerCount = 4, int seed = Seed)
        {
            GameEngine engine = GameEngine.Create(
                new GameSetup(playerCount, ExpansionFlags.Enterprise), 0, null,
                PlaceholderCardCatalog.Default, new Random(seed));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);
            return engine;
        }

        [Test]
        public void 企业目录_八块板各有零到五级()
        {
            Assert.AreEqual(8, EnterpriseCatalog.Boards.Count);
            AssertUniqueIds();

            foreach (EnterpriseBoardDef board in EnterpriseCatalog.Boards)
            {
                Assert.IsNotEmpty(board.Name);
                for (int level = 1; level <= board.MaxLevel; level++)
                    Assert.IsNotEmpty(board.Level(level).Text, $"{board.Name} {level} 级没有文字。");
            }

            Assert.AreEqual(3, EnterpriseCatalog.Departments.Count);
            foreach (RhineDepartmentDef department in EnterpriseCatalog.Departments)
            {
                for (int level = 2; level <= 5; level++)
                    Assert.IsNotNull(department.Level(level), $"{department.Name} 缺少 {level} 级奖励。");
            }
        }

        private static void AssertUniqueIds()
        {
            var seen = new HashSet<int>();
            foreach (EnterpriseBoardDef board in EnterpriseCatalog.Boards)
                Assert.IsTrue(seen.Add((int)board.Id), $"企业 id 重复：{board.Id}");
        }

        [Test]
        public void 开局_每名玩家抽到两张不同的企业板()
        {
            GameEngine engine = NewEnterpriseGame(4);
            EnterpriseState enterprises = engine.State.Enterprises;

            Assert.IsFalse(enterprises.DraftDone);
            Assert.IsEmpty(enterprises.ActiveBoards);

            for (int seat = 0; seat < 4; seat++)
            {
                Assert.AreEqual(2, enterprises.Offers[seat].Count, $"座位 {seat} 应当抽到 2 张企业板。");
                Assert.AreNotEqual(enterprises.Offers[seat][0], enterprises.Offers[seat][1]);
            }
        }

        [Test]
        public void 两人局_每人抽四张选两张_两张都升到一级()
        {
            GameEngine engine = NewEnterpriseGame(2);
            EnterpriseState enterprises = engine.State.Enterprises;

            Assert.AreEqual(2, enterprises.PicksPerPlayer, "2 人局每人要选 2 张。");
            Assert.AreEqual(4, enterprises.OffersPerSeat, "2 人局每人抽 4 张。");

            for (int seat = 0; seat < 2; seat++)
                Assert.AreEqual(4, enterprises.Offers[seat].Count, $"座位 {seat} 应当抽到 4 张企业板。");

            for (int seat = 0; seat < 2; seat++)
            {
                for (int pick = 0; pick < 2; pick++)
                {
                    int boardId = enterprises.Offers[seat][0];
                    CommandResult result = engine.Dispatch(new ChooseInitialEnterpriseCommand(seat, boardId));
                    Assert.IsTrue(result.Ok, result.Error);
                }

                Assert.IsTrue(enterprises.SeatDraftDone(seat));
            }

            Assert.IsTrue(enterprises.DraftDone);
            Assert.AreEqual(4, enterprises.ActiveBoards.Count, "2 人局各有 2 家初始企业，场上共 4 块板。");

            for (int seat = 0; seat < 2; seat++)
            {
                Assert.AreEqual(1, enterprises.LevelOf(enterprises.ChosenBoards[seat], seat));
                Assert.AreEqual(1, enterprises.LevelOf(enterprises.SecondChosenBoards[seat], seat));
            }
        }

        [Test]
        public void 选完企业_自动补足四块板并把本人提升到一级()
        {
            GameEngine engine = NewEnterpriseGame(3);
            EnterpriseState enterprises = engine.State.Enterprises;

            for (int seat = 0; seat < 3; seat++)
            {
                int boardId = enterprises.Offers[seat][0];
                CommandResult result = engine.Dispatch(new ChooseInitialEnterpriseCommand(seat, boardId));
                Assert.IsTrue(result.Ok, result.Error);
                Assert.AreEqual(boardId, enterprises.ChosenBoards[seat]);
            }

            Assert.IsTrue(enterprises.DraftDone);
            Assert.AreEqual(4, enterprises.ActiveBoards.Count, "3 人局应当有 4 块场上企业板。");

            for (int seat = 0; seat < 3; seat++)
                Assert.AreEqual(1, enterprises.LevelOf(enterprises.ChosenBoards[seat], seat));

            // 每块板上每名玩家各放 1 个标记：4 块板 = 从供应堆里扣 4 个。
            // 再加开场计分轨起点扣 1 个（BoardSetup.PlaceScoreStartMarkers），
            // 所以每人初始 30 个 → 25 个。
            for (int seat = 0; seat < 3; seat++)
                Assert.AreEqual(25, engine.State.Player(seat).MarkersInSupply,
                    $"座位 {seat}：计分轨 -1 + 4 块企业板各 -1 = 供应堆剩 25。");
        }

        [Test]
        public void 没有待提升时_提升合作等级会被拒绝()
        {
            GameEngine engine = NewEnterpriseGame(4);
            EnterpriseState enterprises = engine.State.Enterprises;

            int boardId = enterprises.Offers[0][0];
            Assert.IsTrue(engine.Dispatch(new ChooseInitialEnterpriseCommand(0, boardId)).Ok);

            CommandResult result = engine.Dispatch(new AdvanceEnterpriseLevelCommand(0, boardId));
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("没有等着你提升", result.Error);
        }

        [Test]
        public void 快照往返保留企业状态()
        {
            GameEngine engine = NewEnterpriseGame(4);
            EnterpriseState enterprises = engine.State.Enterprises;

            int boardId = enterprises.Offers[1][1];
            Assert.IsTrue(engine.Dispatch(new ChooseInitialEnterpriseCommand(1, boardId)).Ok);
            enterprises.MarkEffectUsed(boardId, 1, 1);

            byte[] first = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(engine.State));
            GameStateSnapshot decoded = GameStateSnapshotCodec.Read(first, 0, first.Length);
            GameState restored = decoded.Restore(PlaceholderCardCatalog.Default);
            byte[] second = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(restored));

            CollectionAssert.AreEqual(first, second, "企业状态快照往返后不一致。");
            Assert.AreEqual(boardId, restored.Enterprises.ChosenBoards[1]);
            Assert.AreEqual(1, restored.Enterprises.LevelOf(boardId, 1));
            Assert.IsTrue(restored.Enterprises.HasUsedEffectThisTurn(boardId, 1, 1));
        }

        // ———————————————— 角色牌扩展（EnterpriseExpansion.md §3.2 / §4.2） ————————————————

        /// <summary>4 个资源点 + 3 条双格航道，够摆开局停靠与公路测试。</summary>
        private static MapState ExpansionMap()
        {
            var map = new MapState();
            for (int i = 0; i < 4; i++)
                map.Nodes.Add(new MapNodeState(i, $"A-0{i + 1}", District.A, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 2));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 2));
            map.Routes.Add(new RouteState(2, new[] { 2, 3 }, District.A, 2));
            return map;
        }

        /// <summary>建局后停在「角色牌盖放」阶段（开局入场尚未进行）。</summary>
        private static GameEngine NewExpansionGameAtPlacement(int playerCount = 4)
        {
            GameEngine engine = GameEngine.Create(
                new GameSetup(playerCount, ExpansionFlags.Enterprise), 0, ExpansionMap(),
                PlaceholderCardCatalog.Default, new Random(Seed));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);
            return engine;
        }

        /// <summary>
        /// 建局并推进到玩家行动阶段。企业板不走抽选，直接指定场上企业板，方便逐个验证特效。
        /// 这样做的代价是 <see cref="EnterpriseState.Offers"/> 为空，抽选本身另有测试覆盖。
        /// </summary>
        private static GameEngine NewExpansionGameAtActions(int playerCount = 4)
        {
            GameEngine engine = NewExpansionGameAtPlacement(playerCount);
            Assert.IsTrue(engine.Dispatch(new AdvancePhaseCommand()).Ok);

            EnterpriseState enterprises = engine.State.Enterprises;
            enterprises.Reset(playerCount);
            enterprises.DraftDone = true;
            return engine;
        }

        private static EffectReport RunExtension(GameState state, int seat, params RewardOp[] ops) =>
            EffectResolver.Execute(state, seat, ops, FirstAvailableChoices.Default,
                EnterpriseEffectExtension.Instance);

        [Test]
        public void 企业扩展_开局每人七张角色牌并包含山与玛恩纳()
        {
            GameEngine engine = NewExpansionGameAtPlacement();
            GameState state = engine.State;

            Assert.AreEqual(7, CardCounts.CharacterHandSizeWithExtension);
            for (int seat = 0; seat < 4; seat++)
            {
                PlayerState player = state.Players[seat];
                Assert.AreEqual(7, player.HandCharacterCards.Count, $"座位 {seat} 应当拿到 7 张手牌。");
                Assert.IsFalse(player.SetupDiscardDone, "拿到 7 张后要先完成「7 选 5」。");

                int first = CardIds.CharacterExtensionBase + (int)player.Color * CardCounts.CharacterExtensionRoles;
                CollectionAssert.Contains(player.HandCharacterCards, first);
                CollectionAssert.Contains(player.HandCharacterCards, first + 1);
                Assert.AreEqual("山", state.Catalog.Character(first).Name);
                Assert.AreEqual("玛恩纳", state.Catalog.Character(first + 1).Name);
            }
        }

        [Test]
        public void 企业扩展_七选五后才允许盖放角色牌()
        {
            GameEngine engine = NewExpansionGameAtPlacement();
            GameState state = engine.State;
            PlayerState player = state.Players[0];

            // 开局入场（决定初始资源点）还没走完时，弃牌就要先被挡下（§4.2：决定初始资源点之后才弃）。
            CommandResult early = engine.Dispatch(new DiscardSetupHandCardsCommand(
                0, player.HandCharacterCards[0], player.HandCharacterCards[1]));
            Assert.IsFalse(early.Ok);
            StringAssert.Contains("开局入场", early.Error);

            // 模拟开局入场结束：每名玩家都已停靠在自己的初始资源点、事件牌也结算完了。
            for (int seat = 0; seat < 4; seat++) state.Players[seat].MobileCityNodeId = seat;

            CommandResult blocked = engine.Dispatch(new PlaceCharacterCardCommand(0, player.HandCharacterCards[0]));
            Assert.IsFalse(blocked.Ok);
            StringAssert.Contains("7 张手牌里选 2 张", blocked.Error);

            int a = player.HandCharacterCards[0];
            int b = player.HandCharacterCards[1];
            CommandResult result = engine.Dispatch(new DiscardSetupHandCardsCommand(0, a, b));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(5, player.HandCharacterCards.Count);
            Assert.IsTrue(player.SetupDiscardDone);
            CollectionAssert.DoesNotContain(player.HandCharacterCards, a);
            CollectionAssert.DoesNotContain(player.HandCharacterCards, b);
            CollectionAssert.DoesNotContain(player.DiscardCharacterCards, a, "放回游戏盒的牌不进弃牌区。");
            CollectionAssert.DoesNotContain(player.DiscardCharacterCards, b);

            CommandResult again = engine.Dispatch(new DiscardSetupHandCardsCommand(
                0, player.HandCharacterCards[0], player.HandCharacterCards[1]));
            Assert.IsFalse(again.Ok, "同一名玩家只能弃一次。");

            CommandResult placed = engine.Dispatch(new PlaceCharacterCardCommand(0, player.HandCharacterCards[0]));
            Assert.IsTrue(placed.Ok, placed.Error);
        }

        [Test]
        public void 未启用企业扩展_手牌五张且不需要弃牌()
        {
            GameEngine engine = GameEngine.Create(new GameSetup(4), 0, ExpansionMap(),
                PlaceholderCardCatalog.Default, new Random(Seed));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);

            PlayerState player = engine.State.Players[0];
            Assert.AreEqual(5, player.HandCharacterCards.Count);
            Assert.IsTrue(player.SetupDiscardDone);

            CommandResult result = engine.Dispatch(new DiscardSetupHandCardsCommand(
                0, player.HandCharacterCards[0], player.HandCharacterCards[1]));
            Assert.IsFalse(result.Ok, "没有企业扩展就不该有「7 选 5」。");
        }

        // ———————————————— 公路（§5.1 / §7.2） ————————————————

        [Test]
        public void 开辟商路_清空航道影响力并让全员免路费()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            RouteState route = state.Map.Route(0);

            route.Slots.Place(0, 0); // 发动者自己的影响力
            route.Slots.Place(1, 1); // 对手的影响力

            int actorVouchers = state.Players[0].Vouchers;
            int opponentVouchers = state.Players[1].Vouchers;
            int actorMarkers = state.Players[0].MarkersInSupply;
            int opponentMarkers = state.Players[1].MarkersInSupply;

            EffectReport report = RunExtension(state, 0, RewardOp.PlaceHighway());

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.IsTrue(route.IsHighway, "航道上应当放下公路指示物。");
            Assert.AreEqual(-1, route.Slots.OwnerOf(0), "影响力被清空并退回供应堆。");
            Assert.AreEqual(-1, route.Slots.OwnerOf(1));
            Assert.AreEqual(actorMarkers + 1, state.Players[0].MarkersInSupply);
            Assert.AreEqual(opponentMarkers + 1, state.Players[1].MarkersInSupply);

            Assert.AreEqual(actorVouchers + 2, state.Players[0].Vouchers, "受影响 +1，发动者额外再 +1。");
            Assert.AreEqual(opponentVouchers + 1, state.Players[1].Vouchers);

            // 公路对所有玩家免路费（§5.1），哪怕上面已经没有自己的影响力。
            Assert.AreEqual(0, TollCalculator.RouteCost(state, 0, 0));
            Assert.AreEqual(0, TollCalculator.RouteCost(state, 3, 0));
            Assert.AreEqual(TollCalculator.CostPerRoute, TollCalculator.RouteCost(state, 3, 1));
        }

        [Test]
        public void 开辟商路_同一条航道不会重复铺公路()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;

            Assert.IsTrue(RunExtension(state, 0, RewardOp.PlaceHighway()).AnyApplied);
            Assert.AreEqual(1, HighwayCount(state));

            // 剩下的两条航道仍可铺；铺满之后就没有合法目标了。
            Assert.IsTrue(RunExtension(state, 0, RewardOp.PlaceHighway()).AnyApplied);
            Assert.IsTrue(RunExtension(state, 0, RewardOp.PlaceHighway()).AnyApplied);
            Assert.AreEqual(3, HighwayCount(state));
            Assert.IsFalse(RunExtension(state, 0, RewardOp.PlaceHighway()).AnyApplied);
        }

        private static int HighwayCount(GameState state)
        {
            int count = 0;
            foreach (RouteState route in state.Map.Routes)
                if (route.IsHighway) count++;
            return count;
        }

        // ———————————————— 玛恩纳 / 山的 4 个效果 ————————————————

        [Test]
        public void 游说赞助_选一家企业提升一级并获得三种资源()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int boardId = (int)EnterpriseId.Hyde;
            enterprises.Activate(boardId); // 所有座位 0 级

            PlayerState player = state.Players[0];
            int orirock = player.Resources.Get(ResourceType.Orirock);
            int originium = player.Resources.Get(ResourceType.Originium);
            int oriron = player.Resources.Get(ResourceType.Oriron);

            EffectReport report = RunExtension(state, 0,
                RewardOp.AdvanceOwnEnterpriseLevel(1),
                RewardOp.Gain(ResourceType.Orirock, 1),
                RewardOp.Gain(ResourceType.Originium, 1),
                RewardOp.Gain(ResourceType.Oriron, 1));

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(1, enterprises.LevelOf(boardId, 0), "自己在这家企业上提升 1 级。");
            Assert.AreEqual(0, enterprises.LevelOf(boardId, 1), "只提升自己，不影响别人。");
            Assert.AreEqual(orirock + 1, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(originium + 1, player.Resources.Get(ResourceType.Originium));
            Assert.AreEqual(oriron + 1, player.Resources.Get(ResourceType.Oriron));
        }

        [Test]
        public void 公事公办_所有玩家在同一家企业上各提升一级()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int boardId = (int)EnterpriseId.Hyde;
            enterprises.Activate(boardId);

            EffectReport report = RunExtension(state, 0, RewardOp.AdvanceEnterpriseAllPlayers());

            Assert.IsTrue(report.AnyApplied, report.Describe());
            for (int seat = 0; seat < 4; seat++)
                Assert.AreEqual(1, enterprises.LevelOf(boardId, seat), $"座位 {seat} 也应当提升 1 级。");
        }

        [Test]
        public void 公事公办另一分支_触发一个已解锁的企业特效()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int boardId = (int)EnterpriseId.Hyde;
            enterprises.Activate(boardId);
            enterprises.Advance(boardId, 0); // 海德兄弟 1 级：获得 4 源岩（企业特效）

            PlayerState player = state.Players[0];
            int before = player.Resources.Get(ResourceType.Orirock);

            EffectReport report = RunExtension(state, 0, RewardOp.TriggerEnterpriseEffectOnce());

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(before + 4, player.Resources.Get(ResourceType.Orirock));
            Assert.IsTrue(enterprises.HasUsedEffectThisTurn(boardId, 0, 1));
        }

        [Test]
        public void 商业网络_支付金券后提升一级并触发该企业特效()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int boardId = (int)EnterpriseId.Hyde;
            enterprises.Activate(boardId);

            PlayerState player = state.Players[0];
            player.Vouchers = 10;
            int orirock = player.Resources.Get(ResourceType.Orirock);

            EffectReport report = RunExtension(state, 0,
                RewardOp.PayVouchers(2),
                RewardOp.AdvanceAndTriggerEnterprise());

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(8, player.Vouchers, "先支付 2 金券。");
            Assert.AreEqual(1, enterprises.LevelOf(boardId, 0), "提升 1 级后再触发。");
            Assert.AreEqual(orirock + 4, player.Resources.Get(ResourceType.Orirock), "触发的是刚解锁的 1 级特效。");
            Assert.IsTrue(enterprises.HasUsedEffectThisTurn(boardId, 0, 1));
        }

        [Test]
        public void 企业特效_同一等级一回合只能触发一次()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int boardId = (int)EnterpriseId.Hyde;
            enterprises.Activate(boardId);
            enterprises.Advance(boardId, 0);

            Assert.IsTrue(RunExtension(state, 0, RewardOp.TriggerEnterpriseEffectOnce()).AnyApplied);
            Assert.IsFalse(RunExtension(state, 0, RewardOp.TriggerEnterpriseEffectOnce()).AnyApplied,
                "同一特效本回合已经触发过了。");
        }

        [Test]
        public void 企业扩展效果_关掉扩展后一律不受理()
        {
            GameEngine engine = GameEngine.Create(new GameSetup(4), 0, ExpansionMap(),
                PlaceholderCardCatalog.Default, new Random(Seed));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);

            GameState state = engine.State;
            Assert.IsFalse(EnterpriseRules.Enabled(state));
            Assert.IsFalse(EnterpriseEffectExtension.Instance.Handles(RewardOp.PlaceHighway(), state));
            Assert.IsFalse(RunExtension(state, 0, RewardOp.PlaceHighway()).AnyApplied);
        }
    }
}
