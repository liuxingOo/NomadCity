using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>企业扩展：数据目录、开局抽选、合作等级与快照往返。</summary>
    public sealed class EnterpriseTests
    {
        private const int Seed = 20261110;

        private static GameEngine NewEnterpriseGame(int playerCount = 4, int seed = Seed)
        {
            GameEngine engine = GameEngine.Create(
                new GameSetup(playerCount, ExpansionFlags.Enterprise), 0, null,
                PlaceholderCardCatalog.Default, new Random(seed));
            Assert.IsTrue(engine.Dispatch(new StartGameCommand(0)).Ok);
            return engine;
        }

        [Test]
        public void 企业目录_八块板各有零到五级()
        {
            Assert.AreEqual(8, EnterpriseCatalog.Boards.Count);
            AssertUniqueIds();

            foreach (EnterpriseBoardDef board in EnterpriseCatalog.Boards)
            {
                Assert.IsNotEmpty(board.Name);
                for (int level = 1; level <= board.MaxLevel; level++)
                    Assert.IsNotEmpty(board.Level(level).Text, $"{board.Name} {level} 级没有文字。");
            }

            Assert.AreEqual(3, EnterpriseCatalog.Departments.Count);
            foreach (RhineDepartmentDef department in EnterpriseCatalog.Departments)
            {
                for (int level = 2; level <= 5; level++)
                    Assert.IsNotNull(department.Level(level), $"{department.Name} 缺少 {level} 级奖励。");
            }
        }

        private static void AssertUniqueIds()
        {
            var seen = new HashSet<int>();
            foreach (EnterpriseBoardDef board in EnterpriseCatalog.Boards)
                Assert.IsTrue(seen.Add((int)board.Id), $"企业 id 重复：{board.Id}");
        }

        [Test]
        public void 开局_每名玩家抽到两张不同的企业板()
        {
            GameEngine engine = NewEnterpriseGame(4);
            EnterpriseState enterprises = engine.State.Enterprises;

            Assert.IsFalse(enterprises.DraftDone);
            Assert.IsEmpty(enterprises.ActiveBoards);

            for (int seat = 0; seat < 4; seat++)
            {
                Assert.AreEqual(2, enterprises.Offers[seat].Count, $"座位 {seat} 应当抽到 2 张企业板。");
                Assert.AreNotEqual(enterprises.Offers[seat][0], enterprises.Offers[seat][1]);
            }
        }

        [Test]
        public void 选完企业_自动补足四块板并把本人提升到一级()
        {
            GameEngine engine = NewEnterpriseGame(3);
            EnterpriseState enterprises = engine.State.Enterprises;

            for (int seat = 0; seat < 3; seat++)
            {
                int boardId = enterprises.Offers[seat][0];
                CommandResult result = engine.Dispatch(new ChooseInitialEnterpriseCommand(seat, boardId));
                Assert.IsTrue(result.Ok, result.Error);
                Assert.AreEqual(boardId, enterprises.ChosenBoards[seat]);
            }

            Assert.IsTrue(enterprises.DraftDone);
            Assert.AreEqual(4, enterprises.ActiveBoards.Count, "3 人局应当有 4 块场上企业板。");

            for (int seat = 0; seat < 3; seat++)
                Assert.AreEqual(1, enterprises.LevelOf(enterprises.ChosenBoards[seat], seat));
        }

        [Test]
        public void 没有待提升时_提升合作等级会被拒绝()
        {
            GameEngine engine = NewEnterpriseGame(4);
            EnterpriseState enterprises = engine.State.Enterprises;

            int boardId = enterprises.Offers[0][0];
            Assert.IsTrue(engine.Dispatch(new ChooseInitialEnterpriseCommand(0, boardId)).Ok);

            CommandResult result = engine.Dispatch(new AdvanceEnterpriseLevelCommand(0, boardId));
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("没有等着你提升", result.Error);
        }

        [Test]
        public void 快照往返保留企业状态()
        {
            GameEngine engine = NewEnterpriseGame(4);
            EnterpriseState enterprises = engine.State.Enterprises;

            int boardId = enterprises.Offers[1][1];
            Assert.IsTrue(engine.Dispatch(new ChooseInitialEnterpriseCommand(1, boardId)).Ok);
            enterprises.MarkEffectUsed(boardId, 1, 1);

            byte[] first = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(engine.State));
            GameStateSnapshot decoded = GameStateSnapshotCodec.Read(first, 0, first.Length);
            GameState restored = decoded.Restore(PlaceholderCardCatalog.Default);
            byte[] second = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(restored));

            CollectionAssert.AreEqual(first, second, "企业状态快照往返后不一致。");
            Assert.AreEqual(boardId, restored.Enterprises.ChosenBoards[1]);
            Assert.AreEqual(1, restored.Enterprises.LevelOf(boardId, 1));
            Assert.IsTrue(restored.Enterprises.HasUsedEffectThisTurn(boardId, 1, 1));
        }
    }
}
