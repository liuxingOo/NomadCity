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

        [Test]
        public void 企业合作等级上限是五级()
        {
            foreach (EnterpriseBoardDef board in EnterpriseCatalog.Boards)
                Assert.AreEqual(5, board.MaxLevel, $"{board.Name} 的等级上限应为 5 级。");

            var enterprises = new EnterpriseState();
            enterprises.Reset(4);
            int boardId = (int)EnterpriseId.FusenBank;
            enterprises.Activate(boardId);
            for (int i = 0; i < 8; i++) enterprises.Advance(boardId, 0);

            Assert.AreEqual(5, enterprises.LevelOf(boardId, 0), "反复提升也只能停在 5 级。");
        }

        [Test]
        public void 莱茵生命科室板_开局按随机种子抽定()
        {
            var seen = new HashSet<int>();
            for (int seed = 1; seed <= 12; seed++)
            {
                GameEngine engine = NewEnterpriseGame(4, seed);
                int department = engine.State.Enterprises.RhineDepartment;
                Assert.That(department, Is.InRange(0, EnterpriseCatalog.DepartmentCount - 1),
                    $"种子 {seed}：科室板应当在 0–{EnterpriseCatalog.DepartmentCount - 1} 之间。");
                seen.Add(department);
            }

            Assert.Greater(seen.Count, 1, "12 颗不同种子应当抽出不止一种科室板，而不是固定取模。");
        }

        [Test]
        public void 同一随机种子_科室板与开局抽牌可复现()
        {
            GameEngine first = NewEnterpriseGame(4, 424242);
            GameEngine second = NewEnterpriseGame(4, 424242);

            Assert.AreEqual(first.State.Enterprises.RhineDepartment,
                second.State.Enterprises.RhineDepartment, "同一颗种子必须抽出同一张科室板。");
            CollectionAssert.AreEqual(first.State.Enterprises.Deck, second.State.Enterprises.Deck);
            for (int seat = 0; seat < 4; seat++)
                CollectionAssert.AreEqual(first.State.Enterprises.Offers[seat],
                    second.State.Enterprises.Offers[seat]);
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

            int actorScore = state.Players[0].Score;
            int opponentScore = state.Players[1].Score;
            int actorMarkers = state.Players[0].MarkersInSupply;
            int opponentMarkers = state.Players[1].MarkersInSupply;

            EffectReport report = RunExtension(state, 0, RewardOp.PlaceHighway());

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.IsTrue(route.IsHighway, "航道上应当放下公路指示物。");
            Assert.AreEqual(-1, route.Slots.OwnerOf(0), "影响力被清空并退回供应堆。");
            Assert.AreEqual(-1, route.Slots.OwnerOf(1));
            Assert.AreEqual(actorMarkers + 1, state.Players[0].MarkersInSupply);
            Assert.AreEqual(opponentMarkers + 1, state.Players[1].MarkersInSupply);

            Assert.AreEqual(actorScore + 2, state.Players[0].Score, "受影响 +1，发动者额外再 +1。");
            Assert.AreEqual(opponentScore + 1, state.Players[1].Score);

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
        public void 海德四级_洗回供应区补满后免费建造一次()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            int boardId = (int)EnterpriseId.Hyde;
            state.Enterprises.Activate(boardId);
            for (int i = 0; i < 4; i++) state.Enterprises.Advance(boardId, 0);

            PlayerState player = state.Players[0];
            player.Resources.Add(ResourceType.Orirock, 6);
            int built = player.BuiltFacilities.Count;
            int supply = state.FacilitySupply.Count;

            var command = new TriggerEnterpriseEffectCommand(0, boardId, 4, FirstAvailableChoices.Default);
            CommandResult check = command.Validate(state);
            Assert.IsTrue(check.Ok, check.Error);
            command.Apply(state);

            Assert.AreEqual(supply, state.FacilitySupply.Count, "供应区洗回后要重新补满。");
            Assert.AreEqual(built + 1, player.BuiltFacilities.Count, "免费建造 1 次。");
        }

        [Test]
        public void 雷神工业三级_一回合内可以重复触发()
        {
            GameEngine engine = NewExpansionGameAtActions();
            GameState state = engine.State;
            int boardId = (int)EnterpriseId.Raythean;
            state.Enterprises.Activate(boardId);
            for (int i = 0; i < 3; i++) state.Enterprises.Advance(boardId, 0);
            state.Players[0].Resources.Add(ResourceType.Originium, 10);

            var first = new TriggerEnterpriseEffectCommand(0, boardId, 3, FirstAvailableChoices.Default);
            Assert.IsTrue(first.Validate(state).Ok);
            first.Apply(state);

            var second = new TriggerEnterpriseEffectCommand(0, boardId, 3, FirstAvailableChoices.Default);
            Assert.IsTrue(second.Validate(state).Ok, "雷神工业 3 级的企业特效一回合内不限次数。");
        }

        [Test]
        public void 雷姆必拓二级_只能替换资源点上的影响力()
        {
            GameEngine engine = NewExpansionGameAtActions(3);
            GameState state = engine.State;
            MapNodeState node = state.Map.Node(0);
            node.HasResourceToken = true;
            node.Slots.Place(0, 1);
            state.Map.Route(0).Slots.Place(0, 2);
            state.Players[0].Vouchers = 10;

            Assert.IsTrue(RunExtension(state, 0,
                RewardOp.PayVouchers(4), RewardOp.ReplaceResourceNodeInfluence(1)).AnyApplied);

            Assert.AreEqual(0, node.Slots.OwnerOf(0), "资源点上的影响力被替换成自己的。");
            Assert.AreEqual(2, state.Map.Route(0).Slots.OwnerOf(0), "航道上的影响力不能被选作替换目标。");
        }

        // ———————————————— 干员牌效果（Doc/EnterpriseExpansion.md §9.6） ————————————————

        private static int InfluenceCount(GameState state, int seat)
        {
            int count = 0;
            foreach (MapNodeState node in state.Map.Nodes)
                for (int i = 0; i < node.Slots.Capacity; i++)
                    if (node.Slots.OwnerOf(i) == seat) count++;
            foreach (RouteState route in state.Map.Routes)
                for (int i = 0; i < route.Slots.Capacity; i++)
                    if (route.Slots.OwnerOf(i) == seat) count++;
            return count;
        }

        [Test]
        public void 商业手腕_目标玩家按四金券卖光所选资源()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            PlayerState actor = state.Players[0];
            actor.Resources.Add(ResourceType.Orirock, 3);
            int vouchers = actor.Vouchers;
            int score = actor.Score;

            Assert.IsTrue(RunExtension(state, 0,
                RewardOp.OperatorSellToPlayer(4), RewardOp.Score(1)).AnyApplied);

            Assert.AreEqual(0, actor.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(vouchers + 12, actor.Vouchers);
            Assert.AreEqual(score + 1, actor.Score);
        }

        [Test]
        public void 制衡战略_所有玩家各移除一个影响力()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            state.Map.Route(0).Slots.Place(0, 0);
            state.Map.Route(0).Slots.Place(1, 1);
            int markers = state.Players[1].MarkersInSupply;

            Assert.IsTrue(RunExtension(state, 0, RewardOp.OperatorBalanceInfluence()).AnyApplied);

            Assert.AreEqual(0, InfluenceCount(state, 0));
            Assert.AreEqual(0, InfluenceCount(state, 1));
            Assert.AreEqual(markers + 1, state.Players[1].MarkersInSupply);
        }

        [Test]
        public void 源岩DIY_支付一源岩后消耗掉()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            PlayerState actor = state.Players[0];

            Assert.IsFalse(RunExtension(state, 0, RewardOp.OperatorPayAndBuild()).AnyApplied,
                "没有源岩时不执行这条效果。");

            actor.Resources.Add(ResourceType.Orirock, 1);
            RunExtension(state, 0, RewardOp.OperatorPayAndBuild());
            Assert.AreEqual(0, actor.Resources.Get(ResourceType.Orirock));
        }

        [Test]
        public void 暴躁铁皮_两张不同名设施放回游戏盒并补满供应区()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            var before = new List<int>(state.FacilitySupply);
            int oriron = state.Players[0].Resources.Oriron;

            Assert.IsTrue(RunExtension(state, 0,
                RewardOp.OperatorSupplyToBox(2), RewardOp.Gain(ResourceType.Oriron, 3)).AnyApplied);

            Assert.AreEqual(BoardSetup.FacilitySupplySize, state.FacilitySupply.Count, "取走后要立刻补牌。");
            int removed = 0;
            foreach (int id in before)
                if (!state.FacilitySupply.Contains(id)) removed++;
            Assert.GreaterOrEqual(removed, 1);
            Assert.AreEqual(oriron + 3, state.Players[0].Resources.Oriron);
        }

        [Test]
        public void 掌控全局_触发该企业每项已解锁特效()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            EnterpriseState enterprises = state.Enterprises;
            int boardId = (int)EnterpriseId.Hyde;
            enterprises.Activate(boardId);
            enterprises.Advance(boardId, 0);
            enterprises.Advance(boardId, 0);

            PlayerState actor = state.Players[0];
            actor.Vouchers = 20;
            int orirock = actor.Resources.Orirock;
            int vouchers = actor.Vouchers;

            Assert.IsTrue(RunExtension(state, 0, RewardOp.TriggerAllEnterpriseEffects()).AnyApplied);

            Assert.AreEqual(orirock + 4 - 2, actor.Resources.Orirock, "1 级得 4 源岩，2 级再付 2 源岩。");
            Assert.AreEqual(vouchers + 15, actor.Vouchers, "2 级：支付 2 源岩获得 15 金券。");
        }

        [Test]
        public void 致命攻势_两名对手各被替换一个影响力()
        {
            GameEngine engine = NewExpansionGameAtActions(3);
            GameState state = engine.State;
            state.Map.Route(0).Slots.Place(0, 1);
            state.Map.Route(1).Slots.Place(0, 2);
            int markers = state.Players[0].MarkersInSupply;

            Assert.IsTrue(RunExtension(state, 0, RewardOp.OperatorReplaceTwoOpponents()).AnyApplied);

            Assert.AreEqual(0, InfluenceCount(state, 1));
            Assert.AreEqual(0, InfluenceCount(state, 2));
            Assert.AreEqual(2, InfluenceCount(state, 0));
            Assert.AreEqual(markers - 2, state.Players[0].MarkersInSupply);
        }

        [Test]
        public void 沁礁黑市_从对手取至多四个资源且双方各得一分()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            PlayerState actor = state.Players[0];
            PlayerState target = state.Players[1];
            target.Resources.Add(ResourceType.Orirock, 6);
            int actorScore = actor.Score;
            int targetScore = target.Score;

            Assert.IsTrue(RunExtension(state, 0, RewardOp.OperatorTakeOpponentResources(4)).AnyApplied);

            Assert.AreEqual(4, actor.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(2, target.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(actorScore + 1, actor.Score);
            Assert.AreEqual(targetScore + 1, target.Score);
        }

        [Test]
        public void 我寻思能行_免费移动城市改为移动对手影响力()
        {
            GameEngine engine = NewExpansionGameAtActions(2);
            GameState state = engine.State;
            PlayerState actor = state.Players[0];
            actor.MobileCityNodeId = 0;
            state.Map.Route(0).Slots.Place(0, 1);
            int markers = state.Players[1].MarkersInSupply;

            Assert.IsTrue(RunExtension(state, 0, RewardOp.OperatorRelocateInsteadOfRemove()).AnyApplied);
            Assert.IsTrue(RunExtension(state, 0, RewardOp.FreeCityMove()).AnyApplied);

            Assert.AreEqual(markers, state.Players[1].MarkersInSupply, "影响力应当被移动，而不是退回供应堆。");
            Assert.AreEqual(1, InfluenceCount(state, 1));
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
