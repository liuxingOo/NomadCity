using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 城市面板查询器（§6.5b / §10.2）：位置 + 颜色能不能凑成某个样式条件。
    /// 41 张设施牌的颜色还没录入，所以这里用一个只覆盖颜色的目录顶替，
    /// 专门验证匹配规则本身：平移、旋转（不含翻转）、彩色当任意色、已用过的格子不再参与。
    /// </summary>
    public sealed class CityPanelQueryTests
    {
        // 本组测试用到的设施牌 id（都在普通设施号段 100–140 内）。
        private const int RedFacility = 100;
        private const int RainbowFacility = 101;
        private const int BlueFacility = 102;
        private const int YellowFacility = 103;

        private ColorCatalog catalog;
        private PlayerState player;

        [SetUp]
        public void SetUp()
        {
            catalog = new ColorCatalog();
            GameEngine engine = NewQueryGame(catalog);
            player = engine.State.Players[0];

            catalog.SetColor(RedFacility, FacilityColor.Red);
            catalog.SetColor(RainbowFacility, FacilityColor.Rainbow);
            catalog.SetColor(BlueFacility, FacilityColor.Blue);
            catalog.SetColor(YellowFacility, FacilityColor.Yellow);
            catalog.SetColor(catalog.CoreCommandTowerId(player.Color), FacilityColor.Green);
        }

        [Test]
        public void 查询_空格取不到颜色_也不能参与匹配()
        {
            Assert.IsNull(CityPanelQuery.ColorAt(player, catalog, CityPanelQuery.SlotOf(0, 0)), "空格不该有颜色。");

            PatternMatch match;
            Assert.IsFalse(CityPanelQuery.TryFind(player, catalog, SingleCell(FacilityColor.Red), out match),
                "开局面板上只有第 8 格的核心指挥塔（本组测试把它设为绿色），其余都是空格；" +
                "空格取不到颜色，所以要求「一格雷」以外的组合不该匹配成功。");
        }

        [Test]
        public void 查询_颜色按卡牌目录解析()
        {
            Put(0, 0, RedFacility);

            Assert.AreEqual(FacilityColor.Red, CityPanelQuery.ColorAt(player, catalog, CityPanelQuery.SlotOf(0, 0)));
        }

        [Test]
        public void 查询_彩色设施可以当作任意颜色()
        {
            Put(0, 0, RainbowFacility);

            PatternMatch match;
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, SingleCell(FacilityColor.Blue), out match),
                "彩色设施在组合里应当能当成任意颜色。");
            Assert.AreEqual(CityPanelQuery.SlotOf(0, 0), match.Slots[0]);
        }

        [Test]
        public void 查询_斜分格_允许的颜色其一即可_其余颜色不行()
        {
            // §10.2：牌面画成斜分的方块代表「多色其一即可」，这里取「蓝 / 黄」。
            var condition = new PatternCondition(
                new PatternCell(0, 0, FacilityColor.Blue, FacilityColor.Yellow));

            Put(0, 0, BlueFacility);
            PatternMatch match;
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, condition, out match), "斜分格里的蓝色应当满足条件。");

            Put(0, 0, YellowFacility);
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, condition, out match), "斜分格里的黄色应当满足条件。");

            Put(0, 0, RedFacility);
            Assert.IsFalse(CityPanelQuery.TryFind(player, catalog, condition, out match), "红色不在斜分格允许的颜色里。");
        }

        [Test]
        public void 查询_彩色设施可以满足斜分格()
        {
            var condition = new PatternCondition(
                new PatternCell(0, 0, FacilityColor.Blue, FacilityColor.Yellow));

            Put(0, 0, RainbowFacility);

            PatternMatch match;
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, condition, out match),
                "§10.2：彩色设施任何时候都能当作任意颜色，斜分格也不例外。");
            Assert.AreEqual(CityPanelQuery.SlotOf(0, 0), match.Slots[0]);
        }

        [Test]
        public void 查询_条件可以整体平移_用到的格子跟着变()
        {
            Put(1, 1, RedFacility); // 第 2 行中间

            PatternMatch match;
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, SingleCell(FacilityColor.Red), out match));
            Assert.AreEqual(CityPanelQuery.SlotOf(1, 1), match.Slots[0]);
        }

        [Test]
        public void 列出所有摆法_同一组格子只出现一次()
        {
            Put(0, 0, RedFacility);
            Put(0, 2, RedFacility);

            List<PatternMatch> matches = CityPanelQuery.FindAll(player, catalog, SingleCell(FacilityColor.Red));

            // 单格条件转 4 个角度都命中同一格，所以只该剩 2 组不同的摆法（两张红设施各一组）。
            Assert.AreEqual(2, matches.Count, "旋转带出来的重复摆法应当被丢掉。");
            Assert.AreEqual(CityPanelQuery.SlotOf(0, 0), matches[0].Slots[0]);
            Assert.AreEqual(CityPanelQuery.SlotOf(0, 2), matches[1].Slots[0]);
        }

        [Test]
        public void 查询_条件可以旋转()
        {
            // 条件是「左红右蓝」，面板上摆的是竖着的「上红下蓝」→ 只能靠旋转 90° 凑出来。
            var condition = new PatternCondition(
                new PatternCell(0, 0, FacilityColor.Red),
                new PatternCell(0, 1, FacilityColor.Blue));

            Put(0, 0, RedFacility);
            Put(1, 0, BlueFacility);

            PatternMatch match;
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, condition, out match));
            Assert.AreEqual(90, match.RotationDegrees);
            CollectionAssert.AreEqual(
                new[] { CityPanelQuery.SlotOf(0, 0), CityPanelQuery.SlotOf(1, 0) }, match.Slots);
        }

        [Test]
        public void 查询_已经用过的格子不再参与匹配()
        {
            Put(1, 1, RedFacility);
            player.MarkPanelSlotUsed(CityPanelQuery.SlotOf(1, 1));

            PatternMatch match;
            Assert.IsFalse(CityPanelQuery.TryFind(player, catalog, SingleCell(FacilityColor.Red), out match),
                "§6.5b：每个设施只能用于一次宣告，用过的格子不能再进新的组合。");
        }

        [Test]
        public void 查询_核心指挥塔也可以参与组合()
        {
            // 面板上除了开局预置的核心指挥塔（第 8 格）之外没有别的设施。
            int coreSlot = PlayerState.CityPanelCoreSlot;

            PatternMatch match;
            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, SingleCell(FacilityColor.Green), out match),
                "核心指挥塔虽然不是建设出来的，但一样可以参与城市样式组合（已与规则持有者确认）。");
            Assert.AreEqual(coreSlot, match.Slots[0]);
        }

        [Test]
        public void 查询_格子不够时匹配失败_凑齐三格后成功()
        {
            var condition = new PatternCondition(
                new PatternCell(0, 0, FacilityColor.Red),
                new PatternCell(0, 1, FacilityColor.Red),
                new PatternCell(0, 2, FacilityColor.Red));

            Put(0, 0, RedFacility);
            Put(0, 1, RedFacility);

            PatternMatch match;
            Assert.IsFalse(CityPanelQuery.TryFind(player, catalog, condition, out match),
                "只有两格红设施，凑不出三连。");

            Put(0, 2, RedFacility);

            Assert.IsTrue(CityPanelQuery.TryFind(player, catalog, condition, out match));
            CollectionAssert.AreEqual(
                new[] { 0, 1, 2 }, match.Slots);
        }

        // —— 工具 ——

        private static PatternCondition SingleCell(FacilityColor color) =>
            new PatternCondition(new PatternCell(0, 0, color));

        private void Put(int row, int column, int facilityId) =>
            player.CityPanelSlots[CityPanelQuery.SlotOf(row, column)] = facilityId;

        private static GameEngine NewQueryGame(ColorCatalog cardCatalog)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));

            return GameEngine.Create(new GameSetup(4), 0, map, cardCatalog, new Random(1));
        }

        /// <summary>只覆盖设施颜色的目录：其余查询一律转发给占位目录。</summary>
        private sealed class ColorCatalog : ICardCatalog
        {
            private readonly Dictionary<int, FacilityCardDef> overrides = new Dictionary<int, FacilityCardDef>();
            private readonly ICardCatalog fallback = PlaceholderCardCatalog.Default;

            public void SetColor(int facilityId, FacilityColor color) =>
                overrides[facilityId] = new FacilityCardDef(facilityId, FacilityKind.Standard,
                    $"测试设施 {facilityId}", color);

            public CharacterCardDef Character(int id) => fallback.Character(id);

            public FacilityCardDef Facility(int id) =>
                overrides.TryGetValue(id, out FacilityCardDef def) ? def : fallback.Facility(id);

            public EventCardDef Event(int id) => fallback.Event(id);

            public CityPatternCardDef CityPattern(int id) => fallback.CityPattern(id);

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
