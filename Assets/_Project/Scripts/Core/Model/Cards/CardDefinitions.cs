namespace NomadCity.Core.Model
{
    /// <summary>
    /// 卡牌定义层：描述「一张牌是什么」。纯 C#、无 UnityEngine 依赖。
    /// 引擎内部一律用 int id 表示卡牌，牌堆 / 手牌 / 供应区里存的都是 id；
    /// 这些定义只用于查名字与读取静态属性，规则实现不依赖卡面文案。
    ///
    /// 卡面尚未切片，因此**费用、分数、颜色、效果等牌面数据一律留空**，
    /// 等 `Doc/CardEffects.md`、`Doc/RuleSpec.md` §14 的牌面录入完成后再补。
    /// 届时数据由 Data 层的 ScriptableObject 转换为这些定义（见 <see cref="ICardCatalog"/>）。
    /// </summary>

    /// <summary>角色牌定义。基础版 5 角色 × 4 色 = 20 张；同一颜色的 5 张角色相同。</summary>
    public sealed class CharacterCardDef
    {
        public readonly int Id;
        public readonly PlayerColor Color;

        /// <summary>角色名：雷蛇 / 极境 / 德克萨斯 / 锡人 / 坎诺特。</summary>
        public readonly string Name;

        // 计谋 / 策略效果：待卡面录入后补充（RuleSpec §11 通用效果执行器）。

        public CharacterCardDef(int id, PlayerColor color, string name)
        {
            Id = id;
            Color = color;
            Name = name;
        }

        public override string ToString() => $"角色牌[{Id}] {Color}·{Name}";
    }

    /// <summary>设施牌的类别。核心指挥塔与延伸枢纽不在设施牌堆内，单独取出使用（RuleSpec §4.1）。</summary>
    public enum FacilityKind
    {
        /// <summary>普通设施牌：构成 41 张的设施牌堆。</summary>
        Standard = 0,

        /// <summary>核心指挥塔：每人 1 张，颜色与玩家一致。</summary>
        CoreCommandTower = 1,

        /// <summary>延伸枢纽：「物流枢纽」的子建筑，红 / 黄 / 蓝三色各 1 张（没有绿色）。</summary>
        ExtensionHub = 2,
    }

    /// <summary>设施牌定义。</summary>
    public sealed class FacilityCardDef
    {
        public readonly int Id;
        public readonly FacilityKind Kind;
        public readonly string Name;

        // 设施颜色 / 奖励分数 / 资源费用 / 金券费用 / 入场效果：待卡面录入后补充。

        public FacilityCardDef(int id, FacilityKind kind, string name)
        {
            Id = id;
            Kind = kind;
            Name = name;
        }

        public override string ToString() => $"设施牌[{Id}] {Kind}·{Name}";
    }

    /// <summary>
    /// 事件牌定义。牌背分红 / 绿 / 黄三色（共 22 张：绿 6、黄 10、红 6），
    /// 与资源点颜色一一对应，决定翻开哪种颜色的事件牌（RuleSpec §7 / §10.3）。
    /// </summary>
    public sealed class EventCardDef
    {
        public readonly int Id;

        /// <summary>牌背颜色，等价于对应资源点的颜色。</summary>
        public readonly NodeColor BackColor;

        // 提供的资源种类 / 数量（1 或 2）/ 事件区选项 / 是否带「4」标记：待卡面录入后补充。

        public EventCardDef(int id, NodeColor backColor)
        {
            Id = id;
            BackColor = backColor;
        }

        public override string ToString() => $"事件牌[{Id}] 牌背 {BackColor}";
    }

    /// <summary>城市样式牌定义。基础版 6 张：I 级 4 张、II 级 2 张（RuleSpec §3 / §10.2）。</summary>
    public sealed class CityPatternCardDef
    {
        public readonly int Id;

        /// <summary>样式等级：1 = I 级，2 = II 级。</summary>
        public readonly int Level;

        // 奖励分数 / 宣告条件 / 解锁的特殊行动：待卡面录入后补充。

        public CityPatternCardDef(int id, int level)
        {
            Id = id;
            Level = level;
        }

        public override string ToString() => $"城市样式牌[{Id}] {Level} 级";
    }
}