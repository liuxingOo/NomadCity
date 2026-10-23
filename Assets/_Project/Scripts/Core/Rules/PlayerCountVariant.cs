using System;
using NomadCity.Core.Model;

namespace NomadCity.Core.Rules
{
    /// <summary>
    /// 人数变体参数（规则规格 §13.2）：按人数集中配置差异，禁止在流程代码里散落 `if (playerCount == 3)`。
    /// 收录「初始资金」「红色区域开放回合」「初始资源点限制」「每回合设施供应区变动」等按人数不同的参数；
    /// 地图面由地图数据本身决定（2／3 人用三人面），不在这张表里。
    ///
    /// 顺位（order）指从本回合起始玩家开始、顺时针数下来的次序，0 = 起始玩家。
    /// </summary>
    public sealed class PlayerCountVariant
    {
        public readonly int PlayerCount;

        /// <summary>红色区域开放回合：从这一回合起，红色区域的资源点才可探索／作为城市移动目的地（§10.3 / §13.2）。</summary>
        public readonly int RedZoneOpenTurn;

        /// <summary>不允许作为初始资源点的资源点编号（§12 变体 2）：2 人局为 "B-01"，其余为 null。</summary>
        public readonly string ForbiddenStartNodeCode;

        /// <summary>非起始玩家决定初始资源点后是否立即放 1 个影响力（§12 变体 2）：仅 2 人局。</summary>
        public readonly bool NonFirstPlayerPlacesStartInfluence;

        /// <summary>每回合角色牌盖放前是否每人弃 1 张设施供应区设施牌（§12 变体 4）：仅 2 人局。</summary>
        public readonly bool DiscardsSupplyCardEachTurn;

        private readonly int[] initialVouchers; // 各顺位获得的初始金券
        private readonly int[] initialBonusScore; // 各顺位额外获得的分数（仅 2 人局非起始玩家）

        private PlayerCountVariant(int playerCount, int redZoneOpenTurn, int[] initialVouchers, int[] initialBonusScore,
            string forbiddenStartNodeCode = null, bool nonFirstPlayerPlacesStartInfluence = false,
            bool discardsSupplyCardEachTurn = false)
        {
            PlayerCount = playerCount;
            RedZoneOpenTurn = redZoneOpenTurn;
            ForbiddenStartNodeCode = forbiddenStartNodeCode;
            NonFirstPlayerPlacesStartInfluence = nonFirstPlayerPlacesStartInfluence;
            DiscardsSupplyCardEachTurn = discardsSupplyCardEachTurn;
            this.initialVouchers = initialVouchers;
            this.initialBonusScore = initialBonusScore;
        }

        /// <summary>4 人：10 / 12 / 14 / 18；红色区域第 4 回合开放。</summary>
        private static readonly PlayerCountVariant Four = new(4, 4, new[] { 10, 12, 14, 18 }, new[] { 0, 0, 0, 0 });

        /// <summary>3 人：10 / 12 / 18；红色区域第 5 回合开放。</summary>
        private static readonly PlayerCountVariant Three = new(3, 5, new[] { 10, 12, 18 }, new[] { 0, 0, 0 });

        /// <summary>
        /// 2 人（§12）：起始 10、非起始 18 并额外获得 3 分；红色区域第 6 回合开放；
        /// 初始资源点禁选 B-01、非起始玩家额外放 1 个影响力；每回合盖放前每人弃 1 张设施牌。
        /// </summary>
        private static readonly PlayerCountVariant Two = new(2, 6, new[] { 10, 18 }, new[] { 0, 3 },
            forbiddenStartNodeCode: "B-01", nonFirstPlayerPlacesStartInfluence: true,
            discardsSupplyCardEachTurn: true);

        /// <summary>取某局对应人数的变体参数。</summary>
        public static PlayerCountVariant For(GameState state) => For(state.Players.Count);

        /// <summary>取某人数对应的变体参数。</summary>
        public static PlayerCountVariant For(int playerCount) => playerCount switch
        {
            2 => Two,
            3 => Three,
            4 => Four,
            _ => throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "只支持 2–4 人。"),
        };

        /// <summary>某顺位玩家的初始金券。</summary>
        public int InitialVouchers(int order) => initialVouchers[order];

        /// <summary>某顺位玩家的初始额外分数。</summary>
        public int InitialBonusScore(int order) => initialBonusScore[order];
    }
}