using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 收尾阶段发动持续计谋的「每收尾阶段一次」能力：
    /// 泥岩小队（支付 2 源岩 → 放置 1 个影响力）／为了大家（移除 2 个标记 → 放置 1 个影响力）。
    ///
    /// 收尾阶段本来是全自动的；只要场上还有玩家可以发动这类能力，<see cref="TurnFlow"/> 会停在
    /// <see cref="TurnPhase.Cleanup"/> 等玩家决定。用掉之后若没人还能发动，会自动推进到下一位；
    /// 想直接跳过则发 <see cref="AdvancePhaseCommand"/>。
    /// </summary>
    public sealed class UsePersistentTacticCleanupCommand : ICommand
    {
        /// <summary>要发动的持续计谋种类。</summary>
        public readonly PersistentTacticKind Kind;

        private readonly IEffectChoices choices;

        public UsePersistentTacticCleanupCommand(int seat, PersistentTacticKind kind,
            IEffectChoices choices = null)
        {
            ActorSeat = seat;
            Kind = kind;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => "发动持续计谋（收尾阶段）";

        /// <summary>效果答案来源（联机编码时从这里取答案）。</summary>
        public IEffectChoices EffectChoices => choices;

        /// <summary>不占主要行动名额。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state.Phase != TurnPhase.Cleanup)
                return CommandResult.Fail("只有在收尾阶段才能发动持续计谋的这个能力。");

            // 收尾按本回合行动顺序逐个座位结算：还没轮到这位玩家时不能先发动。
            if (state.CleanupIndex >= 0 && state.CleanupSeat != ActorSeat)
                return CommandResult.Fail("收尾阶段按行动顺序结算，现在还没轮到你。");

            PersistentTacticState tactic = state.FindPersistentTactic(ActorSeat, Kind);
            if (tactic == null) return CommandResult.Fail("你没有这张持续计谋牌。");
            if (tactic.UsedCleanupThisTurn) return CommandResult.Fail("这个收尾阶段已经发动过它了。");
            if (!PersistentTacticRules.CanUseCleanup(state, tactic))
                return CommandResult.Fail("现在不满足发动条件（资源 / 标记 / 可放置的位置不足）。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PersistentTacticState tactic = state.FindPersistentTactic(ActorSeat, Kind);
            PersistentTacticRules.UseCleanup(state, tactic, choices);

            // 这位玩家没有别的可选效果了：把收尾推进到下一位（全部轮完则进入下一回合）。
            CleanupResolver.AdvanceIfDone(state);
        }
    }

    /// <summary>
    /// 收尾阶段「我这一位没有要发动的效果了」：只把自己这一格跳过，轮到行动顺序里的下一位。
    /// 与 <see cref="AdvancePhaseCommand"/> 不同 —— 它不会替别人把整个收尾一起跳过。
    /// </summary>
    public sealed class EndCleanupSeatCommand : ICommand
    {
        public EndCleanupSeatCommand(int seat) => ActorSeat = seat;

        public int ActorSeat { get; }
        public string Name => "结束自己的收尾阶段";
        public bool ConsumesAction => false;
        public IEffectChoices EffectChoices => null;

        public CommandResult Validate(GameState state)
        {
            if (state.Phase != TurnPhase.Cleanup)
                return CommandResult.Fail("现在不是收尾阶段。");
            if (state.CleanupIndex < 0)
                return CommandResult.Fail("收尾阶段已经结算完了。");
            if (state.CleanupSeat != ActorSeat)
                return CommandResult.Fail("现在不是你在结算收尾。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            state.CleanupIndex++;
            CleanupResolver.Advance(state);
        }
    }
}
