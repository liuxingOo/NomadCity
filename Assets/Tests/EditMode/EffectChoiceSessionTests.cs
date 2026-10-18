using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 「先问后改」的效果会话（<see cref="EffectChoiceSession"/>）与记录式选择器（<see cref="RecordedChoices"/>）：
    /// 入场效果需要玩家点选时，引擎在状态副本上试跑到该选择处停下、回传候选项，玩家答完再继续；
    /// 连锁节点（二选一里再挑落点等）的候选清单必须基于前面答案的真实状态。全部为纯 C# EditMode 断言。
    /// </summary>
    public sealed class EffectChoiceSessionTests
    {
        private static GameEngine NewGame(int playerCount = 4, int firstSeat = 0)
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));
            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.A, 1));

            map.Nodes[0].HasResourceToken = true;
            map.Nodes[1].HasResourceToken = true;
            map.Nodes[2].HasResourceToken = true;

            var engine = GameEngine.Create(new GameSetup(playerCount), firstSeat, map,
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(firstSeat));
            engine.Dispatch(new AdvancePhaseCommand()); // → 玩家行动阶段
            return engine;
        }

        /// <summary>载具仓库的入场效果：「拿走 1 个影响力并移动自己的 1 个」或「执行 1 次探索」二选一（§11.4）。</summary>
        private static RewardOp[] VehicleDepotEffects() => new[]
        {
            RewardOp.Alternative(
                new EffectBranch("拿走 1 个影响力并移动自己的 1 个影响力",
                    RewardOp.RemoveInfluence(), RewardOp.RelocateInfluence()),
                new EffectBranch("执行 1 次探索", RewardOp.ExploreOnce())),
        };

        /// <summary>在候选项里按文案关键字找下标；找不到返回 -1。</summary>
        private static int IndexOfLabel(ChoiceRequest request, string keyword)
        {
            for (int i = 0; i < request.Options.Count; i++)
            {
                if (request.Options[i].Label.Contains(keyword)) return i;
            }
            return -1;
        }

        // —— 1. 连锁提问：二选一 → 拿走 → 移动 ——

        [Test]
        public void 二选一分支_会连锁问出拿走与移动且候选随前一步变化()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;

            // 玩家 1 在 A-01 第 1 格、玩家 2 在 A-02 第 1 格
            state.Map.Node(0).Slots.Place(0, 0);
            state.Map.Node(1).Slots.Place(0, 1);

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, VehicleDepotEffects(), choices));

            session.Advance();
            Assert.IsFalse(session.IsComplete);
            Assert.AreEqual(2, session.Pending.Options.Count, "二选一应给出两个分支");
            StringAssert.Contains("二选一", session.Pending.Prompt);
            Assert.IsTrue(session.Pending.AllowCancel == false, "二选一必须选一个，不给「放弃」");

            session.Answer(0); // 选「拿走并移动」那一支

            StringAssert.Contains("拿走", session.Pending.Prompt);
            int removeIndex = IndexOfLabel(session.Pending, "玩家2");
            Assert.GreaterOrEqual(removeIndex, 0, "候选里应有玩家 2 在 A-02 的影响力：" + session.Pending.Prompt);
            session.Answer(removeIndex);

            StringAssert.Contains("移动", session.Pending.Prompt);
            session.Answer(0); // 起点只有「玩家 1 在 A-01 第 1 格」这一处

            StringAssert.Contains("移动到哪个空格", session.Pending.Prompt);
            int destination = IndexOfLabel(session.Pending, "A-02 第 1 格");
            Assert.GreaterOrEqual(destination, 0,
                "刚被拿走的那一格应重新成为可选落点（候选基于前一步答案后的状态）：" + session.Pending.Prompt);
            session.Answer(destination);

            Assert.IsTrue(session.IsComplete, "四个问题答完，整串效果就跑完了");

            // 试跑跑在副本上：正式状态一根手指头都没动过。
            Assert.AreEqual(1, state.Map.Node(1).Slots.OwnerOf(0));
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0));
        }

        // —— 2. 答案回放：正式执行与试跑一致 ——

        [Test]
        public void 答案回放_正式执行得到与试跑一致的结果()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Map.Node(0).Slots.Place(0, 0);
            state.Map.Node(1).Slots.Place(0, 1);

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, VehicleDepotEffects(), choices));

            // 照「拿走对手的，再把在 A-01 的那枚移过去」这条路走完。
            session.Advance();
            session.Answer(0);
            session.Answer(IndexOfLabel(session.Pending, "玩家2"));
            session.Answer(IndexOfLabel(session.Pending, "玩家1 的"));
            session.Answer(IndexOfLabel(session.Pending, "A-02 第 1 格"));

            Assert.IsTrue(session.IsComplete);

            // 把玩家点出来的答案交给引擎在正式状态上重放一遍。
            EffectReport report = EffectResolver.Execute(state, 0, VehicleDepotEffects(),
                new RecordedChoices(session.Answers, strict: false));

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0), "自己的影响力被移走了");
            Assert.AreEqual(0, state.Map.Node(1).Slots.OwnerOf(0), "移到了刚拿走的那一格上");
        }

        // —— 3. 探索：先问要探索哪个资源点 ——

        [Test]
        public void 探索入场效果_先问要探索的资源点()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;
            player.Vouchers += 50;
            state.Map.Node(1).HasResourceToken = false; // 只有没指示物的点能探索

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, new[] { RewardOp.ExploreOnce() }, choices));

            session.Advance();
            Assert.IsFalse(session.IsComplete);
            StringAssert.Contains("探索", session.Pending.Prompt);
            Assert.GreaterOrEqual(IndexOfLabel(session.Pending, "A-02"), 0);

            DriveToEnd(session, 40);
            Assert.IsTrue(session.IsComplete, "探索的连锁选择（含事件选项）都应被逐个问完");

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.ExploreOnce() },
                new RecordedChoices(session.Answers, strict: false));

            Assert.IsTrue(report.AnyApplied,
                $"answers={session.Answers.Count} [{string.Join(",", session.Answers)}] / {report.Describe()}");
            Assert.IsTrue(state.Map.Node(1).HasResourceToken, "目标点翻出了指示物");
            Assert.AreEqual(0, state.Map.Node(1).Slots.OwnerOf(0), "探索归来的影响力落在目标点");
        }

        // —— 4. 进行一次建造：依次问设施 → 落点 → 付费方式 ——

        [Test]
        public void 进行一次建造_依次问出设施_落点_付费方式()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            state.DeveloperMode = true;

            int emptySlots = 0;
            for (int i = 0; i < PlayerState.CityPanelSlotCount; i++)
            {
                if (i != PlayerState.CityPanelCoreSlot && player.CityPanelSlots[i] < 0) emptySlots++;
            }

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, new[] { RewardOp.BuildOnce() }, choices));

            session.Advance();
            Assert.AreEqual("选择要建设的设施牌", session.Pending.Prompt);
            Assert.AreEqual(state.FacilitySupply.Count, session.Pending.Options.Count, "候选就是设施供应区里的牌");

            session.Answer(0);
            Assert.AreEqual("选择要放到城市面板哪一格", session.Pending.Prompt);
            Assert.AreEqual(emptySlots, session.Pending.Options.Count);

            session.Answer(0);
            StringAssert.Contains("付费方式", session.Pending.Prompt);
        }

        // —— 5. 记录式选择器：答案用尽即抛，交给会话接着问 ——

        [Test]
        public void 记录式选择器_答案用尽时抛出待问的请求()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;

            var choices = new RecordedChoices(Array.Empty<int>());
            var pending = Assert.Throws<ChoicePendingException>(() =>
                choices.ChooseBranch(0, default, state, new[]
                {
                    new EffectBranch("甲"), new EffectBranch("乙"),
                }));

            Assert.IsNotNull(pending);
            Assert.AreEqual(2, pending.Request.Options.Count);
            Assert.IsFalse(pending.Request.AllowCancel);
        }

        // —— 6. 合计获得 N 个资源：逐种问「给几个」，余量给最后一种 ——

        [Test]
        public void 合计分配资源_逐种问出数量且余量给最后一种()
        {
            ResourceType[] options = { ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron };

            // 还没答：先把第一种的数量问出来。
            var choices = new RecordedChoices(Array.Empty<int>());
            var pending = Assert.Throws<ChoicePendingException>(() =>
                choices.ChooseResourceSpread(0, RewardOp.GainSpread(5, options), null, 5, options, null));

            StringAssert.Contains("源岩", pending.Request.Prompt);
            Assert.AreEqual(6, pending.Request.Options.Count); // 0–5 个共 6 个选项

            // 答「源岩 3、源石 2」→ 余量 0，第三种不再问，结果合计正好 5。
            var answered = new RecordedChoices(new[] { 3, 2 });
            IReadOnlyDictionary<ResourceType, int> spread =
                answered.ChooseResourceSpread(0, RewardOp.GainSpread(5, options), null, 5, options, null);

            Assert.AreEqual(3, spread[ResourceType.Orirock]);
            Assert.AreEqual(2, spread[ResourceType.Originium]);
            Assert.IsFalse(spread.ContainsKey(ResourceType.Oriron));
        }

        [Test]
        public void 合计分配资源_全给第一种只弹一次窗()
        {
            ResourceType[] options = { ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron };

            var choices = new RecordedChoices(new[] { 5 });
            IReadOnlyDictionary<ResourceType, int> spread =
                choices.ChooseResourceSpread(0, RewardOp.GainSpread(5, options), null, 5, options, null);

            Assert.AreEqual(5, spread[ResourceType.Orirock]);
            Assert.AreEqual(1, spread.Count);
        }

        /// <summary>一路选第一项直到跑完（有上限，防止意外死循环）。</summary>
        private static void DriveToEnd(EffectChoiceSession session, int maxSteps)
        {
            session.Advance();

            for (int step = 0; step < maxSteps && !session.IsComplete; step++)
            {
                session.Answer(0);
            }
        }
    }
}
