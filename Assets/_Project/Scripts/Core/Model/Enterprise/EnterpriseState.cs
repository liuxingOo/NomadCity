using System;
using System.Collections.Generic;

namespace NomadCity.Core.Model.Enterprise
{
    /// <summary>
    /// 企业扩展的运行时状态（联机快照的一部分）。
    ///
    /// 术语：
    /// - **企业牌堆**：8 张合作企业板，背面朝上。开局每名玩家抽 2 张，选 1 张。
    /// - **场上企业板**：本局实际使用的 4 块（4 人局 = 4 名玩家各 1 张；2／3 人局补足到 4 块）。
    /// - **合作等级**：每名玩家在每块场上企业板上各有一个 0–5 级的等级。
    ///   等级只升不降（结构科 5 级特效除外）。
    /// - **企业特效**：1 级起，牌面标了「企业特效」的等级。升级只解锁，不自动结算；
    ///   玩家可在自己的回合内触发，同一块板的同一个等级**一回合只能触发 1 次**。
    /// </summary>
    public sealed class EnterpriseState
    {
        /// <summary>企业牌堆（企业板 id，背面朝上）。</summary>
        public readonly List<int> Deck = new();

        /// <summary>本局场上的企业板 id（按上场顺序）。</summary>
        public readonly List<int> ActiveBoards = new();

        /// <summary>各座位开局抽到的 2 张企业板 id（选完即空）。</summary>
        public readonly List<List<int>> Offers = new();

        /// <summary>各座位最终选择的企业板 id；-1 = 还没选。</summary>
        public readonly List<int> ChosenBoards = new();

        /// <summary>各座位当前的莱茵生命科室板 id；-1 = 没有（不是莱茵生命）。</summary>
        public readonly List<int> Departments = new();

        /// <summary>合作等级：第一维是企业板 id，第二维是座位号；-1 = 该玩家不在这块板上。</summary>
        private int[][] levels = Array.Empty<int[]>();

        /// <summary>企业特效使用记录：位掩码，第 level 位 = 本回合该级特效已用过。</summary>
        private int[][] usedMasks = Array.Empty<int[]>();

        /// <summary>开局「抽 2 选 1」是否已经全部完成。</summary>
        public bool DraftDone;

        /// <summary>正等着谁来提升 1 级合作等级；-1 = 没有待处理的提升。</summary>
        public int PendingAdvanceSeat = -1;

        /// <summary>待提升时不能选的企业板 id（「选择 1 家其他企业」时排除自己这张）；-1 = 不排除。</summary>
        public int PendingAdvanceExcludeBoard = -1;

        /// <summary>待提升的原因（状态行文案用）。</summary>
        public string PendingAdvanceReason = string.Empty;

        /// <summary>本回合是否有企业特效被触发过（收尾阶段复位）。</summary>
        public void ResetTurnEffects()
        {
            foreach (int[] masks in usedMasks)
                for (int seat = 0; seat < masks.Length; seat++)
                    masks[seat] = 0;
        }

        /// <summary>重置成「还没开局」的状态：只保留座位数量。</summary>
        public void Reset(int playerCount)
        {
            Deck.Clear();
            ActiveBoards.Clear();
            Offers.Clear();
            ChosenBoards.Clear();
            Departments.Clear();

            levels = new int[EnterpriseCatalog.BoardCount][];
            usedMasks = new int[EnterpriseCatalog.BoardCount][];
            for (int board = 0; board < EnterpriseCatalog.BoardCount; board++)
            {
                levels[board] = new int[playerCount];
                usedMasks[board] = new int[playerCount];
                for (int seat = 0; seat < playerCount; seat++) levels[board][seat] = -1;
            }

            for (int seat = 0; seat < playerCount; seat++)
            {
                Offers.Add(new List<int>(2));
                ChosenBoards.Add(-1);
                Departments.Add(-1);
            }

            DraftDone = false;
            PendingAdvanceSeat = -1;
            PendingAdvanceExcludeBoard = -1;
            PendingAdvanceReason = string.Empty;
        }

        public bool IsActive(int boardId) => ActiveBoards.Contains(boardId);

