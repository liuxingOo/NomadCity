using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 「角色牌盖放」（§4.2 阶段 1 / §10.1.1）：从自己手牌里选 1 张背面朝上放到面前。
    /// 手牌为空时先把自己弃牌区的角色牌全部回收再盖放。这是回合开头的准备动作，不占主要行动名额。
    /// </summary>
    public sealed class PlaceCharacterCardCommand : ICommand
    {
        /// <summary>要盖放的角色牌 id（手牌为空时也可以是弃牌区里的牌）。</summary>
        public readonly int CardId;

        public PlaceCharacterCardCommand(int seat, int cardId)
        {
            ActorSeat = seat;
            CardId = cardId;
        }

        public int ActorSeat { get; }
        public string Name => "盖放角色牌";

        /// <summary>回合开头的准备动作，不占主要行动名额（§6.1）。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Phase != TurnPhase.CharacterPlacement)
                return CommandResult.Fail("盖放角色牌只能在回合的「角色牌盖放」阶段进行（§4.2 阶段 1）。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            PlayerState player = state.Player(ActorSeat);
            if (player.PlacedCharacterCard >= 0)
                return CommandResult.Fail("你本回合已经盖放过角色牌了（§10.1.1）。");

            // 2 人局（§12 变体 4）：盖放前要先弃完设施牌，先弃后盖。
            if (state.SupplyDiscardRemaining > 0)
                return CommandResult.Fail(
                    "2 人局盖放角色牌前，每名玩家要先从设施供应区弃 1 张设施牌（§12 变体 4）。");

            // 企业扩展：开局入场还要从 7 张手牌里选 2 张放回游戏盒（EnterpriseExpansion.md §4.2）。
            if (!player.SetupDiscardDone)
                return CommandResult.Fail("请先从 7 张手牌里选 2 张放回游戏盒（企业扩展开局入场）。");

            // 企业扩展：红区开放回合要先选 1 家企业提升 1 级，再开始盖放（§3.2）。
            if (state.Enterprises != null && state.Enterprises.RedZoneAdvanceSeat == ActorSeat)
                return CommandResult.Fail("红色区域已开放：请先点「企业」按钮，选择 1 家企业把自己的合作等级提升 1 级。");

            // 手牌空时按规则先回收弃牌区，所以候选池是「手牌 ∪ 弃牌区」。
            IReadOnlyList<int> pool = player.HandCharacterCards.Count > 0
                ? player.HandCharacterCards
                : player.DiscardCharacterCards;
            if (pool.Count == 0) return CommandResult.Fail("你没有任何角色牌可以盖放。");
            if (!Contains(pool, CardId)) return CommandResult.Fail($"角色牌 {CardId} 不在你的手牌里。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);

            // §4.2 阶段 1 / §10.1.1：手牌为空时，先把自己弃牌区的角色牌全部回收再盖放。
            if (player.HandCharacterCards.Count == 0 && player.DiscardCharacterCards.Count > 0)
            {
                player.HandCharacterCards.AddRange(player.DiscardCharacterCards);
                player.DiscardCharacterCards.Clear();
            }

            player.HandCharacterCards.Remove(CardId);
            player.PlacedCharacterCard = CardId;
            player.UsedCharacterCardThisTurn = false;
            player.UsedCharacterStrategyThisTurn = false;
            player.UsedCharacterTacticsThisTurn = false;
            player.CharacterSecondEffectPending = false;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 「使用角色牌」（快速行动 §6.3a / §10.1.2）：发动本回合已盖放角色牌上的「策略」或「计谋」。
    /// 每人每回合至多使用一次这张牌；**起始玩家**可以连续发动策略与计谋两种效果（本命令共调用两次，
    /// 中间用 <see cref="EndCharacterCardUseCommand"/> 结束），非起始玩家二选一。
    /// 用完后这张牌正面朝上移入自己的角色牌弃牌区。
    /// </summary>
    public sealed class UseCharacterCardCommand : ICommand
    {
        /// <summary>这次要发动的是策略还是计谋。</summary>
        public readonly CharacterEffectKind Effect;

        /// <summary>本次效果的结算报告；Apply 之后才有值。</summary>
        public EffectReport Report { get; private set; }

        /// <summary>效果里需要玩家点选时由它回答；不传则用「总是取第一个合法项」的自动结算。</summary>
        private readonly IEffectChoices choices;

        public UseCharacterCardCommand(int seat, CharacterEffectKind effect, IEffectChoices choices = null)
        {
            ActorSeat = seat;
            Effect = effect;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => Effect == CharacterEffectKind.Tactics ? "使用角色牌（计谋）" : "使用角色牌（策略）";

        /// <summary>效果答案来源（联机编码时要从这里取答案，见 <c>CommandCodec</c>）。</summary>
        public IEffectChoices EffectChoices => choices;

        /// <summary>快速行动，不占主要行动名额（§6.3a）。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            // 用 ValidateActiveSeat（不看连用中途），否则自己在连用第二步时会被自己的闸门挡住。
            CommandResult turn = ActionRules.ValidateActiveSeat(state, ActorSeat);
            if (!turn.Ok) return turn;

            // §6.3f：本轮用过城市样式特殊行动就不能再用角色牌（源石工业中枢 / 高效移动管理体系）。
            CommandResult banned = ActionRules.ValidateCharacterCardAllowed(state, ActorSeat);
            if (!banned.Ok) return banned;

            PlayerState player = state.Player(ActorSeat);
            if (player.PlacedCharacterCard < 0)
                return CommandResult.Fail("你必须先在本回合盖放一张角色牌，才能使用它（§10.1.1）。");
            if (player.UsedCharacterCardThisTurn)
                return CommandResult.Fail("本回合已经使用过这张角色牌了（§6.5a）。");
            if (Effect == CharacterEffectKind.Strategy && player.UsedCharacterStrategyThisTurn)
                return CommandResult.Fail("本回合已经用掉这张牌的「策略」了。");
            if (Effect == CharacterEffectKind.Tactics && player.UsedCharacterTacticsThisTurn)
                return CommandResult.Fail("本回合已经用掉这张牌的「计谋」了。");

            // 非起始玩家每回合只能发动两种效果中的一种（§6.5a）。
            bool startPlayer = state.FirstPlayerSeat == ActorSeat;
            if (!startPlayer && (player.UsedCharacterStrategyThisTurn || player.UsedCharacterTacticsThisTurn))
                return CommandResult.Fail("只有起始玩家才能连续发动策略与计谋（§6.5a）；你本回合已经用掉了一种效果。");

            CharacterCardDef def = state.Catalog.Character(player.PlacedCharacterCard);
            if (def == null || !def.HasEffects)
                return CommandResult.Fail($"「{def?.Name}」的策略 / 计谋效果还没有录入。");
            if (def.OpsOf(Effect).Count == 0)
                return CommandResult.Fail($"「{def.NameOf(Effect)}」还没有录入效果。");

            // 计谋可能要「先支付 N 金券」（如山·商业网络）：付不起不能发动。
            int voucherCost = 0;
            foreach (RewardOp op in def.OpsOf(Effect))
            {
                if (op.Kind == RewardKind.PayVouchers) voucherCost += op.Amount;
            }
            if (voucherCost > 0 && !ActionRules.IgnoresCosts(state) && player.Vouchers < voucherCost)
                return CommandResult.Fail($"发动「{def.NameOf(Effect)}」需要先支付 {voucherCost} 金券。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            CharacterCardDef def = state.Catalog.Character(player.PlacedCharacterCard);

            // 企业角色牌（山 / 玛恩纳）的效果由企业扩展执行器处理。
            Report = EffectResolver.Execute(state, ActorSeat, def.OpsOf(Effect), choices,
                EnterpriseEffectExtension.Instance);

            if (Effect == CharacterEffectKind.Strategy) player.UsedCharacterStrategyThisTurn = true;
            else player.UsedCharacterTacticsThisTurn = true;

            // 起始玩家还有另一种效果没用 → 先留住这张牌，等玩家决定「继续用另一种」或「结束使用」（§10.1.2）。
            bool otherLeft = state.FirstPlayerSeat == ActorSeat
                             && (Effect == CharacterEffectKind.Strategy
                                 ? !player.UsedCharacterTacticsThisTurn
                                 : !player.UsedCharacterStrategyThisTurn);
            if (otherLeft)
            {
                player.CharacterSecondEffectPending = true;
                return;
            }

            DiscardPlacedCard(player);
        }

        /// <summary>把盖放的角色牌移入弃牌区，并标记本回合已使用（§10.1.3）。</summary>
        internal static void DiscardPlacedCard(PlayerState player)
        {
            if (player.PlacedCharacterCard >= 0)
            {
                player.DiscardCharacterCards.Add(player.PlacedCharacterCard);
                player.PlacedCharacterCard = -1;
            }
            player.UsedCharacterCardThisTurn = true;
            player.CharacterSecondEffectPending = false;
        }
    }

    /// <summary>
    /// 「结束角色牌使用」：起始玩家用掉两种效果之一后，选择不再发动另一种，直接把这张牌移入弃牌区（§10.1.2）。
    /// </summary>
    public sealed class EndCharacterCardUseCommand : ICommand
    {
        public EndCharacterCardUseCommand(int seat)
        {
            ActorSeat = seat;
        }

        public int ActorSeat { get; }
        public string Name => "结束角色牌使用";

        /// <summary>只是收尾，不占主要行动名额。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActiveSeat(state, ActorSeat);
            if (!turn.Ok) return turn;

            PlayerState player = state.Player(ActorSeat);
            if (!player.CharacterSecondEffectPending)
                return CommandResult.Fail("你现在没有「先用掉一种效果、等待决定是否继续」的角色牌使用。");

            return CommandResult.Success;
        }

        public void Apply(GameState state) => UseCharacterCardCommand.DiscardPlacedCard(state.Player(ActorSeat));
    }

    /// <summary>
    /// 企业扩展·开局入场：从 7 张手牌里选 2 张放回游戏盒（EnterpriseExpansion.md §4.2）。
    /// 必须在开局入场（决定初始资源点 + 结算事件牌）走完之后、本人盖放角色牌之前完成；
    /// 放回游戏盒的牌直接移出本局，不进弃牌区。
    /// </summary>
    public sealed class DiscardSetupHandCardsCommand : ICommand
    {
        public readonly int CardA;
        public readonly int CardB;

        public DiscardSetupHandCardsCommand(int seat, int cardA, int cardB)
        {
            ActorSeat = seat;
            CardA = cardA;
            CardB = cardB;
        }

        public int ActorSeat { get; }
        public string Name => "开局弃 2 张角色牌";

        /// <summary>开局设置，不占主要行动名额。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (!state.Setup.IsEnterpriseEnabled)
                return CommandResult.Fail("本局没有启用企业扩展，开局手牌是 5 张，不需要弃牌。");
            if (state.TurnNumber != 1 || state.Phase != TurnPhase.CharacterPlacement)
                return CommandResult.Fail("开局弃牌只能在第 1 回合的角色牌盖放阶段进行。");

            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            PlayerState player = state.Player(ActorSeat);
            if (player.SetupDiscardDone)
                return CommandResult.Fail("你本局已经完成开局弃牌了。");

            // 开局入场「7 选 5」属于开局设置，排在 2 人局弃设施牌之前，所以这里不看 SupplyDiscardRemaining。
            if (ChooseStartNodeCommand.CurrentOperator(state) >= 0)
                return CommandResult.Fail("请先完成开局入场（决定初始资源点并结算事件牌）。");

            if (CardA == CardB)
                return CommandResult.Fail("要放回游戏盒的必须是两张不同的角色牌。");
            if (!Contains(player.HandCharacterCards, CardA) || !Contains(player.HandCharacterCards, CardB))
                return CommandResult.Fail("选中的角色牌不在你的手牌里。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            player.HandCharacterCards.Remove(CardA);
            player.HandCharacterCards.Remove(CardB);
            player.SetupDiscardDone = true;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }
}
