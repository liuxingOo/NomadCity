using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 快速行动「宣告城市样式」（§6.3b / §6.5b / §10.2）。
    /// 这里用只覆盖「颜色 + 一张样式牌的条件/分数」的目录顶替占位目录中的真实牌面，
    /// 专门验证命令本身：条件匹配、用到的格子标记已用、扣 1 个玩家标记、计分、以及快速行动不占名额。
    /// </summary>
    public sealed class DeclarePatternTests
    {
        private const int RedFacility = 100;
        private const int BlueFacility = 102;
        private const int PatternId = CardIds.CityPatternBase; // 500，本组测试用测试条件覆盖它的真实牌面

        private DeclareCatalog catalog;
        private GameEngine engine;
        private PlayerState player;

        [SetUp]
        public void SetUp()
        {
            catalog = new DeclareCatalog();
            engine = NewDeclareGame(catalog);
            player = engine.State.Players[0];

            catalog.SetFacilityColor(RedFacility, FacilityColor.Red);
            catalog.SetFacilityColor(BlueFacility, FacilityColor.Blue);
            catalog.SetFacilityColor(catalog.CoreCommandTowerId(player.Color), FacilityColor.Green);

            // 给第 1 张样式牌录入：条件 =「一格雷」，奖励 3 分。
            catalog.SetPattern(new CityPatternCardDef(PatternId, 1, "测试样式牌", 3, SingleCell(FacilityColor.Red)));
            Put(0, 0, RedFacility); // 面板左上角放一张红设施
        }

        [Test]
        public void 宣告_条件满足时成功_标记已用格_扣标记_计分()
        {
            int scoreBefore = player.Score;
            int slot = CityPanelQuery.SlotOf(0, 0);

            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(player.CityPanelUsed[slot], "§6.5b 步 1：用到的设施要标记为已使用。");
            Assert.AreEqual(29, player.MarkersInSupply, "§10.2 步 2：宣告要从供应堆拿出 1 个玩家标记。");
            Assert.AreEqual(scoreBefore + 3, player.Score, "§6.5b 步 3：宣告奖励分数立即结算。");

            CityPatternState pattern = FindPattern(engine.State, PatternId);
            Assert.AreEqual(1, pattern.CountOf(0), "宣告后玩家标记要放到这张样式牌上。");
        }

        [Test]
        public void 宣告_是快速行动_不占用主要行动名额()
        {
            Assert.AreEqual(0, engine.State.ActiveSeat, "先确认轮到座位 0。");

            engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            Assert.AreEqual(0, engine.State.ActiveSeat, "§6.3b：宣告是快速行动，不应推进行动顺位。");
            Assert.AreEqual(1, engine.State.ActionRound);
        }

        [Test]
        public void 宣告_同一张样式牌可以多次宣告_每次用不同设施()
        {
            Put(0, 1, RedFacility); // 再多一张红设施，好让第二次宣告用得上

            CommandResult first = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));
            CommandResult second = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            Assert.IsTrue(first.Ok, first.Error);
            Assert.IsTrue(second.Ok, second.Error);
            Assert.AreEqual(28, player.MarkersInSupply);
            Assert.AreEqual(2, FindPattern(engine.State, PatternId).CountOf(0),
                "§10.2：样式是公共目标，同一玩家可以多次组合出同一个样式。");
        }

        [Test]
        public void 宣告_用过的设施不能重复参与_凑不出条件时被拒绝()
        {
            // 面板上只有一张红设施，第一次宣告会把它标记为已用；第二次就再没有可用设施了。
            engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            CommandResult second = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            Assert.IsFalse(second.Ok, "§6.5b：同一设施只能用于一次宣告。");
        }

        [Test]
        public void 宣告_条件未录入的样式牌被拒绝()
        {
            // 占位目录里 500–505 的真实牌面已录入条件，这里把 501 换成一张没有条件的牌来验证守卫。
            catalog.SetPattern(new CityPatternCardDef(CardIds.CityPatternBase + 1, 1, "测试样式牌·条件未录入"));

            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(0, CardIds.CityPatternBase + 1));

            Assert.IsFalse(result.Ok, "牌面条件未录入时不允许宣告。");
        }

        [Test]
        public void 宣告_面板凑不出条件时被拒绝()
        {
            catalog.SetPattern(new CityPatternCardDef(PatternId, 1, "测试样式牌", 3, SingleCell(FacilityColor.Blue)));
            // 面板上只有红设施（左上角），凑不出「一格雷」改后的「一格蓝」。

            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 宣告_供应堆没有玩家标记时被拒绝()
        {
            player.MarkersInSupply = 0;

            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId));

            Assert.IsFalse(result.Ok, "§10.2 步 2：没有玩家标记则无法宣告。");
        }

        [Test]
        public void 宣告_不是当前行动玩家时被拒绝()
        {
            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(1, PatternId));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 宣告_不在场上的样式牌被拒绝()
        {
            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(0, CardIds.CityPatternBase + 99));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 宣告_多组摆法时_指定用哪几格_只标记挑中的那一组()
        {
            // 面板上有两张红设施（左上角 与 第一行第 3 格），「一格雷」因此有 2 组摆法。
            Put(0, 2, RedFacility);

            List<PatternMatch> matches = CityPanelQuery.FindAll(player, catalog, SingleCell(FacilityColor.Red));
            Assert.AreEqual(2, matches.Count, "两张红设施就是 2 组摆法。");

            int picked = matches[1].Slots[0];
            var command = new DeclareCityPatternCommand(0, PatternId, placementIndex: 1);
            CommandResult result = engine.Dispatch(command);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(picked, command.Match.Slots[0], "应当用玩家挑的那一组摆法。");
            Assert.IsTrue(player.CityPanelUsed[picked], "挑中的那一格要标记为已用于宣告。");
            Assert.IsFalse(player.CityPanelUsed[matches[0].Slots[0]],
                "没挑中的那一格不该被标记，留给后面的宣告用。");
        }

        [Test]
        public void 宣告_指定的摆法下标越界时被拒绝()
        {
            CommandResult result = engine.Dispatch(new DeclareCityPatternCommand(0, PatternId, placementIndex: 99));

            Assert.IsFalse(result.Ok, "越界的摆法下标应当被拒绝。");
        }

        // —— 工具 ——

        private static PatternCondition SingleCell(FacilityColor color) =>
            new PatternCondition(new PatternCell(0, 0, color));

        private void Put(int row, int column, int facilityId) =>
            player.CityPanelSlots[CityPanelQuery.SlotOf(row, column)] = facilityId;

        private static CityPatternState FindPattern(GameState state, int cardId)
        {
            for (int i = 0; i < state.CityPatternCards.Count; i++)
                if (state.CityPatternCards[i] == cardId) return state.CityPatternStates[i];
            return null;
        }

        private static GameEngine NewDeclareGame(ICardCatalog cardCatalog)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));

            var engine = GameEngine.Create(new GameSetup(4), 0, map, cardCatalog, new Random(1));
            engine.Dispatch(new StartGameCommand(0));   // 回合 1，盖放阶段
            engine.Dispatch(new AdvancePhaseCommand()); // → 玩家行动阶段
            return engine;
        }

        /// <summary>只覆盖设施颜色与城市样式牌定义的目录：其余查询一律转发给占位目录。</summary>
        private sealed class DeclareCatalog : ICardCatalog
        {
            private readonly Dictionary<int, FacilityCardDef> facilityOverrides = new Dictionary<int, FacilityCardDef>();
            private readonly Dictionary<int, CityPatternCardDef> patternOverrides = new Dictionary<int, CityPatternCardDef>();
            private readonly ICardCatalog fallback = PlaceholderCardCatalog.Default;

            public void SetFacilityColor(int facilityId, FacilityColor color) =>
                facilityOverrides[facilityId] = new FacilityCardDef(facilityId, FacilityKind.Standard,
                    $"测试设施 {facilityId}", color);

            public void SetPattern(CityPatternCardDef def) => patternOverrides[def.Id] = def;

            public CharacterCardDef Character(int id) => fallback.Character(id);

            public FacilityCardDef Facility(int id) =>
                facilityOverrides.TryGetValue(id, out FacilityCardDef def) ? def : fallback.Facility(id);

            public EventCardDef Event(int id) => fallback.Event(id);

            public CityPatternCardDef CityPattern(int id) =>
                patternOverrides.TryGetValue(id, out CityPatternCardDef def) ? def : fallback.CityPattern(id);

            public IReadOnlyList<int> CharacterCardIds(PlayerColor color) => fallback.CharacterCardIds(color);

            public IReadOnlyList<int> FacilityDeckIds(bool enterpriseEnabled) => fallback.FacilityDeckIds(enterpriseEnabled);

            public IReadOnlyList<int> EventDeckIds(NodeColor backColor, bool enterpriseEnabled) =>
                fallback.EventDeckIds(backColor, enterpriseEnabled);

            public IReadOnlyList<int> CityPatternIds(bool enterpriseEnabled) => fallback.CityPatternIds(enterpriseEnabled);

            public int CoreCommandTowerId(PlayerColor color) => fallback.CoreCommandTowerId(color);

            public IReadOnlyList<int> ExtensionHubIds() => fallback.ExtensionHubIds();
        }
    }
}
