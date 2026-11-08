using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 主要行动「使用城市样式特殊行动」（§6.3f / §10.2-4）：使用某张已宣告过的城市样式牌解锁的特殊行动，一次做完：
    /// 1) 结算牌面费用（<see cref="CityPatternCardDef.SpecialActionCost"/>，可含自由分配支付）；
    /// 2) 结算牌面效果（<see cref="CityPatternCardDef.SpecialActionEffects"/>）；
    /// 3) 把这张样式牌上自己的一个可用标记**下移 1 格**（I 级「未使用」→「已使用」；II 级「2」→已使用、「1」→已使用）；
    /// 4) 记入「本回合已用过的样式牌」，使每张牌每回合至多用一次。
    ///
    /// 使用条件（<see cref="Validate"/>）：
    /// - 是当前行动玩家；这张牌在场上且已录入特殊行动；
    /// - 这张牌上自己有可用格的标记（<see cref="CityPatternState.HasUsableMarker"/>）；
    /// - 本回合还没用过这张牌的特殊行动；
    /// - 费用付得起（开发者模式跳过，见 <see cref="ActionRules.IgnoresCosts"/>）。
    ///
    /// 这是**主要行动**，占用本回合的主要行动名额（§6.3f），因此 ConsumesAction = true。
    /// 「源石工业中枢」的「额外 2 次主要行动」由 <c>RewardKind.ExtraMainActions</c> 写入玩家状态，
    /// 由 <see cref="TurnFlow.CompletePlayerAction"/> 在推进座位前消费。
    ///
    /// 效果里需要玩家点选时由 <paramref name="choices"/> 回答；不传则用「总是取第一个合法项」的自动结算
    /// （界面正常玩法会经由 <c>EffectChoiceSession</c> 先问后改）。
    /// </summary>
    public sealed class UsePatternSpecialActionCommand : ICommand
    {
        /// <summary>要使用特殊行动的城市样式牌 id（必须是在场上的那 6 张之一）。</summary>
        public readonly int PatternCardId;

        /// <summary>本行效果里需要玩家点选时由它回答；不传则用自动结算。</summary>
        private readonly IEffectChoices choices;

        /// <summary>费用 + 效果的结算报告（合并）；<see cref="Apply"/> 之后才有值。</summary>
        public EffectReport Report { get; private set; }

        public UsePatternSpecialActionCommand(int seat, int patternCardId, IEffectChoices choices = null)
        {
            ActorSeat = seat;
            PatternCardId = patternCardId;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => "使用城市样式特殊行动";

        /// <summary>效果答案来源（联机编码时要从这里取答案，见 <c>CommandCodec</c>）。</summary>
        public IEffectChoices EffectChoices => choices;

        /// <summary>主要行动，占用本回合的主要行动名额（§6.3f）。</summary>
        public bool ConsumesAction => true;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            CommandResult quota = ActionRules.ValidateMainActionAvailable(state, ActorSeat);
            if (!quota.Ok) return quota;

            // 先查「在不在场上」再取定义：目录对越界 id 会抛异常，不能直接拿来判空。
            CityPatternState patternState = state.PatternStateOf(PatternCardId);
            if (patternState == null)
                return CommandResult.Fail("这张城市样式牌不在场上（§4.1 步 3 最多放出 6 张）。");

            CityPatternCardDef card = state.Catalog.CityPattern(PatternCardId);
            if (!card.HasSpecialAction)
                return CommandResult.Fail($"《{CardName(card)}》的特殊行动还没录入，暂时无法使用。");

            PlayerState player = state.Player(ActorSeat);
            if (player.UsedPatternActionsThisTurn.Contains(PatternCardId))
                return CommandResult.Fail($"本回合已经用过《{CardName(card)}》的特殊行动（每张牌每回合限一次）。");

            if (!patternState.HasUsableMarker(ActorSeat))
                return CommandResult.Fail($"《{CardName(card)}》上没有你可用的玩家标记，无法使用特殊行动（§10.2-4）。");

            if (!ActionRules.IgnoresCosts(state) &&
                !EffectResolver.CanPay(state, ActorSeat, card.SpecialActionCost, out string reason))
                return CommandResult.Fail($"支付《{CardName(card)}》特殊行动的费用失败：{reason}。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            CityPatternCardDef card = state.Catalog.CityPattern(PatternCardId);

            // 把「当前特殊行动所在样式牌」记进状态通道，供 PlaceInfluencePerPatternMarkers 取计数来源。
            int previousPatternId = state.ActiveSpecialActionPatternId;
            state.ActiveSpecialActionPatternId = PatternCardId;

            var entries = new System.Collections.Generic.List<EffectLogEntry>();
            EffectReport costReport = EffectResolver.Execute(state, ActorSeat, card.SpecialActionCost, choices);
            EffectReport effectReport = EffectResolver.Execute(state, ActorSeat, card.SpecialActionEffects, choices);
            entries.AddRange(costReport.Entries);
            entries.AddRange(effectReport.Entries);
            Report = new EffectReport(ActorSeat, entries, false, null);

            state.ActiveSpecialActionPatternId = previousPatternId;

            // 标记下移 1 格（I 级 0→1；II 级 0→1、2→3），并记入本回合已用过的样式牌。
            state.PatternStateOf(PatternCardId)?.AdvanceMarker(ActorSeat);
            player.UsedPatternActionsThisTurn.Add(PatternCardId);
        }

        private static string CardName(CityPatternCardDef card) =>
            string.IsNullOrEmpty(card.Name) ? $"城市样式牌[{card.Id}]" : card.Name;
    }
}
