using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 主要行动 c：探索（§6.4c）。规则把一次探索拆成三步按顺序执行：
    /// 1. 支付路费（<see cref="ExploreCommand"/>）；
    /// 2. 翻开对应颜色的事件牌、放上资源点指示物、选一个选项领奖励
    ///    （翻牌在前半段的 <see cref="ExploreCommand"/> 里，选项由 <see cref="ResolveEventOptionCommand"/> 结算）；
    /// 3. 在目标资源点放置 1 个自己的影响力（<see cref="ExploreInfluenceCommand"/>）。
    ///
    /// 拆成两条命令的原因：第 2 步必须先让玩家看到卡面才能选选项，而命令通道是「一次校验、一次执行」，
    /// 中间插不进玩家的选择。所以引擎只管翻牌，选项结算与之后的影响力放置由界面（或 AI）依次派发。
    ///
    /// 主要行动名额在第 3 步才消耗 —— 三步全部走完，这个主要行动才算完成（§6.1）。
    /// 「从移动城市所在地出发」这一条决定了路费：起点取玩家移动城市停靠的资源点（§6.4c 步 1）。
    /// </summary>
    public sealed class ExploreCommand : ICommand
    {
        /// <summary>被探索的目标资源点 id。</summary>
        public readonly int NodeId;

        /// <summary>
        /// 玩家在地图上自己点选的一组航道；为空则用引擎给的默认最省方案
        /// （<see cref="TollCalculator.FindCheapestPath"/>）。自选路线只要求把起点与目标点连通，多绕几跳就多付钱。
        /// </summary>
        public readonly IReadOnlyList<int> SelectedRouteIds;

        /// <summary>
        /// 玩家对「付给谁」的选择（航道 id → 座位号，§6.6）：某条航道上有多位对手时用得上。
        /// 缺项或选得不合法时退回该航道上第一位对手。
        /// </summary>
        public readonly IReadOnlyDictionary<int, int> Payees;

        public ExploreCommand(int seat, int nodeId,
            IReadOnlyList<int> routeIds = null, IReadOnlyDictionary<int, int> payees = null)
        {
            ActorSeat = seat;
            NodeId = nodeId;
            SelectedRouteIds = routeIds;
            Payees = payees;
        }

        public int ActorSeat { get; }
        public string Name => "探索";

        /// <summary>探索要到第 3 步（放置影响力）走完才占用主要行动名额。</summary>
        public bool ConsumesAction => false;

        /// <summary>本次探索实际经过的航道（自选优先，否则默认最省方案）。</summary>
        public IReadOnlyList<int> RouteIds { get; private set; }

        /// <summary>本次探索支付的路费拆解：付了多少、每条航道付给谁。</summary>
        public TollCalculator.Toll Toll { get; private set; }

        /// <summary>
        /// 本次探索要走的航道：玩家自选了就用自选的那组（去重后，同一航道只计一次），
        /// 否则走默认最省方案。界面预览路费时也用这一个入口，保证与派发时一致。
        /// </summary>
        public IReadOnlyList<int> PlanRoutes(GameState state)
        {
            if (SelectedRouteIds != null && SelectedRouteIds.Count > 0)
                return TollCalculator.DistinctRoutes(SelectedRouteIds);

            return TollCalculator.FindCheapestPath(
                state, ActorSeat, state.Player(ActorSeat).MobileCityNodeId, NodeId);
        }

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            PlayerState player = state.Player(ActorSeat);
            if (NodeId < 0 || NodeId >= state.Map.Nodes.Count)
                return CommandResult.Fail($"资源点 {NodeId} 不存在。");

            MapNodeState node = state.Map.Node(NodeId);

            // §6.4c 步 1：目标点必须是「没有资源点指示物且已开放探索」的资源点。
            if (node.HasResourceToken)
                return CommandResult.Fail($"资源点 {node.Code} 上已经有资源点指示物了，不需要探索。");
            if (!ActionRules.IsNodeOpen(state, node))
                return CommandResult.Fail($"资源点 {node.Code} 属于红色区域，本回合尚未开放探索。");
            if (ActionRules.HasOpponentCity(state, NodeId, ActorSeat))
                return CommandResult.Fail($"资源点 {node.Code} 上停靠着对手的移动城市。");

            if (DeckOf(state, node.Color).Count == 0)
                return CommandResult.Fail($"{ColorName(node.Color)}事件牌堆已空，无法翻开事件牌。");

            // 步 2 与步 3 的前置：要放指示物、也要在目标点放得下影响力。
            if (player.MarkersInSupply <= 0)
                return CommandResult.Fail("你的玩家供应堆里没有标记了，无法在目标点放置影响力（§11.3）。");
            if (ActionRules.FirstFreeSlot(node.Slots) < 0)
                return CommandResult.Fail($"资源点 {node.Code} 没有空的影响力空格，无法在目标点放置影响力。");

            // 路费：从移动城市所在地出发。
            if (player.MobileCityNodeId < 0)
                return CommandResult.Fail("你的移动城市还没有停靠在任何资源点上，无法计算路费。");

            // 自选路线的合法性（§6.6 步 1）：航道必须存在，且确实能把起点与目标点连起来。
            if (SelectedRouteIds != null && SelectedRouteIds.Count > 0)
            {
                foreach (int routeId in SelectedRouteIds)
                {
                    if (routeId < 0 || routeId >= state.Map.Routes.Count)
                        return CommandResult.Fail($"航道 {routeId} 不存在。");
                }
                if (!TollCalculator.RoutesConnect(state.Map, SelectedRouteIds, player.MobileCityNodeId, NodeId))
                    return CommandResult.Fail($"选中的航道不能把移动城市所在地与 {node.Code} 连起来。");
            }

            IReadOnlyList<int> path = PlanRoutes(state);
            if (path == null || path.Count == 0)
                return CommandResult.Fail($"从移动城市所在地到 {node.Code} 没有通路。");

            // 「付给谁」的合法性（§6.6）：指定的收款方必须是该航道上的对手。
            if (Payees != null)
            {
                foreach (KeyValuePair<int, int> entry in Payees)
                {
                    if (!ContainsRoute(path, entry.Key)) continue; // 不在本次路线里的选择忽略
                    if (!TollCalculator.IsOpponentOnRoute(state, entry.Key, ActorSeat, entry.Value))
                        return CommandResult.Fail(
                            $"航道 {state.Map.RouteLabel(entry.Key)} 上没有 {SeatText(entry.Value)} 的影响力，不能付给他" +
                            $"（这条航道上现在有影响力的是：{OwnersText(state, entry.Key, ActorSeat)}）。");
                }
            }

            if (!ActionRules.IgnoresCosts(state))
            {
                int total = TollCalculator.Compute(state, ActorSeat, path, Payees).Total;
                if (player.Vouchers < total)
                    return CommandResult.Fail($"路费需要 {total} 金券，你只有 {player.Vouchers}。");
            }

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            MapNodeState node = state.Map.Node(NodeId);

            // 步 1：支付路费（§6.6）——没有自己影响力的航道每条 2 金券；
            // 该航道上有对手影响力就付给那名对手，否则退给无限的公共池（不记账）。
            RouteIds = PlanRoutes(state);
            Toll = TollCalculator.Compute(state, ActorSeat, RouteIds, Payees);
            if (!ActionRules.IgnoresCosts(state)) // 开发者模式：不付路费
            {
                foreach (TollCalculator.Payment payment in Toll.Payments)
                {
                    player.Vouchers -= payment.Amount;
                    if (payment.PayeeSeat != TollCalculator.SupplyPayee)
                        state.Player(payment.PayeeSeat).Vouchers += payment.Amount;
                }
            }

            // 步 2（前半）：翻开对应颜色的事件牌堆顶，把对应的资源点指示物放到目标点。
            List<int> deck = DeckOf(state, node.Color);
            int cardId = deck[0];
            deck.RemoveAt(0);
            EventCardDef card = state.Catalog.Event(cardId);

            node.HasResourceToken = true;
            node.TokenResource = card.TokenResource;
            node.TokenAmount = card.TokenAmount;
            node.EventCardId = cardId;
            node.EventResolved = false;

            // 步 2（后半）由 ResolveEventOptionCommand 结算；步 3 由 ExploreInfluenceCommand 补上，
            // 这里先把「还没放影响力」记在资源点上。
            node.ExplorePending = true;
        }

        /// <summary>该颜色的事件牌堆（与资源点颜色一一对应，§4.1 步 2）。</summary>
        public static List<int> DeckOf(GameState state, NodeColor color) => color switch
        {
            NodeColor.Green => state.EventDeckGreen,
            NodeColor.Yellow => state.EventDeckYellow,
            _ => state.EventDeckRed,
        };

        private static string ColorName(NodeColor color) => color switch
        {
            NodeColor.Green => "绿色",
            NodeColor.Yellow => "黄色",
            _ => "红色",
        };

        private static string SeatText(int seat) =>
            seat == TollCalculator.SupplyPayee ? "公共供应堆" : $"玩家 {seat + 1}";

        /// <summary>
        /// 这条航道上现在站着谁（按空格顺序去重，没有就写「无」）。
        /// 「不能付给玩家 X」这种拒绝必须带上它，否则玩家只能自己去猜引擎眼里的影响力长什么样。
        /// </summary>
        private static string OwnersText(GameState state, int routeId, int actorSeat)
        {
            InfluenceSlots slots = state.Map.Route(routeId).Slots;
            var seen = new List<int>();

            for (int slot = 0; slot < slots.Capacity; slot++)
            {
                int owner = slots.OwnerOf(slot);
                if (owner < 0 || seen.Contains(owner)) continue;
                seen.Add(owner);
            }

            if (seen.Count == 0) return "无";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < seen.Count; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append(seen[i] == actorSeat ? $"你自己（玩家 {seen[i] + 1}）" : $"玩家 {seen[i] + 1}");
            }
            return sb.ToString();
        }

        /// <summary>这组航道里有没有某一条（自选路线的合法性与收款方校验用）。</summary>
        private static bool ContainsRoute(IReadOnlyList<int> routes, int routeId)
        {
            if (routes == null) return false;
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i] == routeId) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 主要行动 c·步 3：在探索的目标资源点放置 1 个自己的影响力（§6.4c 步 3）。
    /// 必须在事件选项结算完成后调用 —— 规则要求先结算事件、再放影响力，
    /// 所以这里要求资源点上的事件已结算（<see cref="MapNodeState.EventResolved"/>）。
    /// 它是探索三步里的最后一步，因此由它消耗本回合的主要行动名额。
    /// </summary>
    public sealed class ExploreInfluenceCommand : ICommand
    {
        /// <summary>探索的目标资源点 id。</summary>
        public readonly int NodeId;

        public ExploreInfluenceCommand(int seat, int nodeId)
        {
            ActorSeat = seat;
            NodeId = nodeId;
        }

        public int ActorSeat { get; }
        public string Name => "探索·放置影响力";
        public bool ConsumesAction => true;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            if (NodeId < 0 || NodeId >= state.Map.Nodes.Count)
                return CommandResult.Fail($"资源点 {NodeId} 不存在。");

            MapNodeState node = state.Map.Node(NodeId);
            if (!node.HasResourceToken || node.EventCardId < 0)
                return CommandResult.Fail($"资源点 {node.Code} 上没有探索翻开的资源点指示物。");
            if (!node.ExplorePending)
                return CommandResult.Fail($"资源点 {node.Code} 不是本次探索的目标点。");
            if (!node.EventResolved)
                return CommandResult.Fail($"请先结算资源点 {node.Code} 的事件选项，再放置影响力（§6.4c）。");
            if (ActionRules.HasOpponentCity(state, NodeId, ActorSeat))
                return CommandResult.Fail($"资源点 {node.Code} 上停靠着对手的移动城市。");

            PlayerState player = state.Player(ActorSeat);
            if (player.MarkersInSupply <= 0)
                return CommandResult.Fail("你的玩家供应堆里没有标记了，无法放置影响力。");
            if (ActionRules.FirstFreeSlot(node.Slots) < 0)
                return CommandResult.Fail($"资源点 {node.Code} 没有空的影响力空格。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            MapNodeState node = state.Map.Node(NodeId);

            int slot = ActionRules.FirstFreeSlot(node.Slots);
            node.Slots.Place(slot, ActorSeat);
            state.Player(ActorSeat).MarkersInSupply--;

            node.ExplorePending = false; // 三步走完，这次探索结清
        }
    }
}
