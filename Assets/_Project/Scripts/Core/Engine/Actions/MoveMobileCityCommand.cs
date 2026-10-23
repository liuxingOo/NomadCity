using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 主要行动 d·步 1–2：城市移动（§6.4d）。支付 3 源石，把移动城市移到相邻且已开放的资源点：
    /// 清掉所用航道与目标点上所有对手的影响力（退回各自供应堆）；目标点若还没有资源点指示物，
    /// 翻开对应颜色的事件牌补上（事件选项由随后的 <see cref="ResolveEventOptionCommand"/> 结算）。
    ///
    /// 它**不占行动名额** —— 三步里的最后一步「在出发点放置 1 个自己的影响力」由
    /// <see cref="MoveCityInfluenceCommand"/> 执行并占用名额，这样中间才有机会让玩家结算事件选项。
    /// 出发点记在 <see cref="GameState.CityMoveOriginNodeId"/>，也通过 <see cref="OriginNodeId"/> 暴露给界面。
    /// </summary>
    public sealed class MoveMobileCityCommand : ICommand
    {
        /// <summary>支付 3 源石（§6.4d 步 1 / §6.2）。</summary>
        public const int OriginiumCost = 3;

        public readonly int TargetNodeId;

        /// <summary>出发点（移动城市原来的所在地）的资源点 id；<see cref="Apply"/> 后才有值。</summary>
        public int OriginNodeId { get; private set; } = -1;

        public MoveMobileCityCommand(int seat, int targetNodeId)
        {
            ActorSeat = seat;
            TargetNodeId = targetNodeId;
        }

        public int ActorSeat { get; }
        public string Name => "城市移动";

        /// <summary>步 1–2 不占名额；步 3（<see cref="MoveCityInfluenceCommand"/>）才占。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            PlayerState player = state.Player(ActorSeat);
            int origin = player.MobileCityNodeId;
            if (origin < 0) return CommandResult.Fail("你的移动城市还没有停靠在任何资源点上。");
            if (TargetNodeId < 0 || TargetNodeId >= state.Map.Nodes.Count)
                return CommandResult.Fail($"目标资源点 {TargetNodeId} 不存在。");
            if (TargetNodeId == origin) return CommandResult.Fail("移动城市已经在目标资源点上。");

            if (!ActionRules.IgnoresCosts(state) && player.Resources.Originium < OriginiumCost)
                return CommandResult.Fail($"城市移动需要 {OriginiumCost} 源石。");

            if (state.Map.FindRoute(origin, TargetNodeId) == null)
                return CommandResult.Fail("目标资源点与移动城市所在地不相邻（需要仅通过一条航道相连）。");

            MapNodeState target = state.Map.Node(TargetNodeId);
            if (!ActionRules.IsNodeOpen(state, target))
                return CommandResult.Fail($"资源点 {target.Code} 属于红色区域，本回合尚未开放。");
            if (ActionRules.HasOpponentCity(state, TargetNodeId, ActorSeat))
                return CommandResult.Fail($"资源点 {target.Code} 上停靠着对手的移动城市。");

            if (player.MarkersInSupply <= 0) return CommandResult.Fail("没有玩家标记可在出发点放置影响力。");
            if (ActionRules.FirstFreeSlot(state.Map.Node(origin).Slots) < 0)
                return CommandResult.Fail("出发点没有空的影响力空格，无法在出发点放置影响力。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            int origin = player.MobileCityNodeId;

            if (!ActionRules.IgnoresCosts(state))
                player.Resources.TrySpend(ResourceType.Originium, OriginiumCost);

            // 清掉所用航道与目标点上所有对手的影响力。
            RouteState route = state.Map.FindRoute(origin, TargetNodeId);
            ActionRules.ClearRouteInfluencesOfOthers(state, route.Id, ActorSeat);
            ActionRules.ClearNodeInfluencesOfOthers(state, TargetNodeId, ActorSeat);

            player.MobileCityNodeId = TargetNodeId;
            state.LastCityMoveRouteId = route.Id; // 供「在经过的航道上放置影响力」取落点候选

            // 步 2 末尾（§6.4d）：目标点若还没有资源点指示物，翻开对应颜色的事件牌补上。
            // 事件选项由玩家随后的 ResolveEventOptionCommand 结算；步 3（出发点放影响力）
            // 由 MoveCityInfluenceCommand 执行 —— 三步走完才算这次城市移动结束。
            MapNodeState target = state.Map.Node(TargetNodeId);
            if (!target.HasResourceToken)
            {
                System.Collections.Generic.List<int> deck = ExploreCommand.DeckOf(state, target.Color);
                if (deck.Count > 0)
                {
                    int cardId = deck[0];
                    deck.RemoveAt(0);
                    EventCardDef card = state.Catalog.Event(cardId);

                    target.HasResourceToken = true;
                    target.TokenResource = card.TokenResource;
                    target.TokenAmount = card.TokenAmount;
                    target.EventCardId = cardId;
                    target.EventResolved = false; // 等玩家选事件选项
                }
            }

            OriginNodeId = origin;
            state.CityMoveOriginNodeId = origin; // 步 3 的落点
        }
    }

    /// <summary>
    /// 主要行动 d·步 3：在**出发点**放置 1 个自己的影响力（§6.4d 步 3）。
    /// 必须在移动完成、且目标点上若翻开了事件牌也已结算之后调用 —— 规则要求先翻牌选选项、再放影响力。
    /// 它是城市移动三步里的最后一步，因此由它占用本回合的主要行动名额。
    /// </summary>
    public sealed class MoveCityInfluenceCommand : ICommand
    {
        /// <summary>出发点（移动城市原来的所在地）的资源点 id。</summary>
        public readonly int OriginNodeId;

        public MoveCityInfluenceCommand(int seat, int originNodeId)
        {
            ActorSeat = seat;
            OriginNodeId = originNodeId;
        }

        public int ActorSeat { get; }
        public string Name => "城市移动·放置影响力";
        public bool ConsumesAction => true;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            if (state.CityMoveOriginNodeId < 0)
                return CommandResult.Fail("你还没有执行城市移动（§6.4d 步 1–2）。");
            if (OriginNodeId != state.CityMoveOriginNodeId)
                return CommandResult.Fail(
                    $"出发点应当是 {state.Map.Node(state.CityMoveOriginNodeId).Code}（移动城市原来的所在地）。");

            if (OriginNodeId < 0 || OriginNodeId >= state.Map.Nodes.Count)
                return CommandResult.Fail($"资源点 {OriginNodeId} 不存在。");

            // 目标点上刚翻开的事件牌要先结算（§6.4d 步 2 → 步 3）。
            PlayerState player = state.Player(ActorSeat);
            MapNodeState target = state.Map.Node(player.MobileCityNodeId);
            if (target.HasResourceToken && target.EventCardId >= 0 && !target.EventResolved)
                return CommandResult.Fail(
                    $"请先结算资源点 {target.Code} 的事件选项，再在出发点放置影响力（§6.4d）。");

            if (player.MarkersInSupply <= 0)
                return CommandResult.Fail("你的玩家供应堆里没有标记了，无法放置影响力。");

            MapNodeState origin = state.Map.Node(OriginNodeId);
            if (ActionRules.FirstFreeSlot(origin.Slots) < 0)
                return CommandResult.Fail($"出发点 {origin.Code} 没有空的影响力空格。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            MapNodeState origin = state.Map.Node(OriginNodeId);

            int slot = ActionRules.FirstFreeSlot(origin.Slots);
            origin.Slots.Place(slot, ActorSeat);
            state.Player(ActorSeat).MarkersInSupply--;

            state.CityMoveOriginNodeId = -1; // 三步走完，这次城市移动结清
        }
    }
}