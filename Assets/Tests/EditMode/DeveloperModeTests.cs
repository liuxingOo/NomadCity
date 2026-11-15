using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 新开发者模式的纯 C# 断言：命令只在 state.DeveloperMode 下可用，
    /// 并且真的能改轮次、玩家数值、城市面板、城市样式、角色牌与企业等级。
    /// </summary>
    public sealed class DeveloperModeTests
    {
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
            foreach (MapNodeState node in map.Nodes) node.HasResourceToken = true;
            return map;
        }

        private void NewGame(bool developerMode, bool enterprise = false)
        {
            GameSetup setup = new GameSetup(4,
                enterprise ? ExpansionFlags.Enterprise : ExpansionFlags.None);
            engine = GameEngine.Create(setup, 0, SmallMap(), PlaceholderCardCatalog.Default, new Random(7));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);
            Assert.IsTrue(engine.Dispatch(new AdvancePhaseCommand()).Ok);

            if (enterprise)
            {
                engine.State.Enterprises.Reset(4);
                engine.State.Enterprises.DraftDone = true;
            }

            foreach (PlayerState seat in engine.State.Players) seat.SetupDiscardDone = true;

            state = engine.State;
            state.Player(0).MobileCityNodeId = 0;
            state.Player(0).MarkersInSupply = 30;
            state.DeveloperMode = developerMode;
            player = state.Player(0);
        }

        [Test]
        public void 非开发者模式时所有开发者命令都被拒绝()
        {
            NewGame(developerMode: false);

            Assert.IsFalse(engine.Dispatch(new DeveloperSetTurnCommand(3, 1)).Ok);
            Assert.IsFalse(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Score, 10)).Ok);
            Assert.IsFalse(engine.Dispatch(new DeveloperBuildFacilityCommand(0,
                CardIds.FacilityBase, 0)).Ok);
            Assert.IsFalse(engine.Dispatch(new DeveloperRemoveFacilityCommand(0, -1)).Ok);
            Assert.IsFalse(engine.Dispatch(new DeveloperResetPanelUseCommand(0)).Ok);
            Assert.IsFalse(engine.Dispatch(new DeveloperDeclareCityPatternCommand(0,
                CardIds.CityPatternBase)).Ok);
            Assert.IsFalse(engine.Dispatch(new DeveloperUseCharacterCardCommand(0,
                player.HandCharacterCards.Count > 0 ? player.HandCharacterCards[0] : CardIds.CharacterBase,
                CharacterEffectKind.Strategy)).Ok);
            Assert.IsFalse(engine.Dispatch(new DeveloperSetEnterpriseLevelCommand(0, 0, 2)).Ok);
        }

        [Test]
        public void 设置回合与行动轮次()
        {
            NewGame(developerMode: true);

            Assert.IsTrue(engine.Dispatch(new DeveloperSetTurnCommand(5, 2)).Ok);
            Assert.AreEqual(5, state.TurnNumber);
            Assert.AreEqual(2, state.ActionRound);

            Assert.IsTrue(engine.Dispatch(new DeveloperSetTurnCommand(1, 1)).Ok);
            Assert.AreEqual(1, state.TurnNumber);
            Assert.AreEqual(1, state.ActionRound);
        }

        [Test]
        public void 修改分数金券标记与四种资源()
        {
            NewGame(developerMode: true);

            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Score, 42)).Ok);
            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Vouchers, 9)).Ok);
            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Markers, 12)).Ok);
            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Orirock, 7)).Ok);
            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Originium, 6)).Ok);
            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.Oriron, 5)).Ok);
            Assert.IsTrue(engine.Dispatch(
                new DeveloperSetPlayerStatCommand(0, DeveloperPlayerStat.OriginitePrime, 4)).Ok);

            Assert.AreEqual(42, player.Score);
            Assert.AreEqual(9, player.Vouchers);
            Assert.AreEqual(12, player.MarkersInSupply);
            Assert.AreEqual(7, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(6, player.Resources.Get(ResourceType.Originium));
            Assert.AreEqual(5, player.Resources.Get(ResourceType.Oriron));
            Assert.AreEqual(4, player.Resources.Get(ResourceType.OriginitePrime));
        }

        [Test]
        public void 从牌堆指定设施建设_移除设施_重置倒置()
        {
            NewGame(developerMode: true);

            int facilityId = state.FacilityDeck[0];
            FacilityCardDef card = state.Catalog.Facility(facilityId);
            int scoreBefore = player.Score;
            int deckBefore = state.FacilityDeck.Count;
            int coreTower = player.CityPanelSlots[PlayerState.CityPanelCoreSlot];

            Assert.IsTrue(engine.Dispatch(new DeveloperBuildFacilityCommand(0, facilityId, 0)).Ok);
            Assert.AreEqual(facilityId, player.CityPanelSlots[0]);
            Assert.AreEqual(scoreBefore + card.Score, player.Score);
            Assert.AreEqual(deckBefore - 1, state.FacilityDeck.Count);
            CollectionAssert.DoesNotContain(state.FacilityDeck, facilityId);

            player.CityPanelUsed[0] = true;
            Assert.IsTrue(engine.Dispatch(new DeveloperResetPanelUseCommand(0, 0)).Ok);
            Assert.IsFalse(player.CityPanelUsed[0]);

            player.CityPanelUsed[1] = true;
            Assert.IsTrue(engine.Dispatch(new DeveloperRemoveFacilityCommand(0, -1)).Ok);
            for (int slot = 0; slot < PlayerState.CityPanelSlotCount; slot++)
            {
                if (slot == PlayerState.CityPanelCoreSlot) continue;
                Assert.AreEqual(-1, player.CityPanelSlots[slot]);
                Assert.IsFalse(player.CityPanelUsed[slot]);
            }

            Assert.AreEqual(coreTower, player.CityPanelSlots[PlayerState.CityPanelCoreSlot],
                "核心指挥塔必须保留。");
            CollectionAssert.Contains(state.FacilityDeck, facilityId);
        }

        [Test]
        public void 从全部城市样式里指定宣告()
        {
            NewGame(developerMode: true);

            int patternId = CardIds.CityPatternBase + CardCounts.CityPatterns - 1;
            CityPatternCardDef card = state.Catalog.CityPattern(patternId);
            int markersBefore = player.MarkersInSupply;
            int scoreBefore = player.Score;

            Assert.IsTrue(engine.Dispatch(new DeveloperDeclareCityPatternCommand(0, patternId)).Ok);
            Assert.AreEqual(markersBefore - 1, player.MarkersInSupply);
            Assert.AreEqual(scoreBefore + card.Score, player.Score);
            CollectionAssert.Contains(state.CityPatternCards, patternId);
            Assert.IsNotNull(state.PatternStateOf(patternId));
        }

        [Test]
        public void 从手牌直接使用角色牌不受盖放限制()
        {
            NewGame(developerMode: true);

            int cardId = -1;
            CharacterEffectKind effect = CharacterEffectKind.Strategy;
            foreach (int id in player.HandCharacterCards)
            {
                CharacterCardDef def = state.Catalog.Character(id);
                if (def == null) continue;
                if (def.StrategyOps.Count > 0)
                {
                    cardId = id;
                    effect = CharacterEffectKind.Strategy;
                    break;
                }
                if (def.TacticsOps.Count > 0 && cardId < 0)
                {
                    cardId = id;
                    effect = CharacterEffectKind.Tactics;
                }
            }

            Assert.GreaterOrEqual(cardId, 0, "手牌里至少要有一张带策略或计谋的角色牌。");
            Assert.AreNotEqual(player.PlacedCharacterCard, cardId, "应选一张没有盖放的手牌来证明不受盖放限制。");

            Assert.IsTrue(engine.Dispatch(
                new DeveloperUseCharacterCardCommand(0, cardId, effect)).Ok);
        }

        [Test]
        public void 企业等级可以设到未上场的企业()
        {
            NewGame(developerMode: true, enterprise: true);

            Assert.IsFalse(state.Enterprises.IsActive(0));
            Assert.IsTrue(engine.Dispatch(new DeveloperSetEnterpriseLevelCommand(0, 0, 3)).Ok);
            Assert.IsTrue(state.Enterprises.IsActive(0));
            Assert.AreEqual(3, state.Enterprises.LevelOf(0, 0));
        }
    }
}
