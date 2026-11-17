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

        /// <summary>
        /// 二选一里挑「连续放置 2 个影响力」：第二个落点的候选必须基于第一个答案之后的状态。
        /// 拿走 / 移动 / 探索 / 建设这类行动已改由「行动事件」接管（玩家行动阶段只登记挂起行动），
        /// 所以这里用同一套连锁提问的「部署」来验证会话机制本身。
        /// </summary>
        private static RewardOp[] TwoPlacementEffects() => new[]
        {
            RewardOp.Alternative(
                new EffectBranch("连续放置 2 个影响力",
                    RewardOp.Influence(InfluencePlacementScope.DeployRules, 1),
                    RewardOp.Influence(InfluencePlacementScope.DeployRules, 1)),
                new EffectBranch("获得 3 金券", RewardOp.Vouchers(3))),
        };

        // —— 1. 连锁提问：二选一 → 第一次落点 → 第二次落点 ——

        [Test]
        public void 二选一分支_会连锁问出两次落点且候选随前一步变化()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Players[0].MarkersInSupply = 30;

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, TwoPlacementEffects(), choices));

            session.Advance();
            Assert.IsFalse(session.IsComplete);
            Assert.AreEqual(2, session.Pending.Options.Count, "二选一应给出两个分支");
            StringAssert.Contains("二选一", session.Pending.Prompt);
            Assert.IsTrue(session.Pending.AllowCancel == false, "二选一必须选一个，不给「放弃」");

            session.Answer(0); // 选「连续放置 2 个影响力」那一支

            StringAssert.Contains("放置影响力的位置", session.Pending.Prompt);
            int firstCandidates = session.Pending.Options.Count;
            Assert.GreaterOrEqual(firstCandidates, 2, "候选里应有多个可放置的空格：" + session.Pending.Prompt);
            session.Answer(0); // 第一个影响力

            StringAssert.Contains("放置影响力的位置", session.Pending.Prompt);
            Assert.AreEqual(firstCandidates - 1, session.Pending.Options.Count,
                "刚放下的那一格不该再出现在候选里（候选基于前一步答案后的状态）");
            session.Answer(0); // 第二个影响力

            Assert.IsTrue(session.IsComplete, "三个问题答完，整串效果就跑完了");

            // 试跑跑在副本上：正式状态一根手指头都没动过。
            Assert.AreEqual(30, state.Players[0].MarkersInSupply);
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0));
        }

        // —— 2. 答案回放：正式执行与试跑一致 ——

        [Test]
        public void 答案回放_正式执行得到与试跑一致的结果()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.Players[0].MarkersInSupply = 30;

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, TwoPlacementEffects(), choices));

            // 照「连续放置 2 个影响力」这条路走完（第二个落点跟着第一个答案后的候选走）。
            session.Advance();
            session.Answer(0);
            session.Answer(0);
            session.Answer(0);

            Assert.IsTrue(session.IsComplete);

            // 把玩家点出来的答案交给引擎在正式状态上重放一遍。
            EffectReport report = EffectResolver.Execute(state, 0, TwoPlacementEffects(),
                new RecordedChoices(session.Answers, strict: false));

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0), "第一个影响力落在 A-01 第 1 格");
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(1), "第二个影响力落在同一资源点的下一个空格");
            Assert.AreEqual(28, state.Players[0].MarkersInSupply);
        }

        // —— 3. 探索：效果授予的行动整段交给界面 ——

        [Test]
        public void 探索入场效果_玩家行动阶段不再连环提问_直接登记待执行行动()
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
            Assert.IsTrue(session.IsComplete, "效果授予的探索交给界面走完整流程，会话里不再连环提问");

            // 试跑跑在副本上：正式状态没有被登记。
            Assert.IsNull(state.FindPending(0, PendingActionKind.Explore));

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.ExploreOnce() },
                new RecordedChoices(session.Answers, strict: false));

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.IsNotNull(state.FindPending(0, PendingActionKind.Explore));
            Assert.IsFalse(state.Map.Node(1).HasResourceToken, "指示物等玩家在界面上真的探索时才翻");
        }

        // —— 4. 进行一次建造：同样交给界面（选牌 / 落点 / 付费方式都在那边） ——

        [Test]
        public void 建造入场效果_玩家行动阶段不再连环提问_直接登记待执行行动()
        {
            GameEngine engine = NewGame();
            GameState state = engine.State;
            state.DeveloperMode = true;
            int builtBefore = state.Players[0].BuiltFacilities.Count;

            var session = new EffectChoiceSession(state,
                (sandbox, choices) => EffectResolver.Execute(sandbox, 0, new[] { RewardOp.BuildOnce() }, choices));

            session.Advance();
            Assert.IsTrue(session.IsComplete, "效果授予的建设交给界面走完整流程");

            EffectReport report = EffectResolver.Execute(state, 0, new[] { RewardOp.BuildOnce() },
                new RecordedChoices(session.Answers, strict: false));

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.IsNotNull(state.FindPending(0, PendingActionKind.Build));
            Assert.AreEqual(builtBefore, state.Players[0].BuiltFacilities.Count, "还没真的建造");
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
