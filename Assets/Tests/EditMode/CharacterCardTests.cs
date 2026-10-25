using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 角色牌机制（§6.5a / §10.1）与基础 5 角色的策略 / 计谋效果（<c>Doc/CardEffects.md</c> §3）的单元测试。
    /// 角色名与 id 的对应：绿 0 雷蛇 / 1 极境 / 2 德克萨斯 / 3 锡人 / 4 坎诺特。
    /// </summary>
    public sealed class CharacterCardTests
    {
        private const int RazerCard = 0;       // 雷蛇
        private const int JijingCard = 1;      // 极境
        private const int TexasCard = 2;       // 德克萨斯
        private const int TinmanCard = 3;      // 锡人
        private const int CannotteCard = 4;    // 坎诺特

        private static MapState SmallMap()
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
            return map;
        }

        /// <summary>建局并停在「角色牌盖放」阶段（§4.2 阶段 1）。</summary>
        private static GameEngine NewGameAtPlacement(int playerCount = 4, int firstSeat = 0)
        {
            GameEngine engine = GameEngine.Create(new GameSetup(playerCount), firstSeat, SmallMap(),
                PlaceholderCardCatalog.Default, new Random(1));
            engine.Dispatch(new StartGameCommand(firstSeat));
            return engine;
        }

        /// <summary>建局并推进到「玩家行动」阶段。</summary>
        private static GameEngine NewGameAtActions(int playerCount = 4, int firstSeat = 0)
        {
            GameEngine engine = NewGameAtPlacement(playerCount, firstSeat);
            engine.Dispatch(new AdvancePhaseCommand());
            return engine;
        }

        /// <summary>盖放一张牌后再推进到「玩家行动」阶段（盖放只能在盖放阶段做）。</summary>
        private static GameEngine PlaceThenAdvance(int seat, int cardId, int playerCount = 4, int firstSeat = 0)
        {
            GameEngine engine = NewGameAtPlacement(playerCount, firstSeat);
            engine.Dispatch(new PlaceCharacterCardCommand(seat, cardId));
            engine.Dispatch(new AdvancePhaseCommand());
            return engine;
        }

        private static CharacterCardDef Role(int role) =>
            PlaceholderCardCatalog.Default.Character(CardIds.CharacterBase + role);

        private static int OwnInfluenceCount(GameState state, int seat)
        {
            int count = 0;
            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                {
                    if (node.Slots.OwnerOf(i) == seat) count++;
                }
            }
            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                {
                    if (route.Slots.OwnerOf(i) == seat) count++;
                }
            }
            return count;
        }

        // ————————————————————— 盖放（§4.2 阶段 1 / §10.1.1） —————————————————————

        [Test]
        public void 盖放_选中的牌从手牌移到盖放位()
        {
            GameEngine engine = NewGameAtPlacement();
            PlayerState player = engine.State.Players[0];
            int card = player.HandCharacterCards[0];

            CommandResult result = engine.Dispatch(new PlaceCharacterCardCommand(0, card));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(card, player.PlacedCharacterCard);
            CollectionAssert.DoesNotContain(player.HandCharacterCards, card);
            Assert.AreEqual(4, player.HandCharacterCards.Count);
        }

        [Test]
        public void 盖放_手牌为空时先回收弃牌区再盖放()
        {
            GameEngine engine = NewGameAtPlacement();
            PlayerState player = engine.State.Players[0];
            player.HandCharacterCards.Clear();
            player.DiscardCharacterCards.AddRange(new[] { 10, 11, 12 });

            CommandResult result = engine.Dispatch(new PlaceCharacterCardCommand(0, 10));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(10, player.PlacedCharacterCard);
            CollectionAssert.AreEquivalent(new[] { 11, 12 }, player.HandCharacterCards);
            Assert.IsEmpty(player.DiscardCharacterCards);
        }

        [Test]
        public void 盖放_不能重复盖放同一回合()
        {
            GameEngine engine = NewGameAtPlacement();
            PlayerState player = engine.State.Players[0];
            engine.Dispatch(new PlaceCharacterCardCommand(0, player.HandCharacterCards[0]));

            CommandResult again = engine.Dispatch(new PlaceCharacterCardCommand(0, player.HandCharacterCards[0]));

            Assert.IsFalse(again.Ok);
        }

        [Test]
        public void 盖放_行动阶段不能再盖放()
        {
            GameEngine engine = NewGameAtActions();
            PlayerState player = engine.State.Players[0];

            CommandResult result = engine.Dispatch(new PlaceCharacterCardCommand(0, player.HandCharacterCards[0]));

            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void 盖放_所有人盖放完自动进入玩家行动_从起始玩家开始()
        {
            GameEngine engine = NewGameAtPlacement(4, 2);
            GameState state = engine.State;
            // 跳过第 1 回合的开局入场闸门（那条闸门由下面的用例单独验证）。
            state.TurnNumber = 2;

            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                PlayerState player = state.Players[seat];
                CommandResult result = engine.Dispatch(new PlaceCharacterCardCommand(seat, player.HandCharacterCards[0]));
                Assert.IsTrue(result.Ok, result.Error);
            }

            Assert.AreEqual(TurnPhase.PlayerActions, state.Phase, "最后一名玩家盖放完，应自动进入玩家行动。");
            Assert.AreEqual(1, state.ActionRound);
            Assert.AreEqual(2, state.ActiveSeat, "玩家行动第 1 轮从起始玩家开始。");
        }

        [Test]
        public void 盖放_第1回合开局入场没选完时不会自动进入玩家行动()
        {
            GameEngine engine = NewGameAtPlacement(4, 0);
            GameState state = engine.State;

            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                PlayerState player = state.Players[seat];
                engine.Dispatch(new PlaceCharacterCardCommand(seat, player.HandCharacterCards[0]));
            }

            Assert.AreEqual(TurnPhase.CharacterPlacement, state.Phase,
                "第 1 回合还没人选初始资源点，不能跳过开局入场进入玩家行动。");
        }

        // ————————————————————— 使用（§6.3a / §6.5a / §10.1.2） —————————————————————

        [Test]
        public void 使用_非起始玩家发动一种效果后弃牌并标记用过()
        {
            GameEngine engine = PlaceThenAdvance(0, RazerCard);
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MarkersInSupply = 30;
            state.ActiveSeat = 0;
            state.FirstPlayerSeat = 1; // 0 号不是起始玩家

            CommandResult result = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Strategy));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(player.UsedCharacterCardThisTurn);
            Assert.IsTrue(player.UsedCharacterStrategyThisTurn);
            Assert.AreEqual(-1, player.PlacedCharacterCard);
            CollectionAssert.Contains(player.DiscardCharacterCards, RazerCard);
            Assert.AreEqual(2, OwnInfluenceCount(state, 0));
        }

        [Test]
        public void 使用_非起始玩家不能再发动另一种效果()
        {
            GameEngine engine = PlaceThenAdvance(0, RazerCard);
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            state.ActiveSeat = 0;
            state.FirstPlayerSeat = 1;
            player.MarkersInSupply = 30;
            player.Vouchers = 10;
            CommandResult first = engine.Dispatch(new UseCharacterCardCommand(0, CharacterEffectKind.Strategy));
            Assert.IsTrue(first.Ok, first.Error);

            CommandResult second = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics));

            Assert.IsFalse(second.Ok);
        }

        [Test]
        public void 使用_起始玩家可以连续发动策略与计谋()
        {
            GameEngine engine = PlaceThenAdvance(0, RazerCard); // 盖放只发生在盖放阶段
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            state.ActiveSeat = 0;
            state.FirstPlayerSeat = 0;
            player.MarkersInSupply = 30;
            player.Vouchers = 10;
            // 对手的影响力放在 0 号点 0 号格：候选按「点号 → 格号」排序，它会排在最前，
            // 「控制阵地」必定把它当成替换目标（§2）。
            state.Map.Node(0).Slots.Place(0, 1);

            CommandResult first = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Strategy));

            Assert.IsTrue(first.Ok, first.Error);
            Assert.IsTrue(player.CharacterSecondEffectPending, "起始玩家用掉一种效果后应等待决定是否继续");
            Assert.AreEqual(RazerCard, player.PlacedCharacterCard, "还没结束使用，牌应留在盖放位");

            CommandResult second = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Tactics));

            Assert.IsTrue(second.Ok, second.Error);
            Assert.IsTrue(player.UsedCharacterCardThisTurn);
            Assert.IsFalse(player.CharacterSecondEffectPending);
            Assert.AreEqual(-1, player.PlacedCharacterCard);
            Assert.AreEqual(7, player.Vouchers);   // 10 - 3
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0)); // 对手影响力被替换成自己的
            Assert.AreEqual(31, state.Players[1].MarkersInSupply, "被替换掉的标记退回原所有者");
        }

        [Test]
        public void 使用_起始玩家连用中途不能插入其他行动()
        {
            GameEngine engine = PlaceThenAdvance(0, RazerCard); // 盖放只发生在盖放阶段
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            state.ActiveSeat = 0;
            state.FirstPlayerSeat = 0;
            player.MarkersInSupply = 30;
            CommandResult used = engine.Dispatch(new UseCharacterCardCommand(0, CharacterEffectKind.Strategy));
            Assert.IsTrue(used.Ok, used.Error);

            CommandResult blocked = engine.Dispatch(
                new PlaceInfluenceCommand(0, InfluenceSlotRef.OnNode(1, 0)));
            Assert.IsFalse(blocked.Ok, "§10.1.2：两种效果之间不得插入其他行动");

            CommandResult end = engine.Dispatch(new EndCharacterCardUseCommand(0));
            Assert.IsTrue(end.Ok, end.Error);
            Assert.IsTrue(player.UsedCharacterCardThisTurn);
            Assert.IsFalse(player.CharacterSecondEffectPending);
            Assert.AreEqual(-1, player.PlacedCharacterCard);
            CollectionAssert.Contains(player.DiscardCharacterCards, RazerCard);

            // 结束之后就能正常行动了。
            CommandResult after = engine.Dispatch(new PlaceInfluenceCommand(0, InfluenceSlotRef.OnNode(1, 0)));
            Assert.IsTrue(after.Ok, after.Error);
        }

        [Test]
        public void 使用_没有盖放牌时被拒绝()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            state.ActiveSeat = 0;
            state.FirstPlayerSeat = 0;
            state.Players[0].MarkersInSupply = 30;

            CommandResult result = engine.Dispatch(
                new UseCharacterCardCommand(0, CharacterEffectKind.Strategy));

            Assert.IsFalse(result.Ok);
        }

        // ————————————————————— 雷蛇 —————————————————————

        [Test]
        public void 雷蛇策略_安保协议_放置两个影响力并延迟移除一个()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MarkersInSupply = 30;

            EffectReport report = EffectResolver.Execute(state, 0, Role(RazerCard).StrategyOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(2, OwnInfluenceCount(state, 0));
            Assert.AreEqual(28, player.MarkersInSupply);
            Assert.AreEqual(1, state.DeferredEffects.Count, "移除应被延迟到收尾阶段");

            CleanupResolver.ResolveDeferredEffects(state);

            Assert.AreEqual(1, OwnInfluenceCount(state, 0));
            Assert.AreEqual(29, player.MarkersInSupply);
        }

        [Test]
        public void 雷蛇计谋_控制阵地_付三金券并替换一个影响力()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MarkersInSupply = 30;
            player.Vouchers = 5;
            state.Map.Node(1).Slots.Place(0, 1); // 对手的影响力

            EffectReport report = EffectResolver.Execute(state, 0, Role(RazerCard).TacticsOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(2, player.Vouchers);
            Assert.AreEqual(0, state.Map.Node(1).Slots.OwnerOf(0), "同格应换成自己的影响力");
            Assert.AreEqual(29, player.MarkersInSupply);
            Assert.AreEqual(31, state.Players[1].MarkersInSupply, "被移除的标记退回原所有者");
        }

        // ————————————————————— 极境 —————————————————————

        [Test]
        public void 极境策略_后勤调配_获得持有量最少的一种资源四个()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Resources.Add(ResourceType.Orirock, 1);
            player.Resources.Add(ResourceType.Originium, 5);
            player.Resources.Add(ResourceType.Oriron, 3);

            EffectReport report = EffectResolver.Execute(state, 0, Role(JijingCard).StrategyOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(5, player.Resources.Get(ResourceType.Orirock), "源岩原来最少，+4");
            Assert.AreEqual(5, player.Resources.Get(ResourceType.Originium));
        }

        [Test]
        public void 极境计谋_导航通讯_付三至纯源石后免费移动到相邻且有自己影响力的点()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;
            player.Resources.Add(ResourceType.OriginitePrime, 3);
            state.Map.Node(1).Slots.Place(0, 0); // 自己的影响力

            EffectReport report = EffectResolver.Execute(state, 0, Role(JijingCard).TacticsOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(1, player.MobileCityNodeId);
            Assert.AreEqual(0, player.Resources.Get(ResourceType.OriginitePrime));
            Assert.AreEqual(0, state.Map.Node(0).Slots.OwnerOf(0), "出发点放下 1 个影响力");
        }

        [Test]
        public void 极境计谋_相邻但没有自己影响力的点不可选()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.MobileCityNodeId = 0;
            player.MarkersInSupply = 30;
            player.Resources.Add(ResourceType.OriginitePrime, 3);

            EffectResolver.Execute(state, 0, Role(JijingCard).TacticsOps);

            // 付费那一步会照常结算，但「免费城市移动」一步要求目标点上有自己的影响力；
            // 没有合法相邻点 → 城市不动、出发点也不放下影响力。
            Assert.AreEqual(0, player.MobileCityNodeId, "相邻点上没有自己的影响力，不应移动");
            Assert.AreEqual(30, player.MarkersInSupply, "没有移动就不应在出发点放下影响力");
            Assert.AreEqual(-1, state.Map.Node(0).Slots.OwnerOf(0));
        }

        // ————————————————————— 德克萨斯 —————————————————————

        [Test]
        public void 德克萨斯策略_特别递送_获得十二金券并把一张供应区设施放回牌堆底部()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            int deckBefore = state.FacilityDeck.Count;
            int chosen = state.FacilitySupply[0];
            int vouchersBefore = player.Vouchers;

            EffectReport report = EffectResolver.Execute(state, 0, Role(TexasCard).StrategyOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(vouchersBefore + 12, player.Vouchers);
            CollectionAssert.DoesNotContain(state.FacilitySupply, chosen, "被放回牌堆的那张应从供应区消失");
            Assert.AreEqual(6, state.FacilitySupply.Count, "按补充规则补牌至 6 张（§6.4e）");
            Assert.AreEqual(deckBefore, state.FacilityDeck.Count, "补牌消耗 1 张、又收回 1 张，总数不变");
        }

        [Test]
        public void 德克萨斯策略_牌堆为空时这张牌被立刻翻回供应区()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            int chosen = state.FacilitySupply[0];
            state.FacilityDeck.Clear();
            state.FacilityDeck.Add(chosen); // 唯一一张牌：放回底部后又被翻回供应区
            state.FacilitySupply.Remove(chosen);

            EffectReport report = EffectResolver.Execute(state, 0, Role(TexasCard).StrategyOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            CollectionAssert.Contains(state.FacilitySupply, chosen, "牌堆空时该牌会被立刻翻回供应区");
        }

        [Test]
        public void 德克萨斯计谋_叙拉古人_付三金券移动三个影响力()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Vouchers = 3;
            player.MarkersInSupply = 30;
            state.Map.Node(0).Slots.Place(0, 0);
            state.Map.Node(0).Slots.Place(1, 0);

            EffectReport report = EffectResolver.Execute(state, 0, Role(TexasCard).TacticsOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(0, player.Vouchers);
            Assert.AreEqual(2, OwnInfluenceCount(state, 0), "移动不增减影响力总数");
            Assert.AreEqual(30, player.MarkersInSupply, "移动不动玩家标记（§6.4b）");
        }

        // ————————————————————— 锡人 —————————————————————

        [Test]
        public void 锡人策略_建立威信_可选链逐步执行()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Vouchers = 27;
            int scoreBefore = player.Score;

            EffectReport report = EffectResolver.Execute(state, 0, Role(TinmanCard).StrategyOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(scoreBefore + 1, player.Score);
            Assert.AreEqual(0, player.Vouchers, "两级各支付 12 / 15 金券");
            Assert.AreEqual(2, player.Resources.Get(ResourceType.OriginitePrime));
        }

        [Test]
        public void 锡人策略_建立威信_中途终止则其后步骤全部跳过()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Vouchers = 27;
            int scoreBefore = player.Score;

            var choices = new RecordedChoices(new[] { 1 }, false); // 第一步就选「终止」
            EffectReport report = EffectResolver.Execute(state, 0, Role(TinmanCard).StrategyOps, choices);

            Assert.IsTrue(report.AnyApplied, "拿到 1 分那一步照常结算");
            Assert.AreEqual(scoreBefore + 1, player.Score);
            Assert.AreEqual(27, player.Vouchers, "终止之后两级都不执行");
            Assert.AreEqual(0, player.Resources.Get(ResourceType.OriginitePrime));
        }

        [Test]
        public void 锡人计谋_人员召集_收回弃牌区并每张二选一()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.HandCharacterCards.Clear();
            player.HandCharacterCards.Add(TinmanCard);
            player.DiscardCharacterCards.AddRange(new[] { 15, 16 });
            int vouchersBefore = player.Vouchers;

            EffectReport report = EffectResolver.Execute(state, 0, Role(TinmanCard).TacticsOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.IsEmpty(player.DiscardCharacterCards);
            CollectionAssert.AreEquivalent(new[] { TinmanCard, 15, 16 }, player.HandCharacterCards);
            Assert.AreEqual(vouchersBefore + 10, player.Vouchers, "两张各选「获得 5 金券」");
        }

        // ————————————————————— 坎诺特 —————————————————————

        [Test]
        public void 坎诺特策略_秘密渠道_按单价把资源换成金券()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Resources.Add(ResourceType.Orirock, 5);
            int vouchersBefore = player.Vouchers;

            var choices = new ScriptedChoices
            {
                SellAmounts = new Dictionary<ResourceType, int> { [ResourceType.Orirock] = 2 },
            };
            EffectReport report = EffectResolver.Execute(state, 0, Role(CannotteCard).StrategyOps, choices);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(3, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(vouchersBefore + 6, player.Vouchers, "源岩单价 3");
        }

        [Test]
        public void 坎诺特计谋_征收物资_全体按二金券卖光所选资源且出牌者得一分()
        {
            GameEngine engine = NewGameAtActions();
            GameState state = engine.State;
            PlayerState player = state.Players[0];
            player.Resources.Add(ResourceType.Orirock, 4);
            state.Players[1].Resources.Add(ResourceType.Orirock, 3);
            int actorVouchers = player.Vouchers;
            int otherVouchers = state.Players[1].Vouchers;
            int scoreBefore = player.Score;

            EffectReport report = EffectResolver.Execute(state, 0, Role(CannotteCard).TacticsOps);

            Assert.IsTrue(report.AnyApplied, report.Describe());
            Assert.AreEqual(0, player.Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(0, state.Players[1].Resources.Get(ResourceType.Orirock));
            Assert.AreEqual(actorVouchers + 8, player.Vouchers, "自己 4 个 × 2 金券");
            Assert.AreEqual(otherVouchers + 6, state.Players[1].Vouchers, "他人各自收取（3 个 × 2）");
            Assert.AreEqual(scoreBefore + 1, player.Score);
        }

        /// <summary>只回答被测节点关心的那个选择，其余一律取首个合法候选。</summary>
        private sealed class ScriptedChoices : IEffectChoices
        {
            public Dictionary<ResourceType, int> SellAmounts;

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
                SellAmounts;

            public IReadOnlyDictionary<ResourceType, int> ChooseResourceSpread(int seat, RewardOp op, GameState state,
                int total, IReadOnlyList<ResourceType> options, IReadOnlyDictionary<ResourceType, int> holdings) =>
                null;

            public int ChooseResourceType(int seat, RewardOp op, GameState state,
                IReadOnlyList<ResourceType> candidates) => candidates.Count > 0 ? 0 : -1;

            public int ChooseSupplyFacility(int seat, RewardOp op, GameState state,
                IReadOnlyList<int> candidates) => candidates.Count > 0 ? candidates[0] : -1;

            public int ChooseOptionalStep(int seat, RewardOp op, GameState state, int stepIndex, string label) => 0;
        }
    }
}
