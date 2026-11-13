using System;
using System.Collections.Generic;

namespace NomadCity.Core.Model.Enterprise
{
    /// <summary>
    /// 企业扩展的运行时状态（联机快照的一部分）。
    ///
    /// 术语：
    /// - **企业牌堆**：8 张合作企业板，背面朝上。3／4 人局每人抽 2 选 1；2 人局每人抽 4 选 2。
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

        /// <summary>干员牌堆（8 张干员角色牌 id）；罗德岛 3 / 5 级【干员特派】从这里抽牌。</summary>
        public readonly List<int> OperatorDeck = new();

        /// <summary>本局场上的企业板 id（按上场顺序）。</summary>
        public readonly List<int> ActiveBoards = new();

        /// <summary>开局每人要选几张企业板：3／4 人局「抽 2 选 1」= 1；2 人局「抽 4 选 2」= 2。</summary>
        public int PicksPerPlayer = 1;

        /// <summary>各座位开局抽到的张数（= <see cref="PicksPerPlayer"/> × 2）。</summary>
        public int OffersPerSeat = 2;

        /// <summary>各座位开局抽到的企业板 id（选完即空）。</summary>
        public readonly List<List<int>> Offers = new();

        /// <summary>各座位最终选择的企业板 id；-1 = 还没选。</summary>
        public readonly List<int> ChosenBoards = new();

        /// <summary>各座位选择的第 2 张企业板 id（只有 2 人局会用到）；-1 = 还没选。</summary>
        public readonly List<int> SecondChosenBoards = new();

        /// <summary>各座位当前的莱茵生命科室板 id；-1 = 没有（不是莱茵生命）。</summary>
        public readonly List<int> Departments = new();

        /// <summary>合作等级：第一维是企业板 id，第二维是座位号；-1 = 该玩家不在这块板上。</summary>
        private int[][] levels = Array.Empty<int[]>();

        /// <summary>企业特效使用记录：位掩码，第 level 位 = 本回合该级特效已用过。</summary>
        private int[][] usedMasks = Array.Empty<int[]>();

        /// <summary>开局企业抽选（3／4 人局抽 2 选 1、2 人局抽 4 选 2）是否已经全部完成。</summary>
        public bool DraftDone;

        /// <summary>正等着谁来提升 1 级合作等级；-1 = 没有待处理的提升。</summary>
        public int PendingAdvanceSeat = -1;

        /// <summary>待提升时不能选的企业板 id（「选择 1 家其他企业」时排除自己这张）；-1 = 不排除。</summary>
        public int PendingAdvanceExcludeBoard = -1;

        /// <summary>待提升的原因（状态行文案用）。</summary>
        public string PendingAdvanceReason = string.Empty;

        /// <summary>红色区域开放：本轮「每人选 1 家企业提升 1 级」是否已经开始。</summary>
        public bool RedZoneAdvanceStarted;

        /// <summary>红色区域开放：正在等哪一位玩家提升；-1 = 当前没有红区提升。</summary>
        public int RedZoneAdvanceSeat = -1;

        /// <summary>红色区域开放：已经处理过的座位位掩码（完成或没有可提升的板）。</summary>
        public int RedZoneAdvanceDoneMask;

        /// <summary>本回合是否有企业特效被触发过（收尾阶段复位）。</summary>
        public void ResetTurnEffects()
        {
            foreach (int[] masks in usedMasks)
                for (int seat = 0; seat < masks.Length; seat++)
                    masks[seat] = 0;
        }

        /// <summary>重置成「还没开局」的状态：只保留座位数量与每人要选的张数。</summary>
        public void Reset(int playerCount, int picksPerPlayer = 1)
        {
            PicksPerPlayer = picksPerPlayer < 1 ? 1 : picksPerPlayer > 2 ? 2 : picksPerPlayer;
            OffersPerSeat = PicksPerPlayer * 2;

            Deck.Clear();
            OperatorDeck.Clear();
            for (int id = 0; id < CardCounts.OperatorCards; id++)
                OperatorDeck.Add(CardIds.OperatorCharacterBase + id);
            ActiveBoards.Clear();
            Offers.Clear();
            ChosenBoards.Clear();
            SecondChosenBoards.Clear();
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
                Offers.Add(new List<int>(OffersPerSeat));
                ChosenBoards.Add(-1);
                SecondChosenBoards.Add(-1);
                Departments.Add(-1);
            }

            DraftDone = false;
            PendingAdvanceSeat = -1;
            PendingAdvanceExcludeBoard = -1;
            PendingAdvanceReason = string.Empty;
            RedZoneAdvanceStarted = false;
            RedZoneAdvanceSeat = -1;
            RedZoneAdvanceDoneMask = 0;
        }

        /// <summary>某座位已经选定的企业板张数。</summary>
        public int ChosenCount(int seat)
        {
            if (seat < 0 || seat >= ChosenBoards.Count) return 0;
            int count = ChosenBoards[seat] >= 0 ? 1 : 0;
            if (seat < SecondChosenBoards.Count && SecondChosenBoards[seat] >= 0) count++;
            return count;
        }

        /// <summary>某座位的开局企业抽选是否已完成（2 人局要选满 2 张）。</summary>
        public bool SeatDraftDone(int seat) => ChosenCount(seat) >= PicksPerPlayer;

        /// <summary>某座位是否已经选过这张企业板。</summary>
        public bool HasChosen(int seat, int boardId)
        {
            if (seat < 0 || seat >= ChosenBoards.Count) return false;
            if (ChosenBoards[seat] == boardId) return true;
            return seat < SecondChosenBoards.Count && SecondChosenBoards[seat] == boardId;
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
                PicksPerPlayer = PicksPerPlayer,
                OffersPerSeat = OffersPerSeat,
                PendingAdvanceSeat = PendingAdvanceSeat,
                PendingAdvanceExcludeBoard = PendingAdvanceExcludeBoard,
                PendingAdvanceReason = PendingAdvanceReason };

            copy.Deck.AddRange(Deck);
            copy.OperatorDeck.AddRange(OperatorDeck);
            copy.ActiveBoards.AddRange(ActiveBoards);
            copy.ChosenBoards.AddRange(ChosenBoards);
            copy.SecondChosenBoards.AddRange(SecondChosenBoards);
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
