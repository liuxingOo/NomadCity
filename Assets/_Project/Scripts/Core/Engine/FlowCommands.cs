using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>开局：进入第 1 回合的角色牌盖放阶段。</summary>
    public sealed class StartGameCommand : ICommand
    {
        /// <summary>本局首位起始玩家的座位号。</summary>
        public readonly int FirstPlayerSeat;

        public StartGameCommand(int firstPlayerSeat) => FirstPlayerSeat = firstPlayerSeat;

        public int ActorSeat => -1;
        public string Name => "开始游戏";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Players.Count == 0) return CommandResult.Fail("还没有建立玩家。");
            if (FirstPlayerSeat < 0 || FirstPlayerSeat >= state.Players.Count)
                return CommandResult.Fail($"起始玩家座位号 {FirstPlayerSeat} 超出范围。");
            return CommandResult.Success;
        }

        public void Apply(GameState state) => TurnFlow.Start(state, FirstPlayerSeat);
    }

    /// <summary>
    /// 推进到下一个阶段。骨架期用它手动走通流程；
    /// 正式对局里多数阶段边界会由各行动模块在满足条件时自动推进
    /// （例如玩家行动阶段由 <see cref="TurnFlow.CompletePlayerAction"/> 在两轮结束时收尾）。
    /// </summary>
    public sealed class AdvancePhaseCommand : ICommand
    {
        public int ActorSeat => -1;
        public string Name => "推进阶段";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");

            // 玩家行动阶段不能手动推进（§5 / §6.1）：每回合两轮，每名玩家每轮都要执行 1 个主要行动，
            // 两轮走完由 TurnFlow.CompletePlayerAction 自动收进采集阶段。允许手动跳过会让全套行动作废。
            if (state.Phase == TurnPhase.PlayerActions)
                return CommandResult.Fail(
                    $"本回合的第 {state.ActionRound}/{TurnFlow.ActionRoundsPerTurn} 轮还没走完——" +
                    "每名玩家每轮都要执行 1 个主要行动，两轮都走完才会自动进入采集。");

            return CommandResult.Success;
        }

        public void Apply(GameState state) => TurnFlow.AdvancePhase(state);
    }

    /// <summary>
    /// 开局入场·步 2：决定初始资源点（§4.3）。从起始玩家起依次选一个**不同的绿色资源点**
    /// （绿色事件牌对应的资源点），做三件事：
    /// 1. 把自己的移动城市停靠上去；
    /// 2. 翻开一张绿色事件牌，把对应的资源点指示物放到该资源点上；
    /// 3. 特例：选 <c>B-01</c> 时立即获得 2 源岩 / 2 源石 / 2 异铁。
    ///
    /// 牌面文案只通过 <see cref="GameState.Catalog"/> 查表取用（引擎只存 id）。
    /// 翻出的牌会记在资源点状态上（<see cref="MapNodeState.EventCardId"/>），
    /// 选项奖励由随后的 <see cref="ResolveEventOptionCommand"/> 结算，本命令不替玩家做选择。
    /// </summary>
    public sealed class ChooseStartNodeCommand : ICommand
    {
        public readonly int NodeId;

        public ChooseStartNodeCommand(int seat, int nodeId)
        {
            ActorSeat = seat;
            NodeId = nodeId;
        }

        public int ActorSeat { get; }
        public string Name => "决定初始资源点";

        /// <summary>开局设置，不是回合里的行动，所以不占用行动名额。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.TurnNumber != 1 || state.Phase != TurnPhase.CharacterPlacement)
                return CommandResult.Fail("只有在第 1 回合的角色牌盖放阶段（开局入场）才能决定初始资源点。");

            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");
            if (state.Player(ActorSeat).MobileCityNodeId >= 0)
                return CommandResult.Fail("你已经决定过初始资源点了。");

            int chooser = NextChooser(state);
            if (chooser != ActorSeat) return CommandResult.Fail($"当前轮到玩家 {chooser + 1} 决定初始资源点。");

            if (NodeId < 0 || NodeId >= state.Map.Nodes.Count)
                return CommandResult.Fail($"资源点 {NodeId} 不存在。");

            MapNodeState node = state.Map.Node(NodeId);
            if (node.Color != NodeColor.Green)
                return CommandResult.Fail($"只有绿色事件牌对应的资源点能作为初始资源点，{node.Code} 不是。");
            if (ActionRules.HasOpponentCity(state, NodeId, ActorSeat))
                return CommandResult.Fail($"资源点 {node.Code} 上已经停靠了其他玩家的移动城市。");

            if (state.EventDeckGreen.Count == 0)
                return CommandResult.Fail("绿色事件牌堆已空，无法翻开事件牌放置资源点指示物。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            MapNodeState node = state.Map.Node(NodeId);

            // 1. 移动城市停靠到该资源点。
            player.MobileCityNodeId = NodeId;

            // 2. 翻开绿色牌堆顶的一张，把对应的资源点指示物放到这个资源点上（牌面数据走 catalog）。
            int cardId = state.EventDeckGreen[0];
            state.EventDeckGreen.RemoveAt(0);
            EventCardDef card = state.Catalog.Event(cardId);

            node.HasResourceToken = true;
            node.TokenResource = card.TokenResource;
            node.TokenAmount = card.TokenAmount;
            node.EventCardId = cardId; // 记下翻出的牌，供后续事件选项奖励结算

            // 3. 特例：选择 B-01 作为初始资源点，立即获得 2 源岩 / 2 源石 / 2 异铁（§4.3 步 2）。
            if (node.Code == "B-01")
            {
                player.Resources.Add(ResourceType.Orirock, 2);
                player.Resources.Add(ResourceType.Originium, 2);
                player.Resources.Add(ResourceType.Oriron, 2);
            }
        }

        /// <summary>下一位还没决定初始资源点的座位（从本回合起始玩家起顺时针）；都决定完了返回 -1。</summary>
        public static int NextChooser(GameState state)
        {
            int seat = state.FirstPlayerSeat;
            for (int i = 0; i < state.Players.Count; i++)
            {
                if (state.Player(seat).MobileCityNodeId < 0) return seat;
                seat = state.NextSeat(seat);
            }
            return -1;
        }
    }

    /// <summary>
    /// 结算资源点上事件牌的选项（§4.3 步 2 后半段 / §6.4c 步 2 / §11.2）。玩家在翻出的事件牌选项里挑一个：
    /// 含「→」的选项用 <see cref="AcceptCost"/> 表示是否支付条件段（放弃则整个效果结束）。
    /// 效果一律交给 <see cref="EffectResolver.ExecuteOption"/> 结算，本命令不含任何牌面专属逻辑。
    /// 每个资源点只结算一次（<see cref="MapNodeState.EventResolved"/>），避免反复领奖励。
    ///
    /// 两处会翻牌，因此谁有权结算按阶段分别判定：
    /// - 开局入场（§4.3）：由把移动城市停靠在该点的玩家结算；
    /// - 玩家行动阶段（§6.4c 探索）：由当前行动玩家结算 —— 探索是分步行动，
    ///   <see cref="ExploreCommand"/> 不占用行动名额，所以整个探索过程中行动玩家不变。
    /// </summary>
    public sealed class ResolveEventOptionCommand : ICommand
    {
        /// <summary>事件牌所在的资源点 id。</summary>
        public readonly int NodeId;

        /// <summary>选择的选项下标（0 起）。</summary>
        public readonly int OptionIndex;

        /// <summary>是否支付条件段（「→」前的操作）；选项无代价时无意义。</summary>
        public readonly bool AcceptCost;

        public ResolveEventOptionCommand(int seat, int nodeId, int optionIndex, bool acceptCost = true)
        {
            ActorSeat = seat;
            NodeId = nodeId;
            OptionIndex = optionIndex;
            AcceptCost = acceptCost;
        }

        public int ActorSeat { get; }
        public string Name => "结算事件选项";

        /// <summary>结算不是回合里的行动，不占用行动名额。</summary>
        public bool ConsumesAction => false;

        /// <summary>执行报告（<see cref="Apply"/> 后填充）；UI 可据此显示做了什么、哪里没做成。</summary>
        public EffectReport Report { get; private set; }

        public CommandResult Validate(GameState state)
        {
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Phase != TurnPhase.CharacterPlacement && state.Phase != TurnPhase.PlayerActions)
                return CommandResult.Fail("当前阶段不能结算事件选项。");

            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");
            if (NodeId < 0 || NodeId >= state.Map.Nodes.Count)
                return CommandResult.Fail($"资源点 {NodeId} 不存在。");

            MapNodeState node = state.Map.Node(NodeId);
            if (!node.HasResourceToken || node.EventCardId < 0)
                return CommandResult.Fail($"资源点 {node.Code} 上还没有翻开的事件牌。");
            if (node.EventResolved)
                return CommandResult.Fail($"资源点 {node.Code} 的事件牌已经结算过了。");

            // 谁有权结算：开局入场看「移动城市停靠」，探索看「是不是当前行动玩家」。
            bool setup = state.TurnNumber == 1 && state.Phase == TurnPhase.CharacterPlacement;
            if (setup)
            {
                if (state.Player(ActorSeat).MobileCityNodeId != NodeId)
                    return CommandResult.Fail($"资源点 {node.Code} 上停靠的不是你的移动城市。");
            }
            else if (ActorSeat != state.ActiveSeat)
            {
                return CommandResult.Fail("还没轮到你行动，不能结算这个资源点的事件。");
            }

            EventCardDef card = LookUpCard(state, node.EventCardId);
            if (card == null)
                return CommandResult.Fail($"资源点上记录的事件牌 {node.EventCardId} 不是有效的事件牌。");
            if (OptionIndex < 0 || OptionIndex >= card.Options.Count)
                return CommandResult.Fail($"事件牌「{card.Name}」没有第 {OptionIndex + 1} 个选项。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            MapNodeState node = state.Map.Node(NodeId);
            EventCardDef card = state.Catalog.Event(node.EventCardId);
            EventOption option = card.Options[OptionIndex];

            Report = EffectResolver.ExecuteOption(state, ActorSeat, option, AcceptCost, anchorNodeId: NodeId);

            // 含主动放弃 / 条件不足，都算这个资源点结算过了（不接受反复重试领奖励）。
            node.EventResolved = true;
        }

        /// <summary>
        /// 查这张 id 是否为已知事件牌；合法则返回定义，否则返回 null。
        /// <see cref="ICardCatalog.Event"/> 对无效 id 会抛异常，所以先做边界检查再查表。
        /// </summary>
        private static EventCardDef LookUpCard(GameState state, int id)
        {
            bool known = (id >= CardIds.EventBase && id < CardIds.EventBase + CardCounts.EventCards)
                      || (id >= CardIds.EventEnterpriseBase
                          && id < CardIds.EventEnterpriseBase + CardIds.EventEnterpriseCount);
            return known ? state.Catalog.Event(id) : null;
        }
    }
}