using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 主要行动 d：城市移动（§6.4d）。支付 3 源石，把移动城市移到相邻且已开放的资源点：
    /// 清掉所用航道与目标点上所有对手的影响力（退回各自供应堆），并在**出发点**放置 1 个自己的影响力。
    /// 目标点若没有资源点指示物，规则要求翻事件牌补上 —— 那依赖事件牌内容，待 §11 效果系统就绪后再补。
    /// </summary>
    public sealed class MoveMobileCityCommand : ICommand
    {
        /// <summary>支付 3 源石（§6.4d 步 1 / §6.2）。</summary>
        public const int OriginiumCost = 3;

        public readonly int TargetNodeId;

        public MoveMobileCityCommand(int seat, int targetNodeId)
        {
            ActorSeat = seat;
            TargetNodeId = targetNodeId;
        }

        public int ActorSeat { get; }
        public string Name => "城市移动";
        public bool ConsumesAction => true;

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

            // 在出发点放置 1 个自己的影响力。
            int slot = ActionRules.FirstFreeSlot(state.Map.Node(origin).Slots);
            state.Map.Node(origin).Slots.Place(slot, ActorSeat);
            player.MarkersInSupply--;
        }
    }
}