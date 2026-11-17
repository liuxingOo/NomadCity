using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 「移除影响力」行动事件（第一批只作为卡牌效果，不设操作区按钮）。
    /// 卡牌 / 企业 / 城市样式给出的「移除 1 个影响力」会登记成
    /// <see cref="PendingActionKind.RemoveInfluence"/> 挂起行动，由界面走完整流程后派发这条命令。
    ///
    /// 限制（只能移除自己的 / 只能移除他人的）从挂起记录里读，不信调用方参数。
    /// 没有挂起 id 时只能是开发者模式的测试入口。
    /// </summary>
    public sealed class RemoveInfluenceCommand : ICommand
    {
        /// <summary>要移除的影响力格。</summary>
        public readonly InfluenceSlotRef Slot;

        /// <summary>授予这次移除的挂起行动 id（0 = 开发者测试入口）。</summary>
        public readonly int PendingActionId;

        /// <summary>移除时若所有者有「为了大家」，「标记放回供应堆 / 放到牌上」由它回答；不传取第一项。</summary>
        private readonly IEffectChoices choices;

        public RemoveInfluenceCommand(int seat, InfluenceSlotRef slot, int pendingActionId = 0,
            IEffectChoices choices = null)
        {
            ActorSeat = seat;
            Slot = slot;
            PendingActionId = pendingActionId;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => "移除影响力";

        /// <summary>移除影响力不是主要行动，效果给的次数也不占名额。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");

            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            PendingInfluenceScope scope = PendingInfluenceScope.Any;
            if (PendingActionId > 0)
            {
                CommandResult check = PendingActionRules.ValidatePending(state, ActorSeat,
                    PendingActionKind.RemoveInfluence, PendingActionId);
                if (!check.Ok) return check;

                PendingAction pending = state.FindPending(PendingActionId);
                if (pending != null) scope = pending.Limits.Scope;
            }
            else if (!state.DeveloperMode)
            {
                return CommandResult.Fail("「移除影响力」只能由卡牌效果触发（或开发者模式测试）。");
            }

            if (Slot.SlotIndex < 0 || Slot.SlotIndex >= state.Map.SlotsOf(Slot).Capacity)
                return CommandResult.Fail($"影响力格 {Slot} 不存在。");

            int owner = state.Map.OccupantOf(Slot);
            if (owner < 0) return CommandResult.Fail($"影响力格 {Slot} 上没有影响力。");

            switch (scope)
            {
                case PendingInfluenceScope.Own when owner != ActorSeat:
                    return CommandResult.Fail("这次效果只能移除自己的影响力。");
                case PendingInfluenceScope.Others when owner == ActorSeat:
                    return CommandResult.Fail("这次效果只能移除其他玩家的影响力。");
            }

            return CommandResult.Success;
        }

        /// <summary>移除时选择供给（联机编码要从这里取答案，和 <see cref="BuildFacilityCommand"/> 一致）。</summary>
        public IEffectChoices EffectChoices => choices;

        public void Apply(GameState state)
        {
            ActionRules.RemoveInfluenceAt(state, Slot, choices);
            if (PendingActionId > 0) state.ConsumePending(PendingActionId);
        }
    }

    /// <summary>
    /// 放弃一条挂起行动。只在两种情况下合法：
    /// 1) 该座位已经找不到任何合法目标（自动收尾，避免卡死）；
    /// 2) 开发者模式（调试时手动清掉）。
    /// 「效果给的行动必须执行」这条规则由这里兜底：有目标时不能放弃。
    /// </summary>
    public sealed class AbandonPendingActionCommand : ICommand
    {
        public readonly int PendingActionId;

        public AbandonPendingActionCommand(int seat, int pendingActionId)
        {
            ActorSeat = seat;
            PendingActionId = pendingActionId;
        }

        public int ActorSeat { get; }
        public string Name => "放弃挂起行动";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");

            PendingAction pending = state.FindPending(PendingActionId);
            if (pending == null) return CommandResult.Fail("这条挂起行动已经不存在了。");
            if (pending.Seat != ActorSeat) return CommandResult.Fail("这不是你的挂起行动。");

            if (state.DeveloperMode) return CommandResult.Success;

            if (PendingActionRules.HasAnyLegalTarget(state, ActorSeat, pending.Kind, pending.Limits))
                return CommandResult.Fail(
                    $"「{pending.Label}」还有可以执行的目标，必须做完才能继续。");

            return CommandResult.Success;
        }

        public void Apply(GameState state) => state.DropPending(PendingActionId);
    }
}
