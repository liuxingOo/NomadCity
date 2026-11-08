using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>一次「移动影响力」：把 From 上的自己影响力挪到 To（§6.4b）。</summary>
    public readonly struct InfluenceMove
    {
        public readonly InfluenceSlotRef From;
        public readonly InfluenceSlotRef To;

        public InfluenceMove(InfluenceSlotRef from, InfluenceSlotRef to)
        {
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// 主要行动 a：部署（§6.4a）。从自己的玩家供应堆取 1 个标记，放到一个合法的影响力空格上。
    /// </summary>
    public sealed class PlaceInfluenceCommand : ICommand
    {
        public readonly InfluenceSlotRef Slot;

        public PlaceInfluenceCommand(int seat, InfluenceSlotRef slot)
        {
            ActorSeat = seat;
            Slot = slot;
        }

        public int ActorSeat { get; }
        public string Name => "部署";
        public bool ConsumesAction => true;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            CommandResult quota = ActionRules.ValidateMainActionAvailable(state, ActorSeat);
            if (!quota.Ok) return quota;

            if (state.Player(ActorSeat).MarkersInSupply <= 0)
                return CommandResult.Fail("你的玩家供应堆里没有标记了，无法放置影响力（§11.3）。");

            return ActionRules.CanPlaceInfluence(state, ActorSeat, Slot);
        }

        public void Apply(GameState state)
        {
            state.Player(ActorSeat).MarkersInSupply--;
            state.Map.SlotsOf(Slot).Place(Slot.SlotIndex, ActorSeat);
        }
    }

    /// <summary>
    /// 主要行动 b：调度（§6.4b）。把自己的影响力挪到别的空格，一次至多 2 步。
    /// 目标空格要满足与「部署」相同的条件；不增减影响力总数，因此不动玩家标记。
    /// </summary>
    public sealed class RelocateInfluenceCommand : ICommand
    {
        /// <summary>一次调度里的移动步骤，1–2 步。</summary>
        public readonly IReadOnlyList<InfluenceMove> Moves;

        public RelocateInfluenceCommand(int seat, IReadOnlyList<InfluenceMove> moves)
        {
            ActorSeat = seat;
            Moves = moves;
        }

        public int ActorSeat { get; }
        public string Name => "调度";
        public bool ConsumesAction => true;

        public CommandResult Validate(GameState state)
        {
            CommandResult turn = ActionRules.ValidateActivePlayer(state, ActorSeat);
            if (!turn.Ok) return turn;

            CommandResult quota = ActionRules.ValidateMainActionAvailable(state, ActorSeat);
            if (!quota.Ok) return quota;

            if (Moves == null || Moves.Count == 0) return CommandResult.Fail("调度至少要移动 1 个影响力。");
            if (Moves.Count > 2) return CommandResult.Fail("调度一次至多移动 2 个影响力。");

            // 把同一命令里前面几步的效果累加进去，逐条模拟校验（只读，不改 state）。
            var planned = new Dictionary<InfluenceSlotRef, int>();
            foreach (InfluenceMove move in Moves)
            {
                if (move.From.Equals(move.To)) return CommandResult.Fail("起点与目标不能是同一个空格。");

                int owner = ActionRules.Occupant(move.From, state, planned);
                if (owner < 0) return CommandResult.Fail($"起点 {move.From} 上没有影响力。");
                if (owner != ActorSeat) return CommandResult.Fail($"起点 {move.From} 上的影响力不是你的。");

                CommandResult place = ActionRules.CanPlaceInfluence(state, ActorSeat, move.To, planned);
                if (!place.Ok) return place;

                planned[move.From] = -1;
                planned[move.To] = ActorSeat;
            }

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            foreach (InfluenceMove move in Moves)
            {
                state.Map.SlotsOf(move.From).Clear(move.From.SlotIndex);
                state.Map.SlotsOf(move.To).Place(move.To.SlotIndex, ActorSeat);
            }
        }
    }
}