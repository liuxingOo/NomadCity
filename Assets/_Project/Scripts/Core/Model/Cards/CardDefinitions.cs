using System;
using System.Collections.Generic;

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

    /// <summary>设施牌的颜色（RuleSpec §6.4e 牌面构成）。彩色设施在宣告城市样式时可视为任意颜色（§6.5b）。</summary>
    public enum FacilityColor
    {
        Green = 0,
        Yellow = 1,
        Red = 2,
        Blue = 3,

        /// <summary>彩色：组合城市样式时可以当成任意一种颜色。</summary>
        Rainbow = 4,
    }

    /// <summary>建设的付费方式：牌面的资源费用与金券费用二选一（RuleSpec §6.4e 步 1）。</summary>
    public enum FacilityPayment
    {
        None = 0,

        /// <summary>按牌面的资源费用支付。</summary>
        Resources = 1,

        /// <summary>按牌面的金券费用支付。</summary>
        Vouchers = 2,
    }

    /// <summary>设施牌上的关键词（RuleSpec §6.4e）。</summary>
    [Flags]
    public enum FacilityKeyword
    {
        None = 0,

        /// <summary>唯一：自己城市面板上已有同名牌时不能再建设这张牌。</summary>
        Unique = 1 << 0,

        /// <summary>免费：这次操作跳过支付。</summary>
        Free = 1 << 1,
    }

    /// <summary>设施牌定义。费用 / 分数 / 颜色 / 效果等牌面数据在 41 张卡面录入前一律留默认值。</summary>
    public sealed class FacilityCardDef
    {
        public readonly int Id;
        public readonly FacilityKind Kind;
        public readonly string Name;

        /// <summary>设施颜色。</summary>
        public readonly FacilityColor Color;

        /// <summary>牌面奖励分数，建设时立即结算（§6.4e 步 2）。</summary>
        public readonly int Score;

        /// <summary>资源费用：一串 <see cref="RewardKind.PayResource"/> 单元；空表示不花资源。</summary>
        public readonly IReadOnlyList<RewardOp> ResourceCost;

        /// <summary>金券费用；0 表示不花金券。</summary>
        public readonly int VoucherCost;

        /// <summary>关键词（唯一 / 免费）。</summary>
        public readonly FacilityKeyword Keywords;

        /// <summary>入场效果，建设第三步结算（§6.4e 步 3）；空表示这张牌没有入场效果。</summary>
        public readonly IReadOnlyList<RewardOp> EntryEffects;

        public FacilityCardDef(int id, FacilityKind kind, string name,
            FacilityColor color = FacilityColor.Green,
            int score = 0,
            IReadOnlyList<RewardOp> resourceCost = null,
            int voucherCost = 0,
            FacilityKeyword keywords = FacilityKeyword.None,
            IReadOnlyList<RewardOp> entryEffects = null)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Color = color;
            Score = score;
            ResourceCost = resourceCost ?? Array.Empty<RewardOp>();
            VoucherCost = voucherCost;
            Keywords = keywords;
            EntryEffects = entryEffects ?? Array.Empty<RewardOp>();
        }

        /// <summary>牌面关键词「唯一」。</summary>
        public bool IsUnique => (Keywords & FacilityKeyword.Unique) != 0;

        /// <summary>牌面关键词「免费」。</summary>
        public bool IsFree => (Keywords & FacilityKeyword.Free) != 0;

        /// <summary>资源费用是否付得起（只看持有量，不扣减）。</summary>
        public bool CanPayWithResources(ResourcePool pool)
        {
            foreach (RewardOp op in ResourceCost)
                if (!pool.Has(op.Resource, op.Amount)) return false;
            return true;
        }

        public override string ToString() => $"设施牌[{Id}] {Kind}·{Name}";
    }

    /// <summary>
    /// 一个最小「奖励 / 代价」单元的类别。事件牌选项的奖励与条件段都由它组合而成。
    /// 这是纯数据单元、不含结算逻辑；结算由 <c>RuleSpec.md</c> §11 的通用效果执行器统一处理，
    /// 不得为每张牌硬编码（§11 明文要求）。
    /// </summary>
    public enum RewardKind
    {
        /// <summary>获得资源：<see cref="RewardOp.Resource"/> + <see cref="RewardOp.Amount"/>。</summary>
        GainResource = 0,

        /// <summary>支付资源：资源不足时条件段无法执行。</summary>
        PayResource = 1,

        /// <summary>获得哥伦比亚金券。</summary>
        GainVouchers = 2,

        /// <summary>支付哥伦比亚金券。</summary>
        PayVouchers = 3,

        /// <summary>获得分数。</summary>
        GainScore = 4,

        /// <summary>失去分数。</summary>
        LoseScore = 5,

        /// <summary>放置影响力：落点由 <see cref="RewardOp.Scope"/> 决定。</summary>
        PlaceInfluence = 6,

        /// <summary>选择企业：仅选定企业板，本身不改变等级（见 EnterpriseExpansion.md §4.1）。</summary>
        ChooseEnterprise = 7,

        /// <summary>所有对手各获得资源。</summary>
        OpponentsGainResource = 8,

        /// <summary>所有对手各获得金券。</summary>
        OpponentsGainVouchers = 9,
    }

    /// <summary>
    /// 「放置影响力」的落点规则，对应牌面不同写法（RuleSpec §6.4a 部署规则 / 牌面指定的限定落点）。
    /// </summary>
    public enum InfluencePlacementScope
    {
        /// <summary>牌面用通用的「部署影响力」图标、未附落点文字 —— 适用「部署」主要行动的放置规则，落点不限。</summary>
        DeployRules = 0,

        /// <summary>「在此资源点或相邻的航道上」——限定在事件所在资源点或其相连的航道。</summary>
        ThisNodeOrAdjacentRoutes = 1,

        /// <summary>「在此资源点相邻的航道上」——仅限定在事件所在资源点相连的航道（不含资源点本身）。</summary>
        AdjacentRoutesOnly = 2,
    }

    /// <summary>一个最小「奖励 / 代价」单元。值类型，可自由组合成列表；由静态工厂方法构造。</summary>
    public readonly struct RewardOp
    {
        public readonly RewardKind Kind;

        /// <summary>涉及的资源种类；仅 GainResource / PayResource / OpponentsGainResource 使用。</summary>
        public readonly ResourceType Resource;

        public readonly int Amount;

        /// <summary>「放置影响力」的落点规则；仅 PlaceInfluence 使用。</summary>
        public readonly InfluencePlacementScope Scope;

        private RewardOp(RewardKind kind, ResourceType resource, int amount, InfluencePlacementScope scope)
        {
            Kind = kind;
            Resource = resource;
            Amount = amount;
            Scope = scope;
        }

        public static RewardOp Gain(ResourceType type, int amount) => new(RewardKind.GainResource, type, amount, default);
        public static RewardOp Pay(ResourceType type, int amount) => new(RewardKind.PayResource, type, amount, default);
        public static RewardOp Vouchers(int amount) => new(RewardKind.GainVouchers, default, amount, default);
        public static RewardOp PayVouchers(int amount) => new(RewardKind.PayVouchers, default, amount, default);
        public static RewardOp Score(int amount) => new(RewardKind.GainScore, default, amount, default);
        public static RewardOp LoseScore(int amount) => new(RewardKind.LoseScore, default, amount, default);
        public static RewardOp Influence(InfluencePlacementScope scope, int amount = 1) => new(RewardKind.PlaceInfluence, default, amount, scope);
        public static RewardOp Enterprise(int count = 1) => new(RewardKind.ChooseEnterprise, default, count, default);
        public static RewardOp OpponentsGain(ResourceType type, int amount) => new(RewardKind.OpponentsGainResource, type, amount, default);
        public static RewardOp OpponentsGainVouchers(int amount) => new(RewardKind.OpponentsGainVouchers, default, amount, default);

        public override string ToString() => Kind switch
        {
            RewardKind.GainResource => $"获得 {Resource}×{Amount}",
            RewardKind.PayResource => $"支付 {Resource}×{Amount}",
            RewardKind.GainVouchers => $"获得金券×{Amount}",
            RewardKind.PayVouchers => $"支付金券×{Amount}",
            RewardKind.GainScore => $"获得 {Amount} 分",
            RewardKind.LoseScore => $"失去 {Amount} 分",
            RewardKind.PlaceInfluence => $"放置影响力×{Amount}（{Scope}）",
            RewardKind.ChooseEnterprise => $"选择企业×{Amount}",
            RewardKind.OpponentsGainResource => $"所有对手各获得 {Resource}×{Amount}",
            RewardKind.OpponentsGainVouchers => $"所有对手各获得金券×{Amount}",
            _ => Kind.ToString(),
        };
    }

    /// <summary>
    /// 事件牌的一个选项（牌面「你的选择…」下的条目）。
    /// 含「→」的选项拆成：<see cref="Cost"/> = 条件段（可整体放弃）、<see cref="Rewards"/> = 结果段（必须执行）。
    /// </summary>
    public sealed class EventOption
    {
        /// <summary>选项文案（牌面原文）。</summary>
        public readonly string Text;

        /// <summary>条件段（「→」前的操作），可整体放弃；无代价时为空。</summary>
        public readonly IReadOnlyList<RewardOp> Cost;

        /// <summary>结果段（「→」后的奖励），条件满足（或无条件）时必须执行。</summary>
        public readonly IReadOnlyList<RewardOp> Rewards;

        public bool HasCost => Cost.Count > 0;

        public EventOption(string text, IReadOnlyList<RewardOp> rewards)
            : this(text, Array.Empty<RewardOp>(), rewards) { }

        public EventOption(string text, IReadOnlyList<RewardOp> cost, IReadOnlyList<RewardOp> rewards)
        {
            Text = text;
            Cost = cost ?? Array.Empty<RewardOp>();
            Rewards = rewards ?? Array.Empty<RewardOp>();
        }

        public override string ToString() => HasCost
            ? $"{Text}｜代价：{string.Join("、", Cost)}｜奖励：{string.Join("、", Rewards)}"
            : $"{Text}｜奖励：{string.Join("、", Rewards)}";
    }

    /// <summary>
    /// 事件牌定义。牌背分红 / 绿 / 黄三色（基础版共 22 张：绿 6、黄 10、红 6），
    /// 与资源点颜色一一对应，决定翻开哪种颜色的事件牌（RuleSpec §7 / §10.3）。
    /// 另含 2 张企业扩展专用替换件（带「E」标记）。数据来源见 <c>Doc/EventCards.md</c>。
    /// </summary>
    public sealed class EventCardDef
    {
        public readonly int Id;

        /// <summary>牌名（如「中立采石场」）。</summary>
        public readonly string Name;

        /// <summary>牌背颜色，等价于对应资源点的颜色。</summary>
        public readonly NodeColor BackColor;

        /// <summary>资源点指示物提供的资源种类。</summary>
        public readonly ResourceType TokenResource;

        /// <summary>资源点指示物提供的数量：1 或 2（红色区域「X2」，RuleSpec §10.3）。</summary>
        public readonly int TokenAmount;

        /// <summary>牌名右上角带「4」标记 = 三人局放回游戏盒（RuleSpec §4.1）。</summary>
        public readonly bool FourPlayerOnly;

        /// <summary>牌名右上角带「E」标记 = 企业扩展专用替换件。</summary>
        public readonly bool IsEnterpriseCard;

        /// <summary>本牌替代的基础牌 id；非替换件为 -1。</summary>
        public readonly int VariantOfId;

        /// <summary>事件区选项（「你的选择…」）。</summary>
        public readonly IReadOnlyList<EventOption> Options;

        public EventCardDef(int id, string name, NodeColor backColor, ResourceType tokenResource, int tokenAmount,
            bool fourPlayerOnly, bool isEnterpriseCard, int variantOfId, IReadOnlyList<EventOption> options)
        {
            Id = id;
            Name = name;
            BackColor = backColor;
            TokenResource = tokenResource;
            TokenAmount = tokenAmount;
            FourPlayerOnly = fourPlayerOnly;
            IsEnterpriseCard = isEnterpriseCard;
            VariantOfId = variantOfId;
            Options = options ?? Array.Empty<EventOption>();
        }

        public override string ToString() => $"事件牌[{Id}] {Name}·牌背 {BackColor}·指示物 {TokenResource}×{TokenAmount}";
    }

    /// <summary>
    /// 城市样式宣告条件（§6.5b / §10.2「颜色 + 位置组合」）里的一个格子：相对行列 + 允许的颜色。
    /// 坐标是**相对**的（条件自己的左上角为 0,0），匹配时整体平移到城市面板上。
    ///
    /// 牌面上画成**斜分**的方块代表「多色其一即可」（如蓝红斜分 = 蓝色或红色都行）。
    /// 任何情况下，彩色设施（<see cref="FacilityColor.Rainbow"/>）都能当作任意颜色（§10.2）。
    /// </summary>
    public readonly struct PatternCell
    {
        /// <summary>相对行号（0 起，向下为正）。</summary>
        public readonly int Row;

        /// <summary>相对列号（0 起，向右为正）。</summary>
        public readonly int Column;

        /// <summary>这一格允许的设施颜色；单色格 1 个，斜分格 2 个及以上。</summary>
        public readonly IReadOnlyList<FacilityColor> Colors;

        public PatternCell(int row, int column, params FacilityColor[] colors)
        {
            Row = row;
            Column = column;
            Colors = colors ?? Array.Empty<FacilityColor>();
        }

        /// <summary>某个颜色的设施能不能满足这一格：彩色万能，否则看是否落在允许的颜色里。</summary>
        public bool Accepts(FacilityColor actual)
        {
            if (actual == FacilityColor.Rainbow) return true; // §10.2：彩色设施可视为任意颜色
            if (Colors.Count == 0) return true;

            foreach (FacilityColor allowed in Colors)
                if (allowed == FacilityColor.Rainbow || allowed == actual) return true;

            return false;
        }

        public override string ToString()
        {
            string colors = Colors.Count == 0 ? "任意" : string.Join("/", Colors);
            return $"(行{Row},列{Column}) {colors}";
        }
    }

    /// <summary>
    /// 城市样式牌上的一个宣告条件：一组带颜色的相对格子。
    /// 匹配时可以整体平移、并按 90° 的倍数旋转（§10.2：可旋转，**不可翻转**）。
    /// 匹配算法见 Engine 层的 <c>CityPanelQuery</c>。
    /// </summary>
    public sealed class PatternCondition
    {
        public readonly IReadOnlyList<PatternCell> Cells;

        public PatternCondition(IReadOnlyList<PatternCell> cells)
        {
            Cells = cells ?? Array.Empty<PatternCell>();
        }

        public PatternCondition(params PatternCell[] cells) : this((IReadOnlyList<PatternCell>)cells) { }

        public bool IsEmpty => Cells.Count == 0;

        public override string ToString() => $"样式条件[{Cells.Count} 格] {string.Join("、", Cells)}";
    }

    /// <summary>
    /// 城市样式牌定义。基础版 6 张 + 企业扩展 6 张：I 级 4 张、II 级 2 张（RuleSpec §3 / §10.2；
    /// 扩展见 <c>Doc/EnterpriseExpansion.md</c> §2 / §3.1）。宣告条件与奖励分数已按牌面录入；
    /// 宣告效果 / 解锁的特殊行动等牌面数据在录入前一律留默认值，因此本类仍是「数据驱动」的：
    /// 录入后 <c>DeclareCityPatternCommand</c> 自动生效，无需改动规则代码。
    /// </summary>
    public sealed class CityPatternCardDef
    {
        public readonly int Id;

        /// <summary>样式等级：1 = I 级，2 = II 级。</summary>
        public readonly int Level;

        /// <summary>牌名，如「军工业化区域」「源石工业中枢ᴱ」。</summary>
        public readonly string Name;

        /// <summary>宣告奖励分数，宣告时立即结算（§6.5b 步 3 / §9.1）。牌面未录入时为 0。</summary>
        public readonly int Score;

        /// <summary>宣告条件：一组带颜色的相对格子（位置 + 颜色）。牌面未录入时为 null。</summary>
        public readonly PatternCondition Condition;

        /// <summary>宣告效果，宣告第三步结算（§6.5b 步 3 / §11）；空表示这张牌没有宣告效果。牌面未录入时为空。</summary>
        public readonly IReadOnlyList<RewardOp> DeclareEffects;

        public CityPatternCardDef(int id, int level, string name = null, int score = 0,
            PatternCondition condition = null, IReadOnlyList<RewardOp> declareEffects = null)
        {
            Id = id;
            Level = level;
            Name = name;
            Score = score;
            Condition = condition;
            DeclareEffects = declareEffects ?? Array.Empty<RewardOp>();
        }

        /// <summary>是否已经录入宣告条件 —— 没录入的牌无法被宣告（骨架阶段的守卫）。</summary>
        public bool HasCondition => Condition != null && !Condition.IsEmpty;

        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Name) ? $"城市样式牌[{Id}]" : Name;
            return HasCondition
                ? $"{name}（{Level} 级·{Condition}）"
                : $"{name}（{Level} 级）";
        }
    }
}