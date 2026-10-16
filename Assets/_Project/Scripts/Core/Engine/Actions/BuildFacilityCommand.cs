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
    /// 41 张设施牌的牌面数据尚未录入（RuleSpec §13.1 待办），因此目前设施牌的费用 / 分数 /
    /// 入场效果都还是默认的 0 / 空 —— 本命令不依赖任何具体牌面数据，数据录入后自动生效。
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

        public BuildFacilityCommand(int seat, int facilityId, int panelSlot, FacilityPayment payment)
        {
            ActorSeat = seat;
            FacilityId = facilityId;
            PanelSlot = panelSlot;
            Payment = payment;
        }

        public int ActorSeat { get; }
        public string Name => "建设";
        public bool ConsumesAction => true;

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

            if (card.IsUnique && player.BuiltFacilities.Contains(FacilityId))
                return CommandResult.Fail($"「{card.Name}」带「唯一」关键词，你的城市面板上已经有一张了（§6.4e）。");

            return ValidatePayment(card, player);
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            FacilityCardDef card = state.Catalog.Facility(FacilityId);

            Pay(state, player, card);          // 步 1
            PlaceOnPanel(state, player, card); // 步 2
            Report = EffectResolver.Execute(state, ActorSeat, card.EntryEffects); // 步 3
        }

        /// <summary>步 1 校验：免建的牌直接过；其余必须明确选一种费用、并且付得起。</summary>
        private CommandResult ValidatePayment(FacilityCardDef card, PlayerState player)
        {
            if (IsFreeToBuild(card)) return CommandResult.Success;

            switch (Payment)
            {
                case FacilityPayment.Resources:
                    if (!card.CanPayWithResources(player.Resources))
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

        private void Pay(GameState state, PlayerState player, FacilityCardDef card)
        {
            if (IsFreeToBuild(card)) return;

            if (Payment == FacilityPayment.Resources)
            {
                foreach (RewardOp op in card.ResourceCost)
                    player.Resources.TrySpend(op.Resource, op.Amount);
            }
            else
            {
                player.Vouchers -= card.VoucherCost;
                state.SupplyVouchers += card.VoucherCost; // 与发放金券对称：付出去的金券回到供应堆
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
