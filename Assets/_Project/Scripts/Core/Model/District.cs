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
}
