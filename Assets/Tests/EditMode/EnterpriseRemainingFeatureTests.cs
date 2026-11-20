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
    /// <summary>
    /// 企业扩展「本轮收尾」的四块内容（纯 C# EditMode 断言）：
    /// 1) 3 张红色干员计谋「用后放回游戏盒」；
    /// 2) 莱茵生命科室板「板级共享」；
    /// 3) 6 张企业城市样式牌的特殊行动（550–555）；
    /// 4) 4 张「持续留在场上（∞）」计谋及其收尾阶段暂停。
    /// 卡面判读见 <c>Doc/EnterpriseExpansion.md</c> §9.8 / §9.9。
    /// </summary>
    public sealed class EnterpriseRemainingFeatureTests
    {
        // 干员角色牌 id（700 起，按 OperatorNames 顺序）。
        private const int SilverAsh = CardIds.OperatorCharacterBase + 0;   // 银灰：红色计谋「制衡战略」
        private const int Mudrock = CardIds.OperatorCharacterBase + 1;     // 泥岩：泥岩小队（∞）
        private const int Closure = CardIds.OperatorCharacterBase + 3;     // 可露希尔：工程订购（∞）
        private const int W = CardIds.OperatorCharacterBase + 5;           // W：策略「伺机出动」
        private const int Dorothy = CardIds.OperatorCharacterBase + 6;     // 多萝西：为了大家（∞）
        private const int Passenger = CardIds.OperatorCharacterBase + 7;   // 异客：隐蔽仓库（∞）

        // 企业城市样式牌（550–555）。
        private const int FrontierDistrict = 550;   // 城建开拓区：失去 1 分 → 从设施牌堆选 1 张建设
        private const int PioneerOutpost = 551;     // 拓荒者据点：城市点放 2 个影响力 + 按标记数移动
        private const int CoreBusiness = 552;       // 核心商业区：四选一 + 额外 1 次非特殊主要行动
        private const int EnterprisePark = 553;     // 大型企业园：触发 2 次已解锁企业特效
        private const int PoliceDistrict = 554;     // 警备队辖区：三选二 + 本轮禁用角色牌
        private const int PioneerCommand = 555;     // 拓荒指挥中枢：额外非特殊主要行动 + 可连发两种角色牌效果

        private GameEngine engine;
        private GameState state;
        private PlayerState player;

        private static MapState SmallMap()
        {
            var map = new MapState();
            for (int i = 0; i < 3; i++)
                map.Nodes.Add(new MapNodeState(i, $"A-0{i + 1}", District.A, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 2));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 2));
            // 三个点都放指示物：放置影响力与免费移动的落点都够用。
            foreach (MapNodeState node in map.Nodes) node.HasResourceToken = true;
            return map;
        }

        private static GameEngine NewActions(bool enterprise)
        {
            GameSetup setup = new GameSetup(4,
                enterprise ? ExpansionFlags.Enterprise : ExpansionFlags.None);
            GameEngine created = GameEngine.Create(setup, 0, SmallMap(),
                PlaceholderCardCatalog.Default, new Random(7));
            Assert.IsTrue(created.Dispatch(new StartGameCommand(0)).Ok);
            Assert.IsTrue(created.Dispatch(new AdvancePhaseCommand()).Ok);

            if (enterprise)
            {
                // 企业板不走抽选，直接进入可用状态（抽选本身另有测试覆盖）。
                EnterpriseState enterprises = created.State.Enterprises;
                enterprises.Reset(4);
                enterprises.DraftDone = true;
            }

            // 企业扩展开局要「7 选 5」才能开始行动；这些测试直接进入行动阶段，模拟已经完成。
            foreach (PlayerState seat in created.State.Players) seat.SetupDiscardDone = true;

            created.State.Player(0).MobileCityNodeId = 0;
            created.State.Player(0).MarkersInSupply = 30;
            return created;
        }

        [SetUp]
        public void SetUp()
        {
            engine = NewActions(enterprise: false);
            state = engine.State;
            player = state.Player(0);
        }

        private GameEngine NewEnterpriseGame()
        {
            engine = NewActions(enterprise: true);
            state = engine.State;
            player = state.Player(0);
            return engine;
        }

        private static void AddPattern(GameState gameState, int cardId, int level, int seat,
            int markerCount = 1)
        {
            var pattern = new CityPatternState(cardId, level);
            for (int i = 0; i < markerCount; i++) pattern.PlaceMarker(seat, 0);

            // 企业扩展开局随机放出的样式牌里可能已经抽到这张：替换掉原来的状态，避免出现两份。
            int index = gameState.CityPatternCards.IndexOf(cardId);
            if (index < 0)
            {
                gameState.CityPatternCards.Add(cardId);
                gameState.CityPatternStates.Add(pattern);
            }
            else
            {
                gameState.CityPatternStates[index] = pattern;
            }
        }

        [Test]
        public void 罗德岛干员特派_升到3级时自动结算并记入公开展示()
        {
            NewEnterpriseGame();
            int board = (int)EnterpriseId.RhodesIsland;
            state.Enterprises.Activate(board);
            state.Enterprises.SetLevel(board, 0, 2);

            int handBefore = player.HandCharacterCards.Count;

            // 3 级「干员特派」是升级奖励（牌面上没有锁图标与「企业特效」标签）：
            // 升到这一级时立即结算，之后不能再手动触发。
            EnterpriseRules.BeginPendingAdvance(state, 0, -1, "测试：提升 1 级");
            CommandResult result = engine.Dispatch(new AdvanceEnterpriseLevelCommand(
                0, board, FirstAvailableChoices.Default));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(3, state.Enterprises.LevelOf(board, 0));

            Assert.AreEqual(1, state.PublicReveals.Count, "放回游戏盒的角色牌应当记一条「向所有玩家展示」。");
            Assert.AreEqual(0, state.PublicReveals[0].Seat);
            Assert.IsNotEmpty(state.PublicReveals[0].Reason);
            Assert.AreEqual(handBefore, player.HandCharacterCards.Count, "抽 1 张、放回 1 张，手牌数量不变。");
        }

        [Test]
        public void 罗德岛四级_回收的牌记入公开展示()
        {
            NewEnterpriseGame();
            int board = (int)EnterpriseId.RhodesIsland;
            state.Enterprises.Activate(board);
            state.Enterprises.SetLevel(board, 0, 4);
            player.DiscardCharacterCards.Add(CardIds.CharacterBase + 1);

            CommandResult result = engine.Dispatch(new TriggerEnterpriseEffectCommand(
                0, board, 4, FirstAvailableChoices.Default));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(1, state.PublicReveals.Count, "从弃牌区回收的角色牌应当记一条「向所有玩家展示」。");
            Assert.AreEqual(CardIds.CharacterBase + 1, state.PublicReveals[0].CardId);
        }

        [Test]
        public void 公开展示_快照往返保留记录()
        {
            NewEnterpriseGame();
            int board = (int)EnterpriseId.RhodesIsland;
            state.Enterprises.Activate(board);
            state.Enterprises.SetLevel(board, 0, 2);

            // 升到 3 级时由升级奖励自动结算「干员特派」，产生一条公开展示记录。
            EnterpriseRules.BeginPendingAdvance(state, 0, -1, "测试：提升 1 级");
            Assert.IsTrue(engine.Dispatch(new AdvanceEnterpriseLevelCommand(
                0, board, FirstAvailableChoices.Default)).Ok);
            Assert.AreEqual(1, state.PublicReveals.Count);

            byte[] bytes = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(state));
            GameState restored = GameStateSnapshotCodec.Read(bytes, 0, bytes.Length)
                .Restore(state.Catalog);

            Assert.AreEqual(1, restored.PublicReveals.Count, "快照往返后公开展示记录不能丢。");
            Assert.AreEqual(state.PublicReveals[0].Seat, restored.PublicReveals[0].Seat);
            Assert.AreEqual(state.PublicReveals[0].CardId, restored.PublicReveals[0].CardId);
            Assert.AreEqual(state.PublicReveals[0].Reason, restored.PublicReveals[0].Reason);
            CollectionAssert.AreEqual(bytes,
                GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(restored)),
                "公开展示记录快照往返后字节不一致。");
        }

        /// <summary>统计某座位在场上占用的影响力格数。</summary>
        private static int InfluenceCount(GameState gameState, int seat)
        {
            int count = 0;
            foreach (MapNodeState node in gameState.Map.Nodes)
                for (int i = 0; i < node.Slots.Capacity; i++)
                    if (node.Slots.OwnerOf(i) == seat) count++;
            foreach (RouteState route in gameState.Map.Routes)
                for (int i = 0; i < route.Slots.Capacity; i++)
                    if (route.Slots.OwnerOf(i) == seat) count++;
            return count;
        }

        // ———————————————— 1. 红色干员计谋「用后放回游戏盒」 ————————————————

        [Test]
        public void 红色一次性计谋_用后放回游戏盒_不进弃牌区()
        {
            NewEnterpriseGame();
            player.PlacedCharacterCard = SilverAsh;

            CommandResult result = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics));
            Assert.IsTrue(result.Ok, result.Error);

            // 起始玩家会先留住这张牌等「是否继续用另一种效果」，此时已经标记为「放回游戏盒」。
            Assert.IsTrue(player.PlacedCardReturnsToBox);
            Assert.IsTrue(engine.Dispatch(new EndCharacterCardUseCommand(0)).Ok);

            Assert.AreEqual(-1, player.PlacedCharacterCard);
            CollectionAssert.DoesNotContain(player.DiscardCharacterCards, SilverAsh,
                "红色 · 一次性计谋用完要放回游戏盒，不进弃牌区。");
        }

        [Test]
        public void 同一张干员牌的普通策略_用后仍进弃牌区()
        {
            NewEnterpriseGame();
            player.PlacedCharacterCard = SilverAsh;

            Assert.IsTrue(engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Strategy)).Ok);
            // 起始玩家可以接着用另一种效果；这里直接结束使用。
            Assert.IsTrue(engine.Dispatch(new EndCharacterCardUseCommand(0)).Ok);

            CollectionAssert.Contains(player.DiscardCharacterCards, SilverAsh,
                "只有红色 · 一次性效果才放回游戏盒，普通策略照常进弃牌区。");
        }

        [Test]
        public void W策略_放置3个影响力并登记收尾移除2个()
        {
            NewEnterpriseGame();
            player.PlacedCharacterCard = W;
            int supply = player.MarkersInSupply;

            CommandResult result = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Strategy));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(3, InfluenceCount(state, 0), "卡面为放置 3 次影响力。");
            Assert.AreEqual(supply - 3, player.MarkersInSupply);
            Assert.AreEqual(1, state.DeferredEffects.Count,
                "「本回合收尾阶段移除 2 个你的影响力」进入延迟结算队列。");
        }

        // ———————————————— 2. 莱茵生命科室板「板级共享」 ————————————————
        [Test]
        public void 莱茵科室板_板级共享_防御科()
        {
            NewEnterpriseGame();
            state.Enterprises.RhineDepartment = 0; // 防御科
            int seat0 = state.Player(0).Vouchers;
            int seat1 = state.Player(1).Vouchers;

            EnterpriseRules.SettleLevel(state, (int)EnterpriseId.RhineLab, 0, 2,
                FirstAvailableChoices.Default);
            EnterpriseRules.SettleLevel(state, (int)EnterpriseId.RhineLab, 1, 2,
                FirstAvailableChoices.Default);

            Assert.AreEqual(seat0 + 7, state.Player(0).Vouchers);
            Assert.AreEqual(seat1 + 7, state.Player(1).Vouchers,
                "科室板由整块企业板共用，两个座位结算的是同一张防御科。");
        }

        [Test]
        public void 莱茵科室板_板级共享_切换后工程科生效()
        {
            NewEnterpriseGame();
            state.Enterprises.RhineDepartment = 1; // 工程科
            int before = player.Resources.Get(ResourceType.Orirock);

            EnterpriseRules.SettleLevel(state, (int)EnterpriseId.RhineLab, 0, 2,
                FirstAvailableChoices.Default);

            Assert.AreEqual(before + 3, player.Resources.Get(ResourceType.Orirock));
        }

        [Test]
        public void 莱茵科室板_快照往返保留板级字段()
        {
            NewEnterpriseGame();
            state.Enterprises.RhineDepartment = 2; // 结构科

            byte[] bytes = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(state));
            GameState restored = GameStateSnapshotCodec.Read(bytes, 0, bytes.Length)
                .Restore(PlaceholderCardCatalog.Default);

            Assert.AreEqual(2, restored.Enterprises.RhineDepartment);
        }

        // ———————————————— 3. 企业城市样式牌特殊行动（550–555） ————————————————

        [Test]
        public void 城建开拓区_失去1分并从设施牌堆建设()
        {
            NewEnterpriseGame();
            AddPattern(state, FrontierDistrict, 1, 0);
            // 牌堆固定成「城邦行政区」（无入场效果），让分数变化只剩「费用 + 设施牌面分」。
            state.FacilityDeck.Clear();
            state.FacilityDeck.Add(CardIds.FacilityBase);
            player.Score = 5;
            // 从牌堆建设仍要照常支付设施的资源费用；补足资源，只观察「失去 1 分 + 设施牌面分」。
            player.Resources.Set(ResourceType.Orirock, 20);
            player.Resources.Set(ResourceType.Originium, 20);
            player.Resources.Set(ResourceType.Oriron, 20);
            player.Resources.Set(ResourceType.OriginitePrime, 20);
            int facilities = player.BuiltFacilities.Count;

            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, FrontierDistrict));
            Assert.IsTrue(result.Ok, result.Error);

            int builtScore = state.Catalog.Facility(CardIds.FacilityBase).Score;
            Assert.AreEqual(5 - 1 + builtScore, player.Score,
                "特殊行动费用失去 1 分；建设的设施按牌面分数加分。");
            Assert.AreEqual(facilities + 1, player.BuiltFacilities.Count,
                "从设施牌堆选 1 张并直接建设。");
        }

        [Test]
        public void 拓荒者据点_在城市点放2个影响力并按标记数移动()
        {
            NewEnterpriseGame();
            AddPattern(state, PioneerOutpost, 1, 0, markerCount: 2);
            int supply = player.MarkersInSupply;

            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, PioneerOutpost));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(2, InfluenceCount(state, 0), "先在移动城市所在资源点放置 2 个影响力。");
            Assert.AreEqual(supply - 2, player.MarkersInSupply,
                "移动影响力不消耗标记，只有新放的 2 个扣供应堆。");
        }

        [Test]
        public void 核心商业区_四选一加额外非特殊主要行动()
        {
            NewEnterpriseGame();
            AddPattern(state, CoreBusiness, 1, 0);
            int vouchers = player.Vouchers;

            // FirstAvailableChoices 取第一个分支 → 获得 4 金券。
            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, CoreBusiness));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(vouchers + 4, player.Vouchers);
            Assert.AreEqual(1, player.ExtraMainActionsExcludingSpecialThisTurn);
        }

        [Test]
        public void 招商园区_同回合额外非特殊额度不能拿去用特殊行动()
        {
            NewEnterpriseGame();
            AddPattern(state, CoreBusiness, 1, 0);
            state.MainActionTakenThisRound = true;
            player.ExtraMainActionsExcludingSpecialThisTurn = 1;

            CommandResult result = new UsePatternSpecialActionCommand(0, CoreBusiness).Validate(state);

            Assert.IsFalse(result.Ok, "额外的「特殊行动以外」主要行动不能再用来使用城市样式特殊行动。");
        }

        [Test]
        public void 大型企业园_触发2个已解锁企业特效()
        {
            NewEnterpriseGame();
            AddPattern(state, EnterprisePark, 1, 0);
            EnterpriseState enterprises = state.Enterprises;
            enterprises.Activate((int)EnterpriseId.Hyde);
            enterprises.SetLevel((int)EnterpriseId.Hyde, 0, 2); // 1 级 +4 源岩；2 级 -2 源岩 +15 金券
            int orirock = player.Resources.Get(ResourceType.Orirock);
            int vouchers = player.Vouchers;

            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, EnterprisePark));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(orirock + 2, player.Resources.Get(ResourceType.Orirock),
                "先触发 1 级 +4 源岩，再付 2 源岩触发 2 级。");
            Assert.AreEqual(vouchers + 15, player.Vouchers);
        }

        [Test]
        public void 警备队辖区_三选二_放置得源石并移除得异铁()
        {
            NewEnterpriseGame();
            AddPattern(state, PoliceDistrict, 2, 0);
            state.Map.Node(0).Slots.Place(0, 0); // 场上有影响力，满足「移除」选项
            int score = player.Score;
            int originium = player.Resources.Get(ResourceType.Originium);
            int oriron = player.Resources.Get(ResourceType.Oriron);

            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, PoliceDistrict));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(score, player.Score, "三条选项都不给分。");
            Assert.AreEqual(originium + 1, player.Resources.Get(ResourceType.Originium),
                "② 放置 1 个影响力，获得 1 源石。");
            Assert.AreEqual(oriron + 2, player.Resources.Get(ResourceType.Oriron),
                "③ 移除 1 个影响力，获得 2 异铁。");
            Assert.AreEqual(state.ActionRound, player.CharacterCardBanRound,
                "本轮不能再使用角色牌。");
        }

        [Test]
        public void 警备队辖区_付2源岩1异铁可替换对手的影响力()
        {
            NewEnterpriseGame();
            AddPattern(state, PoliceDistrict, 2, 0);
            state.Map.Node(0).Slots.Place(0, 1); // 对手的影响力作为替换目标
            player.Resources.Add(ResourceType.Orirock, 2);
            player.Resources.Add(ResourceType.Oriron, 1);
            int opponentMarkers = state.Player(1).MarkersInSupply;

            var choices = new TestChoices();
            choices.BranchAnswers.Enqueue(0); // 第一选：替换（相对下标 0 = 第一条）
            choices.BranchAnswers.Enqueue(0); // 第二选：剩下「放置」与「移除」里再选第一条（放置）

            CommandResult result = engine.Dispatch(
                new UsePatternSpecialActionCommand(0, PoliceDistrict, choices));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0), "替换后该格变成自己的影响力。");
            Assert.AreEqual(opponentMarkers + 1, state.Player(1).MarkersInSupply,
                "被替换掉的对手标记退回其供应堆。");
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Oriron));
            Assert.AreEqual(1, player.Resources.Get(ResourceType.Originium));
        }

        [Test]
        public void 拓荒指挥中枢_额外非特殊主要行动并允许连发两种角色牌效果()
        {
            NewEnterpriseGame();
            AddPattern(state, PioneerCommand, 2, 0);

            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, PioneerCommand));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(1, player.ExtraMainActionsExcludingSpecialThisTurn);
            Assert.IsTrue(player.BothCharacterEffectsThisTurn);
        }

        // ———————————————— 4. 四张「持续留在场上（∞）」计谋 ————————————————

        [Test]
        public void 泥岩小队_收尾阶段支付2源岩放置1个影响力()
        {
            player.Resources.Add(ResourceType.Orirock, 2);
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Mudrock, Seat = 0, Kind = PersistentTacticKind.MudrockSquad,
            });
            state.Phase = TurnPhase.Cleanup;

            CommandResult result = engine.Dispatch(
                new UsePersistentTacticCleanupCommand(0, PersistentTacticKind.MudrockSquad));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(0, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(29, player.MarkersInSupply);
            Assert.IsTrue(state.PersistentTactics[0].UsedCleanupThisTurn);
        }

        [Test]
        public void 工程订购_打出时压2张异名设施_建完最后一张进弃牌区()
        {
            NewEnterpriseGame();
            // 固定供应区：100 / 103 / 111 是三种不同名称的设施（100–102 是同一张「城邦行政区」的三份）。
            state.FacilitySupply.Clear();
            state.FacilitySupply.Add(CardIds.FacilityBase + 0);
            state.FacilitySupply.Add(CardIds.FacilityBase + 3);
            state.FacilitySupply.Add(CardIds.FacilityBase + 11);
            player.PlacedCharacterCard = Closure;

            CommandResult result = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics));
            Assert.IsTrue(result.Ok, result.Error);

            PersistentTacticState order = state.FindPersistentTactic(0,
                PersistentTacticKind.EngineeringOrder);
            Assert.IsNotNull(order);
            Assert.AreEqual(2, order.AttachedFacilityIds.Count,
                $"牌下设施：{string.Join(",", order.AttachedFacilityIds)}；供应区：{string.Join(",", state.FacilitySupply)}");
            Assert.AreEqual(-1, player.PlacedCharacterCard, "持续计谋留在场上，不占盖放位。");

            int first = order.AttachedFacilityIds[0];
            int second = order.AttachedFacilityIds[1];

            // 牌下取牌建设仍要支付设施的资源费用；补足资源，专注验证「从牌下取牌建设」。
            player.Resources.Set(ResourceType.Orirock, 20);
            player.Resources.Set(ResourceType.Originium, 20);
            player.Resources.Set(ResourceType.Oriron, 20);
            player.Resources.Set(ResourceType.OriginitePrime, 20);
            Assert.IsTrue(engine.Dispatch(new BuildFacilityCommand(0, first, 0,
                FacilityPayment.Resources)).Ok);
            Assert.AreEqual(1, order.AttachedFacilityIds.Count);

            // 每次建设都占 1 次主要行动；这里连续验证两张，先把本回合的名额复位。
            state.MainActionTakenThisRound = false;
            Assert.IsTrue(engine.Dispatch(new BuildFacilityCommand(0, second, 1,
                FacilityPayment.Resources)).Ok);
            Assert.IsNull(state.FindPersistentTactic(0, PersistentTacticKind.EngineeringOrder),
                "牌下设施建完后，牌进所有者的角色牌弃牌区。");
            CollectionAssert.Contains(player.DiscardCharacterCards, Closure);
        }

        [Test]
        public void 为了大家_打出时放标记_收尾移除2个标记放置影响力()
        {
            NewEnterpriseGame();
            player.PlacedCharacterCard = Dorothy;

            Assert.IsTrue(engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics)).Ok);

            PersistentTacticState card = state.FindPersistentTactic(0, PersistentTacticKind.ForEveryone);
            Assert.IsNotNull(card);
            Assert.AreEqual(4, card.Markers, "打出时放至多 4 个标记（默认取满）。");
            Assert.AreEqual(26, player.MarkersInSupply);

            state.Phase = TurnPhase.Cleanup;
            CommandResult result = engine.Dispatch(
                new UsePersistentTacticCleanupCommand(0, PersistentTacticKind.ForEveryone));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(2, card.Markers, "收尾能力移除 2 个标记。");
            Assert.AreEqual(1, InfluenceCount(state, 0), "并放置 1 个影响力。");
        }

        [Test]
        public void 为了大家_自己的影响力被移除时可以选择放到牌上()
        {
            NewEnterpriseGame();
            player.PlacedCharacterCard = Dorothy;
            Assert.IsTrue(engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics)).Ok);
            PersistentTacticState card = state.FindPersistentTactic(0, PersistentTacticKind.ForEveryone);
            Assert.AreEqual(4, card.Markers);

            state.Map.Node(1).Slots.Place(0, 0); // 自己在别处有 1 个影响力
            int supply = player.MarkersInSupply;

            // 效果在玩家行动阶段只登记挂起行动，具体移除由「移除影响力」事件派发命令完成。
            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.RemoveInfluence(1) }, new TestChoices());
            Assert.IsTrue(report.AnyApplied, report.Describe());
            PendingAction pending = state.FindPending(0, PendingActionKind.RemoveInfluence);
            Assert.IsNotNull(pending);
            Assert.AreEqual(4, card.Markers);

            // 分支 1 = 「放到『为了大家』牌上」（移除时问被移除方自己）。
            var choices = new TestChoices { BranchAnswers = new Queue<int>(new[] { 1 }) };
            Assert.IsTrue(engine.Dispatch(new RemoveInfluenceCommand(0,
                InfluenceSlotRef.OnNode(1, 0), pending.Id, choices)).Ok);

            Assert.AreEqual(5, card.Markers);
            Assert.AreEqual(supply, player.MarkersInSupply, "标记没有回到供应堆。");
        }

        [Test]
        public void 隐蔽仓库_支付资源时可用标记代替()
        {
            NewEnterpriseGame();
            player.PlacedCharacterCard = Passenger;
            Assert.IsTrue(engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics)).Ok);

            PersistentTacticState card = state.FindPersistentTactic(0,
                PersistentTacticKind.HiddenWarehouse);
            Assert.IsNotNull(card);
            Assert.AreEqual(5, card.Markers);

            int before = player.Resources.Get(ResourceType.Orirock);
            var choices = new TestChoices { BranchAnswers = new Queue<int>(new[] { 1 }) };
            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.Pay(ResourceType.Orirock, 2) }, choices);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(before, player.Resources.Get(ResourceType.Orirock),
                "2 个源岩全部用牌上标记代替。");
            Assert.AreEqual(3, card.Markers);
        }

        [Test]
        public void 隐蔽仓库_标记用完进弃牌区()
        {
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Passenger, Seat = 0, Kind = PersistentTacticKind.HiddenWarehouse, Markers = 2,
            });

            var choices = new TestChoices { BranchAnswers = new Queue<int>(new[] { 1 }) };
            EffectResolver.Execute(state, 0, new[] { RewardOp.Pay(ResourceType.Orirock, 2) }, choices);

            Assert.IsNull(state.FindPersistentTactic(0, PersistentTacticKind.HiddenWarehouse));
            CollectionAssert.Contains(player.DiscardCharacterCards, Passenger);
        }

        [Test]
        public void 收尾阶段_有可持续发动能力时暂停自动推进()
        {
            player.Resources.Add(ResourceType.Orirock, 2);
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Mudrock, Seat = 0, Kind = PersistentTacticKind.MudrockSquad,
            });
            state.Phase = TurnPhase.Cleanup;

            Assert.IsFalse(TurnFlow.TryAutoAdvance(state));
            Assert.AreEqual(TurnPhase.Cleanup, state.Phase, "还有能发动的持续计谋时要停下来等玩家决定。");
        }

        [Test]
        public void 收尾阶段_用完后自动进入下一回合()
        {
            player.Resources.Add(ResourceType.Orirock, 2);
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Mudrock, Seat = 0, Kind = PersistentTacticKind.MudrockSquad,
            });
            state.Phase = TurnPhase.Cleanup;
            int turn = state.TurnNumber;

            Assert.IsTrue(engine.Dispatch(
                new UsePersistentTacticCleanupCommand(0, PersistentTacticKind.MudrockSquad)).Ok);

            Assert.AreEqual(turn + 1, state.TurnNumber);
            Assert.AreEqual(TurnPhase.CharacterPlacement, state.Phase);
        }

        [Test]
        public void 收尾_按行动顺序逐个座位结算_没有效果的座位被跳过并记录()
        {
            // 4 人局没有收尾效果：从起始玩家起把 4 个座位依次跳过，直接进入下一回合。
            state.Phase = TurnPhase.Harvest;
            state.HarvestPending = false;
            Assert.IsTrue(engine.Dispatch(new AdvancePhaseCommand()).Ok);

            Assert.AreEqual(TurnPhase.CharacterPlacement, state.Phase);
            Assert.AreEqual(2, state.TurnNumber);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, state.CleanupSkippedSeats);
            Assert.AreEqual(-1, state.CleanupIndex);
        }

        [Test]
        public void 收尾_轮到谁就停在谁那里_结束后才轮到下一位()
        {
            player.Resources.Add(ResourceType.Orirock, 2);
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Mudrock, Seat = 0, Kind = PersistentTacticKind.MudrockSquad,
            });

            state.Phase = TurnPhase.Harvest;
            state.HarvestPending = false;
            Assert.IsTrue(engine.Dispatch(new AdvancePhaseCommand()).Ok);

            // 起始玩家 0 号有可选效果：收尾停在他这里，别人还没轮到。
            Assert.AreEqual(TurnPhase.Cleanup, state.Phase);
            Assert.AreEqual(0, state.CleanupSeat);
            CollectionAssert.IsEmpty(state.CleanupSkippedSeats);

            // 1 号也有可发动的持续计谋，但还没轮到他，不能抢先发动。
            state.Player(1).Resources.Add(ResourceType.Orirock, 2);
            state.Player(1).MarkersInSupply = 5;
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Mudrock, Seat = 1, Kind = PersistentTacticKind.MudrockSquad,
            });
            Assert.IsFalse(engine.Dispatch(
                new UsePersistentTacticCleanupCommand(1, PersistentTacticKind.MudrockSquad)).Ok);

            // 0 号结束自己这一位 → 轮到 1 号，0 号不会把 1 号一起跳过。
            Assert.IsTrue(engine.Dispatch(new EndCleanupSeatCommand(0)).Ok);
            Assert.AreEqual(TurnPhase.Cleanup, state.Phase);
            Assert.AreEqual(1, state.CleanupSeat);

            // 1 号发动完，2、3 号没有效果被跳过，然后才进入下一回合。
            int turn = state.TurnNumber;
            Assert.IsTrue(engine.Dispatch(
                new UsePersistentTacticCleanupCommand(1, PersistentTacticKind.MudrockSquad)).Ok);
            Assert.AreEqual(turn + 1, state.TurnNumber);
            Assert.AreEqual(TurnPhase.CharacterPlacement, state.Phase);
            CollectionAssert.AreEqual(new[] { 2, 3 }, state.CleanupSkippedSeats);
        }

        [Test]
        public void 持续计谋_快照往返保留牌与标记()
        {
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Closure, Seat = 0, Kind = PersistentTacticKind.EngineeringOrder, Markers = 0,
            });
            state.PersistentTactics[0].AttachedFacilityIds.Add(CardIds.FacilityBase + 3);
            state.PersistentTactics.Add(new PersistentTacticState
            {
                CardId = Dorothy, Seat = 1, Kind = PersistentTacticKind.ForEveryone, Markers = 3,
            });
            player.ExtraMainActionsExcludingSpecialThisTurn = 1;
            player.BothCharacterEffectsThisTurn = true;
            player.PlacedCardReturnsToBox = true;

            byte[] bytes = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(state));
            GameState restored = GameStateSnapshotCodec.Read(bytes, 0, bytes.Length)
                .Restore(PlaceholderCardCatalog.Default);

            Assert.AreEqual(2, restored.PersistentTactics.Count);
            PersistentTacticState order = restored.FindPersistentTactic(0,
                PersistentTacticKind.EngineeringOrder);
            Assert.IsNotNull(order);
            CollectionAssert.AreEqual(new[] { CardIds.FacilityBase + 3 }, order.AttachedFacilityIds);
            Assert.AreEqual(3, restored.FindPersistentTactic(1, PersistentTacticKind.ForEveryone).Markers);
            Assert.AreEqual(1, restored.Player(0).ExtraMainActionsExcludingSpecialThisTurn);
            Assert.IsTrue(restored.Player(0).BothCharacterEffectsThisTurn);
            Assert.IsTrue(restored.Player(0).PlacedCardReturnsToBox);
        }

        [Test]
        public void 快照往返_保留延迟结算队列与递归效果()
        {
            state.DeferredEffects.Add(new DeferredEffect(1, new[]
            {
                RewardOp.Gain(ResourceType.Orirock, 2),
                RewardOp.Alternative(
                    new EffectBranch("甲", RewardOp.Vouchers(3)),
                    new EffectBranch("乙", RewardOp.Influence(InfluencePlacementScope.DeployRules, 1))),
                RewardOp.Deferred(RewardOp.Score(1)),
                RewardOp.GainSpread(3, ResourceType.Orirock, ResourceType.Originium),
            }));

            byte[] bytes = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(state));
            GameState restored = GameStateSnapshotCodec.Read(bytes, 0, bytes.Length)
                .Restore(PlaceholderCardCatalog.Default);

            Assert.AreEqual(1, restored.DeferredEffects.Count);
            DeferredEffect deferred = restored.DeferredEffects[0];
            Assert.AreEqual(1, deferred.Seat);
            Assert.AreEqual(4, deferred.Ops.Count);

            Assert.AreEqual(RewardKind.GainResource, deferred.Ops[0].Kind);
            Assert.AreEqual(ResourceType.Orirock, deferred.Ops[0].Resource);
            Assert.AreEqual(2, deferred.Ops[0].Amount);

            Assert.AreEqual(RewardKind.Alternative, deferred.Ops[1].Kind);
            Assert.AreEqual(2, deferred.Ops[1].Branches.Count);
            Assert.AreEqual("甲", deferred.Ops[1].Branches[0].Label);
            Assert.AreEqual(RewardKind.GainVouchers, deferred.Ops[1].Branches[0].Ops[0].Kind);
            Assert.AreEqual("乙", deferred.Ops[1].Branches[1].Label);
            Assert.AreEqual(RewardKind.PlaceInfluence, deferred.Ops[1].Branches[1].Ops[0].Kind);

            Assert.AreEqual(RewardKind.Deferred, deferred.Ops[2].Kind);
            Assert.AreEqual(1, deferred.Ops[2].Ops.Count);
            Assert.AreEqual(RewardKind.GainScore, deferred.Ops[2].Ops[0].Kind);

            Assert.AreEqual(2, deferred.Ops[3].SpreadOptions.Count);
            Assert.AreEqual(ResourceType.Originium, deferred.Ops[3].SpreadOptions[1]);
        }

        /// <summary>
        /// 可脚本化回答的选择器：默认全部交给 <see cref="FirstAvailableChoices"/>，
        /// 只覆盖测试需要控制的分支。
        /// </summary>
        private sealed class TestChoices : IEffectChoices
        {
            private static readonly FirstAvailableChoices Fallback = FirstAvailableChoices.Default;

            public Queue<int> BranchAnswers = new Queue<int>();

            public int ChooseBranch(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<EffectBranch> branches) =>
                BranchAnswers.Count > 0 ? BranchAnswers.Dequeue() : Fallback.ChooseBranch(seat, op, gameState, branches);

            public int ChooseAmount(int seat, RewardOp op, GameState gameState, int min, int max) =>
                Fallback.ChooseAmount(seat, op, gameState, min, max);

            public InfluenceSlotRef? ChooseInfluenceSlot(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<InfluenceSlotRef> candidates) =>
                Fallback.ChooseInfluenceSlot(seat, op, gameState, candidates);

            public InfluenceSlotRef? ChooseInfluenceToRemove(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<InfluenceSlotRef> candidates) =>
                Fallback.ChooseInfluenceToRemove(seat, op, gameState, candidates);

            public InfluenceMove? ChooseOwnInfluenceMove(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<InfluenceSlotRef> sources, IReadOnlyList<InfluenceSlotRef> destinations) =>
                Fallback.ChooseOwnInfluenceMove(seat, op, gameState, sources, destinations);

            public int ChooseNode(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseNode(seat, op, gameState, candidates);

            public int ChooseRoute(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseRoute(seat, op, gameState, candidates);

            public int ChooseEnterpriseBoard(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseEnterpriseBoard(seat, op, gameState, candidates);

            public int ChooseEnterpriseEffectLevel(int seat, RewardOp op, GameState gameState, int boardId,
                IReadOnlyList<int> candidates) =>
                Fallback.ChooseEnterpriseEffectLevel(seat, op, gameState, boardId, candidates);

            public int ChooseRhineDepartment(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseRhineDepartment(seat, op, gameState, candidates);

            public int ChooseOpponent(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseOpponent(seat, op, gameState, candidates);

            public int ChoosePlayer(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChoosePlayer(seat, op, gameState, candidates);

            public int ChooseDiscardedCharacterCard(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) =>
                Fallback.ChooseDiscardedCharacterCard(seat, op, gameState, candidates);

            public int ChooseOperatorCard(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseOperatorCard(seat, op, gameState, candidates);

            public int ChooseCharacterCardIntoBox(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) =>
                Fallback.ChooseCharacterCardIntoBox(seat, op, gameState, candidates);

            public BuildChoice? ChooseBuild(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> facilityIds, IReadOnlyList<int> panelSlots, bool freeOnly = false) =>
                Fallback.ChooseBuild(seat, op, gameState, facilityIds, panelSlots, freeOnly);

            public int ChooseExtensionHub(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseExtensionHub(seat, op, gameState, candidates);

            public int ChoosePanelFacility(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidateSlots) =>
                Fallback.ChoosePanelFacility(seat, op, gameState, candidateSlots);

            public IReadOnlyDictionary<ResourceType, int> ChooseSellAmounts(int seat, RewardOp op,
                GameState gameState, IReadOnlyDictionary<ResourceType, int> prices,
                IReadOnlyDictionary<ResourceType, int> holdings) =>
                Fallback.ChooseSellAmounts(seat, op, gameState, prices, holdings);

            public IReadOnlyDictionary<ResourceType, int> ChooseResourceSpread(int seat, RewardOp op,
                GameState gameState, int total, IReadOnlyList<ResourceType> options,
                IReadOnlyDictionary<ResourceType, int> holdings) =>
                Fallback.ChooseResourceSpread(seat, op, gameState, total, options, holdings);

            public int ChooseResourceType(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<ResourceType> candidates) =>
                Fallback.ChooseResourceType(seat, op, gameState, candidates);

            public int ChooseSupplyFacility(int seat, RewardOp op, GameState gameState,
                IReadOnlyList<int> candidates) => Fallback.ChooseSupplyFacility(seat, op, gameState, candidates);

            public int ChooseOptionalStep(int seat, RewardOp op, GameState gameState, int stepIndex,
                string label) => Fallback.ChooseOptionalStep(seat, op, gameState, stepIndex, label);
        }
    }
}
