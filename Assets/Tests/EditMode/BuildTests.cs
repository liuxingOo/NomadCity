using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// ④ 建设行动（§6.4e）：支付费用 → 放牌计分 → 入场效果 → 供应区补牌。
    /// 41 张设施牌的牌面数据尚未录入，因此这里只验证不依赖牌面数值的部分：
    /// 来源（供应区）、落点（城市面板空格 / 核心指挥塔占位）、关键词与补牌。
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

            CommandResult result = engine.Dispatch(
                new BuildFacilityCommand(0, card, slot, FacilityPayment.None));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(card, state.Players[0].CityPanelSlots[slot]);
            CollectionAssert.Contains(state.Players[0].BuiltFacilities, card);
            CollectionAssert.DoesNotContain(state.FacilitySupply, card);
            Assert.AreEqual(BoardSetup.FacilitySupplySize, state.FacilitySupply.Count);
            Assert.AreEqual(1, state.ActiveSeat); // 主要行动会推进顺位
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
