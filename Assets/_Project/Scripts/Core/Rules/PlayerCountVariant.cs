using System;

namespace NomadCity.Core.Rules
{
    /// <summary>
    /// 人数变体参数（规则规格 §13.2）：按人数集中配置差异，禁止在流程代码里散落 `if (playerCount == 3)`。
    /// 当前只收录「开局入场·初始资金」相关的参数；红色区域开放回合、地图面等参数，
    /// 等对应功能落地时再往这张表里加。
    ///
    /// 顺位（order）指从本回合起始玩家开始、顺时针数下来的次序，0 = 起始玩家。
    /// </summary>
    public sealed class PlayerCountVariant
    {
        public readonly int PlayerCount;

        /// <summary>红色区域开放回合：从这一回合起，红色区域的资源点才可探索／作为城市移动目的地（§10.3 / §13.2）。</summary>
        public readonly int RedZoneOpenTurn;

        private readonly int[] initialVouchers; // 各顺位获得的初始金券
        private readonly int[] initialBonusScore; // 各顺位额外获得的分数（仅 2 人局非起始玩家）

        private PlayerCountVariant(int playerCount, int redZoneOpenTurn, int[] initialVouchers, int[] initialBonusScore)
        {
            PlayerCount = playerCount;
            RedZoneOpenTurn = redZoneOpenTurn;
            this.initialVouchers = initialVouchers;
            this.initialBonusScore = initialBonusScore;
        }

        /// <summary>4 人：10 / 12 / 14 / 18；红色区域第 4 回合开放。</summary>
        private static readonly PlayerCountVariant Four = new(4, 4, new[] { 10, 12, 14, 18 }, new[] { 0, 0, 0, 0 });

        /// <summary>3 人：10 / 12 / 18；红色区域第 5 回合开放。</summary>
        private static readonly PlayerCountVariant Three = new(3, 5, new[] { 10, 12, 18 }, new[] { 0, 0, 0 });

        /// <summary>2 人（§12）：起始 10、非起始 18 并额外获得 3 分；红色区域第 6 回合开放。</summary>
        private static readonly PlayerCountVariant Two = new(2, 6, new[] { 10, 18 }, new[] { 0, 3 });

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