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
        }
    }
}
