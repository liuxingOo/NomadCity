namespace NomadCity.Core.Model
{
    /// <summary>
    /// 四名企业家与其影响力颜色的固定对应（规则规格 §4.1）：每张企业家牌就是一个阵营标识，
    /// 基础版 / 企业扩展都不带额外功能，因此引擎只需要颜色，界面按颜色反查姓名。
    /// </summary>
    public static class EntrepreneurCatalog
    {
        /// <summary>按颜色取企业家姓名。</summary>
        public static string NameOf(PlayerColor color)
        {
            switch (color)
            {
                case PlayerColor.Red: return "Agnes";
                case PlayerColor.Blue: return "Paul";
                case PlayerColor.Yellow: return "Martin";
                case PlayerColor.Green: return "Tiag";
                default: return "—";
            }
        }
    }
}
