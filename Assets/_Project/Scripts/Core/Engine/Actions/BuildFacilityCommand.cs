using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 主要行动「建设」（RuleSpec §6.4e），三步一次做完：
    /// 1) 支付费用 —— 按牌面的资源费用或金券费用二选一支付（带「免费」关键词则跳过）；
    /// 2) 把设施牌放到自己城市面板的一个空位上，并**立即**结算牌面奖励分数；
    /// 3) 结算入场效果（§11 通用效果执行器，不为某张牌硬编码）。
    /// 做完后从设施牌堆补牌，让设施供应区恢复 6 张。
    ///
    /// 41 张普通设施牌的牌面数据已录入（见 `Doc/FacilityCards.md`）：费用 / 分数 / 关键词 / 入场效果
    /// 都已生效 —— 含「获得 N 资源」「放置影响力」「`or` 二选一」「拿走／移动／替换影响力」，
    /// 以及以单价售卖资源、结算他牌入场效果、传递起始标记、图标型效果等专用效果节点。
    /// </summary>
    public sealed class BuildFacilityCommand : ICommand
    {
        /// <summary>要建设的设施牌 id（必须来自设施供应区）。</summary>
        public readonly int FacilityId;

        /// <summary>放牌的城市面板格子下标（0–11；核心指挥塔那一格不能放）。</summary>
        public readonly int PanelSlot;

        /// <summary>这次建设选用的付费方式。</summary>
        public readonly FacilityPayment Payment;

        /// <summary>第三步入场效果的结算报告；Apply 之后才有值。</summary>
        public EffectReport Report { get; private set; }

        /// <summary>入场效果里需要玩家点选时由它回答；不传则用「总是取第一个合法项」的自动结算。</summary>
        private readonly IEffectChoices choices;

        public BuildFacilityCommand(int seat, int facilityId, int panelSlot, FacilityPayment payment,
            IEffectChoices choices = null)
        {
            ActorSeat = seat;
            FacilityId = facilityId;
            PanelSlot = panelSlot;
            Payment = payment;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => "建设";
        public bool ConsumesAction => true;

        /// <summary>入场效果的答案来源（联机编码时要从这里取答案，见 <c>CommandCodec</c>）。</summary>
        public IEffectChoices EffectChoices => choices;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            PlayerState player = state.Player(ActorSeat);

            FacilityCardDef card = state.Catalog.Facility(FacilityId);
            if (card == null) return CommandResult.Fail($"设施牌 {FacilityId} 不存在。");

            // 延伸枢纽是「物流枢纽」入场效果换出来的，不是从供应区直接拿的（§4.1）。
            if (card.Kind == FacilityKind.ExtensionHub)
                return CommandResult.Fail("延伸枢纽要通过「物流枢纽」的入场效果获得，这条流程还没做。");

            if (card.Kind != FacilityKind.Standard)
                return CommandResult.Fail($"{card.Name} 不是可以建设的设施牌。");

            if (!state.FacilitySupply.Contains(FacilityId))
                return CommandResult.Fail($"{card.Name} 不在设施供应区里，不能建设。");

            if (!player.HasFreePanelSlot)
                return CommandResult.Fail("城市面板已经放满了，没有空位可以建设（§6.4e 步 2）。");

            if (PanelSlot < 0 || PanelSlot >= PlayerState.CityPanelSlotCount)
                return CommandResult.Fail($"城市面板只有 {PlayerState.CityPanelSlotCount} 个格子，选的位置超出范围。");

            if (PanelSlot == PlayerState.CityPanelCoreSlot)
                return CommandResult.Fail("那一格是核心指挥塔的位置，不能建设。");

            if (player.CityPanelSlots[PanelSlot] >= 0)
                return CommandResult.Fail($"城市面板第 {PanelSlot + 1} 格已经有设施牌了，换一个空位。");

            if (card.IsUnique && HasSameName(state, player, card))
                return CommandResult.Fail($"「{card.Name}」带「唯一」关键词，你的城市面板上已经有一张同名设施了（§6.4e）。");

            return ValidatePayment(state, card, player);
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            FacilityCardDef card = state.Catalog.Facility(FacilityId);

            Pay(state, player, card);          // 步 1
            PlaceOnPanel(state, player, card); // 步 2
            Report = EffectResolver.Execute(state, ActorSeat, card.EntryEffects, choices); // 步 3
        }

        /// <summary>
        /// 「唯一」（§6.4e）：同一玩家的城市面板上，**同名牌**只能有 1 张。
        /// 按牌名而非牌 id 判定 —— 同名异色的设施（如三种颜色的「源石精炼厂」）也算同名。
        /// </summary>
        private static bool HasSameName(GameState state, PlayerState player, FacilityCardDef card)
        {
            foreach (int builtId in player.BuiltFacilities)
            {
                FacilityCardDef built = state.Catalog.Facility(builtId);
                if (built != null && built.Name == card.Name) return true;
            }
            return false;
        }

        /// <summary>步 1 校验：免建的牌直接过；其余必须明确选一种费用、并且付得起。</summary>
        private CommandResult ValidatePayment(GameState state, FacilityCardDef card, PlayerState player)
        {
            if (IsFreeToBuild(card)) return CommandResult.Success;
            if (ActionRules.IgnoresCosts(state)) return CommandResult.Success; // 开发者模式：不查费用

            switch (Payment)
            {
                case FacilityPayment.Resources:
                    if (!FacilityCardDef.CanPayWithResources(
                            EffectiveResourceCost(state, ActorSeat, card), player.Resources, player.Vouchers))
                        return CommandResult.Fail($"资源不足以支付「{card.Name}」的资源费用。");
                    return CommandResult.Success;

                case FacilityPayment.Vouchers:
                    if (player.Vouchers < card.VoucherCost)
                        return CommandResult.Fail($"金券不足以支付「{card.Name}」的金券费用（需要 {card.VoucherCost}）。");
                    return CommandResult.Success;

                default:
                    return CommandResult.Fail("请选择用「资源费用」还是「金券费用」支付（§6.4e 步 1）。");
            }
        }

        /// <summary>这张牌是否免付费：带「免费」关键词，或两个费用都是 0。</summary>
        private static bool IsFreeToBuild(FacilityCardDef card) =>
            card.IsFree || (card.ResourceCost.Count == 0 && card.VoucherCost == 0);

        /// <summary>
        /// 把「建筑费用修正」折算进资源费用列：按 <see cref="FacilityCostModifier.Source"/> 数出的数量 ×
        /// <see cref="FacilityCostModifier.PerCount"/>，从费用列里**对应的那一份资源**上扣减（下限 0，只扣一次）。
        /// 只影响资源费用组，金券费用（<see cref="FacilityPayment.Vouchers"/> 那一组）不受影响。
        /// 例：「城邦工业区」每有 1 种非彩色设施少付 1 源岩。
        /// 公开给界面用：建设第一步显示的「选项一 · 资源」要写真实费用，不能写没折算过的原价。
        /// </summary>
        public static IReadOnlyList<RewardOp> EffectiveResourceCost(GameState state, int seat, FacilityCardDef card)
        {
            if (card.CostModifier == null) return card.ResourceCost;

            int discount = CityPanelQuery.Count(state, seat, card.CostModifier.Source) * card.CostModifier.PerCount;
            if (discount <= 0) return card.ResourceCost;

            var cost = new List<RewardOp>(card.ResourceCost.Count);
            bool discounted = false;
            foreach (RewardOp op in card.ResourceCost)
            {
                if (!discounted && op.Kind == RewardKind.PayResource && op.Resource == card.CostModifier.Resource)
                {
                    cost.Add(RewardOp.Pay(op.Resource, Math.Max(0, op.Amount - discount)));
                    discounted = true;
                    continue;
                }
                cost.Add(op);
            }
            return cost;
        }

        private void Pay(GameState state, PlayerState player, FacilityCardDef card)
        {
            if (IsFreeToBuild(card)) return;
            if (ActionRules.IgnoresCosts(state)) return; // 开发者模式：不扣费

            if (Payment == FacilityPayment.Resources)
            {
                foreach (RewardOp op in EffectiveResourceCost(state, ActorSeat, card))
                {
                    if (op.Kind == RewardKind.PayVouchers)
                    {
                        // 资源费用列里可能混有金券成分（如「城邦工业区」的 金券2）
                        player.Vouchers -= op.Amount;
                    }
                    else
                    {
                        player.Resources.TrySpend(op.Resource, op.Amount);
                    }
                }
            }
            else
            {
                player.Vouchers -= card.VoucherCost;
            }
        }

        /// <summary>步 2：从供应区拿走这张牌（并补牌），放到面板空格上，结算牌面分数。</summary>
        private void PlaceOnPanel(GameState state, PlayerState player, FacilityCardDef card)
        {
            state.FacilitySupply.Remove(FacilityId);
            BoardSetup.RefillFacilitySupply(state);

            player.CityPanelSlots[PanelSlot] = FacilityId;
            player.BuiltFacilities.Add(FacilityId);
            player.Score += card.Score;
        }
    }
}
