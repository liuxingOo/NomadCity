using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 通用效果执行器（§11）的单元测试。全部用 EditMode 断言，不需要预制体、不依赖随机数。
    /// 测试地图是一条直链：N0—R0—N1—R1—N2，每个资源点 2 格、每条航道 1 格。
    /// </summary>
    public sealed class EffectResolverTests
    {
        private static GameState State(int playerCount = 4, ExpansionFlags expansions = ExpansionFlags.None)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 1));

            foreach (MapNodeState node in map.Nodes)
            {
                node.HasResourceToken = true;
                node.TokenResource = ResourceType.Orirock;
                node.TokenAmount = 1;
            }

            var state = new GameState(new GameSetup(playerCount, expansions));
            for (int seat = 0; seat < playerCount; seat++)
            {
                state.Players.Add(new PlayerState(seat, (PlayerColor)seat) { MarkersInSupply = 30 });
            }

            state.Map = map;
            state.Catalog = PlaceholderCardCatalog.Default; // 条件计数（非彩色设施种数）需要查设施定义
            return state;
        }

        // ————————————————————— 基础效果 —————————————————————

        [Test]
        public void 获得资源_金券_分数_均写入玩家()
        {
            GameState state = State();

            EffectResolver.Execute(state, 0, new[]
            {
                RewardOp.Gain(ResourceType.Orirock, 4),
                RewardOp.Vouchers(7),
                RewardOp.Score(2),
            });

            Assert.AreEqual(4, state.Players[0].Resources.Orirock);
            Assert.AreEqual(7, state.Players[0].Vouchers);
            Assert.AreEqual(2, state.Players[0].Score);
        }

        [Test]
        public void 获得至纯源石_写入玩家()
        {
            GameState state = State();

            EffectResolver.Execute(state, 0, new[] { RewardOp.Gain(ResourceType.OriginitePrime, 2) });

            Assert.AreEqual(2, state.Players[0].Resources.OriginitePrime);
        }

        [Test]
        public void 支付不足的项被跳过_后续效果仍执行()
        {
            GameState state = State();
            state.Players[0].Vouchers = 3;

            EffectReport report = EffectResolver.Execute(state, 0, new[]
            {
                RewardOp.PayVouchers(5),
                RewardOp.Gain(ResourceType.Orirock, 2),
            });

            Assert.IsFalse(report.Entries[0].Applied); // 金券不足，未扣
            Assert.AreEqual(3, state.Players[0].Vouchers);
            Assert.IsTrue(report.Entries[1].Applied);  // 尽量执行：后面的照做
            Assert.AreEqual(2, state.Players[0].Resources.Orirock);
        }

        [Test]
        public void 失去分数_不会低于零()
        {
            GameState state = State();
            state.Players[0].Score = 1;

            EffectResolver.Execute(state, 0, new[] { RewardOp.LoseScore(5) });

            Assert.AreEqual(0, state.Players[0].Score);
        }

        [Test]
        public void 对手获得金券_只发给其他玩家()
        {
            GameState state = State();

            EffectResolver.Execute(state, 0, new[] { RewardOp.OpponentsGainVouchers(3) });

            Assert.AreEqual(0, state.Players[0].Vouchers);
            Assert.AreEqual(3, state.Players[1].Vouchers);
            Assert.AreEqual(3, state.Players[2].Vouchers);
            Assert.AreEqual(3, state.Players[3].Vouchers);
        }

        // ————————————————————— 放置影响力 —————————————————————

        [Test]
        public void 部署规则落点_自动取第一个合法空格()
        {
            GameState state = State();

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.Influence(InfluencePlacementScope.DeployRules) });

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(29, state.Players[0].MarkersInSupply);
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0)); // 第一候选＝N0 槽 0
        }

        [Test]
        public void 没有玩家标记时_放置被跳过()
        {
            GameState state = State();
            state.Players[0].MarkersInSupply = 0;

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.Influence(InfluencePlacementScope.DeployRules) });

            Assert.IsFalse(report.AnyApplied);
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0));
        }

        [Test]
        public void 限定落点_仅事件资源点或其相邻航道()
        {
            GameState state = State();
            state.Map.Node(1).Slots.Place(0, 1); // 事件点 N1 自己的两格先占满
            state.Map.Node(1).Slots.Place(1, 1);

            EffectResolver.Execute(state, 0,
                new[] { RewardOp.Influence(InfluencePlacementScope.ThisNodeOrAdjacentRoutes) },
                anchorNodeId: 1);

            // N1 无空位，落到与之相连的 R0（R1 也相邻，但 R0 在前）。
            Assert.AreEqual(0, state.Map.Route(0).Slots.OwnerOf(0));
            Assert.AreEqual(-1, state.Map.Node(2).Slots.OwnerOf(0)); // 未越界到不相邻的点
        }

        [Test]
        public void 相邻航道限定_不包含资源点本身()
        {
            GameState state = State();

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.Influence(InfluencePlacementScope.AdjacentRoutesOnly) },
                anchorNodeId: 1);

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(-1, state.Map.Node(1).Slots.OwnerOf(0)); // 点本身不放
            Assert.AreEqual(0, state.Map.Route(0).Slots.OwnerOf(0));
        }

        // ————————————————————— 二选一（§11.4） —————————————————————

        [Test]
        public void 二选一_取第一分支执行并跳过另一分支()
        {
            GameState state = State();
            state.Players[0].Score = 5;

            // 默认选择器（FirstAvailableChoices）总是取第一个分支。
            var op = RewardOp.Alternative(
                new EffectBranch("获得 2 源岩", RewardOp.Gain(ResourceType.Orirock, 2)),
                new EffectBranch("失去 1 分", RewardOp.LoseScore(1)));

            EffectReport report = EffectResolver.Execute(state, 0, new[] { op });

            Assert.IsTrue(report.Entries[0].Applied);
            Assert.AreEqual(2, state.Players[0].Resources.Orirock);
            Assert.AreEqual(5, state.Players[0].Score); // 另一分支没有被执行
        }

        // ————————————————————— 拿走／移动／替换影响力 —————————————————————

        [Test]
        public void 拿走影响力_对手的标记退回其供应堆()
        {
            GameState state = State();
            state.Map.Node(0).Slots.Place(0, 1);
            state.Players[1].MarkersInSupply = 20;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.RemoveInfluence() });

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0));
            Assert.AreEqual(21, state.Players[1].MarkersInSupply);
        }

        [Test]
        public void 拿走影响力_无影响力可拿时跳过但不阻断后续效果()
        {
            GameState state = State();

            EffectReport report = EffectResolver.Execute(state, 0, new[]
            {
                RewardOp.RemoveInfluence(),
                RewardOp.Gain(ResourceType.Orirock, 1),
            });

            Assert.IsFalse(report.Entries[0].Applied);
            Assert.IsTrue(report.Entries[1].Applied); // 尽量执行
            Assert.AreEqual(1, state.Players[0].Resources.Orirock);
        }

        [Test]
        public void 移动影响力_不改变标记总数()
        {
            GameState state = State();
            state.Map.Node(0).Slots.Place(0, 0);
            state.Players[0].MarkersInSupply = 29;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.RelocateInfluence() });

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0));
            Assert.AreEqual(29, state.Players[0].MarkersInSupply);   // 清一格、占一格，总数不变
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(1));  // 落到下一个合法空格
        }

        [Test]
        public void 替换影响力_在同格放置自己的影响力()
        {
            GameState state = State();
            state.Map.Node(0).Slots.Place(0, 1); // 对手的影响力在 N0 槽 0
            state.Players[1].MarkersInSupply = 20;
            state.Players[0].MarkersInSupply = 10;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.ReplaceInfluence() });

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0)); // 同一个空格换成自己的
            Assert.AreEqual(9, state.Players[0].MarkersInSupply);
            Assert.AreEqual(21, state.Players[1].MarkersInSupply);
        }

        [Test]
        public void 替换影响力_该点有对手城市时只移除不放置()
        {
            GameState state = State();
            state.Map.Node(0).Slots.Place(0, 1);
            state.Players[1].MobileCityNodeId = 0; // 对手城市停靠在 N0
            state.Players[0].MarkersInSupply = 10;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.ReplaceInfluence() });

            Assert.IsTrue(report.Entries[0].Applied); // 移除已经生效
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0));
            Assert.AreEqual(10, state.Players[0].MarkersInSupply); // 没有花掉标记
            StringAssert.Contains("无法在同一空格放置", report.Entries[0].Note);
        }

        // ————————————————————— 条件计数 —————————————————————
        // 「城市化区域」＝每有一个与核心指挥塔十字相邻的设施获得 4 金券；
        // 「城邦工业区」的费用修正＝面板上每 1 种非彩色设施少付 1 源岩（见 BuildTests）。

        [Test]
        public void 条件计数_按核心格十字相邻的设施数获得金券()
        {
            GameState state = State();
            // 核心指挥塔在第 8 格（下标 7），十字相邻 = {4, 10, 6, 8}。
            state.Players[0].CityPanelSlots[4] = CardIds.FacilityBase;      // 城邦行政区
            state.Players[0].CityPanelSlots[10] = CardIds.FacilityBase + 5; // 另一张设施

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.VouchersPerCount(CountSource.AdjacentCoreFacilities, 4) });

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(8, state.Players[0].Vouchers); // 2 个 × 4
        }

        [Test]
        public void 条件计数_非彩色设施按不同牌名去重且排除彩色()
        {
            GameState state = State();
            // 117／131 同名「异铁冶炼厂」（蓝 / 红），只算 1 种；
            // 100 城邦行政区与核心指挥塔（200）都是彩色，不算。
            state.Players[0].CityPanelSlots[0] = CardIds.FacilityBase + 17;   // 异铁冶炼厂（蓝）
            state.Players[0].CityPanelSlots[1] = CardIds.FacilityBase + 31;   // 异铁冶炼厂（红，同名）
            state.Players[0].CityPanelSlots[2] = CardIds.FacilityBase;        // 城邦行政区（彩色）
            state.Players[0].CityPanelSlots[3] = CardIds.CoreCommandTowerBase; // 核心指挥塔（彩色）

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.VouchersPerCount(CountSource.NonColorFacilityKinds, 2) });

            Assert.IsTrue(report.AnyApplied);
            Assert.AreEqual(2, state.Players[0].Vouchers); // 只有 1 种非彩色设施 → 1 × 2
        }

        // ————————————————————— 条件式效果 —————————————————————

        private static EventOption Conditional() => new EventOption(
            "支付 4 金券 → 获得 2 异铁",
            new[] { RewardOp.PayVouchers(4) },
            new[] { RewardOp.Gain(ResourceType.Oriron, 2) });

        [Test]
        public void 条件式_代价付得起_结果段执行()
        {
            GameState state = State();
            state.Players[0].Vouchers = 10;

            EffectReport report = EffectResolver.ExecuteOption(state, 0, Conditional());

            Assert.IsFalse(report.Declined);
            Assert.AreEqual(6, state.Players[0].Vouchers);
            Assert.AreEqual(2, state.Players[0].Resources.Oriron);
        }

        [Test]
        public void 条件式_代价付不起_整段结束且不发奖励()
        {
            GameState state = State();
            state.Players[0].Vouchers = 1;

            EffectReport report = EffectResolver.ExecuteOption(state, 0, Conditional());

            Assert.IsTrue(report.Declined);
            Assert.AreEqual(1, state.Players[0].Vouchers);      // 未扣
            Assert.AreEqual(0, state.Players[0].Resources.Oriron); // 未发
        }

        [Test]
        public void 条件式_玩家放弃_不支付也不发奖励()
        {
            GameState state = State();
            state.Players[0].Vouchers = 10;

            EffectReport report = EffectResolver.ExecuteOption(state, 0, Conditional(), acceptCost: false);

            Assert.IsTrue(report.Declined);
            Assert.AreEqual(10, state.Players[0].Vouchers);
            Assert.AreEqual(0, state.Players[0].Resources.Oriron);
        }

        [Test]
        public void 无条件段的选项_直接执行结果段()
        {
            GameState state = State();
            var option = new EventOption("直接给 3 金券", new[] { RewardOp.Vouchers(3) });

            EffectResolver.ExecuteOption(state, 0, option);

            Assert.AreEqual(3, state.Players[0].Vouchers);
        }

        // ————————————————————— 扩展隔离 —————————————————————
        // 企业专属效果由基础执行器转交扩展接口；未接入时跳过，基础流程不受影响。

        private sealed class FakeEnterpriseExtension : IEffectExtension
        {
            public int Executed;

            public bool Handles(RewardOp op, GameState state) =>
                op.Kind == RewardKind.ChooseEnterprise && state.Setup.IsEnterpriseEnabled;

            public bool Execute(RewardOp op, int seat, GameState state)
            {
                Executed++;
                return true;
            }
        }

        [Test]
        public void 企业效果_未接入扩展时被跳过()
        {
            GameState state = State(expansions: ExpansionFlags.Enterprise);

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.Enterprise() });

            Assert.IsFalse(report.AnyApplied);
            Assert.IsFalse(report.Entries[0].Applied);
        }

        [Test]
        public void 企业效果_接入扩展后转交执行()
        {
            GameState state = State(expansions: ExpansionFlags.Enterprise);
            var extension = new FakeEnterpriseExtension();

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.Enterprise() },
                extension: extension);

            Assert.IsTrue(report.Entries[0].Applied);
            Assert.AreEqual(1, extension.Executed);
        }

        [Test]
        public void 企业效果_未启用企业扩展时扩展不受理()
        {
            GameState state = State(); // 未启用企业扩展
            var extension = new FakeEnterpriseExtension();

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.Enterprise() },
                extension: extension);

            Assert.IsFalse(report.AnyApplied);
            Assert.AreEqual(0, extension.Executed);
        }

        [Test]
        public void 企业效果作为条件段_未接入扩展时条件不可行()
        {
            GameState state = State(expansions: ExpansionFlags.Enterprise);
            var option = new EventOption("选择 1 家企业 → 获得 1 分",
                new[] { RewardOp.Enterprise() },
                new[] { RewardOp.Score(1) });
            state.Players[0].Score = 5;

            EffectReport report = EffectResolver.ExecuteOption(state, 0, option);

            Assert.IsTrue(report.Declined);                        // 条件段（企业效果）无法执行
            Assert.AreEqual(5, state.Players[0].Score);            // 结果段未执行
            Assert.IsFalse(report.AnyApplied);
        }
    }
}
