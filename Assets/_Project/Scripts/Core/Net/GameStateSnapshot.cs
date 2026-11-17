using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// 一局游戏状态的完整快照（阶段 3：开局同步）。
    /// 主机开局后广播一次，客户端据此建立一份 <see cref="GameState"/> 副本，之后靠重放命令流保持一致。
    ///
    /// 快照**自包含**：地图节点与航道的静态属性（编号、区块、颜色、空格数）也一并带上，
    /// 客户端不需要再用自己的 MapData 重建地图，避免两端资源不一致导致坐标 / 邻接错位。
    /// <see cref="GameState.Catalog"/> 是静态查表数据（牌面文案、资源图标等），不进快照，由调用方在
    /// <see cref="Restore"/> 时传入自己那份目录。
    /// </summary>
    public sealed class GameStateSnapshot
    {
        /// <summary>当前快照格式版本；协议握手时与对方比对。</summary>
        public const int Version = 11;

        public int PlayerCount;
        public ExpansionFlags Expansions;

        public int TurnNumber;
        public TurnPhase Phase;
        public int FirstPlayerSeat;
        public int ActiveSeat;
        public int ActionRound;
        public bool HarvestPending;
        public int CityMoveOriginNodeId;
        public int SupplyDiscardRemaining;
        public int SupplyDiscardSeat;
        public bool IsGameOver;
        public bool DeveloperMode;

        public int[] DistrictScores = Array.Empty<int>();

        /// <summary>开局随机排出的行动顺序（座位号，下标 0 = 起始玩家）。</summary>
        public int[] SeatOrder = Array.Empty<int>();

        public int PendingStartMarkerSeat = -1;
        public int LastCityMoveRouteId = -1;
        public int ActiveSpecialActionPatternId = -1;

        /// <summary>向所有玩家公开展示过的牌（只追加）。</summary>
        public readonly List<PublicRevealSnapshot> PublicReveals = new();

        // —— 企业扩展 ——
        public bool EnterpriseDraftDone;
        public int EnterprisePendingAdvanceSeat = -1;
        public int EnterprisePendingAdvanceExcludeBoard = -1;
        public string EnterprisePendingAdvanceReason = string.Empty;

        // —— 企业扩展：红色区域开放的可同步进度 ——
        public bool EnterpriseRedZoneAdvanceStarted;
        public int EnterpriseRedZoneAdvanceSeat = -1;
        public int EnterpriseRedZoneAdvanceDoneMask;

        /// <summary>合作等级：按「企业板 id × 座位号」拉平，长度 = 8 × 人数。</summary>
        public int[] EnterpriseLevels = Array.Empty<int>();

        /// <summary>本回合已触发的企业特效位掩码：同样按「企业板 id × 座位号」拉平。</summary>
        public int[] EnterpriseUsedMasks = Array.Empty<int>();

        /// <summary>各座位开局抽到的企业板，按「座位 × <see cref="EnterpriseOffersPerSeat"/>」拉平（-1 = 空）。</summary>
        public int[] EnterpriseOffers = Array.Empty<int>();

        /// <summary>各座位每人要选几张企业板（3／4 人局 1，2 人局 2）。</summary>
        public int EnterprisePicksPerPlayer = 1;

        /// <summary>各座位抽到的张数（= PicksPerPlayer × 2）。</summary>
        public int EnterpriseOffersPerSeat = 2;

        public readonly List<int> EnterpriseDeck = new();
        public readonly List<int> EnterpriseOperatorDeck = new();
        public readonly List<int> EnterpriseBoards = new();
        public readonly List<int> EnterpriseChosen = new();
        public readonly List<int> EnterpriseSecondChosen = new();

        /// <summary>莱茵生命板级激活科室板 id；-1 = 还没选定。</summary>
        public int EnterpriseRhineDepartment = -1;

        /// <summary>持续留在场上的干员计谋（∞ 计谋）。</summary>
        public readonly List<PersistentTacticSnapshot> PersistentTactics = new();

        /// <summary>卡牌效果授予、等待界面执行的挂起行动。</summary>
        public readonly List<PendingActionSnapshot> PendingActions = new();

        /// <summary>下一条挂起行动的 id。</summary>
        public int NextPendingActionId = 1;

        /// <summary>一条挂起行动的纯数据镜像。</summary>
        public sealed class PendingActionSnapshot
        {
            public int Id;
            public int Seat;
            public int Kind;
            public int Remaining;
            public bool Free;
            public int MaxVoucherSpend;
            public int Scope;
            public int NodeScope;
            public int SourceCardId;
            public string SourceLabel = string.Empty;
        }

        /// <summary>延迟到收尾阶段结算的效果队列（按入队顺序）。</summary>
        public readonly List<DeferredEffectSnapshot> DeferredEffects = new();

        public sealed class PersistentTacticSnapshot
        {
            public int CardId;
            public int Seat;
            public int Kind;
            public int Markers;
            public bool UsedCleanupThisTurn;
            public readonly List<int> AttachedFacilityIds = new();
        }

        /// <summary>一条「向所有玩家展示」的记录。</summary>
        public sealed class PublicRevealSnapshot
        {
            public int Seat;
            public int CardId;
            public string Reason = string.Empty;
        }

        /// <summary>延迟结算队列里的一条：谁的、要执行哪串效果。</summary>
        public sealed class DeferredEffectSnapshot
        {
            public int Seat;
            public readonly List<RewardOpSnapshot> Ops = new();
        }

        /// <summary>RewardOp 的纯数据镜像，支持递归（分支与延迟内嵌 ops）。</summary>
        public sealed class RewardOpSnapshot
        {
            public int Kind;
            public int Resource;
            public int Amount;
            public int Scope;
            public int Count;
            public readonly List<int> SpreadOptions = new();
            public readonly List<EffectBranchSnapshot> Branches = new();
            public readonly List<RewardOpSnapshot> Ops = new();
        }

        public sealed class EffectBranchSnapshot
        {
            public string Label = string.Empty;
            public readonly List<RewardOpSnapshot> Ops = new();
        }

        public readonly List<PlayerSnapshot> Players = new();
        public readonly List<NodeSnapshot> Nodes = new();
        public readonly List<RouteSnapshot> Routes = new();

        public readonly List<int> FacilityDeck = new();
        public readonly List<int> FacilitySupply = new();
        public readonly List<int> ExtensionHubSupply = new();
        public readonly List<int> EventDeckGreen = new();
        public readonly List<int> EventDeckYellow = new();
        public readonly List<int> EventDeckRed = new();

        public readonly List<int> CityPatternCards = new();
        public readonly List<PatternSnapshot> CityPatternStates = new();

        /// <summary>
        /// 把一份运行时状态抓成快照。延迟结算队列目前不支持序列化（<see cref="DeferredEffect"/> 里嵌着
        /// 递归的 <see cref="RewardOp"/>，没有稳定的文本表示），非空时直接抛出，避免悄悄丢数据。
        /// 开局时该队列必为空，所以正常开局同步不受影响。
        /// </summary>
        public static GameStateSnapshot Capture(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var snapshot = new GameStateSnapshot
            {
                PlayerCount = state.Setup.PlayerCount,
                Expansions = state.Setup.Expansions,
                TurnNumber = state.TurnNumber,
                Phase = state.Phase,
                FirstPlayerSeat = state.FirstPlayerSeat,
                ActiveSeat = state.ActiveSeat,
                ActionRound = state.ActionRound,
                HarvestPending = state.HarvestPending,
                CityMoveOriginNodeId = state.CityMoveOriginNodeId,
                SupplyDiscardRemaining = state.SupplyDiscardRemaining,
                SupplyDiscardSeat = state.SupplyDiscardSeat,
                IsGameOver = state.IsGameOver,
                DeveloperMode = state.DeveloperMode,
                DistrictScores = (int[])state.DistrictScores.Clone(),
                SeatOrder = state.SeatOrder == null ? Array.Empty<int>() : (int[])state.SeatOrder.Clone(),
                PendingStartMarkerSeat = state.PendingStartMarkerSeat,
                LastCityMoveRouteId = state.LastCityMoveRouteId,
                ActiveSpecialActionPatternId = state.ActiveSpecialActionPatternId,
                NextPendingActionId = state.NextPendingActionId,
            };

            foreach (PlayerState player in state.Players) snapshot.Players.Add(PlayerSnapshot.Capture(player));

            foreach (MapNodeState node in state.Map.Nodes) snapshot.Nodes.Add(NodeSnapshot.Capture(node));
            foreach (RouteState route in state.Map.Routes) snapshot.Routes.Add(RouteSnapshot.Capture(route));

            snapshot.FacilityDeck.AddRange(state.FacilityDeck);
            snapshot.FacilitySupply.AddRange(state.FacilitySupply);
            snapshot.ExtensionHubSupply.AddRange(state.ExtensionHubSupply);
            snapshot.EventDeckGreen.AddRange(state.EventDeckGreen);
            snapshot.EventDeckYellow.AddRange(state.EventDeckYellow);
            snapshot.EventDeckRed.AddRange(state.EventDeckRed);

            snapshot.CityPatternCards.AddRange(state.CityPatternCards);
            foreach (CityPatternState pattern in state.CityPatternStates)
                snapshot.CityPatternStates.Add(PatternSnapshot.Capture(pattern));

            foreach (PublicReveal reveal in state.PublicReveals)
            {
                snapshot.PublicReveals.Add(new PublicRevealSnapshot
                {
                    Seat = reveal.Seat,
                    CardId = reveal.CardId,
                    Reason = reveal.Reason,
                });
            }

            // —— 企业扩展 ——
            EnterpriseState enterprises = state.Enterprises;
            if (enterprises != null)
            {
                snapshot.EnterpriseDraftDone = enterprises.DraftDone;
                snapshot.EnterprisePendingAdvanceSeat = enterprises.PendingAdvanceSeat;
                snapshot.EnterprisePendingAdvanceExcludeBoard = enterprises.PendingAdvanceExcludeBoard;
                snapshot.EnterprisePendingAdvanceReason = enterprises.PendingAdvanceReason ?? string.Empty;
                snapshot.EnterpriseRedZoneAdvanceStarted = enterprises.RedZoneAdvanceStarted;
                snapshot.EnterpriseRedZoneAdvanceSeat = enterprises.RedZoneAdvanceSeat;
                snapshot.EnterpriseRedZoneAdvanceDoneMask = enterprises.RedZoneAdvanceDoneMask;

                snapshot.EnterpriseDeck.AddRange(enterprises.Deck);
                snapshot.EnterpriseOperatorDeck.AddRange(enterprises.OperatorDeck);
                snapshot.EnterpriseBoards.AddRange(enterprises.ActiveBoards);
                snapshot.EnterpriseChosen.AddRange(enterprises.ChosenBoards);
                snapshot.EnterpriseSecondChosen.AddRange(enterprises.SecondChosenBoards);
                snapshot.EnterpriseRhineDepartment = enterprises.RhineDepartment;
                int playerCount = state.Players.Count;
                int offersPerSeat = enterprises.OffersPerSeat < 1 ? 2 : enterprises.OffersPerSeat;
                snapshot.EnterprisePicksPerPlayer = enterprises.PicksPerPlayer;
                snapshot.EnterpriseOffersPerSeat = offersPerSeat;
                snapshot.EnterpriseOffers = new int[playerCount * offersPerSeat];
                for (int seat = 0; seat < playerCount; seat++)
                {
                    List<int> offer = seat < enterprises.Offers.Count ? enterprises.Offers[seat] : null;
                    for (int slot = 0; slot < offersPerSeat; slot++)
                    {
                        snapshot.EnterpriseOffers[seat * offersPerSeat + slot] =
                            offer != null && slot < offer.Count ? offer[slot] : -1;
                    }
                }

                snapshot.EnterpriseLevels = new int[EnterpriseCatalog.BoardCount * playerCount];
                snapshot.EnterpriseUsedMasks = new int[EnterpriseCatalog.BoardCount * playerCount];
                for (int board = 0; board < EnterpriseCatalog.BoardCount; board++)
                {
                    for (int seat = 0; seat < playerCount; seat++)
                    {
                        int index = board * playerCount + seat;
                        snapshot.EnterpriseLevels[index] = enterprises.LevelOf(board, seat);
                        snapshot.EnterpriseUsedMasks[index] = enterprises.UsedMaskOf(board, seat);
                    }
                }
            }

            foreach (PersistentTacticState tactic in state.PersistentTactics)
            {
                var entry = new PersistentTacticSnapshot
                {
                    CardId = tactic.CardId,
                    Seat = tactic.Seat,
                    Kind = (int)tactic.Kind,
                    Markers = tactic.Markers,
                    UsedCleanupThisTurn = tactic.UsedCleanupThisTurn,
                };
                entry.AttachedFacilityIds.AddRange(tactic.AttachedFacilityIds);
                snapshot.PersistentTactics.Add(entry);
            }

            foreach (PendingAction pending in state.PendingActions)
            {
                snapshot.PendingActions.Add(new PendingActionSnapshot
                {
                    Id = pending.Id,
                    Seat = pending.Seat,
                    Kind = (int)pending.Kind,
                    Remaining = pending.Remaining,
                    Free = pending.Limits.Free,
                    MaxVoucherSpend = pending.Limits.MaxVoucherSpend,
                    Scope = (int)pending.Limits.Scope,
                    NodeScope = (int)pending.Limits.NodeScope,
                    SourceCardId = pending.SourceCardId,
                    SourceLabel = pending.SourceLabel ?? string.Empty,
                });
            }

            foreach (DeferredEffect deferred in state.DeferredEffects)
            {
                var entry = new DeferredEffectSnapshot { Seat = deferred.Seat };
                entry.Ops.AddRange(CaptureOps(deferred.Ops));
                snapshot.DeferredEffects.Add(entry);
            }

            return snapshot;
        }

        /// <summary>把效果序列递归镜像成纯数据（供线格式序列化 / 还原）。</summary>
        private static IEnumerable<RewardOpSnapshot> CaptureOps(IReadOnlyList<RewardOp> ops)
        {
            if (ops == null) yield break;
            foreach (RewardOp op in ops) yield return CaptureOp(op);
        }

        private static RewardOpSnapshot CaptureOp(RewardOp op)
        {
            var snapshot = new RewardOpSnapshot
            {
                Kind = (int)op.Kind,
                Resource = (int)op.Resource,
                Amount = op.Amount,
                Scope = (int)op.Scope,
                Count = (int)op.Count,
            };

            if (op.SpreadOptions != null)
                foreach (ResourceType type in op.SpreadOptions) snapshot.SpreadOptions.Add((int)type);

            if (op.Branches != null)
            {
                foreach (EffectBranch branch in op.Branches)
                {
                    var branchSnapshot = new EffectBranchSnapshot { Label = branch.Label ?? string.Empty };
                    branchSnapshot.Ops.AddRange(CaptureOps(branch.Ops));
                    snapshot.Branches.Add(branchSnapshot);
                }
            }

            if (op.Ops != null) snapshot.Ops.AddRange(CaptureOps(op.Ops));
            return snapshot;
        }

        /// <summary>
        /// 还原成一份全新的 <see cref="GameState"/>。catalog 由调用方提供（静态查表数据，不随对局变化）。
        /// 延迟结算队列会连同其中递归的效果序列一并还原。
        /// </summary>
        public GameState Restore(ICardCatalog catalog)
        {
            var state = new GameState(new GameSetup(PlayerCount, Expansions))
            {
                Catalog = catalog,
                TurnNumber = TurnNumber,
                Phase = Phase,
                FirstPlayerSeat = FirstPlayerSeat,
                ActiveSeat = ActiveSeat,
                ActionRound = ActionRound,
                HarvestPending = HarvestPending,
                CityMoveOriginNodeId = CityMoveOriginNodeId,
                SupplyDiscardRemaining = SupplyDiscardRemaining,
                SupplyDiscardSeat = SupplyDiscardSeat,
                IsGameOver = IsGameOver,
                DeveloperMode = DeveloperMode,
                DistrictScores = (int[])DistrictScores.Clone(),
                SeatOrder = SeatOrder == null ? Array.Empty<int>() : (int[])SeatOrder.Clone(),
                PendingStartMarkerSeat = PendingStartMarkerSeat,
                LastCityMoveRouteId = LastCityMoveRouteId,
                ActiveSpecialActionPatternId = ActiveSpecialActionPatternId,
                NextPendingActionId = NextPendingActionId < 1 ? 1 : NextPendingActionId,
            };

            foreach (PlayerSnapshot player in Players) state.Players.Add(player.Restore());

            var map = new MapState();
            foreach (NodeSnapshot node in Nodes) map.Nodes.Add(node.Restore());
            foreach (RouteSnapshot route in Routes) map.Routes.Add(route.Restore());
            state.Map = map;

            state.FacilityDeck.AddRange(FacilityDeck);
            state.FacilitySupply.AddRange(FacilitySupply);
            state.ExtensionHubSupply.AddRange(ExtensionHubSupply);
            state.EventDeckGreen.AddRange(EventDeckGreen);
            state.EventDeckYellow.AddRange(EventDeckYellow);
            state.EventDeckRed.AddRange(EventDeckRed);

            state.CityPatternCards.AddRange(CityPatternCards);
            foreach (PatternSnapshot pattern in CityPatternStates) state.CityPatternStates.Add(pattern.Restore());

            foreach (PublicRevealSnapshot reveal in PublicReveals)
            {
                state.PublicReveals.Add(new PublicReveal(reveal.Seat, reveal.CardId, reveal.Reason));
            }

            // —— 企业扩展 ——
            EnterpriseState enterprises = state.Enterprises;
            enterprises.Reset(Players.Count, EnterprisePicksPerPlayer < 1 ? 1 : EnterprisePicksPerPlayer);
            enterprises.DraftDone = EnterpriseDraftDone;
            enterprises.PendingAdvanceSeat = EnterprisePendingAdvanceSeat;
            enterprises.PendingAdvanceExcludeBoard = EnterprisePendingAdvanceExcludeBoard;
            enterprises.PendingAdvanceReason = EnterprisePendingAdvanceReason ?? string.Empty;
            enterprises.RedZoneAdvanceStarted = EnterpriseRedZoneAdvanceStarted;
            enterprises.RedZoneAdvanceSeat = EnterpriseRedZoneAdvanceSeat;
            enterprises.RedZoneAdvanceDoneMask = EnterpriseRedZoneAdvanceDoneMask;
            enterprises.Deck.AddRange(EnterpriseDeck);
            // 老快照没有干员牌堆（长度 0）：保留 Reset 装好的 8 张，别把牌堆清空。
            if (EnterpriseOperatorDeck.Count > 0)
            {
                enterprises.OperatorDeck.Clear();
                enterprises.OperatorDeck.AddRange(EnterpriseOperatorDeck);
            }
            enterprises.ActiveBoards.AddRange(EnterpriseBoards);
            enterprises.RhineDepartment = EnterpriseRhineDepartment;

            int offersPerSeat = EnterpriseOffersPerSeat < 1 ? 2 : EnterpriseOffersPerSeat;
            for (int seat = 0; seat < enterprises.ChosenBoards.Count; seat++)
            {
                if (seat < EnterpriseChosen.Count) enterprises.ChosenBoards[seat] = EnterpriseChosen[seat];
                if (seat < EnterpriseSecondChosen.Count)
                    enterprises.SecondChosenBoards[seat] = EnterpriseSecondChosen[seat];

                enterprises.Offers[seat].Clear();
                for (int slot = 0; slot < offersPerSeat; slot++)
                {
                    int index = seat * offersPerSeat + slot;
                    if (index < EnterpriseOffers.Length && EnterpriseOffers[index] >= 0)
                        enterprises.Offers[seat].Add(EnterpriseOffers[index]);
                }
            }

            int playerCount = Players.Count;
            for (int board = 0; board < EnterpriseCatalog.BoardCount; board++)
            {
                for (int seat = 0; seat < playerCount; seat++)
                {
                    int index = board * playerCount + seat;
                    if (index < EnterpriseLevels.Length) enterprises.SetLevel(board, seat, EnterpriseLevels[index]);
                    if (index < EnterpriseUsedMasks.Length)
                        enterprises.SetUsedMask(board, seat, EnterpriseUsedMasks[index]);
                }
            }

            foreach (PersistentTacticSnapshot entry in PersistentTactics)
            {
                var tactic = new PersistentTacticState
                {
                    CardId = entry.CardId,
                    Seat = entry.Seat,
                    Kind = (PersistentTacticKind)entry.Kind,
                    Markers = entry.Markers,
                    UsedCleanupThisTurn = entry.UsedCleanupThisTurn,
                };
                tactic.AttachedFacilityIds.AddRange(entry.AttachedFacilityIds);
                state.PersistentTactics.Add(tactic);
            }

            foreach (DeferredEffectSnapshot entry in DeferredEffects)
                state.DeferredEffects.Add(new DeferredEffect(entry.Seat, RestoreOps(entry.Ops)));

            foreach (PendingActionSnapshot entry in PendingActions)
            {
                state.PendingActions.Add(new PendingAction(
                    entry.Id, entry.Seat, (PendingActionKind)entry.Kind, entry.Remaining,
                    new PendingActionLimits
                    {
                        Free = entry.Free,
                        MaxVoucherSpend = entry.MaxVoucherSpend,
                        Scope = (PendingInfluenceScope)entry.Scope,
                        NodeScope = (PendingNodeScope)entry.NodeScope,
                    },
                    entry.SourceCardId, entry.SourceLabel));
            }

            return state;
        }

        /// <summary>把快照里的纯数据镜像递归还原成效果序列。</summary>
        private static List<RewardOp> RestoreOps(IReadOnlyList<RewardOpSnapshot> snapshots)
        {
            var ops = new List<RewardOp>(snapshots.Count);
            foreach (RewardOpSnapshot snapshot in snapshots) ops.Add(RestoreOp(snapshot));
            return ops;
        }

        private static RewardOp RestoreOp(RewardOpSnapshot snapshot)
        {
            List<EffectBranch> branches = null;
            if (snapshot.Branches.Count > 0)
            {
                branches = new List<EffectBranch>(snapshot.Branches.Count);
                foreach (EffectBranchSnapshot branch in snapshot.Branches)
                    branches.Add(new EffectBranch(branch.Label, RestoreOps(branch.Ops)));
            }

            List<RewardOp> ops = snapshot.Ops.Count > 0 ? RestoreOps(snapshot.Ops) : null;

            List<ResourceType> spread = null;
            if (snapshot.SpreadOptions.Count > 0)
            {
                spread = new List<ResourceType>(snapshot.SpreadOptions.Count);
                foreach (int type in snapshot.SpreadOptions) spread.Add((ResourceType)type);
            }

            return RewardOp.Restore((RewardKind)snapshot.Kind, (ResourceType)snapshot.Resource,
                snapshot.Amount, (InfluencePlacementScope)snapshot.Scope, branches,
                (CountSource)snapshot.Count, ops, spread);
        }

        /// <summary>一名玩家的快照。</summary>
        public sealed class PlayerSnapshot
        {
            public int Seat;
            public PlayerColor Color;
            public int Orirock, Originium, Oriron, OriginitePrime;
            public int Vouchers;
            public int Score;
            public int MarkersInSupply;

            public readonly List<int> HandCharacterCards = new();
            public readonly List<int> DiscardCharacterCards = new();
            public int PlacedCharacterCard = -1;

            /// <summary>红色 · 一次性：这张牌用完后放回游戏盒。</summary>
            public bool PlacedCardReturnsToBox;

            public bool UsedCharacterCardThisTurn;
            public bool UsedCharacterStrategyThisTurn;
            public bool UsedCharacterTacticsThisTurn;
            public bool CharacterSecondEffectPending;

            public readonly List<int> BuiltFacilities = new();
            public int[] CityPanelSlots = new int[PlayerState.CityPanelSlotCount];
            public bool[] CityPanelUsed = new bool[PlayerState.CityPanelSlotCount];

            public int MobileCityNodeId = -1;
            public readonly List<int> UsedPatternActionsThisTurn = new();
            public int ExtraMainActionsThisTurn;

            /// <summary>额外主要行动（不能是特殊行动）的剩余次数。</summary>
            public int ExtraMainActionsExcludingSpecialThisTurn;

            /// <summary>本回合是否可同时发动策略与计谋。</summary>
            public bool BothCharacterEffectsThisTurn;
            public int CharacterCardBanRound;
            public bool FreeMoveRelocatesInfluenceThisTurn;

            /// <summary>开局入场「7 选 5」是否已完成（企业扩展）。</summary>
            public bool SetupDiscardDone = true;

            public static PlayerSnapshot Capture(PlayerState player)
            {
                var snapshot = new PlayerSnapshot
                {
                    Seat = player.SeatIndex,
                    Color = player.Color,
                    Orirock = player.Resources.Orirock,
                    Originium = player.Resources.Originium,
                    Oriron = player.Resources.Oriron,
                    OriginitePrime = player.Resources.OriginitePrime,
                    Vouchers = player.Vouchers,
                    Score = player.Score,
                    MarkersInSupply = player.MarkersInSupply,
                    PlacedCharacterCard = player.PlacedCharacterCard,
                    PlacedCardReturnsToBox = player.PlacedCardReturnsToBox,
                    UsedCharacterCardThisTurn = player.UsedCharacterCardThisTurn,
                    UsedCharacterStrategyThisTurn = player.UsedCharacterStrategyThisTurn,
                    UsedCharacterTacticsThisTurn = player.UsedCharacterTacticsThisTurn,
                    CharacterSecondEffectPending = player.CharacterSecondEffectPending,
                    CityPanelSlots = (int[])player.CityPanelSlots.Clone(),
                    CityPanelUsed = (bool[])player.CityPanelUsed.Clone(),
                    MobileCityNodeId = player.MobileCityNodeId,
                    ExtraMainActionsThisTurn = player.ExtraMainActionsThisTurn,
                    ExtraMainActionsExcludingSpecialThisTurn = player.ExtraMainActionsExcludingSpecialThisTurn,
                    BothCharacterEffectsThisTurn = player.BothCharacterEffectsThisTurn,
                    CharacterCardBanRound = player.CharacterCardBanRound,
                    FreeMoveRelocatesInfluenceThisTurn = player.FreeMoveRelocatesInfluenceThisTurn,
                    SetupDiscardDone = player.SetupDiscardDone,
                };

                snapshot.HandCharacterCards.AddRange(player.HandCharacterCards);
                snapshot.DiscardCharacterCards.AddRange(player.DiscardCharacterCards);
                snapshot.BuiltFacilities.AddRange(player.BuiltFacilities);
                snapshot.UsedPatternActionsThisTurn.AddRange(player.UsedPatternActionsThisTurn);

                return snapshot;
            }

            public PlayerState Restore()
            {
                var player = new PlayerState(Seat, Color)
                {
                    Resources = new ResourcePool
                    {
                        Orirock = Orirock,
                        Originium = Originium,
                        Oriron = Oriron,
                        OriginitePrime = OriginitePrime,
                    },
                    Vouchers = Vouchers,
                    Score = Score,
                    MarkersInSupply = MarkersInSupply,
                    PlacedCharacterCard = PlacedCharacterCard,
                    PlacedCardReturnsToBox = PlacedCardReturnsToBox,
                    UsedCharacterCardThisTurn = UsedCharacterCardThisTurn,
                    UsedCharacterStrategyThisTurn = UsedCharacterStrategyThisTurn,
                    UsedCharacterTacticsThisTurn = UsedCharacterTacticsThisTurn,
                    CharacterSecondEffectPending = CharacterSecondEffectPending,
                    MobileCityNodeId = MobileCityNodeId,
                    ExtraMainActionsThisTurn = ExtraMainActionsThisTurn,
                    ExtraMainActionsExcludingSpecialThisTurn = ExtraMainActionsExcludingSpecialThisTurn,
                    BothCharacterEffectsThisTurn = BothCharacterEffectsThisTurn,
                    CharacterCardBanRound = CharacterCardBanRound,
                    FreeMoveRelocatesInfluenceThisTurn = FreeMoveRelocatesInfluenceThisTurn,
                    SetupDiscardDone = SetupDiscardDone,
                };

                player.HandCharacterCards.AddRange(HandCharacterCards);
                player.DiscardCharacterCards.AddRange(DiscardCharacterCards);
                player.BuiltFacilities.AddRange(BuiltFacilities);
                Array.Copy(CityPanelSlots, player.CityPanelSlots, player.CityPanelSlots.Length);
                Array.Copy(CityPanelUsed, player.CityPanelUsed, player.CityPanelUsed.Length);
                foreach (int cardId in UsedPatternActionsThisTurn) player.UsedPatternActionsThisTurn.Add(cardId);

                return player;
            }
        }

        /// <summary>一个资源点的快照（含静态布局属性，保证客户端不必重建地图）。</summary>
        public sealed class NodeSnapshot
        {
            public int Id;
            public string Code;
            public District District;
            public NodeColor Color;
            public bool IsRedZone;
            public int Capacity;

            public bool HasResourceToken;
            public ResourceType TokenResource;
            public int TokenAmount = 1;
            public int EventCardId = -1;
            public bool EventResolved;
            public bool ExplorePending;

            /// <summary>每个影响力槽位的所有者座位号，-1 = 空。</summary>
            public int[] Owners = Array.Empty<int>();

            public static NodeSnapshot Capture(MapNodeState node)
            {
                var owners = new int[node.Slots.Capacity];
                for (int i = 0; i < owners.Length; i++) owners[i] = node.Slots.OwnerOf(i);

                return new NodeSnapshot
                {
                    Id = node.Id,
                    Code = node.Code,
                    District = node.District,
                    Color = node.Color,
                    IsRedZone = node.IsRedZone,
                    Capacity = node.Slots.Capacity,
                    HasResourceToken = node.HasResourceToken,
                    TokenResource = node.TokenResource,
                    TokenAmount = node.TokenAmount,
                    EventCardId = node.EventCardId,
                    EventResolved = node.EventResolved,
                    ExplorePending = node.ExplorePending,
                    Owners = owners,
                };
            }

            public MapNodeState Restore()
            {
                var node = new MapNodeState(Id, Code, District, Color, IsRedZone, Capacity)
                {
                    HasResourceToken = HasResourceToken,
                    TokenResource = TokenResource,
                    TokenAmount = TokenAmount,
                    EventCardId = EventCardId,
                    EventResolved = EventResolved,
                    ExplorePending = ExplorePending,
                };

                for (int i = 0; i < Owners.Length && i < node.Slots.Capacity; i++)
                {
                    if (Owners[i] >= 0) node.Slots.Place(i, Owners[i]);
                }

                return node;
            }
        }

        /// <summary>一条航道的快照（含静态布局属性）。</summary>
        public sealed class RouteSnapshot
        {
            public int Id;
            public int[] Nodes = Array.Empty<int>();
            public District District;
            public int Capacity;
            public int[] Owners = Array.Empty<int>();

            /// <summary>企业扩展：这条航道上是否已放置公路指示物。</summary>
            public bool IsHighway;

            public static RouteSnapshot Capture(RouteState route)
            {
                var owners = new int[route.Slots.Capacity];
                for (int i = 0; i < owners.Length; i++) owners[i] = route.Slots.OwnerOf(i);

                return new RouteSnapshot
                {
                    Id = route.Id,
                    Nodes = (int[])route.Nodes.Clone(),
                    District = route.District,
                    Capacity = route.Slots.Capacity,
                    Owners = owners,
                    IsHighway = route.IsHighway,
                };
            }

            public RouteState Restore()
            {
                var route = new RouteState(Id, (int[])Nodes.Clone(), District, Capacity)
                {
                    IsHighway = IsHighway,
                };
                for (int i = 0; i < Owners.Length && i < route.Slots.Capacity; i++)
                {
                    if (Owners[i] >= 0) route.Slots.Place(i, Owners[i]);
                }
                return route;
            }
        }

        /// <summary>一张城市样式牌的标记状态快照。</summary>
        public sealed class PatternSnapshot
        {
            public int CardId;
            public int Level;
            public readonly List<MarkerSnapshot> Markers = new();

            public static PatternSnapshot Capture(CityPatternState pattern)
            {
                var snapshot = new PatternSnapshot { CardId = pattern.CardId, Level = pattern.Level };
                foreach (PatternMarker marker in pattern.Markers)
                    snapshot.Markers.Add(new MarkerSnapshot { Seat = marker.Seat, Position = marker.Position });
                return snapshot;
            }

            public CityPatternState Restore()
            {
                var pattern = new CityPatternState(CardId, Level);
                foreach (MarkerSnapshot marker in Markers) pattern.PlaceMarker(marker.Seat, marker.Position);
                return pattern;
            }
        }

        /// <summary>样式牌上的一个标记（座位 + 轨道格位）。</summary>
        public sealed class MarkerSnapshot
        {
            public int Seat;
            public int Position;
        }
    }
}
