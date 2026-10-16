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

        /// <summary>玩家行动阶段内的轮次，1 或 2。</summary>
        public int ActionRound = 1;

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

        // —— 公共资源供应堆 ——
        /// <summary>至纯源石剩余数量（初始 35）。</summary>
        public int SupplyOriginitePrime;

        /// <summary>哥伦比亚金券剩余总值（初始 70）。</summary>
        public int SupplyVouchers;

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

        public GameState(GameSetup setup)
        {
            Setup = setup;
        }

        public PlayerState Player(int seat) => Players[seat];

        public PlayerState ActivePlayer => Players[ActiveSeat];

        /// <summary>座位号顺时针方向的下一位玩家。</summary>
        public int NextSeat(int seat) => (seat + 1) % Players.Count;
    }
}
