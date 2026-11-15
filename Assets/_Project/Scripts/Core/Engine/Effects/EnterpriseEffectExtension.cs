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
                case RewardKind.OperatorSellToPlayer:
                case RewardKind.OperatorBalanceInfluence:
                case RewardKind.OperatorPayResourceBuild:
                case RewardKind.OperatorRelocateInsteadOfRemove:
                case RewardKind.OperatorSupplyToBox:
                case RewardKind.TriggerAllEnterpriseEffects:
                case RewardKind.OperatorReplaceTwoOpponents:
                case RewardKind.OperatorTakeOpponentResources:
                case RewardKind.BuildFromFacilityDeckAndShuffle:
                case RewardKind.TriggerEnterpriseEffectTwice:
                case RewardKind.ChooseTwoOfThreeOptions:
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
                case RewardKind.OperatorSellToPlayer:
                    return TrySellToPlayer(state, seat, op, choices);
                case RewardKind.OperatorBalanceInfluence:
                    return TryBalanceInfluence(state, seat, op, choices);
                case RewardKind.OperatorPayResourceBuild:
                    return TryPayResourceBuild(state, seat, op, choices);
                case RewardKind.OperatorRelocateInsteadOfRemove:
                    state.Player(seat).FreeMoveRelocatesInfluenceThisTurn = true;
                    return true;
                case RewardKind.OperatorSupplyToBox:
                    return TrySupplyToBox(state, seat, op, choices);
                case RewardKind.TriggerAllEnterpriseEffects:
                    return TryTriggerAllEffects(state, seat, op, choices);
                case RewardKind.OperatorReplaceTwoOpponents:
                    return TryReplaceTwoOpponents(state, seat, op, choices);
                case RewardKind.OperatorTakeOpponentResources:
                    return TryTakeOpponentResources(state, seat, op, choices);
                case RewardKind.BuildFromFacilityDeckAndShuffle:
                    EnterpriseEffectResolver.BuildFromDeckAndShuffle(state, seat, choices, null);
                    return true;
                case RewardKind.TriggerEnterpriseEffectTwice:
                    return TryTriggerEffectTwice(state, seat, op, choices);
                case RewardKind.ChooseTwoOfThreeOptions:
                    return TryChooseTwoOfThree(state, seat, op, choices);
                default:
                    return false;
            }
        }

        // —— 干员牌效果（Doc/EnterpriseExpansion.md §9.6）——

        /// <summary>银灰·策略「商业手腕」：选 1 种资源 + 1 位玩家，该玩家按 4 金券 / 个卖光。</summary>
        private static bool TrySellToPlayer(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            IReadOnlyList<ResourceType> options = op.SpreadOptions ?? DefaultResourceOptions;
            int index = choices.ChooseResourceType(seat, op, state, options);
            if (index < 0 || index >= options.Count) return false;

            var candidates = new List<int>();
            for (int i = 0; i < state.Players.Count; i++) candidates.Add(i);

            int target = choices.ChoosePlayer(seat, op, state, candidates);
            if (target < 0 || target >= state.Players.Count) return false;

            PlayerState owner = state.Player(target);
            ResourceType type = options[index];
            int count = owner.Resources.Get(type);
            if (count <= 0) return false;

            owner.Resources.TrySpend(type, count);
            owner.Vouchers += count * Math.Max(1, op.Amount);
            return true;
        }

        /// <summary>银灰·计谋「制衡战略」：所有玩家（含自己）各移除 1 个自己的影响力。</summary>
        private static bool TryBalanceInfluence(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            int removed = 0;
            int count = state.Players.Count;
            for (int offset = 0; offset < count; offset++)
            {
                int owner = (seat + offset) % count;
                List<InfluenceSlotRef> slots = OccupiedSlotsOf(state, owner);
                if (slots.Count == 0) continue;

                InfluenceSlotRef? pick = choices.ChooseInfluenceToRemove(seat, op, state, slots);
                InfluenceSlotRef slot = pick != null && slots.Contains(pick.Value) ? pick.Value : slots[0];

                state.Map.SlotsOf(slot).Clear(slot.SlotIndex);
                state.Player(owner).MarkersInSupply++;
                removed++;
            }
            return removed > 0;
        }

        /// <summary>泥岩·策略「源岩D.I.Y」：支付 1 源岩后建造 1 次；付不起则整条效果不执行。</summary>
        private static bool TryPayResourceBuild(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            PlayerState player = state.Player(seat);
            if (!player.Resources.TrySpend(op.Resource, Math.Max(1, op.Amount)))
                return false;

            EffectReport report = EffectResolver.Execute(state, seat,
                new[] { RewardOp.BuildOnce() }, choices, Instance);
            return report.AnyApplied;
        }

        /// <summary>森蚺·计谋「暴躁铁皮」：从供应区选至多 2 张名称不同的设施牌放回游戏盒。</summary>
        private static bool TrySupplyToBox(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            var usedNames = new HashSet<string>();
            int removed = 0;
            int max = Math.Max(1, op.Amount);

            for (int i = 0; i < max; i++)
            {
                var candidates = new List<int>();
                foreach (int id in state.FacilitySupply)
                {
                    string name = state.Catalog.Facility(id).Name;
                    if (!usedNames.Contains(name)) candidates.Add(id);
                }
                if (candidates.Count == 0) break;

                int pick = choices.ChooseSupplyFacility(seat, op, state, candidates);
                if (pick < 0 || !candidates.Contains(pick)) break;

                usedNames.Add(state.Catalog.Facility(pick).Name);
                state.FacilitySupply.Remove(pick);
                BoardSetup.RefillFacilitySupply(state);
                removed++;
            }
            return removed > 0;
        }

        /// <summary>凯尔希·计谋「掌控全局」：选 1 家企业，触发其每项已解锁且本回合未用过的特效。</summary>
        private static bool TryTriggerAllEffects(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            var candidates = new List<int>();
            foreach (int boardId in state.Enterprises.ActiveBoards)
            {
                if (TriggerableLevels(state, seat, boardId).Count > 0) candidates.Add(boardId);
            }
            if (candidates.Count == 0) return false;

            int chosen = choices.ChooseEnterpriseBoard(seat, op, state, candidates);
            if (chosen < 0 || !candidates.Contains(chosen)) return false;

            // 逐级重新校验：低等级特效可能先给出资源，让高等级的费用变得付得起
            // （例如海德兄弟 1 级得 4 源岩，2 级再支付 2 源岩），因此不能在循环前一次性定死可触发列表。
            bool any = false;
            EnterpriseBoardDef board = EnterpriseCatalog.Board(chosen);
            int current = state.Enterprises.LevelOf(chosen, seat);
            for (int level = 1; level <= Math.Min(current, 5); level++)
            {
                EnterpriseLevel entry = board?.Level(level);
                if (entry == null || !entry.IsEffect) continue;
                if (state.Enterprises.HasUsedEffectThisTurn(chosen, seat, level)) continue;

                var command = new TriggerEnterpriseEffectCommand(seat, chosen, level, choices, ignoreTiming: true);
                if (!command.Validate(state).Ok) continue;
                command.Apply(state);
                any = true;
            }
            return any;
        }

        /// <summary>W·计谋「致命攻势」：选 2 名不同对手，各用你的 1 个影响力替换其 1 个。</summary>
        private static bool TryReplaceTwoOpponents(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            PlayerState player = state.Player(seat);
            var chosenOpponents = new List<int>();
            int max = Math.Max(1, op.Amount);

            for (int i = 0; i < max; i++)
            {
                var candidates = new List<int>();
                for (int other = 0; other < state.Players.Count; other++)
                {
                    if (other == seat || chosenOpponents.Contains(other)) continue;
                    if (OccupiedSlotsOf(state, other).Count > 0) candidates.Add(other);
                }
                if (candidates.Count == 0) break;

                int target = choices.ChooseOpponent(seat, op, state, candidates);
                if (target < 0 || !candidates.Contains(target)) break;

                List<InfluenceSlotRef> slots = OccupiedSlotsOf(state, target);
                InfluenceSlotRef? pick = choices.ChooseInfluenceToRemove(seat, op, state, slots);
                InfluenceSlotRef slot = pick != null && slots.Contains(pick.Value) ? pick.Value : slots[0];

                state.Map.SlotsOf(slot).Clear(slot.SlotIndex);
                state.Player(target).MarkersInSupply++;
                chosenOpponents.Add(target);

                if (player.MarkersInSupply > 0 && ActionRules.CanPlaceInfluence(state, seat, slot).Ok)
                {
                    player.MarkersInSupply--;
                    state.Map.SlotsOf(slot).Place(slot.SlotIndex, seat);
                }
            }
            return chosenOpponents.Count > 0;
        }

        /// <summary>异客·策略「沁礁黑市」：选 1 种资源 + 1 位对手，取其至多 4 个；双方各得 1 分。</summary>
        private static bool TryTakeOpponentResources(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            IReadOnlyList<ResourceType> options = op.SpreadOptions ?? DefaultResourceOptions;
            int index = choices.ChooseResourceType(seat, op, state, options);
            if (index < 0 || index >= options.Count) return false;

            ResourceType type = options[index];
            var candidates = new List<int>();
            for (int other = 0; other < state.Players.Count; other++)
            {
                if (other == seat) continue;
                if (state.Player(other).Resources.Get(type) > 0) candidates.Add(other);
            }
            if (candidates.Count == 0) return false;

            int target = choices.ChooseOpponent(seat, op, state, candidates);
            if (target < 0 || !candidates.Contains(target)) return false;

            PlayerState from = state.Player(target);
            int take = Math.Min(Math.Max(1, op.Amount), from.Resources.Get(type));
            from.Resources.TrySpend(type, take);
            state.Player(seat).Resources.Add(type, take);

            state.Player(seat).Score += 1;
            from.Score += 1;
            return true;
        }

        private static readonly ResourceType[] DefaultResourceOptions =
            { ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron };

        /// <summary>场上属于指定玩家的所有非空影响力格。</summary>
        private static List<InfluenceSlotRef> OccupiedSlotsOf(GameState state, int owner)
        {
            var list = new List<InfluenceSlotRef>();
            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                    if (node.Slots.OwnerOf(i) == owner) list.Add(InfluenceSlotRef.OnNode(node.Id, i));
            }
            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                    if (route.Slots.OwnerOf(i) == owner) list.Add(InfluenceSlotRef.OnRoute(route.Id, i));
            }
            return list;
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

            // 影响力被移除的玩家各得 1 分，发动者额外再得 1 分。
            foreach (int affectedSeat in affected)
                state.Player(affectedSeat).Score += op.Amount;
            state.Player(seat).Score += op.Amount;

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
                if (!entry.UnlimitedPerTurn && state.Enterprises.HasUsedEffectThisTurn(boardId, seat, level)) continue;
                if (!new TriggerEnterpriseEffectCommand(seat, boardId, level, null, ignoreTiming: true)
                        .Validate(state).Ok) continue;
                result.Add(level);
            }
            return result;
        }

        // —— 企业扩展城市样式牌的特殊行动（Doc/EnterpriseExpansion.md §2）——

        /// <summary>大型企业园ᴱ：选 1 家企业，触发其 <c>Amount</c> 次已解锁特效。</summary>
        private static bool TryTriggerEffectTwice(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            var boardCandidates = new List<int>();
            foreach (int boardId in state.Enterprises.ActiveBoards)
            {
                if (TriggerableLevels(state, seat, boardId).Count > 0) boardCandidates.Add(boardId);
            }
            if (boardCandidates.Count == 0) return false;

            int chosenBoard = choices.ChooseEnterpriseBoard(seat, op, state, boardCandidates);
            if (chosenBoard < 0 || !boardCandidates.Contains(chosenBoard)) return false;

            bool any = false;
            for (int i = 0; i < op.Amount; i++)
            {
                if (!TriggerOnBoard(state, seat, chosenBoard, op, choices)) break;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// 警备队辖区ᴱ：从 3 个选项里选 2 个并依次执行。
        /// 选项内容由卡面固定（①支付 2 源岩 + 1 异铁替换 1 个影响力 ②放置 1 个影响力得 1 源石
        /// ③移除 1 个影响力得 2 异铁），<see cref="RewardOp.Branches"/> 只用来给界面提供选项文字。
        /// </summary>
        private static bool TryChooseTwoOfThree(GameState state, int seat, RewardOp op, IEffectChoices choices)
        {
            if (op.Branches == null || op.Branches.Count < 2) return false;

            var available = new List<int>();
            for (int i = 0; i < op.Branches.Count; i++)
            {
                if (CanExecutePoliceOption(state, seat, i)) available.Add(i);
            }
            if (available.Count < 2) return false;

            var remaining = new List<int>(available);
            var selected = new List<int>(2);
            for (int pick = 0; pick < 2; pick++)
            {
                var branchList = new List<EffectBranch>();
                foreach (int index in remaining) branchList.Add(op.Branches[index]);

                int relative = choices.ChooseBranch(seat, op, state, branchList);
                if (relative < 0 || relative >= remaining.Count) return false;

                selected.Add(remaining[relative]);
                remaining.RemoveAt(relative);
            }

            bool any = false;
            foreach (int option in selected)
            {
                if (ExecutePoliceOption(state, seat, option, choices)) any = true;
            }
            return any;
        }

        /// <summary>某警备队选项现在是否可执行（费用 / 影响力 / 空格）。</summary>
        private static bool CanExecutePoliceOption(GameState state, int seat, int option)
        {
            PlayerState player = state.Player(seat);

            switch (option)
            {
                case 0:
                {
                    if (!player.Resources.Has(ResourceType.Orirock, 2) ||
                        !player.Resources.Has(ResourceType.Oriron, 1))
                        return false;
                    return player.MarkersInSupply > 0 && AllOccupiedSlots(state).Count > 0;
                }
                case 1:
                    return player.MarkersInSupply > 0 && AllPlaceableSlots(state, seat).Count > 0;
                case 2:
                    return AllOccupiedSlots(state).Count > 0;
                default:
                    return false;
            }
        }

        /// <summary>执行一个警备队选项；返回是否真的产生了效果。</summary>
        private static bool ExecutePoliceOption(GameState state, int seat, int option, IEffectChoices choices)
        {
            PlayerState player = state.Player(seat);

            switch (option)
            {
                case 0:
                {
                    // 支付 2 源岩 + 1 异铁，替换 1 个影响力：移除任意玩家的 1 个影响力，
                    // 能在同一格放置时再放自己的（对手城市停靠的资源点只能移除，速查表「替换影响力」）。
                    if (!player.Resources.Has(ResourceType.Orirock, 2) ||
                        !player.Resources.Has(ResourceType.Oriron, 1)) return false;
                    player.Resources.TrySpend(ResourceType.Orirock, 2);
                    player.Resources.TrySpend(ResourceType.Oriron, 1);

                    List<InfluenceSlotRef> targets = AllOccupiedSlots(state);
                    if (targets.Count == 0) return false;

                    InfluenceSlotRef? pick = choices.ChooseInfluenceToRemove(seat, default, state, targets);
                    if (pick == null || !targets.Contains(pick.Value)) return false;

                    int owner = state.Map.OccupantOf(pick.Value);
                    state.Map.SlotsOf(pick.Value).Clear(pick.Value.SlotIndex);
                    if (owner >= 0 && owner < state.Players.Count &&
                        !PersistentTacticRules.TryCaptureRemovedInfluence(state, owner, choices))
                        state.Player(owner).MarkersInSupply++;

                    if (player.MarkersInSupply > 0 &&
                        ActionRules.CanPlaceInfluence(state, seat, pick.Value).Ok)
                    {
                        player.MarkersInSupply--;
                        state.Map.SlotsOf(pick.Value).Place(pick.Value.SlotIndex, seat);
                    }
                    return true;
                }
                case 1:
                {
                    // 放置 1 个影响力，获得 1 源石。
                    if (!TryPlaceAnyInfluence(state, seat, choices)) return false;
                    player.Resources.Add(ResourceType.Originium, 1);
                    return true;
                }
                case 2:
                {
                    // 移除 1 个影响力（默认可以是对手的），获得 2 异铁。
                    if (!TryRemoveAnyInfluence(state, seat, choices)) return false;
                    player.Resources.Add(ResourceType.Oriron, 2);
                    return true;
                }
                default:
                    return false;
            }
        }

        private static bool TryRemoveAnyInfluence(GameState state, int seat, IEffectChoices choices)
        {
            List<InfluenceSlotRef> candidates = AllOccupiedSlots(state);
            if (candidates.Count == 0) return false;

            InfluenceSlotRef? pick = choices.ChooseInfluenceToRemove(seat, default, state, candidates);
            if (pick == null || !candidates.Contains(pick.Value)) return false;

            int owner = state.Map.OccupantOf(pick.Value);
            state.Map.SlotsOf(pick.Value).Clear(pick.Value.SlotIndex);
            if (owner >= 0 && owner < state.Players.Count &&
                !PersistentTacticRules.TryCaptureRemovedInfluence(state, owner, choices))
                state.Player(owner).MarkersInSupply++;
            return true;
        }

        private static bool TryPlaceAnyInfluence(GameState state, int seat, IEffectChoices choices)
        {
            PlayerState player = state.Player(seat);
            if (player.MarkersInSupply <= 0) return false;

            List<InfluenceSlotRef> candidates = AllPlaceableSlots(state, seat);
            if (candidates.Count == 0) return false;

            InfluenceSlotRef? pick = choices.ChooseInfluenceSlot(seat, default, state, candidates);
            if (pick == null || !candidates.Contains(pick.Value)) return false;

            player.MarkersInSupply--;
            state.Map.SlotsOf(pick.Value).Place(pick.Value.SlotIndex, seat);
            return true;
        }

        /// <summary>场上所有非空影响力格（任何人的）。</summary>
        private static List<InfluenceSlotRef> AllOccupiedSlots(GameState state)
        {
            var list = new List<InfluenceSlotRef>();
            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                    if (!node.Slots.IsEmpty(i)) list.Add(InfluenceSlotRef.OnNode(node.Id, i));
            }
            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                    if (!route.Slots.IsEmpty(i)) list.Add(InfluenceSlotRef.OnRoute(route.Id, i));
            }
            return list;
        }

        /// <summary>该玩家按 §6.4a 可以放置影响力的所有空格。</summary>
        private static List<InfluenceSlotRef> AllPlaceableSlots(GameState state, int seat)
        {
            var list = new List<InfluenceSlotRef>();
            foreach (MapNodeState node in state.Map.Nodes)
            {
                for (int i = 0; i < node.Slots.Capacity; i++)
                {
                    InfluenceSlotRef slot = InfluenceSlotRef.OnNode(node.Id, i);
                    if (ActionRules.CanPlaceInfluence(state, seat, slot).Ok) list.Add(slot);
                }
            }
            foreach (RouteState route in state.Map.Routes)
            {
                for (int i = 0; i < route.Slots.Capacity; i++)
                {
                    InfluenceSlotRef slot = InfluenceSlotRef.OnRoute(route.Id, i);
                    if (ActionRules.CanPlaceInfluence(state, seat, slot).Ok) list.Add(slot);
                }
            }
            return list;
        }
    }
}
