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

        /// <summary>当前回合，1–8。</summary>
        public int TurnNumber = 1;

        /// <summary>当前阶段。</summary>
        public TurnPhase Phase = TurnPhase.CharacterPlacement;

        /// <summary>本回合起始玩家的座位号。</summary>
        public int FirstPlayerSeat;

        /// <summary>当前轮到行动的玩家座位号。</summary>
        public int ActiveSeat;

        /// <summary>玩家行动阶段内的轮次，1 或 2。</summary>
        public int ActionRound = 1;

        /// <summary>玩家列表，下标即座位号。</summary>
        public readonly List<PlayerState> Players = new();

        /// <summary>地图运行时状态。</summary>
        public MapState Map = new();

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

        public readonly List<int> EventDeckGreen = new();
        public readonly List<int> EventDeckYellow = new();
        public readonly List<int> EventDeckRed = new();

        /// <summary>场上的城市样式牌。</summary>
        public readonly List<int> CityPatternCards = new();

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
