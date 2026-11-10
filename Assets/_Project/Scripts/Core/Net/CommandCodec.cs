using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// 命令与 <see cref="CommandDto"/> 之间的双向编解码。
    /// 下面 <see cref="Encode"/> 与 <see cref="Decode"/> 两个 switch **刻意紧挨着、写在同一文件里**，
    /// 且各自 default 抛异常 —— 以后新增命令只改一处会立刻炸，避免协议悄悄长歪。
    ///
    /// 槽位约定（P0/P1/P2/P3、Flags、Items 的用法）见 <see cref="Encode"/> 内每个分支的注释。
    /// <c>IEffectChoices</c> 本身是回调、不可序列化，但「玩家当场点过的答案」必须原样过网：
    /// 带效果点选的命令（建设 / 使用角色牌 / 特殊行动 / 结算事件选项）统一用
    /// <b>P3 = 答案个数、Items = 答案列表</b>（按提问顺序，-1 = 放弃），解码时重建为
    /// <see cref="RecordedChoices"/>（strict = false，答案用尽就退回自动结算）。
    /// 否则主机回放只能按「第一个合法项」执行，玩家点的落点会被静默换掉。
    /// </summary>
    public static class CommandCodec
    {
        /// <summary>Flags 里 bit0 的含义：ResolveEventOptionCommand.AcceptCost。</summary>
        public const uint AcceptCostBit = 1u << 0;

        /// <summary>Flags 里 bit1 的含义：ResolveEventOptionCommand.GrantRewards。</summary>
        public const uint GrantRewardsBit = 1u << 1;

        // —— 编码：命令对象 → DTO ——

        /// <summary>把一条命令按**实际运行时类型**装箱进 DTO。</summary>
        public static CommandDto Encode(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            switch (command)
            {
                // Seat 一律取 ActorSeat；P0 = FirstPlayerSeat。
                case StartGameCommand c:
                    return new CommandDto { Kind = CommandKind.StartGame, Seat = c.ActorSeat, P0 = c.FirstPlayerSeat };

                // 无参数；Seat = -1。
                case AdvancePhaseCommand c:
                    return new CommandDto { Kind = CommandKind.AdvancePhase, Seat = c.ActorSeat };

                // P0 = NodeId。
                case ChooseStartNodeCommand c:
                    return new CommandDto { Kind = CommandKind.ChooseStartNode, Seat = c.ActorSeat, P0 = c.NodeId };

                // P0 = NodeId；P1 = OptionIndex；Flags: bit0 = AcceptCost, bit1 = GrantRewards；
                // P3 = 选项效果里点过的答案个数，Items = 答案。
                case ResolveEventOptionCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.ResolveEventOption,
                        Seat = c.ActorSeat,
                        P0 = c.NodeId,
                        P1 = c.OptionIndex,
                        P3 = AnswerCount(c.Choices),
                        Items = AnswerItems(c.Choices),
                        Flags = (c.AcceptCost ? AcceptCostBit : 0u) | (c.GrantRewards ? GrantRewardsBit : 0u),
                    };

                // P0 = FacilityId。
                case DiscardSupplyFacilityCommand c:
                    return new CommandDto { Kind = CommandKind.DiscardSupplyFacility, Seat = c.ActorSeat, P0 = c.FacilityId };

                // P0 = FacilityId；P1 = PanelSlot；P2 = (int)Payment；P3 = 入场效果答案个数，Items = 答案。
                case BuildFacilityCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.BuildFacility,
                        Seat = c.ActorSeat,
                        P0 = c.FacilityId,
                        P1 = c.PanelSlot,
                        P2 = (int)c.Payment,
                        P3 = AnswerCount(c.EffectChoices),
                        Items = AnswerItems(c.EffectChoices),
                    };

                // P0 = CardId。
                case PlaceCharacterCardCommand c:
                    return new CommandDto { Kind = CommandKind.PlaceCharacterCard, Seat = c.ActorSeat, P0 = c.CardId };

                // P0 = (int)Effect；P3 = 效果答案个数，Items = 答案。
                case UseCharacterCardCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.UseCharacterCard,
                        Seat = c.ActorSeat,
                        P0 = (int)c.Effect,
                        P3 = AnswerCount(c.EffectChoices),
                        Items = AnswerItems(c.EffectChoices),
                    };

                // 无参数。
                case EndCharacterCardUseCommand c:
                    return new CommandDto { Kind = CommandKind.EndCharacterCardUse, Seat = c.ActorSeat };

                // P0 = PatternCardId；P1 = PlacementIndex。
                case DeclareCityPatternCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.DeclareCityPattern,
                        Seat = c.ActorSeat,
                        P0 = c.PatternCardId,
                        P1 = c.PlacementIndex,
                    };

                // P0 = NodeId；P1 = 航道数（Items 头部）；P2 = 付给谁的对数（Items 尾部，每对 2 个 int：航道 id、座位号）。
                // Items = [routeId..., payeeKey, payeeValue, ...]，付给谁按航道 id 升序排列以保证确定性。
                case ExploreCommand c:
                {
                    int routeCount = c.SelectedRouteIds?.Count ?? 0;
                    int payeeCount = c.Payees?.Count ?? 0;
                    var dto = new CommandDto
                    {
                        Kind = CommandKind.Explore,
                        Seat = c.ActorSeat,
                        P0 = c.NodeId,
                        P1 = routeCount,
                        P2 = payeeCount,
                    };
                    if (routeCount > 0 || payeeCount > 0)
                    {
                        var items = new List<int>(routeCount + payeeCount * 2);
                        if (routeCount > 0) items.AddRange(c.SelectedRouteIds);
                        if (payeeCount > 0)
                        {
                            var pairs = new List<KeyValuePair<int, int>>(c.Payees);
                            pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
                            foreach (KeyValuePair<int, int> pair in pairs)
                            {
                                items.Add(pair.Key);
                                items.Add(pair.Value);
                            }
                        }
                        dto.Items = items.ToArray();
                    }
                    return dto;
                }

                // P0 = NodeId。
                case ExploreInfluenceCommand c:
                    return new CommandDto { Kind = CommandKind.ExploreInfluence, Seat = c.ActorSeat, P0 = c.NodeId };

                // P0 = TargetNodeId。
                case MoveMobileCityCommand c:
                    return new CommandDto { Kind = CommandKind.MoveMobileCity, Seat = c.ActorSeat, P0 = c.TargetNodeId };

                // P0 = OriginNodeId。
                case MoveCityInfluenceCommand c:
                    return new CommandDto { Kind = CommandKind.MoveCityInfluence, Seat = c.ActorSeat, P0 = c.OriginNodeId };

                // 影响力格引用拆成三槽：P0 = (int)Kind，P1 = TargetId，P2 = SlotIndex。
                case PlaceInfluenceCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.PlaceInfluence,
                        Seat = c.ActorSeat,
                        P0 = (int)c.Slot.Kind,
                        P1 = c.Slot.TargetId,
                        P2 = c.Slot.SlotIndex,
                    };

                // P0 = 移动步数；Items = 每步 6 个 int：[FromKind, FromTarget, FromSlot, ToKind, ToTarget, ToSlot]。
                case RelocateInfluenceCommand c:
                {
                    var dto = new CommandDto { Kind = CommandKind.RelocateInfluence, Seat = c.ActorSeat };
                    if (c.Moves != null && c.Moves.Count > 0)
                    {
                        dto.P0 = c.Moves.Count;
                        var items = new int[c.Moves.Count * 6];
                        for (int i = 0; i < c.Moves.Count; i++)
                        {
                            InfluenceMove move = c.Moves[i];
                            int b = i * 6;
                            items[b] = (int)move.From.Kind;
                            items[b + 1] = move.From.TargetId;
                            items[b + 2] = move.From.SlotIndex;
                            items[b + 3] = (int)move.To.Kind;
                            items[b + 4] = move.To.TargetId;
                            items[b + 5] = move.To.SlotIndex;
                        }
                        dto.Items = items;
                    }
                    return dto;
                }

                // P0 = PatternCardId；P3 = 效果答案个数，Items = 答案。
                case UsePatternSpecialActionCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.UsePatternSpecialAction,
                        Seat = c.ActorSeat,
                        P0 = c.PatternCardId,
                        P3 = AnswerCount(c.EffectChoices),
                        Items = AnswerItems(c.EffectChoices),
                    };

                // P0 = 座位数；Items = [seat, 选点数, 选点 id..., ...]，按座位升序排列保证确定性。
                case ResolveHarvestCommand c:
                {
                    var dto = new CommandDto { Kind = CommandKind.ResolveHarvest, Seat = c.ActorSeat };
                    if (c.Choices != null && c.Choices.Count > 0)
                    {
                        var seats = new List<int>(c.Choices.Keys);
                        seats.Sort();
                        var items = new List<int>();
                        foreach (int seat in seats)
                        {
                            IReadOnlyList<int> nodes = c.Choices[seat];
                            items.Add(seat);
                            items.Add(nodes?.Count ?? 0);
                            if (nodes != null) items.AddRange(nodes);
                        }
                        dto.P0 = seats.Count;
                        dto.Items = items.ToArray();
                    }
                    return dto;
                }

                // 挂机跳过：Seat = 被跳过的座位。
                case SkipPendingActionCommand c:
                    return new CommandDto { Kind = CommandKind.SkipPendingAction, Seat = c.ActorSeat };

                // 放弃主要行动：Seat = 行动者座位。
                case PassActionCommand c:
                    return new CommandDto { Kind = CommandKind.PassAction, Seat = c.ActorSeat };

                // 结束回合：Seat = 行动者座位。
                case EndTurnCommand c:
                    return new CommandDto { Kind = CommandKind.EndTurn, Seat = c.ActorSeat };

                // 选择合作企业：P0 = BoardId。
                case ChooseInitialEnterpriseCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.ChooseInitialEnterprise,
                        Seat = c.ActorSeat,
                        P0 = c.BoardId,
                    };

                // 提升合作等级：P0 = BoardId。
                case AdvanceEnterpriseLevelCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.AdvanceEnterpriseLevel,
                        Seat = c.ActorSeat,
                        P0 = c.BoardId,
                    };

                // 触发企业特效：P0 = BoardId；P1 = Level；P3 = 答案个数，Items = 答案。
                case TriggerEnterpriseEffectCommand c:
                    return new CommandDto
                    {
                        Kind = CommandKind.TriggerEnterpriseEffect,
                        Seat = c.ActorSeat,
                        P0 = c.BoardId,
                        P1 = c.Level,
                        P3 = AnswerCount(c.EffectChoices),
                        Items = AnswerItems(c.EffectChoices),
                    };

                // P0 = 座位号；P1 = 选点数；Items = 本座位的选点 id（只传自己的）。
                case SubmitHarvestChoiceCommand c:
                {
                    var dto = new CommandDto
                    {
                        Kind = CommandKind.SubmitHarvestChoice,
                        Seat = c.ActorSeat,
                        P0 = c.Seat,
                        P1 = c.NodeIds.Count,
                    };

                    if (c.NodeIds.Count > 0)
                    {
                        var items = new int[c.NodeIds.Count];
                        for (int i = 0; i < items.Length; i++) items[i] = c.NodeIds[i];
                        dto.Items = items;
                    }

                    return dto;
                }

                default:
                    throw new ArgumentException($"无法编码未知的命令类型：{command.GetType().Name}。", nameof(command));
            }
        }

        // —— 解码：DTO → 命令对象（与上面 Encode 的 switch 一一对应，紧挨着放） ——

        /// <summary>按 <see cref="CommandDto.Kind"/> 重建命令对象；效果点选答案由 <c>P3 + Items</c> 还原成 <see cref="RecordedChoices"/>。</summary>
        public static ICommand Decode(CommandDto dto)
        {
            switch (dto.Kind)
            {
                case CommandKind.StartGame:
                    return new StartGameCommand(dto.P0);

                case CommandKind.AdvancePhase:
                    return new AdvancePhaseCommand();

                case CommandKind.ChooseStartNode:
                    return new ChooseStartNodeCommand(dto.Seat, dto.P0);

                case CommandKind.ResolveEventOption:
                    return new ResolveEventOptionCommand(dto.Seat, dto.P0, dto.P1,
                        acceptCost: (dto.Flags & AcceptCostBit) != 0,
                        grantRewards: (dto.Flags & GrantRewardsBit) != 0,
                        choices: DecodeAnswers(dto));

                case CommandKind.DiscardSupplyFacility:
                    return new DiscardSupplyFacilityCommand(dto.Seat, dto.P0);

                case CommandKind.BuildFacility:
                    return new BuildFacilityCommand(dto.Seat, dto.P0, dto.P1, (FacilityPayment)dto.P2,
                        DecodeAnswers(dto));

                case CommandKind.PlaceCharacterCard:
                    return new PlaceCharacterCardCommand(dto.Seat, dto.P0);

                case CommandKind.UseCharacterCard:
                    return new UseCharacterCardCommand(dto.Seat, (CharacterEffectKind)dto.P0, DecodeAnswers(dto));

                case CommandKind.EndCharacterCardUse:
                    return new EndCharacterCardUseCommand(dto.Seat);

                case CommandKind.DeclareCityPattern:
                    return new DeclareCityPatternCommand(dto.Seat, dto.P0, dto.P1);

                case CommandKind.Explore:
                    return new ExploreCommand(dto.Seat, dto.P0, DecodeRouteIds(dto), DecodePayees(dto));

                case CommandKind.ExploreInfluence:
                    return new ExploreInfluenceCommand(dto.Seat, dto.P0);

                case CommandKind.MoveMobileCity:
                    return new MoveMobileCityCommand(dto.Seat, dto.P0);

                case CommandKind.MoveCityInfluence:
                    return new MoveCityInfluenceCommand(dto.Seat, dto.P0);

                case CommandKind.PlaceInfluence:
                    return new PlaceInfluenceCommand(dto.Seat,
                        new InfluenceSlotRef((InfluenceSlotKind)dto.P0, dto.P1, dto.P2));

                case CommandKind.RelocateInfluence:
                    return new RelocateInfluenceCommand(dto.Seat, DecodeMoves(dto));

                case CommandKind.UsePatternSpecialAction:
                    return new UsePatternSpecialActionCommand(dto.Seat, dto.P0, DecodeAnswers(dto));

                case CommandKind.ResolveHarvest:
                    return new ResolveHarvestCommand(DecodeHarvestChoices(dto));

                case CommandKind.SubmitHarvestChoice:
                {
                    int count = Math.Max(0, dto.P1);
                    if (count > 0 && (dto.Items == null || dto.Items.Length < count))
                        throw new ArgumentException("SubmitHarvestChoiceCommand 的选点数与 Items 长度不符。", nameof(dto));

                    var nodes = new int[count];
                    if (count > 0) Array.Copy(dto.Items, 0, nodes, 0, count);
                    return new SubmitHarvestChoiceCommand(dto.Seat >= 0 ? dto.Seat : dto.P0, nodes);
                }

                case CommandKind.SkipPendingAction:
                    return new SkipPendingActionCommand(dto.Seat >= 0 ? dto.Seat : dto.P0);

                case CommandKind.PassAction:
                    return new PassActionCommand(dto.Seat >= 0 ? dto.Seat : dto.P0);

                case CommandKind.EndTurn:
                    return new EndTurnCommand(dto.Seat >= 0 ? dto.Seat : dto.P0);

                case CommandKind.ChooseInitialEnterprise:
                    return new ChooseInitialEnterpriseCommand(dto.Seat, dto.P0);

                case CommandKind.AdvanceEnterpriseLevel:
                    return new AdvanceEnterpriseLevelCommand(dto.Seat, dto.P0);

                case CommandKind.TriggerEnterpriseEffect:
                    return new TriggerEnterpriseEffectCommand(dto.Seat, dto.P0, dto.P1, DecodeAnswers(dto));

                default:
                    throw new ArgumentException($"无法解码未知的命令 Kind：{dto.Kind}。", nameof(dto));
            }
        }

        /// <summary>答案个数：只有 <see cref="RecordedChoices"/> 能过网（其它实现是本地回调，按 0 处理）。</summary>
        private static int AnswerCount(IEffectChoices choices) =>
            choices is RecordedChoices recorded ? recorded.Answers.Count : 0;

        /// <summary>答案列表：按提问顺序；没有答案时返回 null（对应 DTO 的 Items = null）。</summary>
        private static int[] AnswerItems(IEffectChoices choices)
        {
            if (!(choices is RecordedChoices recorded) || recorded.Answers.Count == 0) return null;

            var items = new int[recorded.Answers.Count];
            for (int i = 0; i < items.Length; i++) items[i] = recorded.Answers[i];
            return items;
        }

        /// <summary>
        /// 还原效果点选答案：P3 个答案按顺序读回。<c>strict = false</c> —— 正式回放时答案用尽是
        /// 「退回自动结算」而不是抛异常（见 <see cref="RecordedChoices"/>）。
        /// </summary>
        private static IEffectChoices DecodeAnswers(CommandDto dto)
        {
            int count = dto.P3;
            if (count <= 0) return null;
            if (dto.Items == null || dto.Items.Length < count)
                throw new ArgumentException("效果点选答案个数与 Items 长度不符。", nameof(dto));

            var answers = new int[count];
            Array.Copy(dto.Items, 0, answers, 0, count);
            return new RecordedChoices(answers, strict: false);
        }

        /// <summary>还原 ExploreCommand 的自选航道：Items 头部 P1 个 int；P1 ≤ 0 表示未指定（null）。</summary>
        private static IReadOnlyList<int> DecodeRouteIds(CommandDto dto)
        {
            if (dto.P1 <= 0) return null;
            if (dto.Items == null || dto.Items.Length < dto.P1)
                throw new ArgumentException("ExploreCommand 的航道数与 Items 长度不符。", nameof(dto));

            var routes = new int[dto.P1];
            Array.Copy(dto.Items, 0, routes, 0, dto.P1);
            return routes;
        }

        /// <summary>还原 ExploreCommand 的付给谁：Items 尾部 P2 对（航道 id、座位号）；P2 ≤ 0 表示未指定（null）。</summary>
        private static IReadOnlyDictionary<int, int> DecodePayees(CommandDto dto)
        {
            if (dto.P2 <= 0) return null;

            int start = dto.P1 > 0 ? dto.P1 : 0;
            if (dto.Items == null || dto.Items.Length < start + dto.P2 * 2)
                throw new ArgumentException("ExploreCommand 的付给谁列表与 Items 长度不符。", nameof(dto));

            var payees = new Dictionary<int, int>(dto.P2);
            for (int i = 0; i < dto.P2; i++)
            {
                int key = dto.Items[start + i * 2];
                int value = dto.Items[start + i * 2 + 1];
                payees[key] = value;
            }
            return payees;
        }

        /// <summary>还原采集选点：P0 个座位，每段 [seat, count, nodeId...]；P0 ≤ 0 表示空字典。</summary>
        private static IReadOnlyDictionary<int, IReadOnlyList<int>> DecodeHarvestChoices(CommandDto dto)
        {
            var result = new Dictionary<int, IReadOnlyList<int>>();
            if (dto.P0 <= 0) return result;

            int[] items = dto.Items ?? Array.Empty<int>();
            int index = 0;
            for (int s = 0; s < dto.P0; s++)
            {
                if (index + 2 > items.Length)
                    throw new ArgumentException("ResolveHarvestCommand 的选点列表与 Items 长度不符。", nameof(dto));

                int seat = items[index++];
                int count = items[index++];
                if (index + count > items.Length)
                    throw new ArgumentException("ResolveHarvestCommand 的选点列表与 Items 长度不符。", nameof(dto));

                var nodes = new int[count];
                for (int i = 0; i < count; i++) nodes[i] = items[index++];
                result[seat] = nodes;
            }
            return result;
        }

        /// <summary>还原 RelocateInfluenceCommand 的移动步：每步 6 个 int；P0 ≤ 0 表示没有移动（null）。</summary>
        private static IReadOnlyList<InfluenceMove> DecodeMoves(CommandDto dto)
        {
            if (dto.P0 <= 0) return null;
            if (dto.Items == null || dto.Items.Length < dto.P0 * 6)
                throw new ArgumentException("RelocateInfluenceCommand 的移动步数与 Items 长度不符。", nameof(dto));

            var moves = new InfluenceMove[dto.P0];
            for (int i = 0; i < dto.P0; i++)
            {
                int b = i * 6;
                var from = new InfluenceSlotRef((InfluenceSlotKind)dto.Items[b], dto.Items[b + 1], dto.Items[b + 2]);
                var to = new InfluenceSlotRef((InfluenceSlotKind)dto.Items[b + 3], dto.Items[b + 4], dto.Items[b + 5]);
                moves[i] = new InfluenceMove(from, to);
            }
            return moves;
        }
    }
}
