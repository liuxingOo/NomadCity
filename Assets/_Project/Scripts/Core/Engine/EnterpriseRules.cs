using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>企业扩展的规则辅助：开局发牌、升级结算、费用校验。</summary>
    public static class EnterpriseRules
    {
        /// <summary>本局是否启用了企业扩展。</summary>
        public static bool Enabled(GameState state) =>
            state?.Setup != null && state.Setup.IsEnterpriseEnabled;

        /// <summary>
        /// 开局：洗混 8 张企业板，按座位依次发牌。
        /// 3／4 人局各发 2 张（抽 2 选 1）；2 人局各发 4 张（抽 4 选 2，§12 变体 4）。
        /// 必须在玩家建立之后、开局入场之前调用。
        /// </summary>
        public static void Prepare(GameState state, Random rng)
        {
            EnterpriseState enterprises = state.Enterprises;
            int picksPerPlayer = state.Players.Count == 2 ? 2 : 1;
            enterprises.Reset(state.Players.Count, picksPerPlayer);

            var deck = new List<int>(EnterpriseCatalog.BoardCount);
            for (int id = 0; id < EnterpriseCatalog.BoardCount; id++) deck.Add(id);
            Shuffle(deck, rng);
            enterprises.Deck.AddRange(deck);

            // 干员牌堆：8 张干员角色牌（罗德岛【干员特派】用），开局一起洗好。
            Shuffle(enterprises.OperatorDeck, rng);

            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                for (int count = 0; count < enterprises.OffersPerSeat && enterprises.Deck.Count > 0; count++)
                {
                    enterprises.Offers[seat].Add(enterprises.Deck[0]);
                    enterprises.Deck.RemoveAt(0);
                }
            }

            // 莱茵生命的科室板：开局用同一个随机源抽定 1 张（同一颗种子可复现），
            // 之后无论是它被选为初始企业，还是作为第 4 块板补进场上，都用这张。
            enterprises.RhineDepartment = rng != null
                ? rng.Next(EnterpriseCatalog.DepartmentCount)
                : 0;
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
                if (!enterprises.IsActive(boardId))
                {
                    enterprises.Activate(boardId);
                    PlaceSetupMarkers(state, boardId);
                }
            }
        }

        /// <summary>
        /// 开局企业抽选还没完成时给玩家看的说明：按人数算出正确的「抽 N 选 M」
        /// （3／4 人局抽 2 选 1，2 人局抽 4 选 2），并点名还差谁没选完。
        /// </summary>
        public static string DescribeDraftPending(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;
            var pending = new List<string>();
            for (int seat = 0; seat < enterprises.ChosenBoards.Count; seat++)
            {
                if (enterprises.SeatDraftDone(seat)) continue;
                int left = enterprises.PicksPerPlayer - enterprises.ChosenCount(seat);
                if (left < 1) left = 1;
                pending.Add($"玩家 {seat + 1}（还差 {left} 张）");
            }

            string who = pending.Count > 0 ? string.Join("、", pending) : "其他玩家";
            return $"请先完成合作企业的「抽 {enterprises.OffersPerSeat} 选 {enterprises.PicksPerPlayer}」："
                   + $"{who} 还没选完，之后才能决定初始资源点。";
        }

        /// <summary>
        /// 开局抽选的「半完成」自愈：所有座位都已经选满，但 <see cref="EnterpriseState.DraftDone"/>
        /// 仍为 false（历史原因是最后一次选择没能走完收尾这一步）。
        /// 这种状态下谁都不能再选（都选满了），也选不了初始资源点，整局就卡死在开局。
        /// 返回是否补完了状态。
        /// </summary>
        public static bool RepairDraftCompletionIfNeeded(GameState state)
        {
            if (!Enabled(state)) return false;

            EnterpriseState enterprises = state.Enterprises;
            if (enterprises.DraftDone || enterprises.ChosenBoards.Count == 0) return false;

            for (int seat = 0; seat < enterprises.ChosenBoards.Count; seat++)
                if (!enterprises.SeatDraftDone(seat)) return false;

            // 选完的座位把这几张没被选中的牌还回牌堆（正常流程在最后一次选择里做，这里补齐）。
            for (int seat = 0; seat < enterprises.Offers.Count; seat++)
            {
                foreach (int offered in enterprises.Offers[seat]) enterprises.Deck.Add(offered);
                enterprises.Offers[seat].Clear();
            }

            enterprises.DraftDone = true;
            CompleteBoardSet(state);
            EnterpriseEffectResolver.PickInitialRhineDepartmentIfNeeded(state);
            return true;
        }

        /// <summary>
        /// 企业板上场时，每名玩家在这块板的 0 级位置各放 1 个玩家标记。
        /// 这些标记来自每人 30 个的供应堆（<see cref="PlayerState.MarkersInSupply"/>），放上去就要扣掉。
        /// </summary>
        public static void PlaceSetupMarkers(GameState state, int boardId)
        {
            if (state == null || !state.Enterprises.IsActive(boardId)) return;

            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                PlayerState player = state.Player(seat);
                if (player.MarkersInSupply > 0) player.MarkersInSupply--;
            }
        }

        /// <summary>
        /// 结算某一级带来的即时奖励（升级时调用）。
        /// 企业特效（<see cref="EnterpriseLevel.IsEffect"/>）只解锁，不在这里结算；
        /// 莱茵生命的 2–5 级奖励由当前科室板决定；其余升级奖励交给企业特效解析器跑一遍，
        /// 这样「干员特派」这类带特殊结算的即时奖励也能正常生效。
        /// 返回这一级的效果明细（升级命令带点选时，界面状态行用它显示做了什么）。
        /// </summary>
        public static EffectReport SettleLevel(GameState state, int boardId, int seat, int level,
            IEffectChoices choices)
        {
            EnterpriseBoardDef board = EnterpriseCatalog.Board(boardId);
            EnterpriseLevel entry = board?.Level(level);
            if (entry == null) return null;

            if (entry.Special == EnterpriseSpecial.DepartmentReward)
                return EnterpriseEffectResolver.SettleDepartment(state, seat, level, choices);

            if (entry.IsEffect) return null;

            return EnterpriseEffectResolver.Resolve(state, seat, boardId, level, choices);
        }

        /// <summary>
        /// 「1 位对手失去 1 分」：由玩家选对手（<see cref="IEffectChoices.ChooseOpponent"/>）；
        /// 放弃选择时退回「分数最高的对手」（并列取座位号最小的）。
        /// </summary>
        public static void LetOneOpponentLoseScore(GameState state, int seat, int amount,
            IEffectChoices choices = null)
        {
            var candidates = new List<int>(state.Players.Count);
            for (int other = 0; other < state.Players.Count; other++)
                if (other != seat) candidates.Add(other);

            if (candidates.Count == 0) return;

            int target = choices != null
                ? choices.ChooseOpponent(seat, default, state, candidates)
                : -1;

            if (target < 0 || !candidates.Contains(target))
            {
                int best = int.MinValue;
                foreach (int other in candidates)
                {
                    int score = state.Player(other).Score;
                    if (score > best)
                    {
                        best = score;
                        target = other;
                    }
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

        // —— 红色区域开放（EnterpriseExpansion.md §3.2）——

        /// <summary>红区提升的提示文案。</summary>
        public const string RedZoneAdvanceReason = "红色区域已开放：选择 1 家企业，把自己的合作等级提升 1 级。";

        /// <summary>
        /// 进入角色牌盖放阶段时调用：本回合是「红色区域开放」回合的话，从起始玩家顺时针
        /// 依次让每名玩家选 1 家企业把自己提升 1 级（可以选自己所在的企业）。
        /// 已开放过 / 场上没有企业板 / 没启用扩展时什么都不做。
        /// </summary>
        public static void BeginRedZoneAdvance(GameState state)
        {
            if (state == null || state.Enterprises == null || !Enabled(state)) return;
            if (state.TurnNumber != PlayerCountVariant.For(state.Players.Count).RedZoneOpenTurn) return;

            EnterpriseState enterprises = state.Enterprises;
            if (enterprises.RedZoneAdvanceStarted) return;
            if (enterprises.ActiveBoards.Count == 0) return;

            enterprises.RedZoneAdvanceStarted = true;
            enterprises.RedZoneAdvanceDoneMask = 0;
            enterprises.RedZoneAdvanceSeat = -1;
            PickNextRedZoneSeat(state);
        }

        /// <summary>
        /// 红区提升完成一位之后调用：如果没有被企业特效（如罗德岛 1 级）重新挂起，
        /// 就顺时针找下一位还能提升的玩家；全部处理完则结束这一轮。
        /// </summary>
        public static void ContinueRedZoneAdvance(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;
            if (enterprises == null || !enterprises.RedZoneAdvanceStarted) return;
            if (enterprises.RedZoneAdvanceSeat >= 0) return;      // 还在等当前这位
            if (enterprises.PendingAdvanceSeat >= 0) return;      // 企业特效又挂起了一次提升，先让它选完
            PickNextRedZoneSeat(state);
        }

        private static void PickNextRedZoneSeat(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;
            int count = state.Players.Count;
            if (count <= 0)
            {
                enterprises.RedZoneAdvanceStarted = false;
                return;
            }

            for (int step = 0; step < count; step++)
            {
                int seat = (state.FirstPlayerSeat + step) % count;
                if ((enterprises.RedZoneAdvanceDoneMask & (1 << seat)) != 0) continue;

                if (!HasAdvanceCandidate(state, seat))
                {
                    // 满级了：跳过，但记下来，免得每次扫描都停在这里。
                    enterprises.RedZoneAdvanceDoneMask |= 1 << seat;
                    continue;
                }

                enterprises.RedZoneAdvanceSeat = seat;
                BeginPendingAdvance(state, seat, -1, RedZoneAdvanceReason);
                return;
            }

            // 所有人都处理完（或都没有可提升的板）：这一轮结束，盖放继续。
            enterprises.RedZoneAdvanceStarted = false;
            enterprises.RedZoneAdvanceSeat = -1;
        }

        /// <summary>这名玩家在场上至少还有一块没满级的板（都在板上，等级 0–5）。</summary>
        private static bool HasAdvanceCandidate(GameState state, int seat)
        {
            EnterpriseState enterprises = state.Enterprises;
            foreach (int boardId in enterprises.ActiveBoards)
            {
                int level = enterprises.LevelOf(boardId, seat);
                if (level >= 0 && level < 5) return true;
            }
            return false;
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
