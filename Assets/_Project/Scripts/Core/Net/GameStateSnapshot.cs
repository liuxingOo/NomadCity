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
        public const int Version = 3;

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

        // —— 企业扩展 ——
        public bool EnterpriseDraftDone;
        public int EnterprisePendingAdvanceSeat = -1;
        public int EnterprisePendingAdvanceExcludeBoard = -1;
        public string EnterprisePendingAdvanceReason = string.Empty;

        /// <summary>合作等级：按「企业板 id × 座位号」拉平，长度 = 8 × 人数。</summary>
        public int[] EnterpriseLevels = Array.Empty<int>();

        /// <summary>本回合已触发的企业特效位掩码：同样按「企业板 id × 座位号」拉平。</summary>
        public int[] EnterpriseUsedMasks = Array.Empty<int>();

        /// <summary>各座位开局抽到的 2 张企业板，每座位固定 2 个槽位（-1 = 空）。</summary>
        public int[] EnterpriseOffers = Array.Empty<int>();

        public readonly List<int> EnterpriseDeck = new();
        public readonly List<int> EnterpriseBoards = new();
        public readonly List<int> EnterpriseChosen = new();
        public readonly List<int> EnterpriseDepartments = new();

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
            if (state.DeferredEffects.Count > 0)
                throw new NotSupportedException(
                    $"快照暂不支持非空的延迟结算队列（当前 {state.DeferredEffects.Count} 项）。" +
                    "请在延迟效果入队前抓快照（开局同步即满足该条件）。");

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

            // —— 企业扩展 ——
            EnterpriseState enterprises = state.Enterprises;
            if (enterprises != null)
            {
                snapshot.EnterpriseDraftDone = enterprises.DraftDone;
                snapshot.EnterprisePendingAdvanceSeat = enterprises.PendingAdvanceSeat;
                snapshot.EnterprisePendingAdvanceExcludeBoard = enterprises.PendingAdvanceExcludeBoard;
                snapshot.EnterprisePendingAdvanceReason = enterprises.PendingAdvanceReason ?? string.Empty;

                snapshot.EnterpriseDeck.AddRange(enterprises.Deck);
                snapshot.EnterpriseBoards.AddRange(enterprises.ActiveBoards);
                snapshot.EnterpriseChosen.AddRange(enterprises.ChosenBoards);
                snapshot.EnterpriseDepartments.AddRange(enterprises.Departments);

                int playerCount = state.Players.Count;
                snapshot.EnterpriseOffers = new int[playerCount * 2];
                for (int seat = 0; seat < playerCount; seat++)
                {
                    List<int> offer = seat < enterprises.Offers.Count ? enterprises.Offers[seat] : null;
                    snapshot.EnterpriseOffers[seat * 2] = offer != null && offer.Count > 0 ? offer[0] : -1;
                    snapshot.EnterpriseOffers[seat * 2 + 1] = offer != null && offer.Count > 1 ? offer[1] : -1;
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

            return snapshot;
        }

        /// <summary>
        /// 还原成一份全新的 <see cref="GameState"/>。catalog 由调用方提供（静态查表数据，不随对局变化）。
        /// 延迟结算队列按设计恒为空（<see cref="Capture"/> 已保证）。
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

            // —— 企业扩展 ——
            EnterpriseState enterprises = state.Enterprises;
            enterprises.Reset(Players.Count);
            enterprises.DraftDone = EnterpriseDraftDone;
            enterprises.PendingAdvanceSeat = EnterprisePendingAdvanceSeat;
            enterprises.PendingAdvanceExcludeBoard = EnterprisePendingAdvanceExcludeBoard;
            enterprises.PendingAdvanceReason = EnterprisePendingAdvanceReason ?? string.Empty;
            enterprises.Deck.AddRange(EnterpriseDeck);
            enterprises.ActiveBoards.AddRange(EnterpriseBoards);

            for (int seat = 0; seat < enterprises.ChosenBoards.Count; seat++)
            {
                if (seat < EnterpriseChosen.Count) enterprises.ChosenBoards[seat] = EnterpriseChosen[seat];
                if (seat < EnterpriseDepartments.Count) enterprises.Departments[seat] = EnterpriseDepartments[seat];

                enterprises.Offers[seat].Clear();
                for (int slot = 0; slot < 2; slot++)
                {
                    int index = seat * 2 + slot;
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

            return state;
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
            public int CharacterCardBanRound;

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
                    UsedCharacterCardThisTurn = player.UsedCharacterCardThisTurn,
                    UsedCharacterStrategyThisTurn = player.UsedCharacterStrategyThisTurn,
                    UsedCharacterTacticsThisTurn = player.UsedCharacterTacticsThisTurn,
                    CharacterSecondEffectPending = player.CharacterSecondEffectPending,
                    CityPanelSlots = (int[])player.CityPanelSlots.Clone(),
                    CityPanelUsed = (bool[])player.CityPanelUsed.Clone(),
                    MobileCityNodeId = player.MobileCityNodeId,
                    ExtraMainActionsThisTurn = player.ExtraMainActionsThisTurn,
                    CharacterCardBanRound = player.CharacterCardBanRound,
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
                    UsedCharacterCardThisTurn = UsedCharacterCardThisTurn,
                    UsedCharacterStrategyThisTurn = UsedCharacterStrategyThisTurn,
                    UsedCharacterTacticsThisTurn = UsedCharacterTacticsThisTurn,
                    CharacterSecondEffectPending = CharacterSecondEffectPending,
                    MobileCityNodeId = MobileCityNodeId,
                    ExtraMainActionsThisTurn = ExtraMainActionsThisTurn,
                    CharacterCardBanRound = CharacterCardBanRound,
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
                };
            }

            public RouteState Restore()
            {
                var route = new RouteState(Id, (int[])Nodes.Clone(), District, Capacity);
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
