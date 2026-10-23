using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 采集阶段结算（§7）：界面收齐所有玩家的选点后，把整份选择作为一条命令送进引擎。
    /// 这样采集也走 <see cref="GameEngine.Dispatch"/> 唯一入口，联机时客户端可以靠重放复现。
    ///
    /// 采集不是「轮到某人」的主要行动：<see cref="ActorSeat"/> 恒为 -1，不占行动名额，也不推进座位。
    /// 结算报告保存在 <see cref="Report"/>，由 <see cref="GameEngine.Dispatch"/> 广播
    /// <see cref="GameEngine.HarvestResolved"/>，界面据此播报收成与路费。
    /// </summary>
    public sealed class ResolveHarvestCommand : ICommand
    {
        private readonly IReadOnlyDictionary<int, IReadOnlyList<int>> chosenBySeat;

        /// <summary>结算报告；<see cref="Apply"/> 之后才有值。</summary>
        public HarvestReport Report { get; private set; }

        /// <summary>各座位选择的资源点（联机编解码需要读取原始选择）。</summary>
        public IReadOnlyDictionary<int, IReadOnlyList<int>> Choices => chosenBySeat;

        public ResolveHarvestCommand(IReadOnlyDictionary<int, IReadOnlyList<int>> chosenBySeat)
        {
            this.chosenBySeat = chosenBySeat;
        }

        public int ActorSeat => -1;
        public string Name => "采集结算";

        /// <summary>采集不属于任何人的主要行动，不占行动名额。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Phase != TurnPhase.Harvest)
                return CommandResult.Fail("当前不在采集阶段（§7）。");
            if (!state.HarvestPending)
                return CommandResult.Fail("本回合的采集已经结算过了。");
            if (chosenBySeat == null)
                return CommandResult.Fail("没有提供任何玩家的选点。");

            // 缺某个座位的键按「该玩家什么都不采」处理（与 HarvestResolver.Resolve 的语义一致），
            // 不在这里强制要求每个座位都有一条记录。
            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            Report = HarvestResolver.Resolve(state, chosenBySeat);
            if (Report.Ok) state.HarvestPending = false;
        }
    }
}
