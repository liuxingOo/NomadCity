using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 快速行动「宣告城市样式」（§6.3b / §6.5b / §10.2），一次做完三步：
    /// 1) 用已建设的设施凑出这张样式牌的宣告条件（位置 + 颜色，可旋转、不可翻转，§10.2 步 3）；
    ///    用到的设施格随即标记为「已用于宣告」（§6.5b 步 1：同一设施只能用一次）；
    /// 2) 从玩家供应堆拿 1 个玩家标记，放到这张样式牌上（§6.5b 步 2 / §10.2 步 2）；
    /// 3) 结算牌面奖励分数与宣告效果（§6.5b 步 3 / §9.1）。
    ///
    /// **骨架说明**：6 张城市样式牌的牌面数据（宣告条件 / 奖励分数 / 宣告效果）尚未录入
    /// （RuleSpec §14 待核实）。本命令是数据驱动的空壳，卡面录入后自动生效；
    /// 宣告条件未录入的牌会被直接拒绝，不会凭空造出规则。
    ///
    /// **组合方式**由引擎自动取**第一组合法摆法**（<see cref="CityPanelQuery.TryFind"/> 的确定性结果，
    /// 按 0°→90°→180°→270° 顺序），玩家不需要在面板上点选具体用哪几格。
    ///
    /// 这是**快速行动**，不占用本回合的主要行动名额（§6.1 / §6.3），因此 ConsumesAction = false。
    /// </summary>
    public sealed class DeclareCityPatternCommand : ICommand
    {
        /// <summary>要宣告的城市样式牌 id（必须是在场上的那 6 张之一）。</summary>
        public readonly int PatternCardId;

        /// <summary>引擎自动选中的组合方式（用到的面板格子 + 旋转角度）；<see cref="Apply"/> 之后才有值。</summary>
        public PatternMatch Match { get; private set; }

        /// <summary>宣告效果的结算报告；<see cref="Apply"/> 之后才有值。</summary>
        public EffectReport Report { get; private set; }

        public DeclareCityPatternCommand(int seat, int patternCardId)
        {
            ActorSeat = seat;
            PatternCardId = patternCardId;
        }

        public int ActorSeat { get; }
        public string Name => "宣告城市样式";

        /// <summary>快速行动，不占主要行动名额（§6.3）。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            // 先查「在不在场上」再取定义：目录对越界 id 会抛异常，不能直接拿来判空。
            if (!IsOnTable(state, PatternCardId))
                return CommandResult.Fail("这张城市样式牌不在场上（§4.1 步 3 最多放出 6 张）。");

            CityPatternCardDef card = state.Catalog.CityPattern(PatternCardId);
            if (!card.HasCondition)
                return CommandResult.Fail($"《{card}》的宣告条件还没录入（骨架阶段），暂时无法宣告。");

            PlayerState player = state.Player(ActorSeat);
            if (player.MarkersInSupply <= 0)
                return CommandResult.Fail("玩家供应堆里没有玩家标记，无法宣告城市样式（§10.2 步 2）。");

            if (!CityPanelQuery.TryFind(player, state.Catalog, card.Condition, out _))
            {
                // 只印牌名：card 的 ToString() 自带等级与条件，再拼一次 Condition 会把条件印两遍。
                string name = string.IsNullOrEmpty(card.Name) ? $"城市样式牌[{card.Id}]" : card.Name;
                return CommandResult.Fail($"你的城市面板凑不出《{name}》的宣告条件（{card.Condition}）。");
            }

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            CityPatternCardDef card = state.Catalog.CityPattern(PatternCardId);

            // 与 Validate 同源、同样确定性的匹配结果（校验只读，这一段不会改变它）。
            CityPanelQuery.TryFind(player, state.Catalog, card.Condition, out PatternMatch match);
            Match = match;

            // 步 1：把用到的设施格标记为「已用于宣告」（§6.5b 步 1）。
            foreach (int slot in Match.Slots) player.MarkPanelSlotUsed(slot);

            // 步 2：从供应堆拿 1 个玩家标记放到这张样式牌上，并把标记记进样式牌的运行时状态。
            // 格位固定放起点格 0：I 级 = 「未使用」区，II 级 = 次数上限「2」区（§6.5b 步 2 第一次的情形）。
            // 「其他情况」（同一玩家反复宣告、II 级推进格位）要等特殊行动接入后再细化。
            player.MarkersInSupply--;
            PatternStateOf(state, PatternCardId)?.PlaceMarker(ActorSeat, 0);

            // 步 3：结算奖励分数与宣告效果（§6.5b 步 3）。
            player.Score += card.Score;
            Report = EffectResolver.Execute(state, ActorSeat, card.DeclareEffects);
        }

        /// <summary>这张样式牌是不是「场上那 6 张」之一。</summary>
        private static bool IsOnTable(GameState state, int cardId)
        {
            foreach (int id in state.CityPatternCards)
                if (id == cardId) return true;
            return false;
        }

        /// <summary>取这张样式牌的运行时状态（下标与 CityPatternCards 对应）；找不到为 null。</summary>
        private static CityPatternState PatternStateOf(GameState state, int cardId)
        {
            for (int i = 0; i < state.CityPatternCards.Count; i++)
                if (state.CityPatternCards[i] == cardId) return state.CityPatternStates[i];
            return null;
        }
    }
}
