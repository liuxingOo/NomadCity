using System.Collections.Generic;
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
    /// **数据现状**：6 张基础城市样式牌的宣告条件与奖励分数**已录入**（<c>CardCatalog.BuildCityPatterns</c>），
    /// 所以本命令可以直接宣告；「宣告效果」（<see cref="CityPatternCardDef.DeclareEffects"/>）字段尚未录入，
    /// 牌面若有该效果则需补录（RuleSpec §14 待核实）。条件未录入的牌会被直接拒绝，不会凭空造出规则。
    ///
    /// **组合方式**由玩家从所有合法摆法里挑一组（<see cref="CityPanelQuery.FindAll"/> 的顺序固定，
    /// 按 0°→90°→180°→270° 再从上到下、从左到右）；<see cref="PlacementIndex"/> 为 -1 时退回「取第一组」，
    /// 只用于测试与无界面场景。
    ///
    /// 这是**快速行动**，不占用本回合的主要行动名额（§6.1 / §6.3），因此 ConsumesAction = false。
    /// </summary>
    public sealed class DeclareCityPatternCommand : ICommand
    {
        /// <summary>要宣告的城市样式牌 id（必须是在场上的那 6 张之一）。</summary>
        public readonly int PatternCardId;

        /// <summary>
        /// 玩家挑的那一组摆法在 <see cref="CityPanelQuery.FindAll"/> 结果里的下标；
        /// -1 表示不挑、自动取第一组（引擎顺序固定，便于测试复现）。
        /// </summary>
        public readonly int PlacementIndex;

        /// <summary>本次实际用到的组合方式（用到的面板格子 + 旋转角度）；<see cref="Apply"/> 之后才有值。</summary>
        public PatternMatch Match { get; private set; }

        /// <summary>宣告效果的结算报告；<see cref="Apply"/> 之后才有值。</summary>
        public EffectReport Report { get; private set; }

        public DeclareCityPatternCommand(int seat, int patternCardId, int placementIndex = -1)
        {
            ActorSeat = seat;
            PatternCardId = patternCardId;
            PlacementIndex = placementIndex;
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
                return CommandResult.Fail($"《{card}》的宣告条件还没录入，暂时无法宣告。");

            PlayerState player = state.Player(ActorSeat);
            if (player.MarkersInSupply <= 0)
                return CommandResult.Fail("玩家供应堆里没有玩家标记，无法宣告城市样式（§10.2 步 2）。");

            List<PatternMatch> matches = CityPanelQuery.FindAll(player, state.Catalog, card.Condition);
            if (matches.Count == 0)
            {
                // 只印牌名：card 的 ToString() 自带等级与条件，再拼一次 Condition 会把条件印两遍。
                string name = string.IsNullOrEmpty(card.Name) ? $"城市样式牌[{card.Id}]" : card.Name;
                return CommandResult.Fail($"你的城市面板凑不出《{name}》的宣告条件（{card.Condition}）。");
            }

            if (!TryPickMatch(matches, out _))
                return CommandResult.Fail("你选的这组摆法已经不成立了，请重新选一组。");

            return CommandResult.Success;
        }

        /// <summary>
        /// 从所有合法摆法里挑出本次要用的一组：指定了下标就用它（越界即失败），未指定（-1）就取第一组。
        /// 「挑哪几格」是玩家的权利（§6.5b 步 1 同一设施只能用一次，用过的格子就不再参与后续宣告），
        /// 所以界面正常玩法都会把候选摆法列给玩家选；自动取第一组只用于测试与无界面场景。
        /// </summary>
        private bool TryPickMatch(List<PatternMatch> matches, out PatternMatch match)
        {
            match = default;
            if (matches == null || matches.Count == 0) return false;

            int index = PlacementIndex < 0 ? 0 : PlacementIndex;
            if (index >= matches.Count) return false;

            match = matches[index];
            return true;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            CityPatternCardDef card = state.Catalog.CityPattern(PatternCardId);

            // 与 Validate 同源、同样确定性的匹配结果（校验只读，这一段不会改变它）。
            List<PatternMatch> matches = CityPanelQuery.FindAll(player, state.Catalog, card.Condition);
            if (!TryPickMatch(matches, out PatternMatch match)) return; // 正常到不了这里（Validate 已挡过）
            Match = match;

            // 步 1：把用到的设施格标记为「已用于宣告」（§6.5b 步 1）。
            foreach (int slot in Match.Slots) player.MarkPanelSlotUsed(slot);

            // 步 2：从供应堆拿 1 个玩家标记放到这张样式牌上，并把标记记进样式牌的运行时状态。
            // 格位固定放起点格 0：I 级 = 「未使用」区，II 级 = 次数上限「2」区（§6.5b 步 2 第一次的情形）。
            // 「其他情况」（同一玩家反复宣告、II 级推进格位）要等特殊行动接入后再细化。
            player.MarkersInSupply--;
            state.PatternStateOf(PatternCardId)?.PlaceMarker(ActorSeat, 0);

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

    }
}
