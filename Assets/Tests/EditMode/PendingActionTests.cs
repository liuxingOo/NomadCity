using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 行动事件（挂起行动）的核心行为：效果在玩家行动阶段不再替玩家结算，
    /// 而是登记一条 <see cref="PendingAction"/>，由界面走完整流程后再派发命令消费它。
    /// </summary>
    public sealed class PendingActionTests
    {
        private static GameEngine NewGame()
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 1));

            map.Nodes[0].HasResourceToken = true;
            map.Nodes[2].HasResourceToken = true;

            var engine = GameEngine.Create(new GameSetup(4), 0, map,
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(0));
            engine.Dispatch(new AdvancePhaseCommand()); // → 玩家行动阶段
            return engine;
        }

        private static void FillResources(PlayerState player)
        {
            player.Resources.Set(ResourceType.Orirock, 20);
            player.Resources.Set(ResourceType.Originium, 20);
            player.Resources.Set(ResourceType.Oriron, 20);
            player.Resources.Set(ResourceType.OriginitePrime, 20);
        }

        private static int FreePanelSlot(PlayerState player)
        {
            for (int i = 0; i < PlayerState.CityPanelSlotCount; i++)
            {
                if (i != PlayerState.CityPanelCoreSlot && player.CityPanelSlots[i] < 0) return i;
            }

            return -1;
        }

        /// <summary>供应区 / 牌堆里第一张按金券价卖的普通设施（找不到返回 -1）。</summary>
        private static int FirstVoucherCard(GameState state)
        {
            int found = Find(state, state.FacilitySupply);
            return found >= 0 ? found : Find(state, state.FacilityDeck);
        }

        private static int Find(GameState state, System.Collections.Generic.List<int> ids)
        {
            foreach (int id in ids)
            {
                FacilityCardDef card = state.Catalog.Facility(id);
                if (card != null && card.Kind == FacilityKind.Standard && card.VoucherCost > 0) return id;
            }

            return -1;
        }

        /// <summary>按名字在卡牌目录里找一张普通设施（找不到返回 -1）。</summary>
        private static int FacilityIdByName(GameState state, string name)
        {
            for (int id = CardIds.FacilityBase; id < CardIds.CoreCommandTowerBase; id++)
            {
                FacilityCardDef card = state.Catalog.Facility(id);
                if (card != null && card.Name == name) return id;
            }

            return -1;
        }

        [Test]
        public void 载具仓库入场效果_在选择会话里选探索_会话能跑完()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            FillResources(player);
            player.MobileCityNodeId = 0;
            player.Vouchers = 10;

            int facilityId = FacilityIdByName(state, "载具仓库");
            Assert.GreaterOrEqual(facilityId, 0, "卡牌目录里应当有「载具仓库」。");

            FacilityCardDef card = state.Catalog.Facility(facilityId);
            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, card.EntryEffects, choices,
                    EnterpriseEffectExtension.Instance));

            session.Advance();
            Assert.IsFalse(session.IsComplete, "载具仓库的入场效果要先问「要哪一种」。");
            Assert.IsNotNull(session.Pending);

            int exploreOption = -1;
            for (int i = 0; i < session.Pending.Options.Count; i++)
            {
                if (session.Pending.Options[i].Label.Contains("探索")) exploreOption = i;
            }

            Assert.GreaterOrEqual(exploreOption, 0, "入场效果里应当有一个「探索」选项。");

            // 界面就是这一步：玩家点「执行 1 次探索」之后会话必须结束，否则会停在建设 L4 等不到下一步。
            session.Answer(exploreOption);
            Assert.IsTrue(session.IsComplete, "选完探索后会话应当直接结束。");
            Assert.IsNull(session.Pending);
        }

        [Test]
        public void 建设载具仓库_入场效果选探索_登记成挂起探索()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            FillResources(player);
            player.MobileCityNodeId = 0;
            player.Vouchers = 10;

            int facilityId = FacilityIdByName(state, "载具仓库");
            Assert.GreaterOrEqual(facilityId, 0, "卡牌目录里应当有「载具仓库」。");

            state.FacilitySupply.Clear();
            state.FacilitySupply.Add(facilityId);

            int slot = FreePanelSlot(player);
            CommandResult built = engine.Dispatch(new BuildFacilityCommand(0, facilityId, slot,
                FacilityPayment.Resources, null, false, false, 0, false));
            Assert.IsTrue(built.Ok, built.Error);

            // 入场效果是「或」二选一：1 = 执行 1 次探索。
            var entry = new ResolveFacilityEntryEffectCommand(0, slot,
                new RecordedChoices(new[] { 1 }, strict: false));
            CommandResult resolved = engine.Dispatch(entry);
            Assert.IsTrue(resolved.Ok, resolved.Error);

            PendingAction pending = state.FindPending(0, PendingActionKind.Explore);
            Assert.IsNotNull(pending, "载具仓库的入场效果应当登记一条挂起探索，交给界面走完整流程。");
            Assert.AreEqual(1, pending.Remaining);
        }

        [Test]
        public void 效果授予的行动_玩家行动阶段登记成挂起行动()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;

            EffectResolver.Execute(state, 0, new[] { RewardOp.BuildOnce() });

            PendingAction pending = state.FindPending(0, PendingActionKind.Build);
            Assert.IsNotNull(pending);
            Assert.AreEqual(1, pending.Remaining);
            Assert.IsTrue(state.HasPending(0));
        }

        [Test]
        public void 没有合法目标_效果不登记挂起行动并说明已跳过()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;

            // 三个点全都有指示物 → 没有可探索的点。
            state.Map.Node(1).HasResourceToken = true;
            state.Players[0].MobileCityNodeId = 0;
            state.Players[0].Vouchers = 10;

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.ExploreOnce() });

            Assert.IsFalse(report.AnyApplied);
            Assert.IsFalse(state.HasPending(0));
            StringAssert.Contains("自动跳过", report.Describe());
        }

        [Test]
        public void 没有合法目标_放弃命令可以直接收尾()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Map.Node(1).HasResourceToken = true;
            state.Players[0].MobileCityNodeId = 0;
            state.Players[0].Vouchers = 10;
            state.EnqueuePending(0, PendingActionKind.Explore, 1, PendingActionLimits.Unlimited, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.Explore);

            CommandResult result = engine.Dispatch(new AbandonPendingActionCommand(0, pending.Id));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsFalse(state.HasPending(0));
        }

        [Test]
        public void 还有合法目标_放弃命令会被拒绝()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Players[0].MobileCityNodeId = 0;
            state.Players[0].Vouchers = 10;
            state.EnqueuePending(0, PendingActionKind.Explore, 1, PendingActionLimits.Unlimited, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.Explore);

            CommandResult result = engine.Dispatch(new AbandonPendingActionCommand(0, pending.Id));

            Assert.IsFalse(result.Ok);
            Assert.IsTrue(state.HasPending(0));
        }

        // —— 2. 次数递减与名额绕过 ——

        [Test]
        public void 挂起行动有多次时_每完成一次递减一次并最终消失()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            FillResources(player);
            player.Vouchers = 100;
            state.EnqueuePending(0, PendingActionKind.Build, 2, PendingActionLimits.Unlimited, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.Build);
            int builtBefore = player.BuiltFacilities.Count; // 开局面板上已经有核心指挥塔

            int first = state.FacilitySupply[0];
            Assert.IsTrue(engine.Dispatch(new BuildFacilityCommand(
                0, first, FreePanelSlot(player), FacilityPayment.Vouchers, null, pendingActionId: pending.Id)).Ok);
            Assert.IsNotNull(state.FindPending(pending.Id));
            Assert.AreEqual(1, pending.Remaining);

            int second = state.FacilitySupply[0];
            Assert.IsTrue(engine.Dispatch(new BuildFacilityCommand(
                0, second, FreePanelSlot(player), FacilityPayment.Vouchers, null, pendingActionId: pending.Id)).Ok);
            Assert.IsNull(state.FindPending(pending.Id));
            Assert.AreEqual(builtBefore + 2, player.BuiltFacilities.Count);
        }

        [Test]
        public void 挂起行动不占主要行动名额()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            FillResources(player);
            state.EnqueuePending(0, PendingActionKind.Build, 1, PendingActionLimits.Unlimited, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.Build);

            Assert.IsTrue(engine.Dispatch(new BuildFacilityCommand(
                0, state.FacilitySupply[0], FreePanelSlot(player), FacilityPayment.Resources, null,
                pendingActionId: pending.Id)).Ok);

            Assert.IsFalse(state.MainActionTakenThisRound, "效果给的建设不能算掉这一轮的主要行动");
        }

        [Test]
        public void 有挂起行动时_不能开始新的主要行动()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            FillResources(player);
            state.EnqueuePending(0, PendingActionKind.Build, 1, PendingActionLimits.Unlimited, -1, "测试");

            CommandResult result = engine.Dispatch(new BuildFacilityCommand(
                0, state.FacilitySupply[0], FreePanelSlot(player), FacilityPayment.Resources));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("卡牌效果授予的行动", result.Error);
        }

        // —— 3. 免费与金券上限 ——

        [Test]
        public void 免费建造_不扣资源也不扣金券()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            int vouchers = player.Vouchers;
            int orirock = player.Resources.Get(ResourceType.Orirock);

            var limits = PendingActionLimits.Unlimited;
            limits.Free = true;
            state.EnqueuePending(0, PendingActionKind.Build, 1, limits, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.Build);

            Assert.IsTrue(engine.Dispatch(new BuildFacilityCommand(
                0, state.FacilitySupply[0], FreePanelSlot(player), FacilityPayment.None, null,
                pendingActionId: pending.Id)).Ok);

            Assert.AreEqual(vouchers, player.Vouchers);
            Assert.AreEqual(orirock, player.Resources.Get(ResourceType.Orirock));
        }

        [Test]
        public void 金券上限_超出上限的设施不能建设()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            FillResources(player);

            int card = FirstVoucherCard(state);
            Assert.GreaterOrEqual(card, 0, "测试用的占位牌里应当有一张按金券计价");

            var limits = PendingActionLimits.Unlimited;
            limits.MaxVoucherSpend = 0;
            state.EnqueuePending(0, PendingActionKind.Build, 1, limits, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.Build);

            CommandResult result = engine.Dispatch(new BuildFacilityCommand(
                0, card, FreePanelSlot(player), FacilityPayment.Resources, null, pendingActionId: pending.Id));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("金券费用", result.Error);
        }

        // —— 4. 移除影响力的范围限制 ——

        [Test]
        public void 移除影响力_限制只剩他人时不能拿自己的开刀()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Map.Node(1).Slots.Place(0, 0); // 自己的
            state.Map.Node(2).Slots.Place(0, 2); // 别人的（2 号点有指示物但影响力照放）
            int markersBefore = state.Players[2].MarkersInSupply;

            var limits = PendingActionLimits.Unlimited;
            limits.Scope = PendingInfluenceScope.Others;
            state.EnqueuePending(0, PendingActionKind.RemoveInfluence, 2, limits, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.RemoveInfluence);

            CommandResult own = engine.Dispatch(new RemoveInfluenceCommand(
                0, InfluenceSlotRef.OnNode(1, 0), pending.Id));
            Assert.IsFalse(own.Ok);
            StringAssert.Contains("只能移除其他玩家", own.Error);

            CommandResult others = engine.Dispatch(new RemoveInfluenceCommand(
                0, InfluenceSlotRef.OnNode(2, 0), pending.Id));
            Assert.IsTrue(others.Ok, others.Error);
            Assert.AreEqual(-1, state.Map.Node(2).Slots.OwnerOf(0));
            Assert.AreEqual(markersBefore + 1, state.Players[2].MarkersInSupply);
        }

        [Test]
        public void 移除自己的影响力_限制只剩自己时不能拿别人的开刀()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Map.Node(1).Slots.Place(0, 0);
            state.Map.Node(2).Slots.Place(0, 2);
            int markersBefore = state.Players[0].MarkersInSupply;

            var limits = PendingActionLimits.Unlimited;
            limits.Scope = PendingInfluenceScope.Own;
            state.EnqueuePending(0, PendingActionKind.RemoveInfluence, 1, limits, -1, "测试");
            PendingAction pending = state.FindPending(0, PendingActionKind.RemoveInfluence);

            CommandResult others = engine.Dispatch(new RemoveInfluenceCommand(
                0, InfluenceSlotRef.OnNode(2, 0), pending.Id));
            Assert.IsFalse(others.Ok);
            StringAssert.Contains("只能移除自己", others.Error);

            CommandResult own = engine.Dispatch(new RemoveInfluenceCommand(
                0, InfluenceSlotRef.OnNode(1, 0), pending.Id));
            Assert.IsTrue(own.Ok, own.Error);
            Assert.AreEqual(markersBefore + 1, state.Players[0].MarkersInSupply);
        }

        // —— 5. 快照往返 ——

        [Test]
        public void 挂起行动_能随快照与网络编码往返()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;

            var limits = PendingActionLimits.Unlimited;
            limits.Free = true;
            limits.MaxVoucherSpend = 3;
            limits.Scope = PendingInfluenceScope.Own;
            state.EnqueuePending(0, PendingActionKind.RemoveInfluence, 2, limits, 12, "测试来源");

            byte[] bytes = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(state));
            GameState restored = GameStateSnapshotCodec.Read(bytes, 0, bytes.Length).Restore(state.Catalog);

            Assert.IsTrue(restored.HasPending(0));
            PendingAction pending = restored.FindPending(0, PendingActionKind.RemoveInfluence);
            Assert.IsNotNull(pending);
            Assert.AreEqual(2, pending.Remaining);
            Assert.IsTrue(pending.Limits.Free);
            Assert.AreEqual(3, pending.Limits.MaxVoucherSpend);
            Assert.AreEqual(PendingInfluenceScope.Own, pending.Limits.Scope);
            Assert.AreEqual("测试来源", pending.SourceLabel);

            byte[] again = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(restored));
            CollectionAssert.AreEqual(bytes, again, "往返一次之后应当编码出一模一样的字节");
        }
    }
}
