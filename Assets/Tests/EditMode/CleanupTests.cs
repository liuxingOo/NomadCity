using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 收尾阶段（§8）的单元测试：样式标记复位、角色牌回收、回合作用域复位。
    /// </summary>
    public sealed class CleanupTests
    {
        private static GameState NewState(int playerCount = 4)
        {
            var state = new GameState(new GameSetup(playerCount));
            for (int seat = 0; seat < playerCount; seat++)
            {
                state.Players.Add(new PlayerState(seat, (PlayerColor)seat) { MarkersInSupply = 30 });
            }
            return state;
        }

        // ————————————————————— 样式标记复位 —————————————————————

        [Test]
        public void 收尾_I级样式牌_已使用区的标记放回未使用区()
        {
            GameState state = NewState();
            state.CityPatternStates.Add(new CityPatternState(500, 1));
            CityPatternState card = state.CityPatternStates[0];
            card.PlaceMarker(0, 1); // 已使用

            CleanupResolver.ResetPatternMarkers(state);

            Assert.AreEqual(0, card.Markers[0].Position);
        }

        [Test]
        public void 收尾_II级样式牌_已使用格的标记向下移动一格()
        {
            GameState state = NewState();
            state.CityPatternStates.Add(new CityPatternState(504, 2));
            CityPatternState card = state.CityPatternStates[0];
            card.PlaceMarker(0, 1); // 「2」后的已使用格 → 应落到「1」
            card.PlaceMarker(1, 3); // 「1」后的已使用格 → 应落到「0」
            card.PlaceMarker(2, 0); // 「2」，可用，不动
            card.PlaceMarker(3, 4); // 「0」，不动

            CleanupResolver.ResetPatternMarkers(state);

            Assert.AreEqual(2, card.Markers[0].Position);
            Assert.AreEqual(4, card.Markers[1].Position);
            Assert.AreEqual(0, card.Markers[2].Position);
            Assert.AreEqual(4, card.Markers[3].Position);
        }

        // ————————————————————— 角色牌回收（§10.1.4） —————————————————————

        [Test]
        public void 收尾_没用过角色牌_盖放牌与弃牌区一起回到手牌()
        {
            GameState state = NewState();
            PlayerState player = state.Players[0];
            player.HandCharacterCards.Add(0);
            player.PlacedCharacterCard = 1;
            player.DiscardCharacterCards.Add(2);
            player.DiscardCharacterCards.Add(3);
            player.UsedCharacterCardThisTurn = false;

            CleanupResolver.ReclaimCharacterCards(state);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, player.HandCharacterCards);
            Assert.IsEmpty(player.DiscardCharacterCards);
            Assert.AreEqual(-1, player.PlacedCharacterCard);
        }

        [Test]
        public void 收尾_用过了角色牌_弃牌区不回收()
        {
            GameState state = NewState();
            PlayerState player = state.Players[0];
            player.HandCharacterCards.Add(0);
            player.PlacedCharacterCard = -1; // 已用掉，牌已进弃牌区
            player.DiscardCharacterCards.Add(1);
            player.UsedCharacterCardThisTurn = true;

            CleanupResolver.ReclaimCharacterCards(state);

            CollectionAssert.AreEqual(new[] { 0 }, player.HandCharacterCards);
            CollectionAssert.AreEqual(new[] { 1 }, player.DiscardCharacterCards);
        }

        // ————————————————————— 回合作用域复位 —————————————————————

        [Test]
        public void 收尾_清空本回合作用域的标志()
        {
            GameState state = NewState();
            PlayerState player = state.Players[0];
            player.UsedCharacterCardThisTurn = true;
            player.UsedPatternActionsThisTurn.Add(500);

            CleanupResolver.Resolve(state);

            Assert.IsFalse(player.UsedCharacterCardThisTurn);
            Assert.IsEmpty(player.UsedPatternActionsThisTurn);
        }

        // ————————————————————— 与流程的衔接 —————————————————————

        [Test]
        public void 收尾_推进阶段从采集进入收尾时自动执行收尾结算()
        {
            GameState state = NewState();
            state.CityPatternStates.Add(new CityPatternState(500, 1));
            state.CityPatternStates[0].PlaceMarker(0, 1);
            state.Players[0].UsedPatternActionsThisTurn.Add(500);
            state.Phase = TurnPhase.Harvest;

            TurnFlow.AdvancePhase(state);

            Assert.AreEqual(TurnPhase.Cleanup, state.Phase);
            Assert.AreEqual(0, state.CityPatternStates[0].Markers[0].Position);
            Assert.IsEmpty(state.Players[0].UsedPatternActionsThisTurn);
        }

        [Test]
        public void 开局_建立样式牌标记状态_与样式牌一一对应()
        {
            GameEngine engine = GameEngine.Create(new GameSetup(4), 0, null,
                PlaceholderCardCatalog.Default, new Random(1));
            GameState state = engine.State;

            Assert.AreEqual(state.CityPatternCards.Count, state.CityPatternStates.Count);
            for (int i = 0; i < state.CityPatternCards.Count; i++)
            {
                CityPatternState pattern = state.CityPatternStates[i];
                Assert.AreEqual(state.CityPatternCards[i], pattern.CardId);
                Assert.AreEqual(PlaceholderCardCatalog.Default.CityPattern(pattern.CardId).Level, pattern.Level);
            }
        }
    }
}