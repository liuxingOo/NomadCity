using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 整局自动模拟：用引擎自带的「挂机跳过」机器人（<see cref="SkipPendingActionCommand"/>）
    /// 把一局从头走到终局，验证全流程不会卡在某个必须手点的步骤上。
    ///
    /// 这是「不想在 Unity 里逐步手测」的自动化替代：
    /// 机器人会替每位玩家做最保守的选择（选第一个初始资源点 / 结算事件 / 盖放第一张牌 /
    /// 每轮放弃主要行动 / 采集提交空选择 / 结束回合），企业扩展的抽企业与「7 选 5」由本测试显式驱动。
    /// 跑一遍不到一秒，可以直接当作每次改规则后的回归测试。
    /// </summary>
    public sealed class FullGameSimulationTests
    {
        /// <summary>步数上限：正常一局远小于这个数；超了说明某一步卡死了。</summary>
        private const int MaxSteps = 20000;

        [Test]
        public void 完整对局_四人基础局自动走到终局()
        {
            GameEngine engine = GameEngine.Create(new GameSetup(4), 0, SimulationMap(),
                PlaceholderCardCatalog.Default, new Random(20261117));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok, "开局命令应当成功。");

            PlayToEnd(engine);

            GameState state = engine.State;
            Assert.IsTrue(state.IsGameOver, "对局应当走到终局。");
            Assert.AreEqual(TurnFlow.TotalTurns, state.TurnNumber, "终局应当停在第 8 回合。");
            Assert.AreEqual(TurnPhase.Cleanup, state.Phase, "终局应当在最后一个回合的收尾阶段结算。");

            for (int seat = 0; seat < state.Players.Count; seat++)
                Assert.GreaterOrEqual(state.Players[seat].MobileCityNodeId, 0,
                    $"座位 {seat} 始终没有停靠过移动城市，开局入场没走完。");
        }

        [Test]
        public void 完整对局_企业扩展七选五后只剩五张且弃掉的牌整局不再出现()
        {
            GameEngine engine = GameEngine.Create(new GameSetup(4, ExpansionFlags.Enterprise), 0,
                SimulationMap(), PlaceholderCardCatalog.Default, new Random(20261117));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok, "开局命令应当成功。");

            // 企业扩展：每名玩家开局拿到 7 张（基础 5 + 山 / 玛恩纳 2 张）。
            for (int seat = 0; seat < engine.State.Players.Count; seat++)
                Assert.AreEqual(CardCounts.CharacterHandSizeWithExtension,
                    engine.State.Players[seat].HandCharacterCards.Count,
                    $"座位 {seat} 开局应当拿到 7 张角色牌。");

            var discarded = new List<int>();
            int steps = 0;
            while (!AllSetupDiscardDone(engine.State))
            {
                Assert.Less(steps++, 500,
                    "开局「7 选 5」没有在合理步数内完成：" + Describe(engine.State));
                Assert.IsTrue(StepOnce(engine, discarded),
                    "第 " + steps + " 步找不到可执行的动作：" + Describe(engine.State));
            }

            // 7 选 5 完成后：每人手牌 5 张，被放回游戏盒的牌已经从手牌移出且没有进弃牌区。
            for (int seat = 0; seat < engine.State.Players.Count; seat++)
            {
                PlayerState player = engine.State.Players[seat];
                Assert.IsTrue(player.SetupDiscardDone, $"座位 {seat} 应当完成开局弃牌。");
                Assert.AreEqual(5, player.HandCharacterCards.Count, $"座位 {seat} 应当只剩 5 张角色牌。");
                Assert.IsEmpty(player.DiscardCharacterCards, "开局放回游戏盒的牌不该进弃牌区。");
            }

            Assert.AreEqual(8, discarded.Count, "4 人局每人弃 2 张，一共应弃 8 张。");
            CollectionAssert.AllItemsAreUnique(discarded, "被放回游戏盒的牌不应重复。");
            AssertDiscardedNowhere(engine.State, discarded);

            // 继续把这一局走完：被弃的牌整局都不会再回到任何玩家的区域。
            PlayToEnd(engine, discarded);

            Assert.IsTrue(engine.State.IsGameOver, "对局应当走到终局。");
            AssertDiscardedNowhere(engine.State, discarded);
        }

        // ————————————————————— 机器人 —————————————————————

        /// <summary>
        /// 模拟用的最小地图：5 个绿色资源点连成一条链，够 4 个人各选一个初始资源点。
        /// 机器人不会主动探索 / 城市移动，所以不需要真实地图的完整结构。
        /// </summary>
        private static MapState SimulationMap()
        {
            var map = new MapState();
            for (int i = 0; i < 5; i++)
                map.Nodes.Add(new MapNodeState(i, $"A-0{i + 1}", District.A, NodeColor.Green, false, 2));
            for (int i = 0; i < 4; i++)
                map.Routes.Add(new RouteState(i, new[] { i, i + 1 }, District.A, 1));
            return map;
        }

        /// <summary>一直走到终局。任何一步无解都直接断言失败，并带上当时的状态。</summary>
        private static void PlayToEnd(GameEngine engine, List<int> discarded = null)
        {
            int steps = 0;
            while (!engine.State.IsGameOver)
            {
                Assert.Less(steps++, MaxSteps,
                    "整局模拟没有在合理步数内结束，可能某一步卡住了：" + Describe(engine.State));
                Assert.IsTrue(StepOnce(engine, discarded),
                    "第 " + steps + " 步找不到可执行的动作：" + Describe(engine.State));
            }
        }

        /// <summary>
        /// 替「现在该操作的人」做一步。返回 false 表示当前状态没有机器人能做的动作（卡住了）。
        /// 企业相关的开局步骤（抽企业、7 选 5、待提升等级）先于挂机跳过处理，因为它们不在
        /// <see cref="SkipPendingActionCommand.PendingSeats"/> 的覆盖范围内。
        /// </summary>
        private static bool StepOnce(GameEngine engine, List<int> discarded)
        {
            GameState state = engine.State;
            // 只有启用企业扩展后 Enterprises 才有意义；基础局里这个对象是空的。
            EnterpriseState enterprises = EnterpriseRules.Enabled(state) ? state.Enterprises : null;

            // 开局：抽 2 选 1（2 人局抽 4 选 2，连续选两次）。
            if (enterprises != null && !enterprises.DraftDone)
            {
                for (int seat = 0; seat < state.Players.Count; seat++)
                {
                    if (enterprises.SeatDraftDone(seat)) continue;
                    IReadOnlyList<int> offers = enterprises.Offers[seat];
                    if (offers.Count == 0) continue;
                    return engine.Dispatch(new ChooseInitialEnterpriseCommand(seat, offers[0])).Ok;
                }

                return false;
            }

            // 开局「7 选 5」：开局入场（选点 + 事件牌）走完后，每名玩家把手里 2 张放回游戏盒。
            if (enterprises != null && enterprises.DraftDone
                && ChooseStartNodeCommand.CurrentOperator(state) < 0)
            {
                for (int seat = 0; seat < state.Players.Count; seat++)
                {
                    PlayerState player = state.Players[seat];
                    if (player.SetupDiscardDone) continue;
                    if (player.HandCharacterCards.Count < 2) continue;

                    int a = player.HandCharacterCards[0];
                    int b = player.HandCharacterCards[1];
                    if (!engine.Dispatch(new DiscardSetupHandCardsCommand(seat, a, b)).Ok) return false;

                    discarded?.Add(a);
                    discarded?.Add(b);
                    return true;
                }
            }

            // 企业特效 / 红色区域开放要求提升 1 级：随便挑一块还没满级的板。
            if (enterprises != null && enterprises.PendingAdvanceSeat >= 0)
            {
                int seat = enterprises.PendingAdvanceSeat;
                int board = FirstAdvanceableBoard(state, seat);
                if (board < 0) return false;
                return engine.Dispatch(new AdvanceEnterpriseLevelCommand(seat, board)).Ok;
            }

            List<int> pending = SkipPendingActionCommand.PendingSeats(state);
            if (pending.Count == 0) return false;
            return engine.Dispatch(new SkipPendingActionCommand(pending[0])).Ok;
        }

        // ————————————————————— 断言辅助 —————————————————————

        private static bool AllSetupDiscardDone(GameState state)
        {
            for (int seat = 0; seat < state.Players.Count; seat++)
                if (!state.Players[seat].SetupDiscardDone) return false;
            return true;
        }

        /// <summary>这些牌必须已经从本局消失：不在任何人的手牌、弃牌区、盖放位里。</summary>
        private static void AssertDiscardedNowhere(GameState state, List<int> discarded)
        {
            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                PlayerState player = state.Players[seat];
                foreach (int cardId in discarded)
                {
                    CollectionAssert.DoesNotContain(player.HandCharacterCards, cardId,
                        $"座位 {seat} 的手牌里又出现了本局已移除的角色牌 {cardId}。");
                    CollectionAssert.DoesNotContain(player.DiscardCharacterCards, cardId,
                        $"座位 {seat} 的弃牌区里出现了本局已移除的角色牌 {cardId}。");
                    Assert.AreNotEqual(cardId, player.PlacedCharacterCard,
                        $"座位 {seat} 盖放的正是本局已移除的角色牌 {cardId}。");
                }
            }
        }

        private static int FirstAdvanceableBoard(GameState state, int seat)
        {
            EnterpriseState enterprises = state.Enterprises;
            foreach (int boardId in enterprises.ActiveBoards)
            {
                if (boardId == enterprises.PendingAdvanceExcludeBoard) continue;
                int level = enterprises.LevelOf(boardId, seat);
                if (level >= 0 && level < 5) return boardId;
            }

            return -1;
        }

        /// <summary>卡住时把「停在哪」写进断言信息，方便一眼看出缺了哪一步。</summary>
        private static string Describe(GameState state)
        {
            if (state == null) return "（没有游戏状态）";

            string enterprise = state.Enterprises == null
                ? string.Empty
                : $"，企业：抽选{(state.Enterprises.DraftDone ? "已" : "未")}完成"
                  + $"，待提升座位 {state.Enterprises.PendingAdvanceSeat}"
                  + $"，红区座位 {state.Enterprises.RedZoneAdvanceSeat}";

            return $"第 {state.TurnNumber} 回合 · {state.Phase} · 行动座位 {state.ActiveSeat}"
                   + $" · 采集待选 {state.HarvestPending}{enterprise}";
        }
    }
}