        /// <summary>某座位在某块企业板上的合作等级；-1 = 不在这块板上。</summary>
        public int LevelOf(int boardId, int seat)
        {
            if (boardId < 0 || boardId >= levels.Length) return -1;
            int[] row = levels[boardId];
            if (seat < 0 || seat >= row.Length) return -1;
            return row[seat];
        }

        /// <summary>把一块企业板放上场：所有座位在这块板上的等级从 0 开始。</summary>
        public void Activate(int boardId)
        {
            if (boardId < 0 || boardId >= levels.Length) return;
            if (!ActiveBoards.Contains(boardId)) ActiveBoards.Add(boardId);
            int[] row = levels[boardId];
            for (int seat = 0; seat < row.Length; seat++)
                if (row[seat] < 0) row[seat] = 0;
        }

        /// <summary>提升 1 级（不超过 5 级）。返回新的等级。</summary>
        public int Advance(int boardId, int seat)
        {
            if (boardId < 0 || boardId >= levels.Length) return -1;
            int[] row = levels[boardId];
            if (seat < 0 || seat >= row.Length) return -1;
            if (row[seat] < 0) return -1;
            if (row[seat] < 5) row[seat]++;
            return row[seat];
        }

        /// <summary>降低 1 级（结构科 5 级特效，最低 0 级）。返回新的等级。</summary>
        public int Lower(int boardId, int seat)
        {
            if (boardId < 0 || boardId >= levels.Length) return -1;
            int[] row = levels[boardId];
            if (seat < 0 || seat >= row.Length) return -1;
            if (row[seat] < 0) return -1;
            if (row[seat] > 0) row[seat]--;
            return row[seat];
        }

        /// <summary>企业特效使用记录的位掩码（联机快照用）。</summary>
        public int UsedMaskOf(int boardId, int seat)
        {
            if (boardId < 0 || boardId >= usedMasks.Length) return 0;
            int[] row = usedMasks[boardId];
            if (seat < 0 || seat >= row.Length) return 0;
            return row[seat];
        }

        /// <summary>直接写入合作等级（联机还原用，不做合法性检查）。</summary>
        public void SetLevel(int boardId, int seat, int level)
        {
            if (boardId < 0 || boardId >= levels.Length) return;
            int[] row = levels[boardId];
            if (seat < 0 || seat >= row.Length) return;
            row[seat] = level;
        }

        /// <summary>直接写入企业特效使用记录（联机还原用）。</summary>
        public void SetUsedMask(int boardId, int seat, int mask)
        {
            if (boardId < 0 || boardId >= usedMasks.Length) return;
            int[] row = usedMasks[boardId];
            if (seat < 0 || seat >= row.Length) return;
            row[seat] = mask;
        }

        public bool HasUsedEffectThisTurn(int boardId, int seat, int level)
        {
            if (boardId < 0 || boardId >= usedMasks.Length) return false;
            int[] row = usedMasks[boardId];
            if (seat < 0 || seat >= row.Length) return false;
            return (row[seat] & (1 << level)) != 0;
        }

        public void MarkEffectUsed(int boardId, int seat, int level)
        {
            if (boardId < 0 || boardId >= usedMasks.Length) return;
            int[] row = usedMasks[boardId];
            if (seat < 0 || seat >= row.Length) return;
            row[seat] |= 1 << level;
        }

        /// <summary>深拷贝一份（供 <see cref="GameState.Clone"/> 用）。</summary>
        public EnterpriseState Clone()
        {
            var copy = new EnterpriseState { DraftDone = DraftDone,
                PendingAdvanceSeat = PendingAdvanceSeat,
                PendingAdvanceExcludeBoard = PendingAdvanceExcludeBoard,
                PendingAdvanceReason = PendingAdvanceReason };

            copy.Deck.AddRange(Deck);
            copy.ActiveBoards.AddRange(ActiveBoards);
            copy.ChosenBoards.AddRange(ChosenBoards);
            copy.Departments.AddRange(Departments);
            foreach (List<int> offer in Offers) copy.Offers.Add(new List<int>(offer));

            copy.levels = new int[levels.Length][];
            for (int board = 0; board < levels.Length; board++) copy.levels[board] = (int[])levels[board].Clone();

            copy.usedMasks = new int[usedMasks.Length][];
            for (int board = 0; board < usedMasks.Length; board++) copy.usedMasks[board] = (int[])usedMasks[board].Clone();

            return copy;
        }
    }
}
