using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 终局结算（§9）。第 8 回合收尾后调用一次：
    /// 先给每个区块的控制者加区块控制分（§9.2），再按剩余资源加资源分（§9.3）。
    /// 全部是对 GameState 的纯计算，不依赖卡牌文案与美术。
    /// </summary>
    public static class FinalScoring
    {
        // 剩余资源兑换比（§9.3）：每 N 个换 1 分（向下取整）。
        public const int OrirockPerPoint = 9;
        public const int OriginiumPerPoint = 8;
        public const int OrironPerPoint = 6;
        public const int OriginitePrimePerPoint = 1;

        /// <summary>某玩家的剩余资源分（§9.3）。</summary>
        public static int RemainingResourceScore(PlayerState player)
        {
            ResourcePool resources = player.Resources;
            return resources.Orirock / OrirockPerPoint
                 + resources.Originium / OriginiumPerPoint
                 + resources.Oriron / OrironPerPoint
                 + resources.OriginitePrime / OriginitePrimePerPoint;
        }

        /// <summary>
        /// 逐个区块算出「谁控制、值多少分」（§9.2 / §10.4）。
        /// 影响力 = 区块内资源点上的影响力 + 区块内航道上的影响力 + 自己的移动城市（+2）。
        /// </summary>
        public static IReadOnlyList<DistrictControl> EvaluateDistricts(GameState state)
        {
            int seats = state.Players.Count;
            var result = new List<DistrictControl>();

            foreach (District district in Districts.All)
            {
                var influence = new int[seats];

                foreach (MapNodeState node in state.Map.Nodes)
                {
                    if (node.District != district) continue;
                    for (int seat = 0; seat < seats; seat++) influence[seat] += node.Slots.CountOf(seat);
                }

                foreach (RouteState route in state.Map.Routes)
                {
                    if (route.District != district) continue;
                    for (int seat = 0; seat < seats; seat++) influence[seat] += route.Slots.CountOf(seat);
                }

                // 移动城市在该区块视为 2 个额外影响力（§10.4.3），且不影响自己在该点上的影响力。
                for (int seat = 0; seat < seats; seat++)
                {
                    int city = state.Player(seat).MobileCityNodeId;
                    if (city >= 0 && city < state.Map.Nodes.Count && state.Map.Nodes[city].District == district)
                        influence[seat] += 2;
                }

                int controller = ControllerOf(influence);
                result.Add(new DistrictControl(district, state.DistrictScores[(int)district], influence, controller));
            }

            return result;
        }

        /// <summary>区块里影响力最多且**唯一**的玩家；无人有影响力或并列最多时返回 -1（§9.2 步 2 / §10.4 步 2）。</summary>
        public static int ControllerOf(IReadOnlyList<int> influence)
        {
            int best = 0;
            foreach (int value in influence) if (value > best) best = value;
            if (best <= 0) return -1;

            int controller = -1;
            for (int seat = 0; seat < influence.Count; seat++)
            {
                if (influence[seat] != best) continue;
                if (controller >= 0) return -1; // 并列最多 → 都不拿分
                controller = seat;
            }
            return controller;
        }

        /// <summary>结算全部终局分数（§9.2 + §9.3），直接加到各玩家的 Score 上。</summary>
        public static void Settle(GameState state)
        {
            foreach (DistrictControl control in EvaluateDistricts(state))
            {
                if (control.Controller >= 0) state.Player(control.Controller).Score += control.Score;
            }

            foreach (PlayerState player in state.Players) player.Score += RemainingResourceScore(player);
        }

        /// <summary>
        /// 胜者（§9.4）：分数最高者；同分则比剩余金券，再比剩余至纯源石；仍相同则共同胜利。
        /// 返回获胜玩家的座位号（可能有多位）。
        /// </summary>
        public static IReadOnlyList<int> Winners(GameState state)
        {
            var contenders = new List<int>();
            int bestScore = int.MinValue;
            foreach (PlayerState player in state.Players)
            {
                if (player.Score > bestScore) bestScore = player.Score;
            }
            foreach (PlayerState player in state.Players)
            {
                if (player.Score == bestScore) contenders.Add(player.SeatIndex);
            }

            if (contenders.Count <= 1) return contenders;

            contenders = KeepMax(state, contenders, player => player.Vouchers);
            if (contenders.Count <= 1) return contenders;

            return KeepMax(state, contenders, player => player.Resources.OriginitePrime);
        }

        private static List<int> KeepMax(GameState state, List<int> seats, Func<PlayerState, int> key)
        {
            int best = int.MinValue;
            foreach (int seat in seats)
            {
                int value = key(state.Player(seat));
                if (value > best) best = value;
            }

            var kept = new List<int>();
            foreach (int seat in seats)
            {
                if (key(state.Player(seat)) == best) kept.Add(seat);
            }
            return kept;
        }
    }

    /// <summary>一个区块的终局结算结果。</summary>
    public readonly struct DistrictControl
    {
        public readonly District District;

        /// <summary>该区块的控制分（来自 <see cref="GameState.DistrictScores"/>）。</summary>
        public readonly int Score;

        /// <summary>各玩家在该区块的影响力（下标 = 座位号），已含移动城市的 +2。</summary>
        public readonly IReadOnlyList<int> Influence;

        /// <summary>控制者座位号；无人有影响力或并列最多时为 -1。</summary>
        public readonly int Controller;

        public DistrictControl(District district, int score, IReadOnlyList<int> influence, int controller)
        {
            District = district;
            Score = score;
            Influence = influence;
            Controller = controller;
        }
    }
}