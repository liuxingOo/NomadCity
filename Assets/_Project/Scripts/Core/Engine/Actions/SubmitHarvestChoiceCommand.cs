using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 联机采集（§7）：每名玩家只提交**自己**的选点，不再由一台机器替所有人选。
    /// 选点先记在 <see cref="GameState.HarvestChoices"/>，收齐所有玩家后由最后一条提交当场结算
    /// （先算后付仍由 <see cref="HarvestResolver"/> 统一完成）。
    ///
    /// 这样做的原因：<see cref="GameEngine.Dispatch"/> 与命令重放对主机、客户端完全一致，
    /// 不需要额外的「主机替我选」消息；<see cref="ActorSeat"/> 就是提交者，联机座位闸门天然生效。
    /// </summary>
    public sealed class SubmitHarvestChoiceCommand : ICommand
    {
        private readonly int[] nodeIds;

        public SubmitHarvestChoiceCommand(int seat, IReadOnlyList<int> nodeIds)
        {
            Seat = seat;
            this.nodeIds = nodeIds == null ? Array.Empty<int>() : Copy(nodeIds);
        }

        /// <summary>提交选择的座位号（= 玩家本人）。</summary>
        public int Seat { get; }

        /// <summary>选中的资源点 id；提交方只传自己的选择。</summary>
        public IReadOnlyList<int> NodeIds => nodeIds;

        /// <summary>收齐所有玩家后产生的结算报告；还没收齐时为 null。</summary>
        public HarvestReport Report { get; private set; }

        public int ActorSeat => Seat;
        public string Name => "提交采集选择";

        /// <summary>采集不属于主要行动，不占行动名额、不推进座位。</summary>
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Phase != TurnPhase.Harvest)
                return CommandResult.Fail("当前不在采集阶段（§7）。");
            if (!state.HarvestPending)
                return CommandResult.Fail("本回合的采集已经结算过了。");
            if (Seat < 0 || Seat >= state.Players.Count)
                return CommandResult.Fail("座位号不合法。");
            if (state.HarvestChoices.ContainsKey(Seat))
                return CommandResult.Fail("你已经提交过本回合的采集选择了，等其他人。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            state.HarvestChoices[Seat] = new List<int>(nodeIds);

            // 还没收齐：等其他人提交，谁都不结算。
            if (state.HarvestChoices.Count < state.Players.Count) return;

            var chosen = new Dictionary<int, IReadOnlyList<int>>();
            foreach (KeyValuePair<int, List<int>> pair in state.HarvestChoices)
                chosen[pair.Key] = pair.Value;

            Report = HarvestResolver.Resolve(state, chosen);
            if (Report.Ok)
            {
                state.HarvestPending = false;
                state.HarvestChoices.Clear();
            }
        }

        private static int[] Copy(IReadOnlyList<int> source)
        {
            var result = new int[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            return result;
        }
    }
}
