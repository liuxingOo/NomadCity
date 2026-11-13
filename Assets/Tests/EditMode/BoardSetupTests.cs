using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 卡牌定义层与开局设置的单元测试。
    /// 全部在 EditMode 下运行（本阶段没有可看的预制体，用断言代替 Play 模式验证）：
    /// 开 Unity 的 Test Runner → EditMode → Run All。
    /// </summary>
    public sealed class BoardSetupTests
    {
        private const int Seed = 20261011;

        private static GameEngine NewGame(int playerCount, int firstSeat = 0, int seed = Seed)
        {
            return GameEngine.Create(new GameSetup(playerCount), firstSeat, null,
                PlaceholderCardCatalog.Default, new Random(seed));
        }

        private static void AssertUnique<T>(IEnumerable<T> items)
        {
            var seen = new HashSet<T>();
            foreach (T item in items) Assert.IsTrue(seen.Add(item), $"出现重复元素：{item}");
        }

        // ————————————————————— 卡牌目录 —————————————————————

        [Test]
        public void Catalog_CharacterCards_每种颜色各有5个角色且颜色正确()
        {
            ICardCatalog catalog = PlaceholderCardCatalog.Default;
            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                IReadOnlyList<int> ids = catalog.CharacterCardIds(color);
                Assert.AreEqual(CardCounts.CharacterRoles, ids.Count);
                AssertUnique(ids);
                foreach (int id in ids)
                {
                    CharacterCardDef def = catalog.Character(id);
                    Assert.AreEqual(color, def.Color);
                    Assert.IsNotEmpty(def.Name);
                }
            }
        }

        [Test]
        public void Catalog_FacilityDeck_共41张普通设施()
        {
            ICardCatalog catalog = PlaceholderCardCatalog.Default;
            IReadOnlyList<int> ids = catalog.FacilityDeckIds(false);

            Assert.AreEqual(CardCounts.StandardFacilities, ids.Count);
            AssertUnique(ids);
            foreach (int id in ids) Assert.AreEqual(FacilityKind.Standard, catalog.Facility(id).Kind);
        }

        [Test]
        public void Catalog_有的设施不在牌堆内()
        {
            ICardCatalog catalog = PlaceholderCardCatalog.Default;
            var deck = new List<int>(catalog.FacilityDeckIds(false));

            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                FacilityCardDef tower = catalog.Facility(catalog.CoreCommandTowerId(color));
                Assert.AreEqual(FacilityKind.CoreCommandTower, tower.Kind);
                CollectionAssert.DoesNotContain(deck, tower.Id);
            }

            IReadOnlyList<int> hubs = catalog.ExtensionHubIds();
            Assert.AreEqual(CardCounts.ExtensionHubs, hubs.Count);
            AssertUnique(hubs);
            foreach (int id in hubs)
            {
                Assert.AreEqual(FacilityKind.ExtensionHub, catalog.Facility(id).Kind);
                CollectionAssert.DoesNotContain(deck, id);
            }
        }

        [Test]
        public void Catalog_事件牌_三色数量为6_10_6且共22张()
        {
            ICardCatalog catalog = PlaceholderCardCatalog.Default;

            var all = new List<int>();
            foreach ((NodeColor color, int expected) in new[]
                     {
                         (NodeColor.Green, CardCounts.EventGreen),
                         (NodeColor.Yellow, CardCounts.EventYellow),
                         (NodeColor.Red, CardCounts.EventRed),
                     })
            {
                IReadOnlyList<int> ids = catalog.EventDeckIds(color, enterpriseEnabled: false);
                Assert.AreEqual(expected, ids.Count);
                AssertUnique(ids);
                foreach (int id in ids) Assert.AreEqual(color, catalog.Event(id).BackColor);
                all.AddRange(ids);
            }

            Assert.AreEqual(CardCounts.EventCards, all.Count);
            AssertUnique(all);
        }

        [Test]
        public void Catalog_城市样式牌_I级4张_II级2张()
        {
            ICardCatalog catalog = PlaceholderCardCatalog.Default;
            IReadOnlyList<int> ids = catalog.CityPatternIds(false);

            Assert.AreEqual(CardCounts.CityPatterns, ids.Count);
            AssertUnique(ids);

            int level1 = 0, level2 = 0;
            foreach (int id in ids)
            {
                int level = catalog.CityPattern(id).Level;
                if (level == 1) level1++;
                else if (level == 2) level2++;
                else Assert.Fail($"样式牌等级只应为 1 或 2，实际 {level}");
            }
            Assert.AreEqual(CardCounts.CityPatternLevel1, level1);
            Assert.AreEqual(CardCounts.CityPatternLevel2, level2);
        }

        // ————————————————————— 开局设置 —————————————————————

        [Test]
        public void Setup_发牌_每人5张本色角色牌且互不重复()
        {
            GameEngine engine = NewGame(4);

            foreach (PlayerState player in engine.State.Players)
            {
                Assert.AreEqual(CardCounts.CharacterRoles, player.HandCharacterCards.Count);
                AssertUnique(player.HandCharacterCards);
                foreach (int id in player.HandCharacterCards)
                    Assert.AreEqual(player.Color, PlaceholderCardCatalog.Default.Character(id).Color);
                Assert.IsEmpty(player.DiscardCharacterCards);
            }
        }

        [Test]
        public void Setup_设施牌堆与供应区_供应区6张_牌堆35张_合计41张无重复()
        {
            GameEngine engine = NewGame(4);
            GameState state = engine.State;

            Assert.AreEqual(BoardSetup.FacilitySupplySize, state.FacilitySupply.Count);
            Assert.AreEqual(CardCounts.StandardFacilities - BoardSetup.FacilitySupplySize, state.FacilityDeck.Count);

            var all = new List<int>(state.FacilitySupply);
            all.AddRange(state.FacilityDeck);
            Assert.AreEqual(CardCounts.StandardFacilities, all.Count);
            AssertUnique(all);
        }

        [Test]
        public void Setup_事件牌堆与样式牌_数量正确()
        {
            GameState state = NewGame(4).State;

            Assert.AreEqual(CardCounts.EventGreen, state.EventDeckGreen.Count);
            Assert.AreEqual(CardCounts.EventYellow, state.EventDeckYellow.Count);
            Assert.AreEqual(CardCounts.EventRed, state.EventDeckRed.Count);
            Assert.AreEqual(CardCounts.CityPatterns, state.CityPatternCards.Count);
        }

        [Test]
        public void Setup_每人拿到本色指挥塔_延伸枢纽三张在一旁()
        {
            GameEngine engine = NewGame(4);
            GameState state = engine.State;

            foreach (PlayerState player in state.Players)
            {
                int towerId = PlaceholderCardCatalog.Default.CoreCommandTowerId(player.Color);
                CollectionAssert.Contains(player.BuiltFacilities, towerId);
            }

            Assert.AreEqual(CardCounts.ExtensionHubs, state.ExtensionHubSupply.Count);
            AssertUnique(state.ExtensionHubSupply);
        }

        [Test]
        public void Setup_玩家标记_初始数量正确()
        {
            GameState state = NewGame(4).State;

            foreach (PlayerState player in state.Players) Assert.AreEqual(30, player.MarkersInSupply);
        }

        [Test]
        public void Setup_各人数局初始资金总额_与人数变体一致()
        {
            foreach (int playerCount in new[] { 2, 3, 4 })
            {
                GameState state = NewGame(playerCount).State;
                PlayerCountVariant variant = PlayerCountVariant.For(playerCount);

                int expected = 0;
                for (int order = 0; order < playerCount; order++) expected += variant.InitialVouchers(order);

                int dealt = 0;
                foreach (PlayerState player in state.Players) dealt += player.Vouchers;

                Assert.AreEqual(expected, dealt, $"{playerCount} 人局的初始资金总额不对。");
            }
        }

        [Test]
        public void Setup_四人局初始资金_按顺位10_12_14_18()
        {
            GameState state = NewGame(4, firstSeat: 0).State;

            Assert.AreEqual(10, state.Players[0].Vouchers);
            Assert.AreEqual(12, state.Players[1].Vouchers);
            Assert.AreEqual(14, state.Players[2].Vouchers);
            Assert.AreEqual(18, state.Players[3].Vouchers);
            foreach (PlayerState player in state.Players) Assert.AreEqual(0, player.Score);
        }

        [Test]
        public void Setup_初始资金从起始玩家开始顺时针发放()
        {
            // 起始玩家是座位 2 → 顺位依次为 2、3、0、1，金额 10 / 12 / 14 / 18。
            GameState state = NewGame(4, firstSeat: 2).State;

            Assert.AreEqual(10, state.Players[2].Vouchers);
            Assert.AreEqual(12, state.Players[3].Vouchers);
            Assert.AreEqual(14, state.Players[0].Vouchers);
            Assert.AreEqual(18, state.Players[1].Vouchers);
        }

        [Test]
        public void Setup_三人局初始资金_10_12_18()
        {
            GameState state = NewGame(3, firstSeat: 0).State;

            Assert.AreEqual(10, state.Players[0].Vouchers);
            Assert.AreEqual(12, state.Players[1].Vouchers);
            Assert.AreEqual(18, state.Players[2].Vouchers);
        }

        [Test]
        public void Setup_二人局_起始10_非起始18并加3分()
        {
            GameState state = NewGame(2, firstSeat: 0).State;

            Assert.AreEqual(10, state.Players[0].Vouchers);
            Assert.AreEqual(0, state.Players[0].Score);
            Assert.AreEqual(18, state.Players[1].Vouchers);
            Assert.AreEqual(3, state.Players[1].Score);
        }

        [Test]
        public void Setup_相同种子_洗牌结果可复现()
        {
            GameState a = NewGame(4, seed: 777).State;
            GameState b = NewGame(4, seed: 777).State;

            CollectionAssert.AreEqual(a.FacilityDeck, b.FacilityDeck);
            CollectionAssert.AreEqual(a.FacilitySupply, b.FacilitySupply);
            CollectionAssert.AreEqual(a.EventDeckGreen, b.EventDeckGreen);
            CollectionAssert.AreEqual(a.EventDeckYellow, b.EventDeckYellow);
            CollectionAssert.AreEqual(a.EventDeckRed, b.EventDeckRed);
        }

        [Test]
        public void Setup_不同种子_洗牌结果不同()
        {
            GameState a = NewGame(4, seed: 1).State;
            GameState b = NewGame(4, seed: 2).State;

            CollectionAssert.AreNotEqual(a.FacilityDeck, b.FacilityDeck);
        }

        [Test]
        public void Setup_之后可以开始游戏_进入第一回合盖放阶段()
        {
            GameEngine engine = NewGame(4, firstSeat: 0);

            CommandResult result = engine.Dispatch(new StartGameCommand(0));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(1, engine.State.TurnNumber);
            Assert.AreEqual(TurnPhase.CharacterPlacement, engine.State.Phase);
            Assert.AreEqual(0, engine.State.FirstPlayerSeat);
            Assert.AreEqual(0, engine.State.ActiveSeat);
        }

        // ————————————————————— 开局入场·步 2：决定初始资源点（§4.3）—————————————————————

        // 测试地图：N0=B-01(绿)、N1=A-01(绿)、N2=A-02(黄)；由 N0—N1—N2 两条航道相连。
        private static GameEngine StartNodeGame(int playerCount = 4, int firstSeat = 0)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "B-01", District.B, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "A-02", District.A, NodeColor.Yellow, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 1));

            GameEngine engine = GameEngine.Create(new GameSetup(playerCount), firstSeat, map,
                PlaceholderCardCatalog.Default, new Random(Seed));
            engine.Dispatch(new StartGameCommand(firstSeat)); // 回合 1，角色牌盖放阶段
            return engine;
        }

        [Test]
        public void 开局选点_停靠移动城市并翻开绿色牌放上指示物()
        {
            GameEngine engine = StartNodeGame();
            GameState state = engine.State;
            int deckBefore = state.EventDeckGreen.Count;

            CommandResult result = engine.Dispatch(new ChooseStartNodeCommand(0, 1)); // A-01（绿色）

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(1, state.Player(0).MobileCityNodeId, "移动城市应停靠在该资源点。");
            Assert.AreEqual(deckBefore - 1, state.EventDeckGreen.Count, "应翻开一张绿色事件牌。");

            MapNodeState node = state.Map.Node(1);
            Assert.IsTrue(node.HasResourceToken, "资源点上应放上指示物。");

            EventCardDef card = PlaceholderCardCatalog.Default.Event(node.EventCardId);
            Assert.AreEqual(card.TokenResource, node.TokenResource);
            Assert.AreEqual(card.TokenAmount, node.TokenAmount);
        }

        [Test]
        public void 开局选点_选B01_立即获得2源岩2源石2异铁()
        {
            GameEngine engine = StartNodeGame();
            PlayerState player = engine.State.Player(0);

            CommandResult result = engine.Dispatch(new ChooseStartNodeCommand(0, 0)); // B-01

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(2, player.Resources.Orirock);
            Assert.AreEqual(2, player.Resources.Originium);
            Assert.AreEqual(2, player.Resources.Oriron);
            Assert.IsTrue(engine.State.Map.Node(0).HasResourceToken, "B-01 同样要放上指示物。");
        }

        [Test]
        public void 开局选点_不能选别人已经占用的资源点()
        {
            GameEngine engine = StartNodeGame();
            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(0, 1)).Ok);

            // 座位 0 已把城市停靠在 A-01，轮到的座位 1 不能再选同一个点。
            Assert.IsFalse(engine.Dispatch(new ChooseStartNodeCommand(1, 1)).Ok);
        }

        [Test]
        public void 开局选点_非绿色资源点被拒()
        {
            GameEngine engine = StartNodeGame();
            Assert.IsFalse(engine.Dispatch(new ChooseStartNodeCommand(0, 2)).Ok); // A-02 是黄色
        }

        [Test]
        public void 开局选点_必须是当前该选的玩家()
        {
            GameEngine engine = StartNodeGame();
            Assert.IsFalse(engine.Dispatch(new ChooseStartNodeCommand(1, 1)).Ok, "轮到的是座位 0。");
        }

        [Test]
        public void 开局选点_不能重复决定()
        {
            GameEngine engine = StartNodeGame();
            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(0, 1)).Ok);
            Assert.IsFalse(engine.Dispatch(new ChooseStartNodeCommand(0, 0)).Ok);
        }

        [Test]
        public void 开局选点_绿色牌堆为空时被拒()
        {
            GameEngine engine = StartNodeGame();
            engine.State.EventDeckGreen.Clear();

            Assert.IsFalse(engine.Dispatch(new ChooseStartNodeCommand(0, 1)).Ok);
        }

        // ————————————————————— 开局入场·步 2 后半段：结算事件选项（§11.2）—————————————————————

        /// <summary>把绿色事件牌堆固定成指定牌序（[0] 为牌堆顶，会作为初始资源点翻出的牌）。</summary>
        private static void StackGreenDeck(GameEngine engine, params int[] cardIds)
        {
            engine.State.EventDeckGreen.Clear();
            engine.State.EventDeckGreen.AddRange(cardIds);
        }

        /// <summary>让座位 0 在 A-01（N1）决定初始资源点，并指定翻出的绿色牌，得到可直接结算事件选项的局面。</summary>
        private static GameEngine EventGame(int cardId)
        {
            GameEngine engine = StartNodeGame();
            StackGreenDeck(engine, cardId);
            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(0, 1)).Ok);
            Assert.AreEqual(cardId, engine.State.Map.Node(1).EventCardId);
            return engine;
        }

        [Test]
        public void 结算事件选项_奖励生效并标记已结算()
        {
            GameEngine engine = EventGame(400); // 中立采石场：选项 0 = 获得源石×3
            PlayerState player = engine.State.Player(0);

            CommandResult result = engine.Dispatch(new ResolveEventOptionCommand(0, 1, 0));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(3, player.Resources.Originium);
            Assert.IsTrue(engine.State.Map.Node(1).EventResolved, "结算后应标记为已结算。");
        }

        [Test]
        public void 结算事件选项_不能重复结算()
        {
            GameEngine engine = EventGame(400);
            Assert.IsTrue(engine.Dispatch(new ResolveEventOptionCommand(0, 1, 0)).Ok);

            CommandResult again = engine.Dispatch(new ResolveEventOptionCommand(0, 1, 0));
            Assert.IsFalse(again.Ok, "同一个资源点的事件只能结算一次。");
        }

        [Test]
        public void 结算事件选项_资源点上没有事件牌时被拒()
        {
            GameEngine engine = StartNodeGame();
            engine.State.Player(0).MobileCityNodeId = 0; // N0（B-01）没有翻牌

            Assert.IsFalse(engine.Dispatch(new ResolveEventOptionCommand(0, 0, 0)).Ok);
        }

        [Test]
        public void 结算事件选项_选项下标越界被拒()
        {
            GameEngine engine = EventGame(400); // 只有 2 个选项

            Assert.IsFalse(engine.Dispatch(new ResolveEventOptionCommand(0, 1, 2)).Ok);
            Assert.IsFalse(engine.Dispatch(new ResolveEventOptionCommand(0, 1, -1)).Ok);
        }

        [Test]
        public void 结算事件选项_条件段付得起_结果段生效()
        {
            GameEngine engine = EventGame(409); // 锈锤领地·选项 0：支付金券×4 → 放置影响力
            PlayerState player = engine.State.Player(0);
            int vouchersBefore = player.Vouchers;

            var command = new ResolveEventOptionCommand(0, 1, 0, acceptCost: true);
            CommandResult result = engine.Dispatch(command);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(vouchersBefore - 4, player.Vouchers, "条件段应扣掉 4 张金券。");
            Assert.AreEqual(29, player.MarkersInSupply, "结果段应放置 1 个影响力标记。");
            Assert.AreEqual(0, engine.State.Map.Node(1).Slots.OwnerOf(0), "第一个合法空格归座位 0。");
            Assert.IsFalse(command.Report.Declined);
            Assert.IsTrue(engine.State.Map.Node(1).EventResolved);
        }

        [Test]
        public void 结算事件选项_条件段付不起_整个效果结束()
        {
            GameEngine engine = EventGame(409);
            PlayerState player = engine.State.Player(0);
            player.Vouchers = 3; // 付不起 4 张金券

            var command = new ResolveEventOptionCommand(0, 1, 0);
            CommandResult result = engine.Dispatch(command);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(3, player.Vouchers, "条件段付不起就不该扣钱。");
            Assert.AreEqual(30, player.MarkersInSupply, "结果段不应执行。");
            Assert.IsTrue(command.Report.Declined);
            Assert.IsTrue(engine.State.Map.Node(1).EventResolved, "条件段不可行也视为已结算，避免反复重试。");
        }

        [Test]
        public void 结算事件选项_主动放弃条件段_结果段不执行()
        {
            GameEngine engine = EventGame(409);
            PlayerState player = engine.State.Player(0);
            int vouchersBefore = player.Vouchers;

            var command = new ResolveEventOptionCommand(0, 1, 0, acceptCost: false);
            CommandResult result = engine.Dispatch(command);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(vouchersBefore, player.Vouchers, "放弃条件段不应扣钱。");
            Assert.AreEqual(30, player.MarkersInSupply, "放弃后结果段不应执行。");
            Assert.IsTrue(command.Report.Declined);
            Assert.IsTrue(engine.State.Map.Node(1).EventResolved);
        }

        [Test]
        public void 结算事件选项_带玩家答案时_按选中的空格放置影响力()
        {
            GameEngine engine = EventGame(409); // 锈锤领地·选项 0：支付金券×4 → 放置影响力
            PlayerState player = engine.State.Player(0);

            // 玩家点的是第 2 个候选空格（答案下标 1），而不是引擎默认的第 1 个。
            var command = new ResolveEventOptionCommand(0, 1, 0, acceptCost: true,
                choices: new RecordedChoices(new[] { 1 }, strict: false));
            CommandResult result = engine.Dispatch(command);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(29, player.MarkersInSupply, "结果段应放置 1 个影响力标记。");
            Assert.AreEqual(0, engine.State.Map.Node(1).Slots.OwnerOf(1),
                "应当放在玩家选中的那个空格（下标 1）。");
            Assert.AreEqual(-1, engine.State.Map.Node(1).Slots.OwnerOf(0),
                "没选的那一格应当还是空的 —— 界面传答案时不能再取第一项。");
        }

        // ————————————————————— 人数变体（§12）—————————————————————

        private static int TotalEvents(GameState state) =>
            state.EventDeckGreen.Count + state.EventDeckYellow.Count + state.EventDeckRed.Count;

        private static bool HasEvent(GameState state, int cardId) =>
            state.EventDeckGreen.Contains(cardId) || state.EventDeckYellow.Contains(cardId)
            || state.EventDeckRed.Contains(cardId);

        // 标「4」的四张（§4.1）：405 废弃矿坑 / 410 情报交换 / 413 敌对生态圈 / 420 深层矿床。
        private static readonly int[] FourPlayerOnlyEvents = { 405, 410, 413, 420 };

        [Test]
        public void Setup_二三人局_事件牌放回标4的四张()
        {
            foreach (int playerCount in new[] { 2, 3 })
            {
                GameState state = NewGame(playerCount).State;

                Assert.AreEqual(CardCounts.EventCards - FourPlayerOnlyEvents.Length, TotalEvents(state),
                    $"{playerCount} 人局的事件牌总数应为 18。");
                foreach (int id in FourPlayerOnlyEvents)
                    Assert.IsFalse(HasEvent(state, id), $"{playerCount} 人局不该出现标「4」的牌 {id}。");
            }
        }

        [Test]
        public void Setup_四人局_事件牌放齐标4的四张()
        {
            GameState state = NewGame(4).State;

            Assert.AreEqual(CardCounts.EventCards, TotalEvents(state));
            foreach (int id in FourPlayerOnlyEvents)
                Assert.IsTrue(HasEvent(state, id), $"4 人局应保留标「4」的牌 {id}。");
        }

        /// <summary>
        /// 2 人局测试地图：N0=B-01(绿)、N1=A-01(绿)、N2=A-03(绿)，由 N0—N1—N2 两条航道相连。
        /// B-01 在 2 人局被禁选（§12 变体 2），所以除它之外还要留两个绿色点够两个人各选一个。
        /// </summary>
        private static GameEngine TwoPlayerStartGame(int firstSeat = 0)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "B-01", District.B, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "A-03", District.A, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 1));

            GameEngine engine = GameEngine.Create(new GameSetup(2), firstSeat, map,
                PlaceholderCardCatalog.Default, new Random(Seed));
            engine.Dispatch(new StartGameCommand(firstSeat));
            return engine;
        }

        [Test]
        public void 开局选点_二人局禁选B01_四人局可以选()
        {
            Assert.IsFalse(TwoPlayerStartGame().Dispatch(new ChooseStartNodeCommand(0, 0)).Ok,
                "2 人局不能选 B-01（§12 变体 2）。");
            Assert.IsTrue(StartNodeGame(4).Dispatch(new ChooseStartNodeCommand(0, 0)).Ok,
                "4 人局可以选 B-01。");
        }

        [Test]
        public void 开局选点_二人局非起始玩家选完_立即在该点放1个影响力()
        {
            GameEngine engine = TwoPlayerStartGame(firstSeat: 0);
            GameState state = engine.State;

            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(0, 1)).Ok, "起始玩家先选。");
            Assert.AreEqual(-1, state.Map.Node(1).Slots.OwnerOf(0), "起始玩家按规则书不放影响力。");
            Assert.AreEqual(30, state.Player(0).MarkersInSupply);

            int before = state.Player(1).MarkersInSupply;
            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(1, 2)).Ok, "非起始玩家后选。");
            Assert.AreEqual(1, state.Map.Node(2).Slots.OwnerOf(0), "影响力要放在自己刚选的资源点上。");
            Assert.AreEqual(before - 1, state.Player(1).MarkersInSupply);
        }

        [Test]
        public void 开局选点_二人局起始玩家是座位1时_座位0放影响力()
        {
            GameEngine engine = TwoPlayerStartGame(firstSeat: 1);
            GameState state = engine.State;

            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(1, 1)).Ok);
            Assert.IsTrue(engine.Dispatch(new ChooseStartNodeCommand(0, 2)).Ok);

            Assert.AreEqual(-1, state.Map.Node(1).Slots.OwnerOf(0), "起始玩家（座位 1）不放。");
            Assert.AreEqual(0, state.Map.Node(2).Slots.OwnerOf(0), "非起始玩家（座位 0）放 1 个。");
        }

        // ————————————————————— 2 人局：回合开头弃设施牌（§12 变体 4）—————————————————————

        [Test]
        public void 二人局_回合开头先弃设施牌_顺序与补牌正确()
        {
            GameEngine engine = NewGame(2, firstSeat: 0);
            GameState state = engine.State;
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);

            Assert.AreEqual(2, state.SupplyDiscardRemaining, "2 人局本回合要弃 2 张。");
            Assert.AreEqual(0, state.SupplyDiscardSeat, "从本回合起始玩家（座位 0）起。");

            // 还没弃完之前不许盖放角色牌。
            int card0 = state.Player(0).HandCharacterCards[0];
            Assert.IsFalse(engine.Dispatch(new PlaceCharacterCardCommand(0, card0)).Ok,
                "弃设施牌之前不能盖放角色牌。");

            // 阶段也不能被推进：命令本身合法，但 TurnFlow 会拦住。
            Assert.IsTrue(engine.Dispatch(new AdvancePhaseCommand()).Ok);
            Assert.AreEqual(TurnPhase.CharacterPlacement, state.Phase, "还没弃完设施牌，阶段不能被推进。");

            // 顺序不对（座位 1 抢先弃）被拒。
            Assert.IsFalse(engine.Dispatch(new DiscardSupplyFacilityCommand(1, state.FacilitySupply[0])).Ok);

            // 座位 0 弃 1 张：供应区补回 6 张、牌堆少 1 张、被弃牌不在供应区。
            int deckBefore = state.FacilityDeck.Count;
            int discarded = state.FacilitySupply[0];
            Assert.IsTrue(engine.Dispatch(new DiscardSupplyFacilityCommand(0, discarded)).Ok);
            Assert.AreEqual(BoardSetup.FacilitySupplySize, state.FacilitySupply.Count, "要立刻补到 6 张。");
            Assert.AreEqual(deckBefore - 1, state.FacilityDeck.Count);
            CollectionAssert.DoesNotContain(state.FacilitySupply, discarded);
            Assert.AreEqual(1, state.SupplyDiscardRemaining);
            Assert.AreEqual(1, state.SupplyDiscardSeat, "轮到座位 1。");

            // 轮到座位 1 了，座位 0 再弃一张被拒。
            Assert.IsFalse(engine.Dispatch(new DiscardSupplyFacilityCommand(0, state.FacilitySupply[0])).Ok);

            // 座位 1 弃完：整步结束，闸门放开。
            Assert.IsTrue(engine.Dispatch(new DiscardSupplyFacilityCommand(1, state.FacilitySupply[0])).Ok);
            Assert.AreEqual(0, state.SupplyDiscardRemaining);
            Assert.AreEqual(-1, state.SupplyDiscardSeat);
            Assert.IsTrue(engine.Dispatch(new PlaceCharacterCardCommand(0, card0)).Ok, "弃完之后才允许盖放。");
        }

        [Test]
        public void 三人四人局_回合开头没有弃设施牌这一步()
        {
            foreach (int playerCount in new[] { 3, 4 })
            {
                GameEngine engine = NewGame(playerCount, firstSeat: 0);
                Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);

                Assert.AreEqual(0, engine.State.SupplyDiscardRemaining, $"{playerCount} 人局不该要弃牌。");
                Assert.AreEqual(-1, engine.State.SupplyDiscardSeat);
            }
        }

        [Test]
        public void 二人局_不在盖放阶段的弃设施牌被拒()
        {
            GameEngine engine = NewGame(2, firstSeat: 0);
            Assert.IsFalse(engine.Dispatch(new DiscardSupplyFacilityCommand(0, 1)).Ok,
                "还没开始游戏（不是盖放阶段）时不能弃设施牌。");
        }
    }
}