using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 企业特效的实际结算：命令的 <c>Apply</c> 与界面的「先收集点选」会话共用这一段。
    ///
    /// 为什么必须共用：界面会在状态副本上反复试跑这一级效果，跑到需要玩家选择处就停下提问；
    /// 如果试跑只走 <see cref="EffectResolver.ExecuteOption"/>，而「只在命令层处理」的特效
    /// （例如罗德岛 4 级从弃牌区选牌）根本不会试跑，界面的选牌窗就永远不会出现。
    /// </summary>
    public static class EnterpriseEffectResolver
    {
        /// <summary>结算某一级企业特效；<paramref name="choices"/> 为空时按「第一个合法项」自动应答。</summary>
        public static EffectReport Resolve(GameState state, int seat, int boardId, int level,
            IEffectChoices choices)
        {
            if (state == null || !EnterpriseRules.Enabled(state)) return null;

            EnterpriseBoardDef board = EnterpriseCatalog.Board(boardId);
            EnterpriseLevel entry = board?.Level(level);
            if (entry == null) return null;

            choices ??= FirstAvailableChoices.Default;

            EffectReport report = entry.Option != null
                ? EffectResolver.ExecuteOption(state, seat, entry.Option, true, choices,
                    EnterpriseEffectExtension.Instance)
                : null;

            switch (entry.Special)
            {
                case EnterpriseSpecial.AdvanceOtherEnterprise:
                    EnterpriseRules.BeginPendingAdvance(state, seat, boardId,
                        $"{board.Name} {level} 级：选择 1 家其他企业，提升 1 级合作等级。");
                    break;

                case EnterpriseSpecial.OpponentLoseScore:
                    EnterpriseRules.LetOneOpponentLoseScore(state, seat, 1, choices);
                    break;

                case EnterpriseSpecial.RecallDiscardAndDiscardHand:
                    report = TriggerEnterpriseEffectCommand.RecallDiscardAndDiscardHand(
                        state, seat, choices, report);
                    break;

                case EnterpriseSpecial.OperatorDispatch:
                    report = DispatchOperators(state, seat, choices, report);
                    break;

                case EnterpriseSpecial.SupplyReshuffleAndFreeBuild:
                    report = TriggerEnterpriseEffectCommand.ReshuffleSupplyAndFreeBuild(
                        state, seat, choices, report);
                    break;

                case EnterpriseSpecial.SwitchDepartment:
                    report = SwitchDepartment(state, seat, choices, report);
                    break;

                case EnterpriseSpecial.BuildWithoutAction:
                    report = BuildWithoutAction(state, seat, choices, report, false, 0);
                    break;

                case EnterpriseSpecial.BuildFreeFromSupply:
                    report = BuildWithoutAction(state, seat, choices, report, true, entry.Amount);
                    break;

                case EnterpriseSpecial.BuildFromDeckAndShuffle:
                    report = BuildFromDeckAndShuffle(state, seat, choices, report);
                    break;

                case EnterpriseSpecial.ExtraHarvest:
                    report = ExtraHarvest(state, seat, choices, report);
                    break;
            }

            return report;
        }

        /// <summary>
        /// 结算莱茵生命**当前科室板**在指定合作等级上的奖励（升级时的 L2–L5，与 1 级切换科室板共用）。
        /// 返回这一级的执行报告；没有科室板、或该级没有奖励时返回 null。
        ///
        /// 还没有科室板时（非界面的开局流程、开发者后门）按固定顺序取第一张，
        /// 免得这一级的奖励凭空消失 —— 正常对局开局就会选好科室板。
        /// </summary>
        internal static EffectReport SettleDepartment(GameState state, int seat, int level, IEffectChoices choices)
        {
            EnterpriseState enterprises = state.Enterprises;
            if (seat < 0 || seat >= enterprises.Departments.Count) return null;

            if (enterprises.Departments[seat] < 0) enterprises.Departments[seat] = 0;

            int departmentId = enterprises.Departments[seat];
            RhineDepartmentDef department = EnterpriseCatalog.Department(departmentId);
            EnterpriseLevel entry = department?.Level(level);
            if (entry == null) return null;

            choices ??= FirstAvailableChoices.Default;

            EffectReport report = entry.Option != null
                ? EffectResolver.ExecuteOption(state, seat, entry.Option, true, choices,
                    EnterpriseEffectExtension.Instance)
                : null;

            switch (entry.Special)
            {
                case EnterpriseSpecial.BuildWithoutAction:
                    return BuildWithoutAction(state, seat, choices, report, false, 0);

                case EnterpriseSpecial.BuildFreeFromSupply:
                    return BuildWithoutAction(state, seat, choices, report, true, entry.Amount);

                case EnterpriseSpecial.LowerLevelAndSettle:
                {
                    // 结构科 5 级：先把莱茵生命的合作等级降 1 级，再结算降级后这一级的奖励。
                    int lowered = enterprises.Lower((int)EnterpriseId.RhineLab, seat);
                    if (lowered >= 2) return SettleDepartment(state, seat, lowered, choices);
                    break;
                }
            }

            return report;
        }

        /// <summary>
        /// 莱茵生命 1 级【切换科室板】：从其余两张科室板里选 1 张换过去，
        /// 然后结算新科室板在**当前合作等级**上的奖励（1 级时科室板没有奖励，只换板）。
        /// </summary>
        private static EffectReport SwitchDepartment(GameState state, int seat, IEffectChoices choices,
            EffectReport report)
        {
            var entries = report != null
                ? new List<EffectLogEntry>(report.Entries)
                : new List<EffectLogEntry>();

            EnterpriseState enterprises = state.Enterprises;
            int current = seat >= 0 && seat < enterprises.Departments.Count ? enterprises.Departments[seat] : -1;

            var candidates = new List<int>(EnterpriseCatalog.DepartmentCount);
            for (int id = 0; id < EnterpriseCatalog.DepartmentCount; id++)
                if (id != current) candidates.Add(id);

            if (candidates.Count == 0)
                return new EffectReport(seat, entries, false, "没有其他科室板可以切换。");

            int picked = choices.ChooseRhineDepartment(seat, default, state, candidates);
            if (picked < 0)
                return new EffectReport(seat, entries, true, "这次没有切换科室板。");

            enterprises.Departments[seat] = picked;
            RhineDepartmentDef department = EnterpriseCatalog.Department(picked);
            string name = department != null ? department.Name : $"科室板 {picked + 1}";

            int cooperation = enterprises.LevelOf((int)EnterpriseId.RhineLab, seat);
            EffectReport settled = SettleDepartment(state, seat, cooperation, choices);
            if (settled != null) entries.AddRange(settled.Entries);

            string note;
            if (settled == null)
                note = $"切换至「{name}」科室板（当前合作等级 {cooperation} 级，该科室板这一级没有奖励）。";
            else if (settled.Declined)
                note = $"切换至「{name}」科室板，但你没有支付费用，{cooperation} 级奖励未结算。";
            else
                note = $"切换至「{name}」科室板，并结算了该科室板 {cooperation} 级奖励。";

            return new EffectReport(seat, entries, false, note);
        }

        /// <summary>
        /// 【进行 1 次建设，不占用主要行动】雷神工业 3 级 / 工程科 4 级；
        /// <paramref name="free"/> = true 时改走「从设施供应区免费建造」，且只允许金券费用不高于
        /// <paramref name="voucherLimit"/> 的设施（工程科 5 级，0 = 不限）。
        /// </summary>
        internal static EffectReport BuildWithoutAction(GameState state, int seat, IEffectChoices choices,
            EffectReport report, bool free, int voucherLimit)
        {
            PlayerState player = state.Player(seat);
            var entries = report != null
                ? new List<EffectLogEntry>(report.Entries)
                : new List<EffectLogEntry>();

            var panelSlots = new List<int>();
            for (int s = 0; s < PlayerState.CityPanelSlotCount; s++)
            {
                if (s == PlayerState.CityPanelCoreSlot) continue;
                if (player.CityPanelSlots[s] < 0) panelSlots.Add(s);
            }

            if (panelSlots.Count == 0)
                return new EffectReport(seat, entries, false, "城市面板没有空位，跳过这次建设。");

            var facilityIds = new List<int>();
            foreach (int id in state.FacilitySupply)
            {
                FacilityCardDef card = state.Catalog.Facility(id);
                if (card == null) continue;
                if (free && voucherLimit > 0 && card.VoucherCost > voucherLimit) continue;
                facilityIds.Add(id);
            }

            if (facilityIds.Count == 0)
                return new EffectReport(seat, entries, false, "设施供应区没有可建设的设施，跳过这次建设。");

            BuildChoice? pick = choices.ChooseBuild(seat, default, state, facilityIds, panelSlots, free);
            if (pick == null)
                return new EffectReport(seat, entries, false, "你放弃了这次建设。");

            var build = new BuildFacilityCommand(seat, pick.Value.FacilityId, pick.Value.PanelSlot,
                pick.Value.Payment, choices, freeBuild: free, ignoreActionBudget: true);
            CommandResult check = build.Validate(state);
            if (!check.Ok)
                return new EffectReport(seat, entries, false, $"建设不可执行：{check.Error}");

            build.Apply(state);
            if (build.Report != null && build.Report.Entries.Count > 0)
                entries.AddRange(build.Report.Entries);

            FacilityCardDef built = state.Catalog.Facility(pick.Value.FacilityId);
            string builtName = built != null ? built.Name : $"设施牌 {pick.Value.FacilityId}";
            return new EffectReport(seat, entries, false,
                $"进行了 1 次建设《{builtName}》（不占用主要行动）。");
        }

        /// <summary>
        /// 雷神工业 5 级【从设施牌堆选 1 张建设，然后洗混牌堆】：
        /// 候选只含牌堆里的普通设施（跳过延伸枢纽与已经同名的「唯一」设施），
        /// 选中的牌临时放进供应区以便复用 <see cref="BuildFacilityCommand"/> 的付费 / 落位 / 入场结算，
        /// 建完再把牌堆重排一次。
        /// </summary>
        internal static EffectReport BuildFromDeckAndShuffle(GameState state, int seat,
            IEffectChoices choices, EffectReport report)
        {
            PlayerState player = state.Player(seat);
            var entries = report != null
                ? new List<EffectLogEntry>(report.Entries)
                : new List<EffectLogEntry>();

            if (state.FacilityDeck.Count == 0)
                return new EffectReport(seat, entries, false, "设施牌堆已经空了，无法从牌堆选择设施。");

            var panelSlots = new List<int>();
            for (int s = 0; s < PlayerState.CityPanelSlotCount; s++)
            {
                if (s == PlayerState.CityPanelCoreSlot) continue;
                if (player.CityPanelSlots[s] < 0) panelSlots.Add(s);
            }

            if (panelSlots.Count == 0)
                return new EffectReport(seat, entries, false, "城市面板没有空位，跳过这次建设。");

            var facilityIds = new List<int>();
            foreach (int id in state.FacilityDeck)
            {
                FacilityCardDef card = state.Catalog.Facility(id);
                if (card == null || card.Kind != FacilityKind.Standard) continue;
                if (card.IsUnique && HasSameNameBuilt(state, player, card)) continue;
                facilityIds.Add(id);
            }

            if (facilityIds.Count == 0)
                return new EffectReport(seat, entries, false, "设施牌堆里没有可建设的设施，跳过这次建设。");

            BuildChoice? pick = choices.ChooseBuild(seat, default, state, facilityIds, panelSlots, false);
            if (pick == null)
            {
                ShuffleFacilityDeck(state);
                return new EffectReport(seat, entries, false, "你放弃了这次建设，设施牌堆已经洗混。");
            }

            int pickedId = pick.Value.FacilityId;
            state.FacilityDeck.Remove(pickedId);
            state.FacilitySupply.Add(pickedId);

            var build = new BuildFacilityCommand(seat, pickedId, pick.Value.PanelSlot,
                pick.Value.Payment, choices, ignoreActionBudget: true);
            CommandResult check = build.Validate(state);
            if (!check.Ok)
            {
                state.FacilitySupply.Remove(pickedId);
                state.FacilityDeck.Insert(0, pickedId);
                return new EffectReport(seat, entries, false, $"建设不可执行：{check.Error}");
            }

            build.Apply(state);
            if (build.Report != null && build.Report.Entries.Count > 0)
                entries.AddRange(build.Report.Entries);

            ShuffleFacilityDeck(state);

            FacilityCardDef built = state.Catalog.Facility(pickedId);
            string builtName = built != null ? built.Name : $"设施牌 {pickedId}";
            return new EffectReport(seat, entries, false,
                $"从设施牌堆选择《{builtName}》并完成建设（不占用主要行动），随后洗混了设施牌堆。");
        }

        /// <summary>
        /// 雷姆必拓 4 级【立刻进行 1 次采集】：规则同采集阶段（§7），只结算这一位。
        /// 逐个问「要采哪个资源点」＋「是否继续」，因此可以采任意多个点；
        /// 路费按 <see cref="HarvestResolver.Plan"/> 判断付不起的点会被跳过。
        /// </summary>
        private static EffectReport ExtraHarvest(GameState state, int seat, IEffectChoices choices,
            EffectReport report)
        {
            var entries = report != null
                ? new List<EffectLogEntry>(report.Entries)
                : new List<EffectLogEntry>();

            IReadOnlyList<int> eligible = HarvestResolver.EligibleNodes(state, seat);
            if (eligible.Count == 0)
                return new EffectReport(seat, entries, false,
                    "没有可以采集的资源点（该资源点既没有你的影响力，移动城市也没有停靠在那里）。");

            var remaining = new List<int>(eligible);
            var chosen = new List<int>();
            while (remaining.Count > 0)
            {
                if (chosen.Count > 0 &&
                    choices.ChooseOptionalStep(seat, default, state, chosen.Count, "继续采集下一个资源点") != 0)
                    break;

                int nodeId = choices.ChooseNode(seat, default, state, remaining);
                if (nodeId < 0 || !remaining.Remove(nodeId)) break;
                chosen.Add(nodeId);
            }

            if (chosen.Count == 0)
                return new EffectReport(seat, entries, false, "这次采集没有选择任何资源点。");

            HarvestResult result = HarvestResolver.ResolveSingle(state, seat, chosen);
            return new EffectReport(seat, entries, false,
                $"进行了 1 次采集：采到 {result.Nodes.Count} 个资源点，支付路费 {result.VouchersPaid} 金券。");
        }

        /// <summary>玩家面板上是否已经有同名牌（「唯一」关键词按牌名判定）。</summary>
        private static bool HasSameNameBuilt(GameState state, PlayerState player, FacilityCardDef card)
        {
            foreach (int builtId in player.BuiltFacilities)
            {
                FacilityCardDef built = state.Catalog.Facility(builtId);
                if (built != null && built.Name == card.Name) return true;
            }
            return false;
        }

        /// <summary>
        /// 「洗混设施牌堆」：对局内没有随机种子，用当前回合 / 行动座位 / 牌堆内容派生一个固定种子，
        /// 主机与各端、以及复盘回放得到的顺序完全一致（与开局洗牌一样只依赖状态）。
        /// </summary>
        private static void ShuffleFacilityDeck(GameState state)
        {
            List<int> deck = state.FacilityDeck;
            if (deck.Count <= 1) return;

            int seed = 17;
            unchecked
            {
                seed = seed * 31 + state.TurnNumber;
                seed = seed * 31 + state.ActiveSeat;
                seed = seed * 31 + deck.Count;
                foreach (int id in deck) seed = seed * 31 + id;
            }

            uint x = (uint)seed | 1u;
            for (int i = deck.Count - 1; i > 0; i--)
            {
                x = x * 1103515245u + 12345u;
                int j = (int)((x >> 16) % (uint)(i + 1));
                int temp = deck[i];
                deck[i] = deck[j];
                deck[j] = temp;
            }
        }

        /// <summary>
        /// 罗德岛 3 / 5 级【干员特派】：抽 2 张干员牌（2 人局抽 3 张）→
        /// 选 1 张加入手牌、其余洗回牌堆，然后必须从手牌 / 弃牌区选 1 张角色牌放回游戏盒；
        /// 也可以把抽到的牌全部洗回，改为获得 1 分。
        /// </summary>
        private static EffectReport DispatchOperators(GameState state, int seat, IEffectChoices choices,
            EffectReport report)
        {
            PlayerState player = state.Player(seat);
            List<int> deck = state.Enterprises.OperatorDeck;

            int drawCount = state.Players.Count == 2 ? 3 : 2;
            var drawn = new List<int>(drawCount);
            while (drawn.Count < drawCount && deck.Count > 0)
            {
                drawn.Add(deck[0]);
                deck.RemoveAt(0);
            }

            if (drawn.Count == 0)
                return new EffectReport(seat, report != null ? report.Entries : Array.Empty<EffectLogEntry>(),
                    false, "干员牌堆已经抽空，本次【干员特派】没有拿到牌。");

            int picked = choices.ChooseOperatorCard(seat, default, state, drawn);

            if (picked < 0)
            {
                deck.AddRange(drawn);
                player.Score += 1;
                return new EffectReport(seat, report != null ? report.Entries : Array.Empty<EffectLogEntry>(),
                    false, $"把 {drawn.Count} 张干员牌全部洗回牌堆，获得 1 分。");
            }

            foreach (int id in drawn)
                if (id != picked) deck.Add(id);
            player.HandCharacterCards.Add(picked);

            var candidates = new List<int>(player.HandCharacterCards);
            candidates.AddRange(player.DiscardCharacterCards);
            int boxed = candidates.Count > 0
                ? choices.ChooseCharacterCardIntoBox(seat, default, state, candidates)
                : -1;
            if (boxed >= 0)
            {
                player.HandCharacterCards.Remove(boxed);
                player.DiscardCharacterCards.Remove(boxed);
            }

            string pickedName = state.Catalog.Character(picked)?.Name ?? $"干员牌 {picked}";
            string boxedName = boxed >= 0
                ? state.Catalog.Character(boxed)?.Name ?? $"角色牌 {boxed}"
                : null;

            string note = boxedName != null
                ? $"抽取 {drawn.Count} 张干员牌，取《{pickedName}》加入手牌；《{boxedName}》放回游戏盒。"
                : $"抽取 {drawn.Count} 张干员牌，取《{pickedName}》加入手牌。";

            return new EffectReport(seat, report != null ? report.Entries : Array.Empty<EffectLogEntry>(),
                false, note);
        }
    }

    /// <summary>
    /// 开局：从抽到的企业板中选择 1 张（企业扩展设置）。
    /// 3／4 人局每人抽 2 选 1；2 人局每人抽 4 选 2（连续执行两次本命令）。
    ///
    /// 每选 1 张做三件事：把这块板放上场（所有座位在这块板上的合作等级从 0 开始）、
    /// 把玩家自己在这块板上提升到 1 级（规则：初始合作等级 0，开局再提升 1 级）、
    /// 全部玩家都选满后把场上企业板补足到 4 块（3 人局补第 4 块）。
    /// </summary>
    public sealed class ChooseInitialEnterpriseCommand : ICommand
    {
        public readonly int BoardId;

        public ChooseInitialEnterpriseCommand(int seat, int boardId)
        {
            ActorSeat = seat;
            BoardId = boardId;
        }

        public int ActorSeat { get; }
        public string Name => "选择合作企业";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!EnterpriseRules.Enabled(state)) return CommandResult.Fail("本局没有启用企业扩展。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Players.Count == 0) return CommandResult.Fail("还没有建立玩家。");

            if (state.TurnNumber != 1 || state.Phase != TurnPhase.CharacterPlacement)
                return CommandResult.Fail("只能在开局入场（第 1 回合的角色牌盖放阶段）选择合作企业。");

            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            EnterpriseState enterprises = state.Enterprises;
            if (enterprises.SeatDraftDone(ActorSeat))
                return CommandResult.Fail("你已经选过合作企业了。");

            if (enterprises.HasChosen(ActorSeat, BoardId))
                return CommandResult.Fail("这张企业板你已经选过了。");

            if (!enterprises.Offers[ActorSeat].Contains(BoardId))
                return CommandResult.Fail(
                    $"这张企业板不在你的可选范围内（只能从抽到的 {enterprises.OffersPerSeat} 张里选）。");

            if (EnterpriseCatalog.Board(BoardId) == null)
                return CommandResult.Fail($"企业板 {BoardId} 不存在。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;

            enterprises.Deck.Remove(BoardId);
            enterprises.Offers[ActorSeat].Remove(BoardId);

            if (enterprises.ChosenBoards[ActorSeat] < 0)
                enterprises.ChosenBoards[ActorSeat] = BoardId;
            else
                enterprises.SecondChosenBoards[ActorSeat] = BoardId;

            if (!enterprises.IsActive(BoardId))
            {
                enterprises.Activate(BoardId);
                // 每名玩家在这块板的 0 级位置各放 1 个标记（从 30 个供应堆里扣）。
                EnterpriseRules.PlaceSetupMarkers(state, BoardId);
            }

            // 玩家自己在这块板上从 0 升到 1 级。
            int level = enterprises.Advance(BoardId, ActorSeat);
            EnterpriseRules.SettleLevel(state, BoardId, ActorSeat, level, FirstAvailableChoices.Default);

            // 莱茵生命：不默认分配科室板，等 UI 弹出三张科室板让玩家挑一张。
            // （RhineLab 的 1 级是 SwitchDepartment 特效，此时还没升到 L2，不需要科室板数据）

            // 2 人局还要选第 2 张：先把剩下的选择留着，别急着还回牌堆。
            if (!enterprises.SeatDraftDone(ActorSeat)) return;

            // 这个座位选完了：没选中的那几张放回牌堆底。
            foreach (int offered in enterprises.Offers[ActorSeat])
                enterprises.Deck.Add(offered);
            enterprises.Offers[ActorSeat].Clear();

            for (int seat = 0; seat < enterprises.ChosenBoards.Count; seat++)
                if (!enterprises.SeatDraftDone(seat)) return;

            enterprises.DraftDone = true;
            EnterpriseRules.CompleteBoardSet(state);
        }
    }

    /// <summary>
    /// 把某块企业板上的合作等级提升 1 级，并结算这一级的即时奖励。
    ///
    /// 只在状态里登记了「待提升」（<see cref="EnterpriseState.PendingAdvanceSeat"/>）时才允许执行，
    /// 来源有：罗德岛 1 级特效（选 1 家其他企业 +1 级）、红色区域开放回合的全体提升等。
    /// </summary>
    public sealed class AdvanceEnterpriseLevelCommand : ICommand
    {
        public readonly int BoardId;

        private readonly IEffectChoices choices;

        public AdvanceEnterpriseLevelCommand(int seat, int boardId, IEffectChoices choices = null)
        {
            ActorSeat = seat;
            BoardId = boardId;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => "提升企业合作等级";
        public bool ConsumesAction => false;

        /// <summary>效果点选答案（联机编码用）。</summary>
        public IEffectChoices EffectChoices => choices;

        /// <summary>这一级结算的效果明细（界面状态行用）。</summary>
        public EffectReport Report { get; private set; }

        public CommandResult Validate(GameState state)
        {
            if (!EnterpriseRules.Enabled(state)) return CommandResult.Fail("本局没有启用企业扩展。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");

            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            EnterpriseState enterprises = state.Enterprises;
            if (enterprises.PendingAdvanceSeat != ActorSeat)
                return CommandResult.Fail("现在没有等着你提升的企业合作等级。");

            if (BoardId == enterprises.PendingAdvanceExcludeBoard)
                return CommandResult.Fail("这次提升不能选择这张企业板（牌面要求选择「其他企业」）。");

            if (!enterprises.IsActive(BoardId))
                return CommandResult.Fail("这张企业板不在本局场上。");

            int level = enterprises.LevelOf(BoardId, ActorSeat);
            if (level < 0) return CommandResult.Fail("你不在这张企业板上。");
            if (level >= 5) return CommandResult.Fail("这张企业板上的合作等级已经满级（5 级）。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;

            // 红区开放：先记下这位已经提升过（放在 SettleLevel 之前，
            // 这样这一级新触发的「待提升」不会被红区队列误判成还没处理）。
            if (enterprises.RedZoneAdvanceSeat == ActorSeat)
            {
                enterprises.RedZoneAdvanceDoneMask |= 1 << ActorSeat;
                enterprises.RedZoneAdvanceSeat = -1;
            }

            int level = enterprises.Advance(BoardId, ActorSeat);

            EnterpriseRules.ClearPendingAdvance(state);
            Report = EnterpriseRules.SettleLevel(state, BoardId, ActorSeat, level,
                choices ?? FirstAvailableChoices.Default);
            EnterpriseRules.ContinueRedZoneAdvance(state);
        }
    }

    /// <summary>
    /// 开发者测试用具：把某座位在某块企业板上的合作等级直接写成指定值，绕开「提升 1 级」的正常途径。
    /// 只用于开发者面板；正常对局里等级只能由命令逐级提升。
    /// </summary>
    public sealed class DeveloperSetEnterpriseLevelCommand : ICommand
    {
        public readonly int BoardId;
        public readonly int Level;

        private readonly IEffectChoices choices;
        private readonly bool settle;

        /// <summary><paramref name="settle"/> = true 时顺便结算该等级的即时奖励（科室板奖励等）。</summary>
        public DeveloperSetEnterpriseLevelCommand(int seat, int boardId, int level,
            IEffectChoices choices = null, bool settle = false)
        {
            ActorSeat = seat;
            BoardId = boardId;
            Level = level;
            this.choices = choices;
            this.settle = settle;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：设置企业合作等级";
        public bool ConsumesAction => false;

        /// <summary>这一级结算出来的效果明细；<see cref="settle"/> 为 false 时为 null。</summary>
        public EffectReport Report { get; private set; }

        public CommandResult Validate(GameState state)
        {
            if (!state.DeveloperMode) return CommandResult.Fail("这是开发者测试命令，只能在开发者模式下使用。");
            if (!EnterpriseRules.Enabled(state)) return CommandResult.Fail("本局没有启用企业扩展。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");
            if (BoardId < 0 || BoardId >= EnterpriseCatalog.BoardCount)
                return CommandResult.Fail($"企业板 {BoardId} 不存在。");
            if (!state.Enterprises.IsActive(BoardId))
                return CommandResult.Fail("这张企业板不在本局场上。");
            if (state.Enterprises.LevelOf(BoardId, ActorSeat) < 0)
                return CommandResult.Fail("这名玩家不在这张企业板上。");
            if (Level < 0 || Level > 5) return CommandResult.Fail("合作等级只能是 0–5。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            state.Enterprises.SetLevel(BoardId, ActorSeat, Level);
            if (settle)
                Report = EnterpriseRules.SettleLevel(state, BoardId, ActorSeat, Level,
                    choices ?? FirstAvailableChoices.Default);
        }
    }

    /// <summary>
    /// 选择莱茵生命初始科室板（三张里挑一张）。
    /// 只有该座位已经选了莱茵生命企业板、且还没分配科室板时才允许执行。
    /// </summary>
    public sealed class PickRhineDepartmentCommand : ICommand
    {
        public readonly int DepartmentId;

        public PickRhineDepartmentCommand(int seat, int departmentId)
        {
            ActorSeat = seat;
            DepartmentId = departmentId;
        }

        public int ActorSeat { get; }
        public string Name => "选择莱茵生命科室板";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!EnterpriseRules.Enabled(state)) return CommandResult.Fail("本局没有启用企业扩展。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");
            if (DepartmentId < 0 || DepartmentId >= EnterpriseCatalog.DepartmentCount)
                return CommandResult.Fail($"科室板 id {DepartmentId} 无效。");

            EnterpriseState enterprises = state.Enterprises;
            if (!enterprises.IsActive((int)EnterpriseId.RhineLab))
                return CommandResult.Fail("莱茵生命不在本局场上。");
            if (enterprises.LevelOf((int)EnterpriseId.RhineLab, ActorSeat) < 0)
                return CommandResult.Fail("你没有选择莱茵生命。");
            // 允许覆盖：如果之前选过，也可以用 SwitchDepartment 特效再次切换
            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;
            if (ActorSeat >= 0 && ActorSeat < enterprises.Departments.Count)
                enterprises.Departments[ActorSeat] = DepartmentId;
        }
    }

    /// <summary>
    /// 触发一个已经解锁的企业特效（企业扩展）。
    ///
    /// 约束：合作等级必须已经达到该级；同一块板的同一个等级**一回合只能触发 1 次**；
    /// 带「支付 X → 获得 Y」的企业特效，支付不足时整段效果不执行。
    /// 需要玩家点选的节点（放置 / 移动 / 移除影响力等）走 <see cref="IEffectChoices"/>，与事件牌共用一套。
    /// </summary>
    public sealed class TriggerEnterpriseEffectCommand : ICommand
    {
        public readonly int BoardId;
        public readonly int Level;

        private readonly IEffectChoices choices;

        /// <summary>
        /// 由卡牌效果（事件牌 / 设施入场 / 城市样式特殊行动）带出来的触发不受「玩家行动阶段 + 自己行动」限制：
        /// 卡面已经给出了时机，效果本身照常结算。
        /// </summary>
        private readonly bool ignoreTiming;

        public TriggerEnterpriseEffectCommand(int seat, int boardId, int level, IEffectChoices choices = null,
            bool ignoreTiming = false)
        {
            ActorSeat = seat;
            BoardId = boardId;
            Level = level;
            this.choices = choices;
            this.ignoreTiming = ignoreTiming;
        }

        public int ActorSeat { get; }
        public string Name => "触发企业特效";
        public bool ConsumesAction => false;

        /// <summary>效果点选答案（联机编码用）。</summary>
        public IEffectChoices EffectChoices => choices;

        /// <summary>本次触发的效果明细（界面状态行用）。</summary>
        public EffectReport Report { get; private set; }

        public CommandResult Validate(GameState state)
        {
            if (!EnterpriseRules.Enabled(state)) return CommandResult.Fail("本局没有启用企业扩展。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Players.Count == 0) return CommandResult.Fail("还没有建立玩家。");

            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            if (!ignoreTiming)
            {
                if (state.Phase != TurnPhase.PlayerActions)
                    return CommandResult.Fail("企业特效只能在玩家行动阶段触发。");
                if (state.ActiveSeat != ActorSeat)
                    return CommandResult.Fail("只能在自己行动时触发企业特效。");
            }

            EnterpriseState enterprises = state.Enterprises;
            if (!enterprises.DraftDone)
                return CommandResult.Fail("开局的企业抽选还没完成。");

            if (!enterprises.IsActive(BoardId))
                return CommandResult.Fail("这张企业板不在本局场上。");

            int current = enterprises.LevelOf(BoardId, ActorSeat);
            if (current < 0) return CommandResult.Fail("你不在这张企业板上。");

            EnterpriseBoardDef board = EnterpriseCatalog.Board(BoardId);
            EnterpriseLevel entry = board?.Level(Level);
            if (entry == null) return CommandResult.Fail($"企业板 {BoardId} 没有 {Level} 级。");
            if (!entry.IsEffect) return CommandResult.Fail($"「{entry.Text}」不是企业特效，达到等级时已经自动结算。");
            if (current < Level)
                return CommandResult.Fail($"合作等级不足：这张板的 {Level} 级特效要合作等级达到 {Level} 级才能触发（你当前 {current} 级）。");

            if (enterprises.HasUsedEffectThisTurn(BoardId, ActorSeat, Level))
                return CommandResult.Fail($"本回合已经触发过「{board.Name}」{Level} 级的企业特效了（同一特效一回合只能触发 1 次）。");

            switch (entry.Special)
            {
                case EnterpriseSpecial.AdvanceOtherEnterprise:
                {
                    bool hasTarget = false;
                    foreach (int other in enterprises.ActiveBoards)
                    {
                        if (other == BoardId) continue;
                        int otherLevel = enterprises.LevelOf(other, ActorSeat);
                        if (otherLevel >= 0 && otherLevel < 5)
                        {
                            hasTarget = true;
                            break;
                        }
                    }
                    if (!hasTarget)
                        return CommandResult.Fail("场上没有其他还有提升空间的企业板。");
                    break;
                }

                case EnterpriseSpecial.OpponentLoseScore:
                    break;

                case EnterpriseSpecial.OperatorDispatch:
                    if (state.Enterprises.OperatorDeck.Count == 0)
                        return CommandResult.Fail("干员牌堆已经抽空了，无法再次特派干员。");
                    break;
                case EnterpriseSpecial.RecallDiscardAndDiscardHand:
                    if (state.Player(ActorSeat).DiscardCharacterCards.Count == 0)
                        return CommandResult.Fail("角色牌弃牌区没有牌，不能使用这个特效。");
                    break;
                case EnterpriseSpecial.SupplyReshuffleAndFreeBuild:
                    break;
                case EnterpriseSpecial.SwitchDepartment:
                    // 三张科室板里总能换到另一张，具体选哪张等结算时再问。
                    break;
                case EnterpriseSpecial.DepartmentReward:
                    return CommandResult.Fail("科室板奖励在升级时自动结算，不需要单独触发。");
                case EnterpriseSpecial.BuildFromDeckAndShuffle:
                    break;
                case EnterpriseSpecial.BuildWithoutAction:
                case EnterpriseSpecial.BuildFreeFromSupply:
                    break;
                case EnterpriseSpecial.ExtraHarvest:
                    break;
                case EnterpriseSpecial.LowerLevelAndSettle:
                    return CommandResult.Fail("「降低 1 级并结算」特效在升级时结算，不需要单独触发。");
            }

            if (entry.Option != null &&
                !EnterpriseRules.CanPay(state, ActorSeat, entry.Option.Cost, out string reason))
                return CommandResult.Fail(reason);

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            state.Enterprises.MarkEffectUsed(BoardId, ActorSeat, Level);
            Report = EnterpriseEffectResolver.Resolve(state, ActorSeat, BoardId, Level, choices);
        }

        /// <summary>
        /// 罗德岛 4 级：从角色牌弃牌区选 1 张加入手牌 → 其余手牌全部进弃牌区 → 获得 10 金券。
        /// </summary>
        /// <summary>
        /// 海德兄弟 4 级：把设施供应区的所有牌放回设施牌堆并补满，然后可免费建造 1 次。
        ///
        /// 「洗回」遵循本项目联机的确定性约定：追加到牌堆底部，不引入随机洗牌 ——
        /// 命令在主机与回放端必须得到完全相同的牌序。
        /// </summary>
        internal static EffectReport ReshuffleSupplyAndFreeBuild(GameState state, int seat,
            IEffectChoices choices, EffectReport report)
        {
            PlayerState player = state.Player(seat);
            var entries = report != null
                ? new List<EffectLogEntry>(report.Entries)
                : new List<EffectLogEntry>();

            int returned = state.FacilitySupply.Count;
            state.FacilityDeck.AddRange(state.FacilitySupply);
            state.FacilitySupply.Clear();
            BoardSetup.RefillFacilitySupply(state);

            if (state.FacilitySupply.Count == 0)
                return new EffectReport(seat, entries, false,
                    $"将 {returned} 张供应区设施牌放回牌堆，但牌堆也空了，无法免费建造。");

            var panelSlots = new List<int>();
            for (int s = 0; s < PlayerState.CityPanelSlotCount; s++)
            {
                if (s == PlayerState.CityPanelCoreSlot) continue;
                if (player.CityPanelSlots[s] < 0) panelSlots.Add(s);
            }

            if (panelSlots.Count == 0)
                return new EffectReport(seat, entries, false,
                    $"将 {returned} 张供应区设施牌放回牌堆并补满；城市面板没有空位，跳过免费建造。");

            var facilityIds = new List<int>(state.FacilitySupply);
            BuildChoice? pick = choices.ChooseBuild(seat, default, state, facilityIds, panelSlots, true);
            if (pick == null)
                return new EffectReport(seat, entries, false,
                    $"将 {returned} 张供应区设施牌放回牌堆并补满；你放弃了免费建造。");

            var build = new BuildFacilityCommand(seat, pick.Value.FacilityId, pick.Value.PanelSlot,
                FacilityPayment.None, choices, freeBuild: true, ignoreActionBudget: true);
            CommandResult check = build.Validate(state);
            if (!check.Ok)
                return new EffectReport(seat, entries, false,
                    $"供应区已放回牌堆并补满，但免费建造不可执行：{check.Error}");

            build.Apply(state);
            if (build.Report != null && build.Report.Entries.Count > 0)
                entries.AddRange(build.Report.Entries);

            FacilityCardDef built = state.Catalog.Facility(pick.Value.FacilityId);
            string builtName = built != null ? built.Name : $"设施牌 {pick.Value.FacilityId}";
            return new EffectReport(seat, entries, false,
                $"将 {returned} 张供应区设施牌放回牌堆并补满，免费建造了《{builtName}》。");
        }

        internal static EffectReport RecallDiscardAndDiscardHand(GameState state, int seat,
            IEffectChoices choices, EffectReport report)
        {
            PlayerState player = state.Player(seat);
            var pile = new List<int>(player.DiscardCharacterCards);
            var hand = new List<int>(player.HandCharacterCards);

            int picked = pile.Count > 0 && choices != null
                ? choices.ChooseDiscardedCharacterCard(seat, default, state, pile)
                : (pile.Count > 0 ? pile[0] : -1);

            player.DiscardCharacterCards.Clear();
            if (picked >= 0) pile.Remove(picked);
            player.DiscardCharacterCards.AddRange(pile);
            player.DiscardCharacterCards.AddRange(hand);

            player.HandCharacterCards.Clear();
            if (picked >= 0) player.HandCharacterCards.Add(picked);
            player.Vouchers += 10;

            string note;
            if (picked >= 0)
            {
                CharacterCardDef card = state.Catalog.Character(picked);
                note = $"从角色牌弃牌区取回《{(card != null ? card.Name : $"角色牌 {picked}")}》，" +
                       $"其余 {hand.Count} 张手牌进弃牌区，获得 10 金券。";
            }
            else
            {
                note = $"弃牌区没有角色牌可取回；{hand.Count} 张手牌进弃牌区，获得 10 金券。";
            }
            return new EffectReport(seat,
                report != null ? report.Entries : Array.Empty<EffectLogEntry>(), false, note);
        }
    }
}
