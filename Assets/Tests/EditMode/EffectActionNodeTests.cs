using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 行动类入场效果节点（§11.4）：进行一次建造 / 免费城市移动 / 执行 1 次探索 /
    /// 结算他牌入场效果 / 按单价售卖资源 / 从余下延伸枢纽选 1 张并建设 / 传递起始标记 / 延迟结算。
    /// 全部为纯 C# EditMode 断言。
    /// </summary>
    public sealed class EffectActionNodeTests
    {
        private static GameEngine NewGame(int playerCount = 4, int firstSeat = 0)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 1));

            // 0 号点上停着移动城市、1 号点留空供搬移 / 探索用：0、2 号点放指示物。
            map.Nodes[0].HasResourceToken = true;
            map.Nodes[2].HasResourceToken = true;

            var engine = GameEngine.Create(new GameSetup(playerCount), firstSeat, map,
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(firstSeat));
            engine.Dispatch(new AdvancePhaseCommand()); // → 玩家行动阶段
            return engine;
        }

        /// <summary>面板上第一个空闲的设施格（核心指挥塔那一格不算）。</summary>
        private static int FreeSlot(PlayerState player)
        {
            for (int i = 0; i < PlayerState.CityPanelSlotCount; i++)
            {
                if (i != PlayerState.CityPanelCoreSlot && player.CityPanelSlots[i] < 0) return i;
            }
            return -1;
        }

        // —— 1. 进行一次建造 ——

        [Test]
        public void 进行一次建造_放置到面板空位并从供应区拿走()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            state.DeveloperMode = true; // 跳过付费，专测「建造」这一步本身

            int card = state.FacilitySupply[0];
            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.BuildOnce() });

            Assert.IsTrue(report.AnyApplied, report.Describe());
            int slot = FreeSlot(player);
            CollectionAssert.Contains(player.BuiltFacilities, card);
            CollectionAssert.DoesNotContain(state.FacilitySupply, card);
            Assert.Greater(slot, -1); // 仍然还留着空位（只建了一张）
        }

        // —— 2. 免费城市移动 ——

        [Test]
        public void 免费城市移动_移动城市并在出发点放影响力()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.FreeCityMove() });

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(1, player.MobileCityNodeId);
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0)); // 出发点放下 1 个自己的影响力
            Assert.AreEqual(29, player.MarkersInSupply);
        }

        // —— 3. 执行 1 次探索 ——

        [Test]
        public void 执行一次探索_翻牌结算并在目标点放影响力()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;
            player.Vouchers += 50;

            int deckBefore = state.EventDeckGreen.Count;
            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.ExploreOnce() });

            Assert.IsTrue(report.AnyApplied, report.Describe());

            MapNodeState node = state.Map.Node(1);
            Assert.IsTrue(node.HasResourceToken);
            Assert.IsTrue(node.EventResolved);
            Assert.IsFalse(node.ExplorePending);
            Assert.AreEqual(deckBefore - 1, state.EventDeckGreen.Count);
            Assert.AreEqual(0, node.Slots.OwnerOf(0)); // 归来的影响力落在目标点
        }

        // —— 4. 结算他牌入场效果 ——

        [Test]
        public void 结算他牌入场效果_触发相邻非彩色设施的入场效果()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];

            // 在核心指挥塔的十字相邻格放一张「源石精炼厂」（蓝，入场效果：获得 6 源石）。
            int slot = CityPanelQuery.CoreNeighbors[0];
            player.CityPanelSlots[slot] = CardIds.FacilityBase + 13;

            int before = player.Resources.Get(ResourceType.Originium);
            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.SettleAnotherEntryEffect() });

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(before + 6, player.Resources.Get(ResourceType.Originium));
        }

        // —— 5. 按单价售卖资源 ——

        [Test]
        public void 按单价售卖资源_默认不卖跳过()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Players[0].Resources.Add(ResourceType.Orirock, 3);

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.SellResources() });

            Assert.IsFalse(report.AnyApplied); // 可选效果，默认一件不卖
        }

        [Test]
        public void 按单价售卖资源_按单价换金券()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Resources.Add(ResourceType.Orirock, 5); // 源岩，单价 3
            int vouchersBefore = player.Vouchers;

            var choices = new FixedChoices
            {
                SellAmounts = new Dictionary<ResourceType, int> { [ResourceType.Orirock] = 2 },
            };
            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.SellResources() }, choices);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(3, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(vouchersBefore + 6, player.Vouchers);
        }

        // —— 6. 从余下延伸枢纽选 1 张并建设 ——

        [Test]
        public void 从余下延伸枢纽选一张并建设()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];

            int hubsBefore = state.ExtensionHubSupply.Count;
            int scoreBefore = player.Score;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.BuildExtensionHub() });

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(hubsBefore - 1, state.ExtensionHubSupply.Count);

            int slot = 0; // 空面板 → 建设落在第一个空格
            int placedId = player.CityPanelSlots[slot];
            bool isHub = false;
            foreach (int hub in PlaceholderCardCatalog.Default.ExtensionHubIds())
            {
                if (hub == placedId) isHub = true;
            }
            Assert.IsTrue(isHub, $"面板第 {slot} 格应当是延伸枢纽，实际是 {placedId}");
            Assert.AreEqual(scoreBefore - 1, player.Score); // 延伸枢纽牌面分数 -1
        }

        // —— 7. 传递起始标记 ——

        [Test]
        public void 传递起始标记_下一回合由指定玩家先手()
        {
            GameEngine engine = NewGame(4, 0);
            GameState state = engine.State;

            EffectResolver.Execute(state, 0, new[] { RewardOp.PassStartMarker() });
            Assert.AreEqual(0, state.PendingStartMarkerSeat);

            // 走到「收尾 → 下一回合」这一步：默认顺时针传给 1，本效果应把它改回 0。
            state.TurnNumber = 3;
            state.Phase = TurnPhase.Cleanup;
            TurnFlow.AdvancePhase(state);

            Assert.AreEqual(4, state.TurnNumber);
            Assert.AreEqual(0, state.FirstPlayerSeat);
            Assert.AreEqual(-1, state.PendingStartMarkerSeat);
        }

        // —— 8. 延迟结算 ——

        [Test]
        public void 延迟结算_收尾阶段才执行()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            int before = player.Resources.Get(ResourceType.Orirock);

            EffectResolver.Execute(state, 0,
                new[] { RewardOp.Deferred(RewardOp.Gain(ResourceType.Orirock, 3)) });

            Assert.AreEqual(1, state.DeferredEffects.Count);
            Assert.AreEqual(before, player.Resources.Get(ResourceType.Orirock)); // 还没执行

            CleanupResolver.ResolveDeferredEffects(state);

            Assert.AreEqual(0, state.DeferredEffects.Count);
            Assert.AreEqual(before + 3, player.Resources.Get(ResourceType.Orirock));
        }

        // —— 8. 合计获得 N 个资源（自由分配，开采电铲） ——

        [Test]
        public void 合计获得五个资源_按选择器给的分配发放()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            int orirock = player.Resources.Get(ResourceType.Orirock);
            int oriron = player.Resources.Get(ResourceType.Oriron);

            RewardOp op = RewardOp.GainSpread(5, ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron);
            var choices = new FixedChoices
            {
                SpreadAmounts = new Dictionary<ResourceType, int>
                {
                    [ResourceType.Orirock] = 3,
                    [ResourceType.Originium] = 2,
                },
            };

            EffectReport report = EffectResolver.Execute(state, 0, new[] { op }, choices);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(orirock + 3, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(2, player.Resources.Get(ResourceType.Originium));
            Assert.AreEqual(oriron, player.Resources.Get(ResourceType.Oriron)); // 没被分配的种类不受影响
        }

        [Test]
        public void 合计获得五个资源_多给的数量被削到合计刚好五个()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            int orirock = player.Resources.Get(ResourceType.Orirock);

            RewardOp op = RewardOp.GainSpread(5, ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron);
            var choices = new FixedChoices
            {
                SpreadAmounts = new Dictionary<ResourceType, int> { [ResourceType.Orirock] = 9 },
            };

            EffectResolver.Execute(state, 0, new[] { op }, choices);

            Assert.AreEqual(orirock + 5, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Originium));
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Oriron));
        }

        [Test]
        public void 合计获得五个资源_没分完的余量补给第一种()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];

            RewardOp op = RewardOp.GainSpread(5, ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron);
            // 只给源石 2 个，剩下的 3 个应当补给列表里的第一种（源岩）。
            var choices = new FixedChoices
            {
                SpreadAmounts = new Dictionary<ResourceType, int> { [ResourceType.Originium] = 2 },
            };

            EffectResolver.Execute(state, 0, new[] { op }, choices);

            Assert.AreEqual(3, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(2, player.Resources.Get(ResourceType.Originium));
        }

        // —— 9. 在经过的航道上放置影响力（高性能动力设施） ——

        [Test]
        public void 免费城市移动后_在经过的航道上放置一个影响力()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.FreeCityMove(), RewardOp.PlaceInfluenceOnCityMoveRoute() });

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(1, player.MobileCityNodeId);
            Assert.AreEqual(0, state.LastCityMoveRouteId); // 0–1 之间那条航道
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0));  // 出发点放了 1 个
            Assert.AreEqual(0, state.Map.Route(0).Slots.OwnerOf(0)); // 经过的航道上放了 1 个
            Assert.AreEqual(28, player.MarkersInSupply);
        }

        [Test]
        public void 没有城市移动过_在经过的航道上放置影响力会跳过()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            int markers = player.MarkersInSupply;

            EffectReport report = EffectResolver.Execute(state, 0,
                new[] { RewardOp.PlaceInfluenceOnCityMoveRoute() });

            Assert.IsFalse(report.AnyApplied);
            Assert.AreEqual(markers, player.MarkersInSupply);
        }

        /// <summary>只回答被测节点关心的那个选择，其余一律取首个合法候选。</summary>
        private sealed class FixedChoices : IEffectChoices
        {
            public Dictionary<ResourceType, int> SellAmounts;
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

            public int ChooseRoute(int seat, RewardOp op, GameState state, IReadOnlyList<int> candidates) =>
                candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseEnterpriseBoard(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseEnterpriseEffectLevel(int seat, RewardOp op, GameState state, int boardId,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseRhineDepartment(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseOpponent(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseDiscardedCharacterCard(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseOperatorCard(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseCharacterCardIntoBox(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public BuildChoice? ChooseBuild(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> facilityIds, IReadOnlyList<int> panelSlots, bool freeOnly = false) =>
                facilityIds.Count > 0 && panelSlots.Count > 0
                    ? new BuildChoice(facilityIds[0], panelSlots[0],
                        freeOnly ? FacilityPayment.None : FacilityPayment.Resources)
                    : (BuildChoice?)null;

            public int ChooseExtensionHub(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChoosePanelFacility(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidateSlots) => candidateSlots.Count > 0 ? candidateSlots[0] : -1;

            public IReadOnlyDictionary<ResourceType, int> ChooseSellAmounts(int seat, RewardOp op, GameState state,
                IReadOnlyDictionary<ResourceType, int> prices, IReadOnlyDictionary<ResourceType, int> holdings) =>
                SellAmounts;

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
