using System;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 开局：从抽到的 2 张企业板中选择 1 张（企业扩展设置）。
    ///
    /// 做完三件事：把选中的板放上场（所有座位在这块板上的合作等级从 0 开始）、
    /// 把玩家自己在这块板上提升到 1 级（规则：初始合作等级 0，开局再提升 1 级）、
    /// 全部玩家都选完后把场上企业板补足到 4 块（2／3 人局）。
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
            if (enterprises.ChosenBoards[ActorSeat] >= 0)
                return CommandResult.Fail("你已经选过合作企业了。");

            if (!enterprises.Offers[ActorSeat].Contains(BoardId))
                return CommandResult.Fail("这张企业板不在你的可选范围内（只能从抽到的 2 张里选 1 张）。");

            if (EnterpriseCatalog.Board(BoardId) == null)
                return CommandResult.Fail($"企业板 {BoardId} 不存在。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            EnterpriseState enterprises = state.Enterprises;

            // 没选的那张放回牌堆底。
            foreach (int offered in enterprises.Offers[ActorSeat])
                if (offered != BoardId) enterprises.Deck.Add(offered);
            enterprises.Offers[ActorSeat].Clear();

            enterprises.Deck.Remove(BoardId);
            enterprises.ChosenBoards[ActorSeat] = BoardId;
            enterprises.Activate(BoardId);

            // 玩家自己在这块板上从 0 升到 1 级。
            int level = enterprises.Advance(BoardId, ActorSeat);
            EnterpriseRules.SettleLevel(state, BoardId, ActorSeat, level, FirstAvailableChoices.Default);

            // 莱茵生命：默认拿防御科，之后可用 1 级特效切换。
            if (BoardId == (int)EnterpriseId.RhineLab && enterprises.Departments[ActorSeat] < 0)
                enterprises.Departments[ActorSeat] = 0;

            bool allChosen = true;
            for (int seat = 0; seat < enterprises.ChosenBoards.Count; seat++)
                if (enterprises.ChosenBoards[seat] < 0) allChosen = false;

            if (!allChosen) return;

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

        public AdvanceEnterpriseLevelCommand(int seat, int boardId)
        {
            ActorSeat = seat;
            BoardId = boardId;
        }

        public int ActorSeat { get; }
        public string Name => "提升企业合作等级";
        public bool ConsumesAction => false;

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
            int level = enterprises.Advance(BoardId, ActorSeat);

            EnterpriseRules.ClearPendingAdvance(state);
            EnterpriseRules.SettleLevel(state, BoardId, ActorSeat, level, FirstAvailableChoices.Default);
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

        public TriggerEnterpriseEffectCommand(int seat, int boardId, int level, IEffectChoices choices = null)
        {
            ActorSeat = seat;
            BoardId = boardId;
            Level = level;
            this.choices = choices;
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

            if (state.Phase != TurnPhase.PlayerActions)
                return CommandResult.Fail("企业特效只能在玩家行动阶段触发。");
            if (state.ActiveSeat != ActorSeat)
                return CommandResult.Fail("只能在自己行动时触发企业特效。");

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
                    return CommandResult.Fail("「干员特派」需要干员牌数据，尚未接入。");
                case EnterpriseSpecial.RecallDiscardAndDiscardHand:
                    return CommandResult.Fail("「弃牌区回手 + 弃手牌」特效尚未接入。");
                case EnterpriseSpecial.SupplyReshuffleAndFreeBuild:
                    return CommandResult.Fail("「供应区洗回牌堆 + 免费建造」特效尚未接入。");
                case EnterpriseSpecial.SwitchDepartment:
                    return CommandResult.Fail("「切换科室板」特效尚未接入。");
                case EnterpriseSpecial.DepartmentReward:
                    return CommandResult.Fail("科室板奖励在升级时自动结算，不需要单独触发。");
                case EnterpriseSpecial.BuildFromDeckAndShuffle:
                    return CommandResult.Fail("「从设施牌堆建造」特效尚未接入。");
                case EnterpriseSpecial.BuildWithoutAction:
                    return CommandResult.Fail("「不占用主要行动的建设」特效尚未接入。");
                case EnterpriseSpecial.ExtraHarvest:
                    return CommandResult.Fail("「立刻进行 1 次采集」特效尚未接入。");
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
            EnterpriseState enterprises = state.Enterprises;
            EnterpriseBoardDef board = EnterpriseCatalog.Board(BoardId);
            EnterpriseLevel entry = board.Level(Level);

            enterprises.MarkEffectUsed(BoardId, ActorSeat, Level);

            if (entry.Option != null)
                Report = EffectResolver.ExecuteOption(state, ActorSeat, entry.Option, true, choices);

            switch (entry.Special)
            {
                case EnterpriseSpecial.AdvanceOtherEnterprise:
                    EnterpriseRules.BeginPendingAdvance(state, ActorSeat, BoardId,
                        $"{board.Name} {Level} 级：选择 1 家其他企业，提升 1 级合作等级。");
                    break;

                case EnterpriseSpecial.OpponentLoseScore:
                    EnterpriseRules.LetOneOpponentLoseScore(state, ActorSeat, 1);
                    break;
            }
        }
    }
}
