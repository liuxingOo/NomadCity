using System;
using System.Collections.Generic;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Model
{
    /// <summary>
    /// 一局游戏的完整状态 —— 引擎的唯一真相来源。
    /// 所有 Command 都是对它的读取与修改；UI 只订阅引擎产出的事件，不直接改这里。
    /// 纯 C#、无 UnityEngine 依赖，可直接作为联机同步的快照。
    /// 牌堆/设施里的元素是卡牌 id，指向 Data 层的卡牌定义，核心逻辑不依赖卡面文案。
    /// </summary>
    public sealed class GameState
    {
        /// <summary>本局的模式配置（人数 + 扩展包）。</summary>
        public readonly GameSetup Setup;

        /// <summary>当前回合，1–8；0 表示尚未开局。</summary>
        public int TurnNumber;

        /// <summary>当前阶段。</summary>
        public TurnPhase Phase = TurnPhase.CharacterPlacement;

        /// <summary>本回合起始玩家的座位号。</summary>
        public int FirstPlayerSeat;

        /// <summary>当前轮到行动的玩家座位号。</summary>
        public int ActiveSeat;

        /// <summary>
        /// 本局的行动顺序：元素是座位号，下标 0 即起始玩家。开局随机排出后在整局内固定不变，
        /// <see cref="NextSeat"/> 按它顺时针推进；为空时退回「座位号 +1」的自然顺序。
        /// </summary>
        public int[] SeatOrder = Array.Empty<int>();

        /// <summary>
        /// 联机采集（§7）各座位已经提交的选点。收齐所有玩家后由最后一条
        /// <see cref="Engine.SubmitHarvestChoiceCommand"/> 当场统一结算并清空；
        /// 单机热座仍然走 <see cref="Engine.ResolveHarvestCommand"/>，不用这个字段。
        /// </summary>
        public readonly Dictionary<int, List<int>> HarvestChoices = new();

        /// <summary>玩家行动阶段内的轮次，1 或 2。</summary>
        public int ActionRound = 1;

        /// <summary>
        /// 采集阶段是否还等着玩家做选择（§7）：进入采集阶段时置 true，
        /// 由 <see cref="Engine.GameEngine.ResolveHarvest"/> 结算后置 false。
        /// 为 true 时不允许推进到收尾阶段，避免有人跳过整个采集。
        /// </summary>
        public bool HarvestPending;

        /// <summary>
        /// 城市移动（§6.4d）已走完步 1–2、正等着玩家在**出发点**放置影响力：值是出发点的资源点 id
        /// （-1 = 没有待收尾的城市移动）。步 3 由 <see cref="Engine.MoveCityInfluenceCommand"/> 执行并把它清回 -1。
        /// </summary>
        public int CityMoveOriginNodeId = -1;

        /// <summary>
        /// 2 人局（§12 变体 4）本回合角色牌盖放前还要弃掉的设施牌张数：进盖放阶段时置为玩家人数，
        /// 每弃 1 张减 1；为 0 表示没有这一步（3／4 人局恒为 0）。大于 0 时不允许盖放角色牌或推进阶段。
        /// </summary>
        public int SupplyDiscardRemaining;

        /// <summary>2 人局本次弃设施牌轮到谁（-1 = 不需要弃牌）。顺序从本回合起始玩家起顺时针。</summary>
        public int SupplyDiscardSeat = -1;

        /// <summary>整局是否已结束（第 8 回合收尾之后置为 true，进入终局结算）。</summary>
        public bool IsGameOver;

        /// <summary>
        /// 开发者模式（仅用于调试）：放开「轮次 / 阶段 / 红色区域是否开放」这几道流程闸门，
        /// 让任何座位在任何阶段都能直接执行行动，方便单独测各个按钮。**不改变规则效果本身**
        /// （金券 / 资源 / 标记该扣还是扣）。由 GameBootstrap 在开局时按 Inspector 上的开关写入，
        /// 正式游戏必须为 false。
        /// </summary>
        public bool DeveloperMode;

        /// <summary>玩家列表，下标即座位号。</summary>
        public readonly List<PlayerState> Players = new();

        /// <summary>地图运行时状态。</summary>
        public MapState Map = new();

        /// <summary>
        /// 本局的卡牌目录：把牌堆里的 int id 翻译成卡牌定义（资源点指示物的资源与数量等）。
        /// 它是**静态查表数据**，不随对局改变，因此不属于联机快照的一部分（各端各持一份相同目录即可）。
        /// 由 <see cref="Engine.GameEngine.Create"/> 与开局设置用同一份目录写入。
        /// </summary>
        public ICardCatalog Catalog;

        /// <summary>
        /// 各区块的终局控制分（下标 = (int)District）。默认取规则规格 §9.2 的标准表；
        /// 建局时可被地图数据（MapData 里配置的区块分）覆盖。
        /// </summary>
        public int[] DistrictScores = Districts.StandardScores();

        // 至纯源石与金券没有数量限制（可用替代品），因此不设「供应堆剩余量」——
        // 发出去就是发出去，付回「供应堆」等同于退回无限池，无需记账。

        // —— 公共牌堆 ——
        /// <summary>设施牌堆（背面朝上）。</summary>
        public readonly List<int> FacilityDeck = new();

        /// <summary>设施供应区（最多 6 张正面朝上）。</summary>
        public readonly List<int> FacilitySupply = new();

        /// <summary>延伸枢纽（红／黄／蓝各 1 张），放在一旁备用，不在设施牌堆内。</summary>
        public readonly List<int> ExtensionHubSupply = new();

        public readonly List<int> EventDeckGreen = new();
        public readonly List<int> EventDeckYellow = new();
        public readonly List<int> EventDeckRed = new();

        /// <summary>场上的城市样式牌。</summary>
        public readonly List<int> CityPatternCards = new();

        /// <summary>场上城市样式牌的标记状态（下标与 <see cref="CityPatternCards"/> 对应）。</summary>
        public readonly List<CityPatternState> CityPatternStates = new();

        /// <summary>
        /// 延迟结算队列（§11.4「本回合收尾阶段结算」）：<see cref="RewardKind.Deferred"/> 效果入队，
        /// 收尾阶段由 <see cref="Engine.CleanupResolver"/> 依次取出执行。
        /// </summary>
        public readonly List<DeferredEffect> DeferredEffects = new();

        /// <summary>
        /// 本回合收尾阶段「传递起始标记」时，起始玩家改由谁担任（「联邦理事会」的效果）；-1 表示不改变。
        /// 由效果写入，下一回合由 <see cref="Engine.TurnFlow"/> 读取后复位。
        /// </summary>
        public int PendingStartMarkerSeat = -1;

        /// <summary>
        /// 最近一次城市移动所用的航道 id（-1 = 还没发生过城市移动）。城市移动只能移到**相邻**资源点，
        /// 因此一次移动只经过一条航道；「高性能动力设施」的「在经过的航道上放置 1 个影响力」按它取落点候选。
        /// </summary>
        public int LastCityMoveRouteId = -1;

        /// <summary>
        /// 正在使用特殊行动的那张城市样式牌 id（-1 = 不在结算特殊行动中）。
        /// 「军工业化区域」的「放置 X 个影响力」按它取计数来源（X = 该牌上自己的标记数，模仿 <see cref="LastCityMoveRouteId"/> 的通道）。
        /// </summary>
        public int ActiveSpecialActionPatternId = -1;

        public GameState(GameSetup setup)
        {
            Setup = setup;
        }

        /// <summary>
        /// 深拷贝一份本状态，用于「先问后改」的效果会话：在副本上反复试跑，正式状态原封不动。
        /// <see cref="Catalog"/> 与 <see cref="Setup"/> 是静态/只读数据，直接共享，不复制。
        /// </summary>
        public GameState Clone()
        {
            var copy = new GameState(Setup)
            {
                TurnNumber = TurnNumber,
                Phase = Phase,
                FirstPlayerSeat = FirstPlayerSeat,
                ActiveSeat = ActiveSeat,
                ActionRound = ActionRound,
                SeatOrder = SeatOrder == null ? Array.Empty<int>() : (int[])SeatOrder.Clone(),
                IsGameOver = IsGameOver,
                HarvestPending = HarvestPending,
                CityMoveOriginNodeId = CityMoveOriginNodeId,
                SupplyDiscardRemaining = SupplyDiscardRemaining,
                SupplyDiscardSeat = SupplyDiscardSeat,
                DeveloperMode = DeveloperMode,
                Catalog = Catalog,
                DistrictScores = (int[])DistrictScores.Clone(),
                Map = Map.Clone(),
                PendingStartMarkerSeat = PendingStartMarkerSeat,
                LastCityMoveRouteId = LastCityMoveRouteId,
                ActiveSpecialActionPatternId = ActiveSpecialActionPatternId,
            };

            foreach (PlayerState player in Players) copy.Players.Add(player.Clone());

            copy.FacilityDeck.AddRange(FacilityDeck);
            copy.FacilitySupply.AddRange(FacilitySupply);
            copy.ExtensionHubSupply.AddRange(ExtensionHubSupply);
            copy.EventDeckGreen.AddRange(EventDeckGreen);
            copy.EventDeckYellow.AddRange(EventDeckYellow);
            copy.EventDeckRed.AddRange(EventDeckRed);
            copy.CityPatternCards.AddRange(CityPatternCards);
            copy.DeferredEffects.AddRange(DeferredEffects); // DeferredEffect 是只读结构体，逐项复制即可

            foreach (CityPatternState pattern in CityPatternStates)
            {
                copy.CityPatternStates.Add(pattern.Clone());
            }

            foreach (KeyValuePair<int, List<int>> choice in HarvestChoices)
            {
                copy.HarvestChoices[choice.Key] = new List<int>(choice.Value);
            }

            return copy;
        }

        public PlayerState Player(int seat) => Players[seat];

        public PlayerState ActivePlayer => Players[ActiveSeat];

        /// <summary>
        /// 行动顺序里的下一位玩家。开局随机排出的顺序写在 <see cref="SeatOrder"/> 里，
        /// 找不到该座位（或顺序没设置）时退回「座位号 +1」。
        /// </summary>
        public int NextSeat(int seat)
        {
            int count = Players.Count;
            if (count <= 0) return 0;

            if (SeatOrder == null || SeatOrder.Length != count)
                return (seat + 1) % count;

            for (int i = 0; i < count; i++)
            {
                if (SeatOrder[i] == seat) return SeatOrder[(i + 1) % count];
            }

            return (seat + 1) % count;
        }

        /// <summary>取某张城市样式牌的标记状态；不在场上时返回 null。</summary>
        public CityPatternState PatternStateOf(int cardId)
        {
            int index = CityPatternCards.IndexOf(cardId);
            return index < 0 ? null : CityPatternStates[index];
        }
    }
}
