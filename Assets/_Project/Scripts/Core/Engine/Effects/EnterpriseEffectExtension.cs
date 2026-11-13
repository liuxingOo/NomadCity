using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 企业扩展专属效果的执行器（「选择 1 家企业」类效果）。
    ///
    /// 基础执行器不认识这些效果，统一转交给这里，保证企业扩展内容不混进基础流程
    /// （RuleSpec §13 / EnterpriseExpansion.md §6）。未启用企业扩展时一律不受理。
    /// </summary>
    public sealed class EnterpriseEffectExtension : IEffectExtension
    {
        /// <summary>无状态，直接用单例。</summary>
        public static readonly EnterpriseEffectExtension Instance = new EnterpriseEffectExtension();

        public bool Handles(RewardOp op, GameState state)
        {
            if (!EnterpriseRules.Enabled(state)) return false;

            switch (op.Kind)
            {
                case RewardKind.ChooseEnterprise:
                case RewardKind.AdvanceEnterpriseAllPlayers:
                case RewardKind.AdvanceOwnEnterpriseLevel:
                case RewardKind.TriggerEnterpriseEffectOnce:
                case RewardKind.AdvanceAndTriggerEnterprise:
                case RewardKind.PlaceHighway:
                    return true;
                default:
                    return false;
            }
        }

        public bool Execute(RewardOp op, int seat, GameState state, IEffectChoices choices)
        {
            if (seat < 0 || seat >= state.Players.Count) return false;
            choices ??= FirstAvailableChoices.Default;

            switch (op.Kind)
            {
                case RewardKind.PlaceHighway:
                    return TryPlaceHighway(state, seat, op, choices);
                case RewardKind.AdvanceOwnEnterpriseLevel:
                    return TryAdvanceOwnLevel(state, seat, op, choices);
                case RewardKind.AdvanceEnterpriseAllPlayers:
                    return TryAdvanceAllPlayers(state, seat, op, choices);
                case RewardKind.ChooseEnterprise:
                case RewardKind.TriggerEnterpriseEffectOnce:
                    return TryTriggerEffect(state, seat, op, choices);
                case RewardKind.AdvanceAndTriggerEnterprise:
                    return TryAdvanceAndTrigger(state, seat, op, choices);
                default:
                    return false;
            }
        }

        // —— 山·策略「开辟商路」——

        private static bool TryPlaceHighway(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            var candidates = new List<int>();
            foreach (RouteState route in state.Map.Routes)
            {
                if (!route.IsHighway) candidates.Add(route.Id);
            }
            if (candidates.Count == 0) return false;

            int routeId = choices.ChooseRoute(seat, op, state, candidates);
            if (routeId < 0 || !candidates.Contains(routeId)) return false;

            RouteState target = state.Map.Route(routeId);
            var affected = new List<int>();

            InfluenceSlots slots = target.Slots;
            for (int i = 0; i < slots.Capacity; i++)
            {
                int owner = slots.OwnerOf(i);
                if (owner < 0) continue;

                slots.Clear(i);
                state.Player(owner).MarkersInSupply++;
                if (!affected.Contains(owner)) affected.Add(owner);
            }

            target.IsHighway = true;

            // 受影响玩家各得 1 金券，发动者额外再得 1 金券。
            foreach (int affectedSeat in affected)
                state.Player(affectedSeat).Vouchers += op.Amount;
            state.Player(seat).Vouchers += op.Amount;

            return true;
        }

        // —— 玛恩纳·计谋「游说赞助」——

        private static bool TryAdvanceOwnLevel(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            List<int> candidates = BoardsWithRoom(state, seat);
            if (candidates.Count == 0) return false;

            int boardId = choices.ChooseEnterpriseBoard(seat, op, state, candidates);
            if (boardId < 0 || !candidates.Contains(boardId)) return false;

            EnterpriseState enterprises = state.Enterprises;
            for (int i = 0; i < op.Amount; i++)
            {
                if (enterprises.LevelOf(boardId, seat) >= 5) break;
                int level = enterprises.Advance(boardId, seat);
                EnterpriseRules.SettleLevel(state, boardId, seat, level, choices);
            }
            return true;
        }

        // —— 玛恩纳·策略「公事公办」分支 A：所有玩家提升 1 级 ——

        private static bool TryAdvanceAllPlayers(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            List<int> candidates = BoardsWithRoom(state, seat);
            if (candidates.Count == 0) return false;

            int boardId = choices.ChooseEnterpriseBoard(seat, op, state, candidates);
            if (boardId < 0 || !candidates.Contains(boardId)) return false;

            // 发动者先结算，其余玩家顺时针。
            EnterpriseState enterprises = state.Enterprises;
            int count = state.Players.Count;
            for (int i = 0; i < count; i++)
            {
                int targetSeat = (seat + i) % count;
                int current = enterprises.LevelOf(boardId, targetSeat);
                if (current < 0 || current >= 5) continue;

                int level = enterprises.Advance(boardId, targetSeat);
                EnterpriseRules.SettleLevel(state, boardId, targetSeat, level, choices);
            }

            return true;
        }

        // —— 「触发 1 个已解锁的企业特效」——

        private static bool TryTriggerEffect(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            var boardCandidates = new List<int>();
            foreach (int boardId in state.Enterprises.ActiveBoards)
            {
                if (TriggerableLevels(state, seat, boardId).Count > 0) boardCandidates.Add(boardId);
            }
            if (boardCandidates.Count == 0) return false;

            int chosenBoard = choices.ChooseEnterpriseBoard(seat, op, state, boardCandidates);
            if (chosenBoard < 0 || !boardCandidates.Contains(chosenBoard)) return false;

            return TriggerOnBoard(state, seat, chosenBoard, op, choices);
        }

        // —— 山·计谋「商业网络」：先提升 1 级，再触发该企业 1 个已解锁特效 ——

        private static bool TryAdvanceAndTrigger(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            // 只列「提升之后至少有一个可触发特效」的企业板。
            // 注意：判定要按**提升后**的等级来算（这里先临时写进去再还原），
            // 否则 0 级时「提升前没有已解锁特效」，整张牌会被误判为无法发动。
            EnterpriseState enterprises = state.Enterprises;
            var candidates = new List<int>();
            foreach (int candidateId in BoardsWithRoom(state, seat))
            {
                int current = enterprises.LevelOf(candidateId, seat);
                enterprises.SetLevel(candidateId, seat, current + 1);
                bool triggerable = TriggerableLevels(state, seat, candidateId).Count > 0;
                enterprises.SetLevel(candidateId, seat, current);
                if (triggerable) candidates.Add(candidateId);
            }
            if (candidates.Count == 0) return false;

            int boardId = choices.ChooseEnterpriseBoard(seat, op, state, candidates);
            if (boardId < 0 || !candidates.Contains(boardId)) return false;

            int level = enterprises.Advance(boardId, seat);
            EnterpriseRules.SettleLevel(state, boardId, seat, level, choices);

            return TriggerOnBoard(state, seat, boardId, op, choices);
        }

        /// <summary>在指定企业板上让玩家选 1 个可触发的特效等级并触发。</summary>
        private static bool TriggerOnBoard(GameState state, int seat, int boardId, RewardOp op,
            IEffectChoices choices)
        {
            List<int> levels = TriggerableLevels(state, seat, boardId);
            if (levels.Count == 0) return false;

            int level = choices.ChooseEnterpriseEffectLevel(seat, op, state, boardId, levels);
            if (level < 0 || !levels.Contains(level)) return false;

            var command = new TriggerEnterpriseEffectCommand(seat, boardId, level, choices, ignoreTiming: true);
            if (!command.Validate(state).Ok) return false;
            command.Apply(state);
            return true;
        }

        /// <summary>该玩家在这块板上还有提升空间（0–4 级）的企业板。</summary>
        private static List<int> BoardsWithRoom(GameState state, int seat)
        {
            var result = new List<int>();
            foreach (int boardId in state.Enterprises.ActiveBoards)
            {
                int level = state.Enterprises.LevelOf(boardId, seat);
                if (level >= 0 && level < 5) result.Add(boardId);
            }
            return result;
        }

        /// <summary>
        /// 该玩家在这块板上本回合还能触发的特效等级（已解锁、未用过、且引擎能执行）。
        /// </summary>
        private static List<int> TriggerableLevels(GameState state, int seat, int boardId)
        {
            var result = new List<int>();
            EnterpriseBoardDef board = EnterpriseCatalog.Board(boardId);
            if (board == null) return result;

            int current = state.Enterprises.LevelOf(boardId, seat);
            if (current < 0) return result;

            int limit = Math.Min(current, 5);
            for (int level = 1; level <= limit; level++)
            {
                EnterpriseLevel entry = board.Level(level);
                if (entry == null || !entry.IsEffect) continue;
                if (state.Enterprises.HasUsedEffectThisTurn(boardId, seat, level)) continue;
                if (!new TriggerEnterpriseEffectCommand(seat, boardId, level, null, ignoreTiming: true)
                        .Validate(state).Ok) continue;
                result.Add(level);
            }
            return result;
        }
    }
}
