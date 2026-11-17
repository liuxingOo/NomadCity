using System;
using System.Collections.Generic;
using System.Text;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// <see cref="GameStateSnapshot"/> 的纯 C# 字节编解码（Core 里没有 Unity 的 DataStreamWriter，所以手写）。
    /// 全部字段按 int 小端序排列，字符串写成「长度 + UTF-8 字节」。字段顺序与
    /// <see cref="Write"/> / <see cref="Read"/> 两段代码**一一对应、紧挨着放**，改一处必须改另一处。
    /// </summary>
    public static class GameStateSnapshotCodec
    {
        /// <summary>抓取当前状态并编码成字节。</summary>
        public static byte[] Write(GameStateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var writer = new SnapshotWriter();

            writer.Int(GameStateSnapshot.Version);
            writer.Int(snapshot.PlayerCount);
            writer.Int((int)snapshot.Expansions);
            writer.Int(snapshot.TurnNumber);
            writer.Int((int)snapshot.Phase);
            writer.Int(snapshot.FirstPlayerSeat);
            writer.Int(snapshot.ActiveSeat);
            writer.Int(snapshot.ActionRound);
            writer.Bool(snapshot.HarvestPending);
            writer.Int(snapshot.CityMoveOriginNodeId);
            writer.Int(snapshot.SupplyDiscardRemaining);
            writer.Int(snapshot.SupplyDiscardSeat);
            writer.Bool(snapshot.IsGameOver);
            writer.Bool(snapshot.DeveloperMode);
            writer.Ints(snapshot.DistrictScores);
            writer.Ints(snapshot.SeatOrder);
            writer.Int(snapshot.PendingStartMarkerSeat);
            writer.Int(snapshot.LastCityMoveRouteId);
            writer.Int(snapshot.ActiveSpecialActionPatternId);

            // —— 企业扩展 ——
            writer.Bool(snapshot.EnterpriseDraftDone);
            writer.Int(snapshot.EnterprisePendingAdvanceSeat);
            writer.Int(snapshot.EnterprisePendingAdvanceExcludeBoard);
            writer.String(snapshot.EnterprisePendingAdvanceReason);
            writer.Bool(snapshot.EnterpriseRedZoneAdvanceStarted);
            writer.Int(snapshot.EnterpriseRedZoneAdvanceSeat);
            writer.Int(snapshot.EnterpriseRedZoneAdvanceDoneMask);
            writer.Int(snapshot.EnterprisePicksPerPlayer);
            writer.Int(snapshot.EnterpriseOffersPerSeat);
            writer.Ints(snapshot.EnterpriseLevels);
            writer.Ints(snapshot.EnterpriseUsedMasks);
            writer.Ints(snapshot.EnterpriseOffers);
            writer.Ints(snapshot.EnterpriseDeck);
            writer.Ints(snapshot.EnterpriseOperatorDeck);
            writer.Ints(snapshot.EnterpriseBoards);
            writer.Ints(snapshot.EnterpriseChosen);
            writer.Ints(snapshot.EnterpriseSecondChosen);
            writer.Int(snapshot.EnterpriseRhineDepartment);

            writer.Int(snapshot.PublicReveals.Count);
            foreach (GameStateSnapshot.PublicRevealSnapshot reveal in snapshot.PublicReveals)
            {
                writer.Int(reveal.Seat);
                writer.Int(reveal.CardId);
                writer.String(reveal.Reason);
            }

            writer.Int(snapshot.PersistentTactics.Count);
            foreach (GameStateSnapshot.PersistentTacticSnapshot tactic in snapshot.PersistentTactics)
            {
                writer.Int(tactic.CardId);
                writer.Int(tactic.Seat);
                writer.Int(tactic.Kind);
                writer.Int(tactic.Markers);
                writer.Bool(tactic.UsedCleanupThisTurn);
                writer.Ints(tactic.AttachedFacilityIds);
            }

            // —— 挂起行动（行动事件） ——
            writer.Int(snapshot.NextPendingActionId);
            writer.Int(snapshot.PendingActions.Count);
            foreach (GameStateSnapshot.PendingActionSnapshot pending in snapshot.PendingActions)
            {
                writer.Int(pending.Id);
                writer.Int(pending.Seat);
                writer.Int(pending.Kind);
                writer.Int(pending.Remaining);
                writer.Bool(pending.Free);
                writer.Int(pending.MaxVoucherSpend);
                writer.Int(pending.Scope);
                writer.Int(pending.NodeScope);
                writer.Int(pending.SourceCardId);
                writer.String(pending.SourceLabel);
            }

            // —— 延迟结算队列 ——
            writer.Int(snapshot.DeferredEffects.Count);
            foreach (GameStateSnapshot.DeferredEffectSnapshot deferred in snapshot.DeferredEffects)
            {
                writer.Int(deferred.Seat);
                WriteOps(writer, deferred.Ops);
            }

            // —— 玩家 ——
            writer.Int(snapshot.Players.Count);
            foreach (GameStateSnapshot.PlayerSnapshot player in snapshot.Players)
            {
                writer.Int(player.Seat);
                writer.Int((int)player.Color);
                writer.Int(player.Orirock);
                writer.Int(player.Originium);
                writer.Int(player.Oriron);
                writer.Int(player.OriginitePrime);
                writer.Int(player.Vouchers);
                writer.Int(player.Score);
                writer.Int(player.MarkersInSupply);
                writer.Ints(player.HandCharacterCards);
                writer.Ints(player.DiscardCharacterCards);
                writer.Int(player.PlacedCharacterCard);
                writer.Bool(player.PlacedCardReturnsToBox);
                writer.Bool(player.UsedCharacterCardThisTurn);
                writer.Bool(player.UsedCharacterStrategyThisTurn);
                writer.Bool(player.UsedCharacterTacticsThisTurn);
                writer.Bool(player.CharacterSecondEffectPending);
                writer.Ints(player.BuiltFacilities);
                writer.Ints(player.CityPanelSlots);
                writer.Bools(player.CityPanelUsed);
                writer.Int(player.MobileCityNodeId);
                writer.Ints(player.UsedPatternActionsThisTurn);
                writer.Int(player.ExtraMainActionsThisTurn);
                writer.Int(player.ExtraMainActionsExcludingSpecialThisTurn);
                writer.Bool(player.BothCharacterEffectsThisTurn);
                writer.Int(player.CharacterCardBanRound);
                writer.Bool(player.FreeMoveRelocatesInfluenceThisTurn);
                writer.Bool(player.SetupDiscardDone);
            }

            // —— 资源点 ——
            writer.Int(snapshot.Nodes.Count);
            foreach (GameStateSnapshot.NodeSnapshot node in snapshot.Nodes)
            {
                writer.Int(node.Id);
                writer.String(node.Code);
                writer.Int((int)node.District);
                writer.Int((int)node.Color);
                writer.Bool(node.IsRedZone);
                writer.Int(node.Capacity);
                writer.Bool(node.HasResourceToken);
                writer.Int((int)node.TokenResource);
                writer.Int(node.TokenAmount);
                writer.Int(node.EventCardId);
                writer.Bool(node.EventResolved);
                writer.Bool(node.ExplorePending);
                writer.Ints(node.Owners);
            }

            // —— 航道 ——
            writer.Int(snapshot.Routes.Count);
            foreach (GameStateSnapshot.RouteSnapshot route in snapshot.Routes)
            {
                writer.Int(route.Id);
                writer.Ints(route.Nodes);
                writer.Int((int)route.District);
                writer.Int(route.Capacity);
                writer.Ints(route.Owners);
                writer.Bool(route.IsHighway);
            }

            // —— 公共牌堆 ——
            writer.Ints(snapshot.FacilityDeck);
            writer.Ints(snapshot.FacilitySupply);
            writer.Ints(snapshot.ExtensionHubSupply);
            writer.Ints(snapshot.EventDeckGreen);
            writer.Ints(snapshot.EventDeckYellow);
            writer.Ints(snapshot.EventDeckRed);

            // —— 城市样式牌 ——
            writer.Ints(snapshot.CityPatternCards);
            writer.Int(snapshot.CityPatternStates.Count);
            foreach (GameStateSnapshot.PatternSnapshot pattern in snapshot.CityPatternStates)
            {
                writer.Int(pattern.CardId);
                writer.Int(pattern.Level);
                writer.Int(pattern.Markers.Count);
                foreach (GameStateSnapshot.MarkerSnapshot marker in pattern.Markers)
                {
                    writer.Int(marker.Seat);
                    writer.Int(marker.Position);
                }
            }

            return writer.ToArray();
        }

        /// <summary>把字节还原成快照；版本不符或数据长度不对时抛出异常（让上层明确拒绝这个包）。</summary>
        public static GameStateSnapshot Read(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            var reader = new SnapshotReader(buffer, offset, length);

            int version = reader.Int();
            if (version != GameStateSnapshot.Version)
                throw new InvalidOperationException(
                    $"快照版本不一致：收到 {version}，本端支持 {GameStateSnapshot.Version}。");

            var snapshot = new GameStateSnapshot
            {
                PlayerCount = reader.Int(),
                Expansions = (NomadCity.Core.Rules.ExpansionFlags)reader.Int(),
                TurnNumber = reader.Int(),
                Phase = (NomadCity.Core.Model.TurnPhase)reader.Int(),
                FirstPlayerSeat = reader.Int(),
                ActiveSeat = reader.Int(),
                ActionRound = reader.Int(),
                HarvestPending = reader.Bool(),
                CityMoveOriginNodeId = reader.Int(),
                SupplyDiscardRemaining = reader.Int(),
                SupplyDiscardSeat = reader.Int(),
                IsGameOver = reader.Bool(),
                DeveloperMode = reader.Bool(),
                DistrictScores = reader.Ints(),
                SeatOrder = reader.Ints(),
                PendingStartMarkerSeat = reader.Int(),
                LastCityMoveRouteId = reader.Int(),
                ActiveSpecialActionPatternId = reader.Int(),
                EnterpriseDraftDone = reader.Bool(),
                EnterprisePendingAdvanceSeat = reader.Int(),
                EnterprisePendingAdvanceExcludeBoard = reader.Int(),
                EnterprisePendingAdvanceReason = reader.String(),
                EnterpriseRedZoneAdvanceStarted = reader.Bool(),
                EnterpriseRedZoneAdvanceSeat = reader.Int(),
                EnterpriseRedZoneAdvanceDoneMask = reader.Int(),
                EnterprisePicksPerPlayer = reader.Int(),
                EnterpriseOffersPerSeat = reader.Int(),
                EnterpriseLevels = reader.Ints(),
                EnterpriseUsedMasks = reader.Ints(),
                EnterpriseOffers = reader.Ints(),
            };

            snapshot.EnterpriseDeck.AddRange(reader.Ints());
            snapshot.EnterpriseOperatorDeck.AddRange(reader.Ints());
            snapshot.EnterpriseBoards.AddRange(reader.Ints());
            snapshot.EnterpriseChosen.AddRange(reader.Ints());
            snapshot.EnterpriseSecondChosen.AddRange(reader.Ints());
            snapshot.EnterpriseRhineDepartment = reader.Int();

            int revealCount = reader.Int();
            for (int i = 0; i < revealCount; i++)
            {
                snapshot.PublicReveals.Add(new GameStateSnapshot.PublicRevealSnapshot
                {
                    Seat = reader.Int(),
                    CardId = reader.Int(),
                    Reason = reader.String(),
                });
            }

            int persistentCount = reader.Int();
            for (int i = 0; i < persistentCount; i++)
            {
                var tactic = new GameStateSnapshot.PersistentTacticSnapshot
                {
                    CardId = reader.Int(),
                    Seat = reader.Int(),
                    Kind = reader.Int(),
                    Markers = reader.Int(),
                    UsedCleanupThisTurn = reader.Bool(),
                };
                tactic.AttachedFacilityIds.AddRange(reader.Ints());
                snapshot.PersistentTactics.Add(tactic);
            }

            // —— 挂起行动（行动事件） ——
            snapshot.NextPendingActionId = reader.Int();
            int pendingCount = reader.Int();
            for (int i = 0; i < pendingCount; i++)
            {
                snapshot.PendingActions.Add(new GameStateSnapshot.PendingActionSnapshot
                {
                    Id = reader.Int(),
                    Seat = reader.Int(),
                    Kind = reader.Int(),
                    Remaining = reader.Int(),
                    Free = reader.Bool(),
                    MaxVoucherSpend = reader.Int(),
                    Scope = reader.Int(),
                    NodeScope = reader.Int(),
                    SourceCardId = reader.Int(),
                    SourceLabel = reader.String(),
                });
            }

            // —— 延迟结算队列 ——
            int deferredCount = reader.Int();
            for (int i = 0; i < deferredCount; i++)
            {
                var deferred = new GameStateSnapshot.DeferredEffectSnapshot { Seat = reader.Int() };
                ReadOps(reader, deferred.Ops);
                snapshot.DeferredEffects.Add(deferred);
            }

            // —— 玩家 ——
            int playerCount = reader.Int();
            for (int i = 0; i < playerCount; i++)
            {
                var player = new GameStateSnapshot.PlayerSnapshot
                {
                    Seat = reader.Int(),
                    Color = (NomadCity.Core.Model.PlayerColor)reader.Int(),
                    Orirock = reader.Int(),
                    Originium = reader.Int(),
                    Oriron = reader.Int(),
                    OriginitePrime = reader.Int(),
                    Vouchers = reader.Int(),
                    Score = reader.Int(),
                    MarkersInSupply = reader.Int(),
                };

                player.HandCharacterCards.AddRange(reader.Ints());
                player.DiscardCharacterCards.AddRange(reader.Ints());
                player.PlacedCharacterCard = reader.Int();
                player.PlacedCardReturnsToBox = reader.Bool();
                player.UsedCharacterCardThisTurn = reader.Bool();
                player.UsedCharacterStrategyThisTurn = reader.Bool();
                player.UsedCharacterTacticsThisTurn = reader.Bool();
                player.CharacterSecondEffectPending = reader.Bool();
                player.BuiltFacilities.AddRange(reader.Ints());
                player.CityPanelSlots = reader.Ints();
                player.CityPanelUsed = reader.Bools();
                player.MobileCityNodeId = reader.Int();
                player.UsedPatternActionsThisTurn.AddRange(reader.Ints());
                player.ExtraMainActionsThisTurn = reader.Int();
                player.ExtraMainActionsExcludingSpecialThisTurn = reader.Int();
                player.BothCharacterEffectsThisTurn = reader.Bool();
                player.CharacterCardBanRound = reader.Int();
                player.FreeMoveRelocatesInfluenceThisTurn = reader.Bool();
                player.SetupDiscardDone = reader.Bool();

                snapshot.Players.Add(player);
            }

            // —— 资源点 ——
            int nodeCount = reader.Int();
            for (int i = 0; i < nodeCount; i++)
            {
                var node = new GameStateSnapshot.NodeSnapshot
                {
                    Id = reader.Int(),
                    Code = reader.String(),
                    District = (NomadCity.Core.Model.District)reader.Int(),
                    Color = (NomadCity.Core.Model.NodeColor)reader.Int(),
                    IsRedZone = reader.Bool(),
                    Capacity = reader.Int(),
                    HasResourceToken = reader.Bool(),
                    TokenResource = (NomadCity.Core.Model.ResourceType)reader.Int(),
                    TokenAmount = reader.Int(),
                    EventCardId = reader.Int(),
                    EventResolved = reader.Bool(),
                    ExplorePending = reader.Bool(),
                    Owners = reader.Ints(),
                };
                snapshot.Nodes.Add(node);
            }

            // —— 航道 ——
            int routeCount = reader.Int();
            for (int i = 0; i < routeCount; i++)
            {
                var route = new GameStateSnapshot.RouteSnapshot
                {
                    Id = reader.Int(),
                    Nodes = reader.Ints(),
                    District = (NomadCity.Core.Model.District)reader.Int(),
                    Capacity = reader.Int(),
                    Owners = reader.Ints(),
                    IsHighway = reader.Bool(),
                };
                snapshot.Routes.Add(route);
            }

            // —— 公共牌堆 ——
            snapshot.FacilityDeck.AddRange(reader.Ints());
            snapshot.FacilitySupply.AddRange(reader.Ints());
            snapshot.ExtensionHubSupply.AddRange(reader.Ints());
            snapshot.EventDeckGreen.AddRange(reader.Ints());
            snapshot.EventDeckYellow.AddRange(reader.Ints());
            snapshot.EventDeckRed.AddRange(reader.Ints());

            // —— 城市样式牌 ——
            snapshot.CityPatternCards.AddRange(reader.Ints());
            int patternCount = reader.Int();
            for (int i = 0; i < patternCount; i++)
            {
                var pattern = new GameStateSnapshot.PatternSnapshot
                {
                    CardId = reader.Int(),
                    Level = reader.Int(),
                };

                int markerCount = reader.Int();
                for (int m = 0; m < markerCount; m++)
                {
                    pattern.Markers.Add(new GameStateSnapshot.MarkerSnapshot
                    {
                        Seat = reader.Int(),
                        Position = reader.Int(),
                    });
                }
                snapshot.CityPatternStates.Add(pattern);
            }

            return snapshot;
        }

        /// <summary>递归写出一串效果（Used by 延迟结算队列）。</summary>
        private static void WriteOps(SnapshotWriter writer,
            IReadOnlyList<GameStateSnapshot.RewardOpSnapshot> ops)
        {
            writer.Int(ops.Count);
            foreach (GameStateSnapshot.RewardOpSnapshot op in ops)
            {
                writer.Int(op.Kind);
                writer.Int(op.Resource);
                writer.Int(op.Amount);
                writer.Int(op.Scope);
                writer.Int(op.Count);
                writer.Int(op.SpreadOptions.Count);
                foreach (int type in op.SpreadOptions) writer.Int(type);

                writer.Int(op.Branches.Count);
                foreach (GameStateSnapshot.EffectBranchSnapshot branch in op.Branches)
                {
                    writer.String(branch.Label);
                    WriteOps(writer, branch.Ops);
                }

                WriteOps(writer, op.Ops);
            }
        }

        /// <summary>递归读回一串效果（与 <see cref="WriteOps"/> 对称）。</summary>
        private static void ReadOps(SnapshotReader reader,
            List<GameStateSnapshot.RewardOpSnapshot> ops)
        {
            int count = reader.Int();
            for (int i = 0; i < count; i++)
            {
                var op = new GameStateSnapshot.RewardOpSnapshot
                {
                    Kind = reader.Int(),
                    Resource = reader.Int(),
                    Amount = reader.Int(),
                    Scope = reader.Int(),
                    Count = reader.Int(),
                };
                op.SpreadOptions.AddRange(reader.Ints());

                int branchCount = reader.Int();
                for (int b = 0; b < branchCount; b++)
                {
                    var branch = new GameStateSnapshot.EffectBranchSnapshot { Label = reader.String() };
                    ReadOps(reader, branch.Ops);
                    op.Branches.Add(branch);
                }

                ReadOps(reader, op.Ops);
                ops.Add(op);
            }
        }

        // —— 内部读写工具：全 int 小端序，变长列表写成「个数 + 元素」——

        private sealed class SnapshotWriter
        {
            private readonly List<byte> bytes = new List<byte>(1024);

            public void Int(int value)
            {
                bytes.Add((byte)value);
                bytes.Add((byte)(value >> 8));
                bytes.Add((byte)(value >> 16));
                bytes.Add((byte)(value >> 24));
            }

            public void Bool(bool value) => Int(value ? 1 : 0);

            public void Ints(IReadOnlyList<int> values)
            {
                if (values == null)
                {
                    Int(0);
                    return;
                }

                Int(values.Count);
                for (int i = 0; i < values.Count; i++) Int(values[i]);
            }

            public void Bools(IReadOnlyList<bool> values)
            {
                if (values == null)
                {
                    Int(0);
                    return;
                }

                Int(values.Count);
                for (int i = 0; i < values.Count; i++) Bool(values[i]);
            }

            public void String(string value)
            {
                if (value == null)
                {
                    Int(-1);
                    return;
                }

                byte[] encoded = Encoding.UTF8.GetBytes(value);
                Int(encoded.Length);
                bytes.AddRange(encoded);
            }

            public byte[] ToArray() => bytes.ToArray();
        }

        private sealed class SnapshotReader
        {
            private readonly byte[] buffer;
            private readonly int end;
            private int pos;

            public SnapshotReader(byte[] buffer, int offset, int length)
            {
                if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
                if (offset + length > buffer.Length)
                    throw new ArgumentException("快照读取区间超出了缓冲区范围。", nameof(length));

                this.buffer = buffer;
                pos = offset;
                end = offset + length;
            }

            public int Int()
            {
                Require(4);
                int value = buffer[pos]
                            | (buffer[pos + 1] << 8)
                            | (buffer[pos + 2] << 16)
                            | (buffer[pos + 3] << 24);
                pos += 4;
                return value;
            }

            public bool Bool() => Int() != 0;

            public int[] Ints()
            {
                int count = Int();
                if (count < 0) throw new ArgumentException("快照里的列表长度不能为负。", nameof(buffer));
                if (count > 0) Require(count * 4);

                var values = new int[count];
                for (int i = 0; i < count; i++) values[i] = Int();
                return values;
            }

            public bool[] Bools()
            {
                int count = Int();
                if (count < 0) throw new ArgumentException("快照里的列表长度不能为负。", nameof(buffer));
                if (count > 0) Require(count * 4);

                var values = new bool[count];
                for (int i = 0; i < count; i++) values[i] = Bool();
                return values;
            }

            public string String()
            {
                int length = Int();
                if (length < 0) return null;
                Require(length);

                string value = Encoding.UTF8.GetString(buffer, pos, length);
                pos += length;
                return value;
            }

            private void Require(int count)
            {
                if (count > end - pos)
                    throw new ArgumentException("快照数据不完整（字段长度超出了包体）。", nameof(buffer));
            }
        }
    }
}
