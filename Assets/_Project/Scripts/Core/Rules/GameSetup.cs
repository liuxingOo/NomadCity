using System;

namespace NomadCity.Core.Rules
{
    /// <summary>扩展包开关。用位标志，方便以后叠加组合。</summary>
    [Flags]
    public enum ExpansionFlags
    {
        None = 0,
        Enterprise = 1 << 0,
        Infected = 1 << 1,
    }

    /// <summary>
    /// 一局游戏的模式配置 —— 主菜单选出的结果，由它驱动 RuleSet 的组装。
    /// 纯数据、可序列化：联机时可直接随开局信息发给各端，不需要额外转换。
    /// </summary>
    [Serializable]
    public sealed class GameSetup
    {
        /// <summary>玩家人数，2–4。2 人与 3 人共用三人面地图。</summary>
        public int PlayerCount;

        /// <summary>启用的扩展包。</summary>
        public ExpansionFlags Expansions;

        public GameSetup()
        {
            PlayerCount = 4;
            Expansions = ExpansionFlags.None;
        }

        public GameSetup(int playerCount, ExpansionFlags expansions = ExpansionFlags.None)
        {
            PlayerCount = playerCount;
            Expansions = expansions;
        }

        public bool Has(ExpansionFlags flag) => (Expansions & flag) == flag;

        public bool IsEnterpriseEnabled => Has(ExpansionFlags.Enterprise);

        /// <summary>人数是否在规则允许的范围内。</summary>
        public bool IsValid => PlayerCount >= 2 && PlayerCount <= 4;

        public override string ToString() =>
            $"{PlayerCount} 人 / 扩展：{(Expansions == ExpansionFlags.None ? "无" : Expansions.ToString())}";
    }
}
