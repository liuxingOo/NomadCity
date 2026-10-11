namespace NomadCity.Core.Model
{
    /// <summary>
    /// 区块。4 人地图 8 个（A–G + R），3 人／2 人地图 7 个（无 G）。
    /// 区块归属与分数在终局结算，依据规则规格 9.2 / 10.4。
    /// 各区块的分数值属于地图数据，由 MapData 提供，不写死在这里。
    /// </summary>
    public enum District
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3,
        E = 4,
        F = 5,
        G = 6,
        R = 7,
    }

    /// <summary>区块相关的常量：全部区块，以及标准控制分表（规则规格 §9.2）。</summary>
    public static class Districts
    {
        /// <summary>全部区块。三人地图没有 G 区的资源点，G 区自然无人得分。</summary>
        public static readonly District[] All =
        {
            District.A, District.B, District.C, District.D,
            District.E, District.F, District.G, District.R,
        };

        /// <summary>标准控制分：A–D = 3，E–F = 4，G = 3，R = 3（下标 = (int)District）。地图数据可覆盖。</summary>
        public static int[] StandardScores() => new[] { 3, 3, 3, 3, 4, 4, 3, 3 };
    }
}
