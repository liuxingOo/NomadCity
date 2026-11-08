using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 结束本回合（§6.1）：每轮 1 个主要行动；主要行动**之前或之后**都可以任意次执行快速行动，
    /// 所以「主要行动做完」不再自动交出行动权 —— 只有玩家主动点「结束回合」，才轮到下一位。
    ///
    /// 它不占行动名额（<see cref="ConsumesAction"/> 为 false），本身也没有任何规则效果：
    /// 真正推进座位的是 <see cref="TurnFlow.CompletePlayerAction"/>（走完两轮则进入采集阶段）。
    /// </summary>
    public sealed class EndTurnCommand : ICommand
    {
        public EndTurnCommand(int seat)
        {
            ActorSeat = seat;
        }

        public int ActorSeat { get; }
        public string Name => "结束回合";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            // 角色牌「连用两种效果」中途不能结束回合（§10.1.2），这道闸门由 ValidateActivePlayer 一并挡住。
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            // 城市移动三步没走完就结束回合，出发点那 1 个影响力会永久丢掉（§6.4d）。
            if (state.CityMoveOriginNodeId >= 0)
                return CommandResult.Fail("城市移动还没走完（还要在出发点放置 1 个影响力），不能结束回合（§6.4d）。");

            // 规则要求每轮必须做 1 次主要行动才能结束回合；开发者模式放开这条，
            // 方便单独测快速行动 / 阶段流程。
            if (!state.DeveloperMode && !state.MainActionTakenThisRound)
                return CommandResult.Fail("本回合还没有执行主要行动：必须先做 1 次主要行动，才能结束回合（§6.1）。");

            // 自己城市所在地还有未结算的事件牌：先结算，否则这张牌会跨回合挂着。
            PlayerState player = state.Player(ActorSeat);
            if (player.MobileCityNodeId >= 0)
            {
                MapNodeState city = state.Map.Node(player.MobileCityNodeId);
                if (city.HasResourceToken && city.EventCardId >= 0 && !city.EventResolved)
                    return CommandResult.Fail($"你的城市所在地 {city.Code} 还有未结算的事件牌，请先结算。");
            }

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            // 主动结束回合 = 连没用掉的「额外主要行动」也一并放弃，直接把行动权交给下一位（§6.3f）。
            // 交出之后 CompletePlayerAction 会复位本轮的主要行动名额。
            state.Player(ActorSeat).ExtraMainActionsThisTurn = 0;
            TurnFlow.CompletePlayerAction(state);
        }
    }
}
