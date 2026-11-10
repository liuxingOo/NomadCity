using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;

namespace NomadCity.Core.Engine
{
    /// <summary>企业扩展的规则辅助：开局发牌、升级结算、费用校验。</summary>
    public static class EnterpriseRules
    {
        /// <summary>本局是否启用了企业扩展。</summary>
        public static bool Enabled(GameState state) =>
            state?.Setup != null && state.Setup.IsEnterpriseEnabled;

        /// <summary>
        /// 开局：洗混 8 张企业板，按座位依次各发 2 张（抽 2 选 1）。
        /// 必须在玩家建立之后、开局入场之前调用。
        /// </summary>
        public static void Prepare(GameState state, Random rng)
        {
            EnterpriseState enterprises = state.Enterprises;
            enterprises.Reset(state.Players.Count);

            var deck = new List<int>(EnterpriseCatalog.BoardCount);
            for (int id = 0; id < EnterpriseCatalog.BoardCount; id++) deck.Add(id);
            Shuffle(deck, rng);
            enterprises.Deck.AddRange(deck);

            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                for (int count = 0; count < 2 && enterprises.Deck.Count > 0; count++)
                {
                    enterprises.Offers[seat].Add(enterprises.Deck[0]);
                    enterprises.Deck.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// 2／3 人局补足场上企业板到 4 块：从牌堆顶依次翻开，直到场上有 4 块。
        /// 4 人局由每人各选 1 张自然凑够 4 块，这里不会多翻。
        /// </summary>
        public static void CompleteBoardSet(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;
            while (enterprises.ActiveBoards.Count < 4 && enterprises.Deck.Count > 0)
            {
                int boardId = enterprises.Deck[0];
                enterprises.Deck.RemoveAt(0);
                if (!enterprises.IsActive(boardId)) enterprises.Activate(boardId);
            }
        }

        /// <summary>
        /// 结算某一级带来的即时奖励（升级时调用）。
        /// 企业特效（<see cref="EnterpriseLevel.IsEffect"/>）只解锁，不在这里结算；
        /// 莱茵生命的 2–5 级奖励由当前科室板决定。
        /// </summary>
        public static void SettleLevel(GameState state, int boardId, int seat, int level, IEffectChoices choices)
        {
            EnterpriseBoardDef board = EnterpriseCatalog.Board(boardId);
            EnterpriseLevel entry = board?.Level(level);
            if (entry == null) return;

            if (entry.Special == EnterpriseSpecial.DepartmentReward)
            {
                SettleDepartment(state, seat, level, choices, out _);
                return;
            }

            if (entry.IsEffect) return;

            if (entry.Option != null)
                EffectResolver.ExecuteOption(state, seat, entry.Option, true, choices);

            if (entry.Special == EnterpriseSpecial.OpponentLoseScore)
                LetOneOpponentLoseScore(state, seat, 1);
        }

        /// <summary>结算莱茵生命当前科室板在指定合作等级上的奖励。</summary>
        private static void SettleDepartment(GameState state, int seat, int level, IEffectChoices choices,
            out bool settled)
        {
            settled = false;
            EnterpriseState enterprises = state.Enterprises;
            if (seat < 0 || seat >= enterprises.Departments.Count) return;

            int departmentId = enterprises.Departments[seat];
            if (departmentId < 0) return;

            RhineDepartmentDef department = EnterpriseCatalog.Department(departmentId);
            EnterpriseLevel entry = department?.Level(level);
            if (entry == null) return;

            if (entry.Option != null)
                EffectResolver.ExecuteOption(state, seat, entry.Option, true, choices);
            settled = true;

            // 结构科 5 级：降低 1 级合作等级，并结算新等级的奖励。
            if (entry.Special == EnterpriseSpecial.LowerLevelAndSettle)
            {
                int lowered = enterprises.Lower((int)EnterpriseId.RhineLab, seat);
                if (lowered >= 2) SettleDepartment(state, seat, lowered, choices, out _);
            }
        }

        /// <summary>
        /// 「1 位对手失去 1 分」：需要选对手。当前引擎先按**分数最高的对手**结算
        /// （并列时取座位号最小的），UI 选定对手的入口留到企业面板一起补。
        /// </summary>
        public static void LetOneOpponentLoseScore(GameState state, int seat, int amount)
        {
            int target = -1;
            int best = int.MinValue;
            for (int other = 0; other < state.Players.Count; other++)
            {
                if (other == seat) continue;
                int score = state.Player(other).Score;
                if (score > best)
                {
                    best = score;
                    target = other;
                }
            }

            if (target < 0) return;
            PlayerState victim = state.Player(target);
            victim.Score = Math.Max(0, victim.Score - amount);
        }

        /// <summary>
        /// 企业特效的条件段能不能完整支付（金券 / 资源 / 分数）。
        /// 现有奖励系统表达不了的代价类型一律视为可支付。
        /// </summary>
        public static bool CanPay(GameState state, int seat, IReadOnlyList<RewardOp> cost, out string reason)
        {
            reason = string.Empty;
            if (cost == null || cost.Count == 0) return true;

            PlayerState player = state.Player(seat);
            foreach (RewardOp op in cost)
            {
                switch (op.Kind)
                {
                    case RewardKind.PayVouchers:
                        if (player.Vouchers < op.Amount)
                        {
                            reason = $"金券不足：需要 {op.Amount}，你只有 {player.Vouchers}。";
                            return false;
                        }
                        break;

                    case RewardKind.PayResource:
                        if (player.Resources.Get(op.Resource) < op.Amount)
                        {
                            reason = $"{op.Resource} 不足：需要 {op.Amount}，你只有 {player.Resources.Get(op.Resource)}。";
                            return false;
                        }
                        break;

                    case RewardKind.LoseScore:
                        if (player.Score < op.Amount)
                        {
                            reason = $"分数不足：需要失去 {op.Amount} 分，你只有 {player.Score} 分。";
                            return false;
                        }
                        break;
                }
            }

            return true;
        }

        /// <summary>把「某座位可以提升 1 级」记到状态上，等玩家选企业板。</summary>
        public static void BeginPendingAdvance(GameState state, int seat, int excludeBoard, string reason)
        {
            EnterpriseState enterprises = state.Enterprises;
            enterprises.PendingAdvanceSeat = seat;
            enterprises.PendingAdvanceExcludeBoard = excludeBoard;
            enterprises.PendingAdvanceReason = reason ?? string.Empty;
        }

        public static void ClearPendingAdvance(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;
            enterprises.PendingAdvanceSeat = -1;
            enterprises.PendingAdvanceExcludeBoard = -1;
            enterprises.PendingAdvanceReason = string.Empty;
        }

        private static void Shuffle(List<int> list, Random rng)
        {
            if (rng == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
