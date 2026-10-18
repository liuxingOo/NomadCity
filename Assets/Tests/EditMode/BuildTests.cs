using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// ④ 建设行动（§6.4e）：支付费用 → 放牌计分 → 入场效果 → 供应区补牌。
    /// 41 张设施牌的牌面数据已录入（Doc/FacilityCards.md），本类既验证不依赖牌面数值的部分
    /// （来源、落点、关键词与补牌），也顺带锁定几张代表性牌的牌面数值。
    /// </summary>
    public sealed class BuildTests
    {
        private static GameEngine NewBuildGame(int playerCount = 4, int firstSeat = 0)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));

            var engine = GameEngine.Create(new GameSetup(playerCount), firstSeat, map,
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(firstSeat)); // 回合 1，盖放阶段
            engine.Dispatch(new AdvancePhaseCommand());       // → 玩家行动阶段
            return engine;
        }

        /// <summary>面板上第一个空闲的设施格（核心指挥塔那一格不算）。</summary>
        private static int FreeSlot(GameState state)
        {
            for (int i = 0; i < PlayerState.CityPanelSlotCount; i++)
            {
                if (i != PlayerState.CityPanelCoreSlot && state.Players[0].CityPanelSlots[i] < 0) return i;
            }
            return -1;
        }

        /// <summary>一张当前不在设施供应区里的标准设施牌 id。</summary>
        private static int FacilityOutsideSupply(GameState state)
        {
            for (int i = 0; i < CardCounts.StandardFacilities; i++)
            {
                int id = CardIds.FacilityBase + i;
                if (!state.FacilitySupply.Contains(id)) return id;
            }
            return -1;
        }

        [Test]
        public void 开局_核心指挥塔占住面板第8格()
        {
            GameEngine engine = NewBuildGame();
            PlayerState player = engine.State.Players[0];

            int towerId = PlaceholderCardCatalog.Default.CoreCommandTowerId(player.Color);

            Assert.AreEqual(towerId, player.CityPanelSlots[PlayerState.CityPanelCoreSlot]);
            CollectionAssert.Contains(player.BuiltFacilities, towerId);
        }

        [Test]
        public void 建设_放到空位_从供应区拿走该牌并补回6张()
        {
            GameEngine engine = NewBuildGame(4, 0);
            GameState state = engine.State;
            int card = state.FacilitySupply[0];
            int slot = FreeSlot(state);

            // 牌面数据已录入，给足资源与金券后按「资源费用」支付（供应区首张可能是任何一种牌）。
            PlayerState player = state.Players[0];
            foreach (ResourceType type in ResourceTypes.Basics) player.Resources.Add(type, 50);
            player.Vouchers += 50;

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, card, slot, FacilityPayment.Resources));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(card, state.Players[0].CityPanelSlots[slot]);
            CollectionAssert.Contains(state.Players[0].BuiltFacilities, card);
            CollectionAssert.DoesNotContain(state.FacilitySupply, card);
            Assert.AreEqual(BoardSetup.FacilitySupplySize, state.FacilitySupply.Count);
            Assert.AreEqual(1, state.ActiveSeat); // 主要行动会推进顺位
        }

        [Test]
        public void 设施牌数据_城邦行政区()
        {
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase); // 100

            Assert.AreEqual("城邦行政区", card.Name);
            Assert.AreEqual(FacilityColor.Rainbow, card.Color);
            Assert.AreEqual(3, card.Score);
            Assert.AreEqual(30, card.VoucherCost);
            Assert.IsTrue(card.IsUnique);
            Assert.IsFalse(card.IsFree);
            Assert.AreEqual(3, card.ResourceCost.Count);
            Assert.AreEqual(3, CountOf(card, ResourceType.Orirock));
            Assert.AreEqual(3, CountOf(card, ResourceType.Originium));
            Assert.AreEqual(3, CountOf(card, ResourceType.Oriron));
        }

        [Test]
        public void 设施牌数据_简陋工程营分数为负()
        {
            // 109／110：简陋工程营，牌面分数 -1（建设时立即结算）。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 9);

            Assert.AreEqual("简陋工程营", card.Name);
            Assert.AreEqual(-1, card.Score);
            Assert.AreEqual(FacilityColor.Rainbow, card.Color);
        }

        [Test]
        public void 设施牌数据_物流枢纽费用为至纯源石加金券()
        {
            // 111／112：物流枢纽 资源费用组 = 至纯源石1 · 金券2（另一组 = 24 金券，整组二选一）。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 11);
            var pool = new ResourcePool();
            pool.Add(ResourceType.OriginitePrime, 1);

            Assert.AreEqual("物流枢纽", card.Name);
            Assert.AreEqual(FacilityColor.Blue, card.Color);
            Assert.AreEqual(2, card.Score);
            Assert.AreEqual(24, card.VoucherCost);
            Assert.IsTrue(card.IsUnique);
            Assert.AreEqual(1, CountOf(card, ResourceType.OriginitePrime));
            Assert.AreEqual(2, CountVouchersOf(card));
            Assert.IsFalse(card.CanPayWithResources(pool, 1), "金券不足时应付不起");
            Assert.IsTrue(card.CanPayWithResources(pool, 2));
        }

        [Test]
        public void 设施牌数据_资源费用可含金券()
        {
            // 121：城邦工业区 资源费用 = 源岩4 · 源石3 · 金券2。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 21);
            var pool = new ResourcePool();
            pool.Add(ResourceType.Orirock, 4);
            pool.Add(ResourceType.Originium, 3);

            Assert.AreEqual("城邦工业区", card.Name);
            Assert.IsFalse(card.CanPayWithResources(pool, 1), "金券不足时应付不起");
            Assert.IsTrue(card.CanPayWithResources(pool, 2));
            Assert.AreEqual(2, CountVouchersOf(card));
        }

        [Test]
        public void 设施牌数据_高性能动力设施_建设照常付费且入场效果为免费城市移动()
        {
            // 124／125：高性能动力设施。牌面关键词栏的「免费」紧邻城市移动图标，只作用于
            // 入场效果里的「免费城市移动」，建设本身仍按 源石4·异铁2 / 20 金券 二选一付费。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 24);

            Assert.AreEqual("高性能动力设施", card.Name);
            Assert.AreEqual(FacilityColor.Yellow, card.Color);
            Assert.IsFalse(card.IsFree, "「免费」不作用于建设");
            Assert.AreEqual(20, card.VoucherCost);
            Assert.AreEqual(4, CountOf(card, ResourceType.Originium));
            Assert.AreEqual(2, CountOf(card, ResourceType.Oriron));

            // 入场效果：免费城市移动 + 在所经航道额外放置 1 个影响力（出发点放置由城市移动本身完成）。
            List<RewardKind> kinds = new();
            foreach (RewardOp op in card.EntryEffects) kinds.Add(op.Kind);
            CollectionAssert.Contains(kinds, RewardKind.FreeCityMove);
            CollectionAssert.Contains(kinds, RewardKind.PlaceInfluenceOnCityMoveRoute);
        }

        [Test]
        public void 设施牌数据_异铁冶炼厂与固源岩提纯厂效果()
        {
            // 117：异铁冶炼厂（蓝）获得 4 异铁；126：固源岩提纯厂（黄）获得 7 源岩。
            FacilityCardDef smelter = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 17);
            FacilityCardDef refinery = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 26);

            Assert.AreEqual(1, smelter.EntryEffects.Count);
            Assert.AreEqual(RewardKind.GainResource, smelter.EntryEffects[0].Kind);
            Assert.AreEqual(ResourceType.Oriron, smelter.EntryEffects[0].Resource);
            Assert.AreEqual(4, smelter.EntryEffects[0].Amount);

            Assert.AreEqual(1, refinery.EntryEffects.Count);
            Assert.AreEqual(ResourceType.Orirock, refinery.EntryEffects[0].Resource);
            Assert.AreEqual(7, refinery.EntryEffects[0].Amount);
        }

        [Test]
        public void 设施牌数据_延伸枢纽只有红黄蓝三张()
        {
            var hubs = PlaceholderCardCatalog.Default.ExtensionHubIds();

            Assert.AreEqual(CardCounts.ExtensionHubs, hubs.Count);
            Assert.AreEqual(-1, PlaceholderCardCatalog.Default.Facility(hubs[0]).Score); // 牌面分数 -1
            Assert.IsFalse(PlaceholderCardCatalog.Default.Facility(hubs[0]).IsUnique);
        }

        [Test]
        public void 设施牌数据_佣兵指挥部入场效果为二选一()
        {
            // 133–135：入场效果是「或」二选一 —— 替换 1 个影响力，或 放置 1 个影响力。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 33);

            Assert.AreEqual("佣兵指挥部", card.Name);
            Assert.AreEqual(1, card.EntryEffects.Count);

            RewardOp node = card.EntryEffects[0];
            Assert.AreEqual(RewardKind.Alternative, node.Kind);
            Assert.AreEqual(2, node.Branches.Count);
            Assert.AreEqual(RewardKind.ReplaceInfluence, node.Branches[0].Ops[0].Kind);
            Assert.AreEqual(RewardKind.PlaceInfluence, node.Branches[1].Ops[0].Kind);
        }

        [Test]
        public void 设施牌数据_护航调度中心放置两个影响力()
        {
            // 136–137：入场效果 = 放置 2 个影响力。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 36);

            Assert.AreEqual("护航调度中心", card.Name);
            Assert.AreEqual(1, card.EntryEffects.Count);

            RewardOp node = card.EntryEffects[0];
            Assert.AreEqual(RewardKind.PlaceInfluence, node.Kind);
            Assert.AreEqual(2, node.Amount);
        }

        [Test]
        public void 设施牌数据_城市化区域入场效果为按相邻核心设施计金券()
        {
            // 114–116：入场效果 = 每有一个与核心指挥塔十字相邻的设施，获得 4 金券。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 14);

            Assert.AreEqual("城市化区域", card.Name);
            Assert.AreEqual(1, card.EntryEffects.Count);

            RewardOp node = card.EntryEffects[0];
            Assert.AreEqual(RewardKind.GainVouchersPerCount, node.Kind);
            Assert.AreEqual(CountSource.AdjacentCoreFacilities, node.Count);
            Assert.AreEqual(4, node.Amount);
        }

        [Test]
        public void 设施牌数据_城邦工业区费用修正为每非彩色种数减源岩()
        {
            // 121–123：建筑费用修正 = 面板上每有 1 种非彩色设施，少付 1 源岩（不是入场效果）。
            FacilityCardDef card = PlaceholderCardCatalog.Default.Facility(CardIds.FacilityBase + 21);

            Assert.AreEqual("城邦工业区", card.Name);
            Assert.IsEmpty(card.EntryEffects);
            Assert.IsNotNull(card.CostModifier);
            Assert.AreEqual(CountSource.NonColorFacilityKinds, card.CostModifier.Source);
            Assert.AreEqual(ResourceType.Orirock, card.CostModifier.Resource);
            Assert.AreEqual(1, card.CostModifier.PerCount);
        }

        [Test]
        public void 设施牌数据_核心指挥塔为彩色()
        {
            // 4 张核心指挥塔牌面都是彩色描边，颜色与玩家一致只体现在归属（§4.1 / §6.5b）。
            for (int color = 0; color < CardCounts.CoreCommandTowers; color++)
            {
                FacilityCardDef tower = PlaceholderCardCatalog.Default.Facility(
                    CardIds.CoreCommandTowerBase + color);

                Assert.AreEqual("核心指挥塔", tower.Name);
                Assert.AreEqual(FacilityColor.Rainbow, tower.Color);
            }
        }

        [Test]
        public void 建设_城邦工业区按面板非彩色设施种数减源岩()
        {
            GameEngine engine = NewBuildGame(4, 0);
            GameState state = engine.State;
            PlayerState player = state.Players[0];

            int card = CardIds.FacilityBase + 21; // 城邦工业区：源岩4 · 源石3 · 金券2
            state.FacilitySupply.Clear();
            state.FacilitySupply.Add(card);

            // 面板上先摆 1 张非彩色设施（异铁冶炼厂·蓝 117）→ 源岩费用由 4 降为 3。
            player.CityPanelSlots[0] = CardIds.FacilityBase + 17;

            foreach (ResourceType type in ResourceTypes.Basics) player.Resources.Add(type, 50);
            player.Vouchers += 50;

            int orirock = player.Resources.Orirock;
            int originium = player.Resources.Originium;
            int vouchers = player.Vouchers;

            CommandResult result = engine.Dispatch(new BuildFacilityCommand(
                0, card, FreeSlot(state), FacilityPayment.Resources));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(orirock - 3, player.Resources.Orirock);       // 4 - 1 种非彩色设施
            Assert.AreEqual(originium - 3, player.Resources.Originium);   // 源石不受修正影响
            Assert.AreEqual(vouchers - 2, player.Vouchers);               // 金券成分照付
        }

        private static int CountOf(FacilityCardDef card, ResourceType type)
        {
            int total = 0;
            foreach (RewardOp op in card.ResourceCost)
                if (op.Kind == RewardKind.PayResource && op.Resource == type) total += op.Amount;
            return total;
        }

        private static int CountVouchersOf(FacilityCardDef card)
        {
            int total = 0;
            foreach (RewardOp op in card.ResourceCost)
                if (op.Kind == RewardKind.PayVouchers) total += op.Amount;
            return total;
        }

        [Test]
        public void 建设_目标格已经有牌时非法()
        {
            GameEngine engine = NewBuildGame();
            GameState state = engine.State;
            int slot = FreeSlot(state);
            state.Players[0].CityPanelSlots[slot] = CardIds.FacilityBase;

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, state.FacilitySupply[0], slot, FacilityPayment.None));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 建设_选核心指挥塔那一格时非法()
        {
            GameEngine engine = NewBuildGame();

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, engine.State.FacilitySupply[0],
                    PlayerState.CityPanelCoreSlot, FacilityPayment.None));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 建设_位置下标越界时非法()
        {
            GameEngine engine = NewBuildGame();

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, engine.State.FacilitySupply[0],
                    PlayerState.CityPanelSlotCount, FacilityPayment.None));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 建设_牌不在设施供应区时非法()
        {
            GameEngine engine = NewBuildGame();
            int outside = FacilityOutsideSupply(engine.State);

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, outside, FreeSlot(engine.State), FacilityPayment.None));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 建设_面板放满时非法()
        {
            GameEngine engine = NewBuildGame();
            GameState state = engine.State;
            for (int i = 0; i < PlayerState.CityPanelSlotCount; i++)
                state.Players[0].CityPanelSlots[i] = CardIds.FacilityBase + i;

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, state.FacilitySupply[0], 0, FacilityPayment.None));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 建设_不是本回合玩家时非法()
        {
            GameEngine engine = NewBuildGame(4, 0);

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(1, engine.State.FacilitySupply[0],
                    FreeSlot(engine.State), FacilityPayment.None));

            Assert.IsFalse(result.Ok);
        }
    }
}
