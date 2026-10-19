using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 主要行动「使用城市样式特殊行动」（§6.3f / §10.2-4）：基础版 6 张（500–505）。
    /// 覆盖费用与效果、标记下移、每张牌每回合一次、以及「源石工业中枢」的额外主要行动与角色牌禁用。
    /// 全部为纯 C# EditMode 断言。
    /// </summary>
    public sealed class CityPatternSpecialActionTests
    {
        private const int MilitaryZone = 500;      // 军工业化区域：放置 X 个影响力（至多 3）
        private const int Mobilization = 501;      // 动员配套体系：替换 1 个影响力
        private const int CompositePower = 502;    // 复合动力系统：1 源石 + 合计 3（源岩/异铁）→ 免费城市移动 + 航道影响力
        private const int SupplyRelay = 503;       // 物资中继站：源岩/源石/异铁各 1
        private const int OriginiumIndustry = 504; // 源石工业中枢：6 金券 → 额外 2 次主要行动；本轮禁用角色牌
        private const int MobilitySystem = 505;    // 高效移动管理体系：3 源石 → 连续 2 次免费城市移动；本轮禁用角色牌

        private GameEngine engine;
        private GameState state;
        private PlayerState player;

        [SetUp]
        public void SetUp()
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 1));
            // 三个点都放指示物：城市移动到任何一处后，出发点都能落到 1 个影响力（§6.4d 步 3）。
            map.Nodes[0].HasResourceToken = true;
            map.Nodes[1].HasResourceToken = true;
            map.Nodes[2].HasResourceToken = true;

            engine = GameEngine.Create(new GameSetup(4), 0, map, PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(0));
            engine.Dispatch(new AdvancePhaseCommand()); // → 玩家行动阶段

            state = engine.State;
            player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;
        }

        // —— 500 军工业化区域 ——

        [Test]
        public void 军工业化区域_按样式牌上自己的标记数放置影响力()
        {
            Mark(MilitaryZone);
            Mark(MilitaryZone); // 模拟宣告两次 → 牌上 2 个标记

            CommandResult result = Run(MilitaryZone);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(2, InfluenceCount(0), "X = 该样式牌上自己的标记数（这里 2 个）。");
            Assert.AreEqual(28, player.MarkersInSupply, "放置 2 个影响力各消耗 1 个玩家标记。");
        }

        [Test]
        public void 军工业化区域_放置数量至多三个()
        {
            for (int i = 0; i < 5; i++) Mark(MilitaryZone); // 5 个标记 → 封顶 3

            CommandResult result = Run(MilitaryZone);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(3, InfluenceCount(0), "「至多 3 个」封顶。");
            Assert.AreEqual(27, player.MarkersInSupply);
        }

        // —— 501 动员配套体系 ——

        [Test]
        public void 动员配套体系_替换掉对手的一个影响力()
        {
            state.Map.Node(0).Slots.Place(0, 1); // 对手（座位 1）在 0 号点有个影响力
            Mark(Mobilization);

            CommandResult result = Run(Mobilization);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0), "对手的影响力被替换成自己的。");
            Assert.AreEqual(29, player.MarkersInSupply);
        }

        // —— 502 复合动力系统 ——

        [Test]
        public void 复合动力系统_支付1源石加合计3个_免费移动并在航道上放影响力()
        {
            Mark(CompositePower);
            player.Resources.Add(ResourceType.Originium, 1);
            player.Resources.Add(ResourceType.Orirock, 3);

            CommandResult result = Run(CompositePower);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Originium), "1 源石付掉。");
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Orirock), "默认分配把合计 3 个全给第一种（源岩）。");
            Assert.AreEqual(1, player.MobileCityNodeId, "执行 1 次免费城市移动。");
            Assert.AreEqual(0, state.Map.Route(0).Slots.OwnerOf(0), "并在经过的航道上放置 1 个影响力。");
        }

        [Test]
        public void 复合动力系统_合计三个可在源岩异铁间任意分配()
        {
            Mark(CompositePower);
            player.Resources.Add(ResourceType.Originium, 1);
            player.Resources.Add(ResourceType.Orirock, 2);
            player.Resources.Add(ResourceType.Oriron, 2);

            var choices = new FixedChoices
            {
                SpreadAmounts = new Dictionary<ResourceType, int>
                {
                    [ResourceType.Orirock] = 2,
                    [ResourceType.Oriron] = 1,
                },
            };

            CommandResult result = Run(CompositePower, choices);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Orirock), "按玩家分配扣 2 个源岩。");
            Assert.AreEqual(1, player.Resources.Get(ResourceType.Oriron), "按玩家分配扣 1 个异铁。");
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Originium));
        }

        [Test]
        public void 复合动力系统_合计三个可以全给异铁()
        {
            Mark(CompositePower);
            player.Resources.Add(ResourceType.Originium, 1);
            player.Resources.Add(ResourceType.Oriron, 3);

            var choices = new FixedChoices
            {
                SpreadAmounts = new Dictionary<ResourceType, int> { [ResourceType.Oriron] = 3 },
            };

            CommandResult result = Run(CompositePower, choices);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Oriron), "0/3 分配成立。");
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Originium));
        }

        [Test]
        public void 复合动力系统_可分配资源不足时被拒绝()
        {
            Mark(CompositePower);
            player.Resources.Add(ResourceType.Originium, 1); // 源岩 / 异铁都没有

            CommandResult result = ValidateOnly(CompositePower);

            Assert.IsFalse(result.Ok, "合计凑不满 3 个时不能使用。");
        }

        // —— 503 物资中继站 ——

        [Test]
        public void 物资中继站_获得三种资源各一个()
        {
            Mark(SupplyRelay);
            int orirock = player.Resources.Get(ResourceType.Orirock);
            int originium = player.Resources.Get(ResourceType.Originium);
            int oriron = player.Resources.Get(ResourceType.Oriron);

            CommandResult result = Run(SupplyRelay);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(orirock + 1, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(originium + 1, player.Resources.Get(ResourceType.Originium));
            Assert.AreEqual(oriron + 1, player.Resources.Get(ResourceType.Oriron));
        }

        // —— 504 源石工业中枢 ——

        [Test]
        public void 源石工业中枢_支付6金券_本轮追加2次主要行动()
        {
            Mark(OriginiumIndustry);
            player.Vouchers = 10;

            CommandResult result = engine.Dispatch(new UsePatternSpecialActionCommand(0, OriginiumIndustry));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(4, player.Vouchers, "6 金券付掉。");
            Assert.AreEqual(1, player.ExtraMainActionsThisTurn, "用完特殊行动后还余 1 次额外主要行动。");
            Assert.AreEqual(0, state.ActiveSeat, "还有额外行动 → 保持当前座位，不推给下一位。");

            TurnFlow.CompletePlayerAction(state); // 额外主要行动 1
            Assert.AreEqual(0, player.ExtraMainActionsThisTurn);
            Assert.AreEqual(0, state.ActiveSeat);

            TurnFlow.CompletePlayerAction(state); // 额外主要行动 2 → 用尽后恢复正常推进
            Assert.AreEqual(1, state.ActiveSeat);
        }

        [Test]
        public void 源石工业中枢_本轮禁用角色牌_下一轮恢复()
        {
            Mark(OriginiumIndustry);
            player.Vouchers = 10;

            engine.Dispatch(new UsePatternSpecialActionCommand(0, OriginiumIndustry));

            Assert.AreEqual(state.ActionRound, player.CharacterCardBanRound);
            Assert.IsFalse(ActionRules.ValidateCharacterCardAllowed(state, 0).Ok, "本轮不能使用角色牌。");

            state.ActionRound++; // 走到下一轮
            Assert.IsTrue(ActionRules.ValidateCharacterCardAllowed(state, 0).Ok, "限制只在本轮内。");
        }

        // —— 505 高效移动管理体系 ——

        [Test]
        public void 高效移动管理体系_支付3源石_连续两次免费城市移动()
        {
            Mark(MobilitySystem);
            player.Resources.Add(ResourceType.Originium, 3);
            int markersBefore = player.MarkersInSupply;

            CommandResult result = Run(MobilitySystem);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Originium), "3 源石付掉。");
            Assert.AreEqual(markersBefore - 2, player.MarkersInSupply, "两次免费移动各在出发点放 1 个影响力。");
            Assert.AreEqual(2, InfluenceCount(0));
            Assert.AreEqual(state.ActionRound, player.CharacterCardBanRound, "本轮不能用角色牌。");
        }

        // —— 通用机制 ——

        [Test]
        public void 使用特殊行动是主要行动_不推进额外行动时会轮到下一位()
        {
            Mark(SupplyRelay);

            engine.Dispatch(new UsePatternSpecialActionCommand(0, SupplyRelay));

            Assert.AreEqual(1, state.ActiveSeat, "主要行动完成后轮到下一位（§6.1）。");
            Assert.AreEqual(0, player.ExtraMainActionsThisTurn);
        }

        [Test]
        public void 同一张样式牌每回合只能用一次特殊行动()
        {
            Mark(SupplyRelay);
            Mark(SupplyRelay); // 两个标记：第一次用完后牌上还有可用标记，用来隔离「每回合一次」这条

            Assert.IsTrue(Run(SupplyRelay).Ok);

            CommandResult second = ValidateOnly(SupplyRelay);

            Assert.IsFalse(second.Ok, "每张样式牌每玩家每回合限一次。");
        }

        [Test]
        public void 没有可用标记时不能使用()
        {
            Mark(SupplyRelay, position: 1); // 标记已在「已使用」格

            CommandResult result = ValidateOnly(SupplyRelay);

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 不在场上的样式牌不能使用()
        {
            CommandResult result = ValidateOnly(CardIds.CityPatternBase + 99);

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 不是当前行动玩家时不能使用()
        {
            Mark(SupplyRelay);

            var command = new UsePatternSpecialActionCommand(1, SupplyRelay);
            CommandResult result = command.Validate(state);

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 命令占用主要行动名额()
        {
            Assert.IsTrue(new UsePatternSpecialActionCommand(0, SupplyRelay).ConsumesAction);
        }

        // —— 剩余次数只认第一个标记（2026-10-19 与规则持有者确认） ——

        [Test]
        public void 二级样式_重复宣告不重置特殊行动剩余次数()
        {
            CityPatternState pattern = state.PatternStateOf(MobilitySystem);

            Mark(MobilitySystem);                      // 本局第一次宣告 → 标记落在「2」区（可用 2 次）
            Assert.IsTrue(pattern.HasUsableMarker(0));

            Assert.IsTrue(pattern.AdvanceMarker(0));   // 用掉 1 次：0→1（已使用）
            pattern.ResetForCleanup();                 // 收尾：1→2（「1」，还剩 1 次）
            Assert.IsTrue(pattern.HasUsableMarker(0));

            Assert.IsTrue(pattern.AdvanceMarker(0));   // 用掉最后 1 次：2→3
            pattern.ResetForCleanup();                 // 收尾：3→4（「0」，次数用尽）
            Assert.IsFalse(pattern.HasUsableMarker(0));

            Mark(MobilitySystem);                      // 重复宣告 → 新标记照样落在「2」区
            Mark(MobilitySystem);

            Assert.IsFalse(pattern.HasUsableMarker(0),
                "重复宣告堆在「2」区的标记不该把次数重置回来，剩余次数只看第一个标记。");
            Assert.AreEqual(3, pattern.Markers.Count, "宣告次数照常记录（供按标记数结算的效果使用）。");
        }

        // —— 工具 ——

        private void Mark(int patternId, int position = 0) => state.PatternStateOf(patternId).PlaceMarker(0, position);

        private CommandResult ValidateOnly(int patternId, IEffectChoices choices = null) =>
            new UsePatternSpecialActionCommand(0, patternId, choices).Validate(state);

        private CommandResult Run(int patternId, IEffectChoices choices = null)
        {
            var command = new UsePatternSpecialActionCommand(0, patternId, choices);
            CommandResult result = command.Validate(state);
            if (result.Ok) command.Apply(state);
            return result;
        }

        private int InfluenceCount(int seat)
        {
            int count = 0;
            foreach (MapNodeState node in state.Map.Nodes) count += SlotsOwnerCount(node.Slots, seat);
            foreach (RouteState route in state.Map.Routes) count += SlotsOwnerCount(route.Slots, seat);
            return count;
        }

        private static int SlotsOwnerCount(InfluenceSlots slots, int seat)
        {
            int count = 0;
            for (int i = 0; i < slots.Capacity; i++) if (slots.OwnerOf(i) == seat) count++;
            return count;
        }

        /// <summary>只回答被测节点关心的那个选择，其余一律取首个合法候选。</summary>
        private sealed class FixedChoices : IEffectChoices
        {
            public Dictionary<ResourceType, int> SpreadAmounts;

            public InfluenceSlotRef? ChooseInfluenceSlot(int seat, RewardOp op, GameState state,
                IReadOnlyList<InfluenceSlotRef> candidates) =>
                candidates.Count > 0 ? candidates[0] : (InfluenceSlotRef?)null;

            public int ChooseBranch(int seat, RewardOp op, GameState state,
                IReadOnlyList<EffectBranch> branches) => branches.Count > 0 ? 0 : -1;

            public InfluenceSlotRef? ChooseInfluenceToRemove(int seat, RewardOp op, GameState state,
                IReadOnlyList<InfluenceSlotRef> candidates) =>
                candidates.Count > 0 ? candidates[0] : (InfluenceSlotRef?)null;

            public InfluenceMove? ChooseOwnInfluenceMove(int seat, RewardOp op, GameState state,
                IReadOnlyList<InfluenceSlotRef> sources, IReadOnlyList<InfluenceSlotRef> destinations) =>
                sources.Count > 0 && destinations.Count > 0
                    ? new InfluenceMove(sources[0], destinations[0])
                    : (InfluenceMove?)null;

            public int ChooseNode(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates) =>
                candidates.Count > 0 ? candidates[0] : -1;

            public BuildChoice? ChooseBuild(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> facilityIds, IReadOnlyList<int> panelSlots) =>
                facilityIds.Count > 0 && panelSlots.Count > 0
                    ? new BuildChoice(facilityIds[0], panelSlots[0], FacilityPayment.Resources)
                    : (BuildChoice?)null;

            public int ChooseExtensionHub(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChoosePanelFacility(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidateSlots) => candidateSlots.Count > 0 ? candidateSlots[0] : -1;

            public IReadOnlyDictionary<ResourceType, int> ChooseSellAmounts(int seat, RewardOp op, GameState state,
                IReadOnlyDictionary<ResourceType, int> prices, IReadOnlyDictionary<ResourceType, int> holdings) =>
                null;

            public IReadOnlyDictionary<ResourceType, int> ChooseResourceSpread(int seat, RewardOp op, GameState state,
                int total, IReadOnlyList<ResourceType> options, IReadOnlyDictionary<ResourceType, int> holdings) =>
                SpreadAmounts;

            public int ChooseResourceType(int seat, RewardOp op, GameState state,
                IReadOnlyList<ResourceType> candidates) => candidates.Count > 0 ? 0 : -1;

            public int ChooseSupplyFacility(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseOptionalStep(int seat, RewardOp op, GameState state, int stepIndex, string label) => 0;
        }
    }
}
