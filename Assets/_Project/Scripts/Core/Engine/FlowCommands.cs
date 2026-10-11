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
            return CommandResult.Success;
        }

        public void Apply(GameState state) => TurnFlow.AdvancePhase(state);
    }
}