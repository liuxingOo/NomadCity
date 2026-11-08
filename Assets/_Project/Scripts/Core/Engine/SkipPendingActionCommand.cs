using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 联机的「挂机跳过」：某个座位长时间不动、其余玩家投票通过后，主机代它做一个**最保守**的选择。
    ///
    /// 只处理会让整桌卡住的那一步：
    ///   开局入场 —— 选第一个合法初始资源点 / 结算事件 / 弃设施牌 / 盖放第一张角色牌；
    ///   玩家行动 —— 先清掉自己城市的待结算事件、城市移动的待放影响力，然后直接跳过本次主要行动 / 结束回合；
    ///   采集阶段 —— 提交一个空的资源点选择（本回合不采集）。
    /// 不改变任何规则，只是把「必须有人点的那一下」替代掉。
    /// </summary>
    public sealed class SkipPendingActionCommand : ICommand
    {
        /// <summary>多久没有任何操作，其他玩家才能发起跳过（桌游需要思考时间，别调太短）。</summary>
        public const float VoteDelaySeconds = 180f;

        /// <summary>被跳过的玩家已经掉线时用这个更短的门槛，不必再等满三分钟。</summary>
        public const float VoteDelayDisconnectedSeconds = 20f;

        public SkipPendingActionCommand(int seat)
        {
            ActorSeat = seat;
        }

        public int ActorSeat { get; }
        public string Name => "投票跳过（自动处理）";

        // 这个包装命令本身不推进行动：它只是替目标座位做「下一步」，是否推进由里面那一步决定。
        // （若在这里声明 true，开局选点 / 采集提交这些不占行动的命令也会被引擎当成主要行动推进。）
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            return TryPlan(state, ActorSeat, out _, out string error)
                ? CommandResult.Success
                : CommandResult.Fail(error);
        }

        /// <summary>
        /// 执行时按引擎的同一套规则收尾：里面那一步若是一次主要行动（<see cref="ICommand.ConsumesAction"/>），
        /// 就在这里补一次座位推进 —— 因为包装命令绕过了引擎对内部命令的那一次自动推进。
        ///
        /// 玩家行动阶段会连做几步（先结算挡路的事件 / 补城市移动的影响力，再放弃行动）：
        /// 一次投票就要把这个人让过去，而不是只清掉一张事件卡、让全桌再等一轮。
        /// 其它阶段（开局入场 / 盖放 / 采集）每次只做一步，避免替掉不该替的操作。
        /// </summary>
        public void Apply(GameState state)
        {
            for (int step = 0; step < 3; step++)
            {
                if (!TryPlan(state, ActorSeat, out ICommand inner, out _)) return;

                inner.Apply(state);

                if (inner.ConsumesAction)
                {
                    TurnFlow.CompletePlayerAction(state);
                    return;
                }

                if (state.Phase != TurnPhase.PlayerActions) return;
            }
        }

        /// <summary>
        /// 当前有哪些座位在「等它操作」。空列表表示这个阶段没有需要投票跳过的人。
        /// 客户端用同一份状态各自算出同样的结果，不需要额外同步。
        /// </summary>
        public static List<int> PendingSeats(GameState state)
        {
            var seats = new List<int>();
            if (state == null || state.IsGameOver || state.Players.Count == 0) return seats;

            switch (state.Phase)
            {
                case TurnPhase.CharacterPlacement:
                    if (state.SupplyDiscardRemaining > 0)
                    {
                        if (state.SupplyDiscardSeat >= 0) seats.Add(state.SupplyDiscardSeat);
                        return seats;
                    }

                    if (state.TurnNumber == 1)
                    {
                        int chooser = ChooseStartNodeCommand.CurrentOperator(state);
                        if (chooser >= 0)
                        {
                            seats.Add(chooser);
                            return seats;
                        }
                    }

                    for (int seat = 0; seat < state.Players.Count; seat++)
                    {
                        PlayerState player = state.Player(seat);
                        if (player.PlacedCharacterCard < 0 &&
                            (player.HandCharacterCards.Count > 0 || player.DiscardCharacterCards.Count > 0))
                        {
                            seats.Add(seat);
                        }
                    }

                    break;

                case TurnPhase.PlayerActions:
                    if (state.ActiveSeat >= 0 && state.ActiveSeat < state.Players.Count)
                        seats.Add(state.ActiveSeat);
                    break;

                case TurnPhase.Harvest:
                    if (!state.HarvestPending) break;

                    for (int seat = 0; seat < state.Players.Count; seat++)
                    {
                        if (!state.HarvestChoices.ContainsKey(seat)) seats.Add(seat);
                    }

                    break;
            }

            return seats;
        }

        public static bool IsPending(GameState state, int seat) => PendingSeats(state).Contains(seat);

        /// <summary>算出「替这个座位做哪一步」。规划必须和 <see cref="PendingSeats"/> 描述的一致。</summary>
        private static bool TryPlan(GameState state, int seat, out ICommand command, out string error)
        {
            command = null;
            error = null;

            if (state == null)
            {
                error = "引擎没有游戏状态。";
                return false;
            }

            if (state.IsGameOver)
            {
                error = "本局已经结束。";
                return false;
            }

            if (seat < 0 || seat >= state.Players.Count)
            {
                error = "座位号无效。";
                return false;
            }

            PlayerState player = state.Player(seat);

            switch (state.Phase)
            {
                case TurnPhase.CharacterPlacement:
                    return TryPlanPlacement(state, seat, player, out command, out error);

                case TurnPhase.PlayerActions:
                    return TryPlanAction(state, seat, player, out command, out error);

                case TurnPhase.Harvest:
                    if (!state.HarvestPending)
                    {
                        error = "采集已经结算完，没有需要跳过的选择。";
                        return false;
                    }

                    if (state.HarvestChoices.ContainsKey(seat))
                    {
                        error = "该玩家已经提交过采集选择。";
                        return false;
                    }

                    command = new SubmitHarvestChoiceCommand(seat, Array.Empty<int>());
                    return true;

                default:
                    error = "当前阶段没有可以跳过的操作。";
                    return false;
            }
        }

        private static bool TryPlanPlacement(GameState state, int seat, PlayerState player, out ICommand command,
            out string error)
        {
            command = null;
            error = null;

            // 2 人局：先替他把设施牌弃掉，否则下一步会一直卡着。
            if (state.SupplyDiscardRemaining > 0 && state.SupplyDiscardSeat == seat)
            {
                if (state.FacilitySupply.Count == 0)
                {
                    error = "设施供应堆是空的，没有可弃的设施牌。";
                    return false;
                }

                command = new DiscardSupplyFacilityCommand(seat, state.FacilitySupply[0]);
                return true;
            }

            if (state.TurnNumber == 1 && ChooseStartNodeCommand.CurrentOperator(state) == seat)
            {
                if (player.MobileCityNodeId >= 0)
                {
                    MapNodeState city = state.Map.Node(player.MobileCityNodeId);
                    if (city.HasResourceToken && city.EventCardId >= 0 && !city.EventResolved)
                    {
                        command = new ResolveEventOptionCommand(seat, city.Id, 0);
                        return true;
                    }
                }

                int node = FirstStartNode(state, seat);
                if (node < 0)
                {
                    error = "没有可以自动选择的初始资源点。";
                    return false;
                }

                command = new ChooseStartNodeCommand(seat, node);
                return true;
            }

            if (player.PlacedCharacterCard >= 0)
            {
                error = "该玩家已经盖放过角色牌。";
                return false;
            }

            List<int> pool = player.HandCharacterCards.Count > 0
                ? player.HandCharacterCards
                : player.DiscardCharacterCards;
            if (pool.Count == 0)
            {
                error = "该玩家没有可以盖放的角色牌。";
                return false;
            }

            command = new PlaceCharacterCardCommand(seat, pool[0]);
            return true;
        }

        private static bool TryPlanAction(GameState state, int seat, PlayerState player, out ICommand command,
            out string error)
        {
            command = null;
            error = null;

            if (state.ActiveSeat != seat)
            {
                error = "还没轮到该玩家行动。";
                return false;
            }

            // 城市移动的第三步还没放影响力：先补上，再跳过行动。
            if (state.CityMoveOriginNodeId >= 0)
            {
                MapNodeState target = state.Map.Node(player.MobileCityNodeId);
                if (target.HasResourceToken && target.EventCardId >= 0 && !target.EventResolved)
                {
                    command = new ResolveEventOptionCommand(seat, target.Id, 0);
                    return true;
                }

                MapNodeState origin = state.Map.Node(state.CityMoveOriginNodeId);
                if (player.MarkersInSupply > 0 && ActionRules.FirstFreeSlot(origin.Slots) >= 0)
                {
                    command = new MoveCityInfluenceCommand(seat, state.CityMoveOriginNodeId);
                    return true;
                }
            }

            // 探索翻出来的事件还没结算：先替他选第一个选项，不然跳过会把事件留在桌上。
            foreach (MapNodeState node in state.Map.Nodes)
            {
                if (node.HasResourceToken && node.EventCardId >= 0 && !node.EventResolved)
                {
                    command = new ResolveEventOptionCommand(seat, node.Id, 0);
                    return true;
                }
            }

            // 本轮主要行动已经做过了：挂机玩家只剩下「结束回合」这一步（快速行动不需要替他做）。
            if (state.MainActionTakenThisRound && player.ExtraMainActionsThisTurn <= 0)
            {
                command = new EndTurnCommand(seat);
                return true;
            }

            command = new PassActionCommand(seat);
            return true;
        }

        /// <summary>第一个「合法」的绿色起始点：跳过 2 人局的禁用点、别人城市所在地；绿色牌堆没牌时随便挑一个绿点。</summary>
        private static int FirstStartNode(GameState state, int seat)
        {
            string forbidden = PlayerCountVariant.For(state).ForbiddenStartNodeCode;
            int fallback = -1;

            foreach (MapNodeState node in state.Map.Nodes)
            {
                if (node.Color != NodeColor.Green) continue;
                if (!string.IsNullOrEmpty(forbidden) && node.Code == forbidden) continue;
                if (ActionRules.HasOpponentCity(state, node.Id, seat)) continue;

                if (state.EventDeckGreen.Count > 0) return node.Id;
                if (fallback < 0) fallback = node.Id;
            }

            return fallback;
        }
    }

    /// <summary>放弃本次主要行动（不做任何事，直接轮下一位）。只在「挂机跳过」里使用。</summary>
    public sealed class PassActionCommand : ICommand
    {
        public PassActionCommand(int seat)
        {
            ActorSeat = seat;
        }

        public int ActorSeat { get; }
        public string Name => "放弃主要行动";
        public bool ConsumesAction => true;

        public CommandResult Validate(GameState state)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (state.IsGameOver) return CommandResult.Fail("本局已经结束。");
            if (state.Phase != TurnPhase.PlayerActions)
                return CommandResult.Fail("现在不是玩家行动阶段，不能放弃行动。");
            if (state.ActiveSeat != ActorSeat)
                return CommandResult.Fail("还没轮到该玩家行动。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            // 推进由「挂机跳过」这层包装命令代做（引擎不再因为 ConsumesAction 自动推进），
            // 这里再推一次会把下一位玩家也一起跳过。
        }
    }
}
