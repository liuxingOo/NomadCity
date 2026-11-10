using System;
using System.Collections.Generic;

namespace NomadCity.Core.Model
{
    /// <summary>
    /// 卡牌定义层：描述「一张牌是什么」。纯 C#、无 UnityEngine 依赖。
    /// 引擎内部一律用 int id 表示卡牌，牌堆 / 手牌 / 供应区里存的都是 id；
    /// 这些定义只用于查名字与读取静态属性，规则实现不依赖卡面文案。
    ///
    /// 基础版牌面数据**均已录入**：22 张事件牌（`Doc/EventCards.md`）、41 张普通设施牌（`Doc/FacilityCards.md`）、
    /// 20 张角色牌（`Doc/CardEffects.md` §3）、6 张城市样式牌（阵型 / 分数 / 特殊行动费用与效果，`Doc/RuleSpec.md` §10.2 / §6.3f）。
    /// 城市样式牌牌面只有「宣告条件 + 宣告分数 + 解锁的特殊行动 + 标记轨道」四块，**没有**独立的「宣告效果」栏
    /// （2026-10-19 核对实体牌面 `Art/Cards/CityPattern/card_cp_lv1.jpg` / `card_cp_lv2.jpg`）。
    /// 企业扩展部分见 `Doc/EnterpriseExpansion.md`。
    /// 后续数据会由 Data 层的 ScriptableObject 转换为这些定义（见 <see cref="ICardCatalog"/>）。
    /// </summary>

    /// <summary>角色牌的两种互相独立的效果（RuleSpec §6.5a）。</summary>
    public enum CharacterEffectKind
    {
        /// <summary>策略。</summary>
        Strategy = 0,

        /// <summary>计谋。</summary>
        Tactics = 1,
    }

    /// <summary>
    /// 角色牌定义。基础版 5 角色 × 4 色 = 20 张；同一颜色的 5 张角色相同。
    /// 策略 / 计谋的牌面名与效果序列来源见 <c>Doc/CardEffects.md</c> §3；
    /// 效果一律表达为通用的 <see cref="RewardOp"/> 序列（RuleSpec §11），不硬编码。
    /// </summary>
    public sealed class CharacterCardDef
    {
        public readonly int Id;
        public readonly PlayerColor Color;

        /// <summary>角色名：雷蛇 / 极境 / 德克萨斯 / 锡人 / 坎诺特。</summary>
        public readonly string Name;

        /// <summary>策略牌面名（如「安保协议」）；未录入为空串。</summary>
        public readonly string StrategyName;

        /// <summary>计谋牌面名（如「控制阵地」）；未录入为空串。</summary>
        public readonly string TacticsName;

        /// <summary>策略效果序列；空表示未录入。</summary>
        public readonly IReadOnlyList<RewardOp> StrategyOps;

        /// <summary>计谋效果序列；空表示未录入。</summary>
        public readonly IReadOnlyList<RewardOp> TacticsOps;

        public CharacterCardDef(int id, PlayerColor color, string name,
            string strategyName = null, string tacticsName = null,
            IReadOnlyList<RewardOp> strategyOps = null, IReadOnlyList<RewardOp> tacticsOps = null)
        {
            Id = id;
            Color = color;
            Name = name;
            StrategyName = strategyName ?? string.Empty;
            TacticsName = tacticsName ?? string.Empty;
            StrategyOps = strategyOps ?? Array.Empty<RewardOp>();
            TacticsOps = tacticsOps ?? Array.Empty<RewardOp>();
        }

        /// <summary>取某一效果（策略 / 计谋）的效果序列。</summary>
        public IReadOnlyList<RewardOp> OpsOf(CharacterEffectKind kind) =>
            kind == CharacterEffectKind.Tactics ? TacticsOps : StrategyOps;

        /// <summary>取某一效果（策略 / 计谋）的牌面名。</summary>
        public string NameOf(CharacterEffectKind kind) =>
            kind == CharacterEffectKind.Tactics ? TacticsName : StrategyName;

        /// <summary>这张角色牌是否已录入效果（策略与计谋都为空则视为未录入）。</summary>
        public bool HasEffects => StrategyOps.Count > 0 || TacticsOps.Count > 0;

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

    /// <summary>
    /// 「每有 1 个／1 种 X，就……」类效果的计数来源（统计学见 <c>CityPanelQuery.Count</c>）。
    /// </summary>
    public enum CountSource
    {
        None = 0,

        /// <summary>城市面板上核心指挥塔十字相邻（上下左右）的设施张数 —— 「城市化区域」。</summary>
        AdjacentCoreFacilities = 1,

        /// <summary>城市面板上**非彩色**设施的种数（按不同牌名去重）—— 「城邦工业区」。</summary>
        NonColorFacilityKinds = 2,

        /// <summary>玩家放在**航道**上的影响力总数 —— 「罗德岛」2 级企业特效。</summary>
        InfluenceOnRoutes = 3,
    }

    /// <summary>
    /// 建筑费用修正：按 <see cref="CountSource"/> 数出来的数量 × <see cref="PerCount"/>，
    /// 从「资源费用」里对应资源的那一份上扣减（不会低于 0）。只影响资源费用组，不影响金券费用。
    /// 例：「城邦工业区」= 面板上每有 1 种非彩色设施，少付 1 源岩。
    /// </summary>
    public sealed class FacilityCostModifier
    {
        public readonly CountSource Source;
        public readonly ResourceType Resource;
        public readonly int PerCount;

        public FacilityCostModifier(CountSource source, ResourceType resource, int perCount)
        {
            Source = source;
            Resource = resource;
            PerCount = perCount;
        }
    }

    /// <summary>设施牌定义。41 张普通设施牌的牌面数据见 <c>Doc/FacilityCards.md</c> 与 <c>CardCatalog.BuildFacilities</c>。</summary>
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

        /// <summary>建筑费用修正（按面板状况少付某种资源）；null 表示没有修正。</summary>
        public readonly FacilityCostModifier CostModifier;

        public FacilityCardDef(int id, FacilityKind kind, string name,
            FacilityColor color = FacilityColor.Green,
            int score = 0,
            IReadOnlyList<RewardOp> resourceCost = null,
            int voucherCost = 0,
            FacilityKeyword keywords = FacilityKeyword.None,
            IReadOnlyList<RewardOp> entryEffects = null,
            FacilityCostModifier costModifier = null)
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
            CostModifier = costModifier;
        }

        /// <summary>牌面关键词「唯一」。</summary>
        public bool IsUnique => (Keywords & FacilityKeyword.Unique) != 0;

        /// <summary>牌面关键词「免费」。</summary>
        public bool IsFree => (Keywords & FacilityKeyword.Free) != 0;

        /// <summary>
        /// 资源费用是否付得起（只看持有量，不扣减）。
        /// 资源费用列里可能混有金券成分（如「城邦工业区」为 源岩4·源石3·金券2），故金券持有量也要一并传入。
        /// </summary>
        public bool CanPayWithResources(ResourcePool pool, int vouchers) =>
            CanPayWithResources(ResourceCost, pool, vouchers);

        /// <summary>
        /// 给定一串费用单元能不能付得起（只看持有量、不扣减）。建筑费用修正折算后的费用列也走这里。
        /// </summary>
        public static bool CanPayWithResources(IReadOnlyList<RewardOp> cost, ResourcePool pool, int vouchers)
        {
            if (cost == null) return true;

            foreach (RewardOp op in cost)
            {
                if (op.Kind == RewardKind.PayVouchers)
                {
                    if (vouchers < op.Amount) return false;
                }
                else if (!pool.Has(op.Resource, op.Amount))
                {
                    return false;
                }
            }
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

        /// <summary>
        /// 二选一：用 <c>or</c> 连接的两个（或更多）完整效果，玩家必须择一执行（§11.4）。
        /// 分支内容存在 <see cref="RewardOp.Branches"/> 里。
        /// </summary>
        Alternative = 10,

        /// <summary>拿走（移除）场上 1 个影响力；可以是别人的，被移除的标记退回其所有者的供应堆。</summary>
        RemoveInfluence = 11,

        /// <summary>移动 1 个自己的影响力到别的空格；不增减影响力总数（§6.4b）。</summary>
        RelocateInfluence = 12,

        /// <summary>
        /// 替换 1 个影响力：移除一个影响力后**在同一个空格**放置一个自己的影响力；
        /// 若该空格已不能放置（如该资源点停着对手的城市），则只移除、不放置。
        /// </summary>
        ReplaceInfluence = 13,

        /// <summary>每数到一个 <see cref="CountSource"/> 就获得 <c>Amount</c> 金券（Amount = 每个计数单位的金券数）。</summary>
        GainVouchersPerCount = 14,

        /// <summary>进行一次建造（「简陋工程营」）：由选择器挑一张设施牌与面板空位，走建设的完整流程（照牌面费用付费）。</summary>
        BuildOnce = 15,

        /// <summary>进行一次城市移动，且**不支付** 3 源石（「高性能动力设施」）。其余步骤同 §6.4d。</summary>
        FreeCityMove = 16,

        /// <summary>执行 1 次探索（「载具仓库」的分支）：由选择器挑一个可探索的资源点，走探索的完整流程。</summary>
        ExploreOnce = 17,

        /// <summary>结算另一张设施的入场效果（「附属能源设施」）：由选择器在**十字相邻的非彩色**设施里挑一个，执行它的入场效果。</summary>
        SettleAnotherEntryEffect = 18,

        /// <summary>本回合收尾阶段「传递起始标记」时，改由本玩家获得（「联邦理事会」）。</summary>
        PassStartMarker = 19,

        /// <summary>按单价把任意资源换成金券（「贸易街区」）：由选择器给出各资源的出售数量。</summary>
        SellResources = 20,

        /// <summary>从余下的延伸枢纽中选 1 张并建设（「物流枢纽」）。</summary>
        BuildExtensionHub = 21,

        /// <summary>延迟结算：内嵌效果（<see cref="RewardOp.Ops"/>）留到本回合收尾阶段执行（§11.4）。</summary>
        Deferred = 22,

        /// <summary>
        /// 自由分配：合计获得 <c>Amount</c> 个资源，由玩家在 <see cref="RewardOp.SpreadOptions"/>
        /// 给出的若干资源种类之间任意分配（「开采电铲」：合计获得 5 个，源岩／源石／异铁任意分配）。
        /// </summary>
        GainSpread = 23,

        /// <summary>
        /// 在**上一次城市移动所用的航道**上放置 1 个影响力（「高性能动力设施」）：
        /// 城市移动只能移到相邻资源点（只经过一条航道），故落点就是那条航道上的空格。
        /// 所用航道记在 <c>GameState.LastCityMoveRouteId</c>。
        /// </summary>
        PlaceInfluenceOnCityMoveRoute = 24,

        /// <summary>
        /// 移除**自己的** <c>Amount</c> 个影响力（退回自己的供应堆；「雷蛇·策略」收尾阶段的延迟效果）。
        /// 与 <see cref="RemoveInfluence"/> 的区别：候选只含自己的影响力格。
        /// </summary>
        RemoveOwnInfluence = 25,

        /// <summary>
        /// 「极境·策略」：在 <see cref="RewardOp.SpreadOptions"/> 给出的基础资源里，选**自己持有量最少**
        /// 的一种（并列时由玩家自选），获得 <c>Amount</c> 个该资源。
        /// </summary>
        ChooseLeastHeldResource = 26,

        /// <summary>
        /// 「极境·计谋」：进行一次免费城市移动（同 <see cref="FreeCityMove"/>），但候选限制为
        /// 「与移动城市相邻**且该资源点上有自己的影响力**」。
        /// </summary>
        FreeCityMoveOwnInfluenced = 27,

        /// <summary>
        /// 「德克萨斯·策略」：从设施供应区选 1 张设施牌，背面朝上放到设施牌堆**底部**，
        /// 之后按补充规则（RuleSpec §6.4e）从牌堆补牌至供应区 6 张。
        /// </summary>
        SupplyCardToDeckBottom = 28,

        /// <summary>
        /// 「锡人·策略」：顺序的**可选**步骤（<see cref="RewardOp.Branches"/>），每步先问玩家
        /// 「执行 / 终止」；任一步终止则其后全部跳过。与 <see cref="Alternative"/> 不同 —— 这里不是
        /// 必选其一，而是逐级可选、随时可停。
        /// </summary>
        OptionalChain = 29,

        /// <summary>
        /// 「锡人·计谋」：把自己角色牌弃牌区的牌**全部**收回手牌；每收回 1 张，在
        /// <see cref="RewardOp.Branches"/> 的两项之间二选一执行（本牌此时还没进弃牌区，故不计入）。
        /// </summary>
        RecallDiscardPerCard = 30,

        /// <summary>
        /// 「坎诺特·计谋」：由出牌者从三种基础资源中选 1 种；**所有玩家**（含出牌者）把该种资源
        /// 按 <c>Amount</c> 金券 / 个的单价全部卖光，所得金券归各自。分数另用 <see cref="GainScore"/> 表达。
        /// </summary>
        LevyResourceSale = 31,

        /// <summary>
        /// 自由支付：合计支付 <c>Amount</c> 个资源，由玩家在 <see cref="RewardOp.SpreadOptions"/>
        /// 给出的若干资源种类之间任意分配（「复合动力系统」特殊行动：合计支付 3 个，源岩／异铁任意分配）。
        /// 与 <see cref="GainSpread"/> 相对；分配由选择器回答（<c>IEffectChoices.ChooseResourceSpread</c>）。
        /// </summary>
        PaySpread = 32,

        /// <summary>
        /// 放置若干个影响力，数量 = **当前特殊行动所在的那张城市样式牌上**自己的玩家标记数，
        /// 并以 <c>Amount</c> 封顶（「军工业化区域」特殊行动：放置 X 个，X = 标记数，至多 3 个）。
        /// 计数来源由 <c>GameState.ActiveSpecialActionPatternId</c> 指定（模仿 <c>LastCityMoveRouteId</c> 的通道）。
        /// </summary>
        PlaceInfluencePerPatternMarkers = 33,

        /// <summary>本回合给本玩家追加 <c>Amount</c> 次「额外主要行动」（「源石工业中枢」特殊行动：额外 2 次）。</summary>
        ExtraMainActions = 34,

        /// <summary>
        /// 本回合「使用特殊行动的那一轮」内，禁止本玩家使用角色牌
        /// （「源石工业中枢」/「高效移动管理体系」特殊行动）。记为 <c>PlayerState.CharacterCardBanRound</c>。
        /// </summary>
        ForbidCharacterCardsThisRound = 35,
    }

    /// <summary>
    /// 一段完整效果，用作 <see cref="RewardKind.Alternative"/> 的一个分支（§11.4 的 `or` 分支）。
    /// </summary>
    public sealed class EffectBranch
    {
        /// <summary>分支说明，供 UI 显示（如「替换 1 个影响力」）；可为空。</summary>
        public readonly string Label;

        /// <summary>这一分支要执行的效果序列（按序执行）。</summary>
        public readonly IReadOnlyList<RewardOp> Ops;

        public EffectBranch(string label, params RewardOp[] ops) : this(label, (IReadOnlyList<RewardOp>)ops) { }

        public EffectBranch(string label, IReadOnlyList<RewardOp> ops)
        {
            Label = label;
            Ops = ops ?? Array.Empty<RewardOp>();
        }

        public override string ToString() => string.IsNullOrEmpty(Label)
            ? string.Join("、", Ops)
            : $"{Label}";
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

        /// <summary>二选一的分支；仅 Alternative 使用。</summary>
        public readonly IReadOnlyList<EffectBranch> Branches;

        /// <summary>计数来源；仅 GainVouchersPerCount 使用。</summary>
        public readonly CountSource Count;

        /// <summary>延迟结算要留到收尾阶段执行的内嵌效果序列；仅 Deferred 使用。</summary>
        public readonly IReadOnlyList<RewardOp> Ops;

        /// <summary>可自由分配的资源种类；GainSpread / PaySpread 使用（顺序即分配顺序）。</summary>
        public readonly IReadOnlyList<ResourceType> SpreadOptions;

        private RewardOp(RewardKind kind, ResourceType resource, int amount, InfluencePlacementScope scope,
            IReadOnlyList<EffectBranch> branches = null, CountSource count = default,
            IReadOnlyList<RewardOp> ops = null, IReadOnlyList<ResourceType> spreadOptions = null)
        {
            Kind = kind;
            Resource = resource;
            Amount = amount;
            Scope = scope;
            Branches = branches;
            Count = count;
            Ops = ops;
            SpreadOptions = spreadOptions;
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

        /// <summary>二选一：玩家必须从这些分支里择一执行（§11.4）。</summary>
        public static RewardOp Alternative(params EffectBranch[] branches) => new(RewardKind.Alternative, default, 1, default, branches);

        /// <summary>拿走场上 1 个影响力（可以是别人的）。</summary>
        public static RewardOp RemoveInfluence(int amount = 1) => new(RewardKind.RemoveInfluence, default, amount, default);

        /// <summary>移动 1 个自己的影响力。</summary>
        public static RewardOp RelocateInfluence(int amount = 1) => new(RewardKind.RelocateInfluence, default, amount, default);

        /// <summary>替换 1 个影响力（移除一个后在同一空格放置一个自己的）。</summary>
        public static RewardOp ReplaceInfluence(int amount = 1) => new(RewardKind.ReplaceInfluence, default, amount, default);

        /// <summary>每数到一个 <paramref name="source"/> 就获得 <paramref name="perUnit"/> 金券。</summary>
        public static RewardOp VouchersPerCount(CountSource source, int perUnit) =>
            new(RewardKind.GainVouchersPerCount, default, perUnit, default, null, source);

        /// <summary>进行一次建造（照牌面费用付费）。</summary>
        public static RewardOp BuildOnce(int amount = 1) => new(RewardKind.BuildOnce, default, amount, default);

        /// <summary>进行一次不付 3 源石的城市移动。</summary>
        public static RewardOp FreeCityMove(int amount = 1) => new(RewardKind.FreeCityMove, default, amount, default);

        /// <summary>执行 1 次探索。</summary>
        public static RewardOp ExploreOnce(int amount = 1) => new(RewardKind.ExploreOnce, default, amount, default);

        /// <summary>结算另一张（十字相邻的非彩色）设施的入场效果。</summary>
        public static RewardOp SettleAnotherEntryEffect() => new(RewardKind.SettleAnotherEntryEffect, default, 1, default);

        /// <summary>收尾阶段传递起始标记时改由本玩家获得。</summary>
        public static RewardOp PassStartMarker() => new(RewardKind.PassStartMarker, default, 1, default);

        /// <summary>按单价把任意资源换成金券。</summary>
        public static RewardOp SellResources() => new(RewardKind.SellResources, default, 1, default);

        /// <summary>从余下的延伸枢纽中选 1 张并建设。</summary>
        public static RewardOp BuildExtensionHub() => new(RewardKind.BuildExtensionHub, default, 1, default);

        /// <summary>延迟结算：把这串效果留到本回合收尾阶段执行（§11.4）。</summary>
        public static RewardOp Deferred(params RewardOp[] ops) =>
            new(RewardKind.Deferred, default, 1, default, null, default, ops);

        /// <summary>合计获得 <paramref name="total"/> 个资源，由玩家在 <paramref name="options"/> 之间任意分配。</summary>
        public static RewardOp GainSpread(int total, params ResourceType[] options) =>
            new(RewardKind.GainSpread, default, total, default, null, default, null, options);

        /// <summary>在上一次城市移动所用的航道上放置 1 个自己的影响力。</summary>
        public static RewardOp PlaceInfluenceOnCityMoveRoute(int amount = 1) =>
            new(RewardKind.PlaceInfluenceOnCityMoveRoute, default, amount, default);

        /// <summary>移除自己的 1 个影响力（退回供应堆）。</summary>
        public static RewardOp RemoveOwnInfluence(int amount = 1) =>
            new(RewardKind.RemoveOwnInfluence, default, amount, default);

        /// <summary>在给定基础资源里选「自己持有量最少」的一种，获得 <paramref name="amount"/> 个。</summary>
        public static RewardOp ChooseLeastHeldResource(int amount, params ResourceType[] options) =>
            new(RewardKind.ChooseLeastHeldResource, default, amount, default, null, default, null, options);

        /// <summary>进行一次免费城市移动，但目标须相邻且该资源点上有自己的影响力。</summary>
        public static RewardOp FreeCityMoveOwnInfluenced(int amount = 1) =>
            new(RewardKind.FreeCityMoveOwnInfluenced, default, amount, default);

        /// <summary>把设施供应区的 1 张设施牌放回设施牌堆底部，并补牌至供应区 6 张。</summary>
        public static RewardOp SupplyCardToDeckBottom(int amount = 1) =>
            new(RewardKind.SupplyCardToDeckBottom, default, amount, default);

        /// <summary>顺序的**可选**步骤：每步先问「执行 / 终止」，任一步终止则其后全部跳过。</summary>
        public static RewardOp OptionalChain(params EffectBranch[] steps) =>
            new(RewardKind.OptionalChain, default, 1, default, steps);

        /// <summary>把弃牌区角色牌全部收回手牌；每张在给定的两项分支里二选一执行。</summary>
        public static RewardOp RecallDiscardPerCard(params EffectBranch[] perCard) =>
            new(RewardKind.RecallDiscardPerCard, default, 1, default, perCard);

        /// <summary>出牌者选 1 种基础资源，所有玩家按 <paramref name="price"/> 金券 / 个卖光、各自收取。</summary>
        public static RewardOp LevyResourceSale(int price = 2) =>
            new(RewardKind.LevyResourceSale, default, price, default);

        /// <summary>合计支付 <paramref name="total"/> 个资源，由玩家在 <paramref name="options"/> 之间任意分配。</summary>
        public static RewardOp PaySpread(int total, params ResourceType[] options) =>
            new(RewardKind.PaySpread, default, total, default, null, default, null, options);

        /// <summary>放置「当前特殊行动所在样式牌上自己的标记数」个影响力，以 <paramref name="cap"/> 封顶。</summary>
        public static RewardOp InfluencePerPatternMarkers(int cap = 3) =>
            new(RewardKind.PlaceInfluencePerPatternMarkers, default, cap, default);

        /// <summary>本回合给本玩家追加 <paramref name="amount"/> 次额外主要行动。</summary>
        public static RewardOp ExtraMainActions(int amount = 1) =>
            new(RewardKind.ExtraMainActions, default, amount, default);

        /// <summary>本回合「使用特殊行动的那一轮」内禁止本玩家使用角色牌。</summary>
        public static RewardOp ForbidCharacterCardsThisRound() =>
            new(RewardKind.ForbidCharacterCardsThisRound, default, 1, default);

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
            RewardKind.Alternative => Branches == null || Branches.Count == 0
                ? "二选一（无分支）"
                : string.Join(" 或 ", Branches),
            RewardKind.RemoveInfluence => $"拿走场上影响力×{Amount}",
            RewardKind.RelocateInfluence => $"移动自己的影响力×{Amount}",
            RewardKind.ReplaceInfluence => $"替换影响力×{Amount}",
            RewardKind.GainVouchersPerCount => $"每点「{Count}」获得金券×{Amount}",
            RewardKind.BuildOnce => $"进行一次建造×{Amount}",
            RewardKind.FreeCityMove => $"进行一次免费的城市移动×{Amount}",
            RewardKind.ExploreOnce => $"执行 {Amount} 次探索",
            RewardKind.SettleAnotherEntryEffect => "结算另一张设施的入场效果",
            RewardKind.PassStartMarker => "收尾阶段传递起始标记时由本玩家获得",
            RewardKind.SellResources => "按单价售卖资源",
            RewardKind.BuildExtensionHub => "从余下延伸枢纽选 1 张并建设",
            RewardKind.Deferred => Ops == null || Ops.Count == 0
                ? "延迟结算（无内嵌效果）"
                : $"延迟结算（{string.Join("、", Ops)}）",
            RewardKind.GainSpread => $"合计获得 {Amount} 个资源（{string.Join("／", SpreadOptions ?? Array.Empty<ResourceType>())} 任意分配）",
            RewardKind.PlaceInfluenceOnCityMoveRoute => $"在经过的航道上放置影响力×{Amount}",
            RewardKind.RemoveOwnInfluence => $"移除自己的影响力×{Amount}",
            RewardKind.ChooseLeastHeldResource => $"获得最少的资源×{Amount}（{string.Join("／", SpreadOptions ?? Array.Empty<ResourceType>())}）",
            RewardKind.FreeCityMoveOwnInfluenced => $"进行一次免费城市移动（限相邻且有自己的影响力）×{Amount}",
            RewardKind.SupplyCardToDeckBottom => "把 1 张设施供应区的牌放回设施牌堆底部",
            RewardKind.OptionalChain => Branches == null || Branches.Count == 0
                ? "可选链（无步骤）"
                : $"可选链（{string.Join(" → ", Branches)}）",
            RewardKind.RecallDiscardPerCard => Branches == null || Branches.Count == 0
                ? "回收角色牌弃牌区（无分支）"
                : $"回收角色牌弃牌区，每张二选一（{string.Join(" 或 ", Branches)}）",
            RewardKind.LevyResourceSale => $"所有玩家按单价 {Amount} 金券卖光所选资源",
            RewardKind.PaySpread => $"合计支付 {Amount} 个（{string.Join("／", SpreadOptions ?? Array.Empty<ResourceType>())} 任意分配）",
            RewardKind.PlaceInfluencePerPatternMarkers => $"放置影响力×样式牌上自己的标记数（至多 {Amount}）",
            RewardKind.ExtraMainActions => $"本回合额外主要行动×{Amount}",
            RewardKind.ForbidCharacterCardsThisRound => "本轮不能使用角色牌",
            _ => Kind.ToString(),
        };
    }

    /// <summary>
    /// 一条延迟结算（<see cref="RewardKind.Deferred"/>）：把一串效果留到本回合收尾阶段，
    /// 以 <see cref="Seat"/> 的身份执行。收尾阶段由 <c>CleanupResolver</c> 依次取出结算（§8.1）。
    /// </summary>
    public readonly struct DeferredEffect
    {
        /// <summary>以哪个座位玩家的身份结算（资源发放 / 影响力操作等都记在他名下）。</summary>
        public readonly int Seat;

        /// <summary>要延后执行的效果序列。</summary>
        public readonly IReadOnlyList<RewardOp> Ops;

        public DeferredEffect(int seat, IReadOnlyList<RewardOp> ops)
        {
            Seat = seat;
            Ops = ops ?? Array.Empty<RewardOp>();
        }
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

        /// <summary>牌名右上角带「4」标记 = 2／3 人局放回游戏盒（RuleSpec §4.1 / §12）。</summary>
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
    /// 扩展见 <c>Doc/EnterpriseExpansion.md</c> §2 / §3.1）。宣告条件、奖励分数与解锁的特殊行动均已按牌面录入；
    /// 牌面无独立的「宣告效果」栏，所以宣告时只结算分数与放标记（<c>DeclareCityPatternCommand</c>），不另设效果字段。
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

        /// <summary>
        /// 解锁的特殊行动的费用（§6.3f / §10.2-4），使用特殊行动时先结算；空表示这次行动不花费用。
        /// 与设施的「资源费用」不同，这里是直接可执行的效果序列（可含 <see cref="RewardKind.PaySpread"/> 之类的自由分配）。
        /// </summary>
        public readonly IReadOnlyList<RewardOp> SpecialActionCost;

        /// <summary>解锁的特殊行动的效果，付完费用后结算；空表示这张牌没有录入特殊行动（无法使用，§6.3f）。</summary>
        public readonly IReadOnlyList<RewardOp> SpecialActionEffects;

        public CityPatternCardDef(int id, int level, string name = null, int score = 0,
            PatternCondition condition = null,
            IReadOnlyList<RewardOp> specialActionCost = null,
            IReadOnlyList<RewardOp> specialActionEffects = null)
        {
            Id = id;
            Level = level;
            Name = name;
            Score = score;
            Condition = condition;
            SpecialActionCost = specialActionCost ?? Array.Empty<RewardOp>();
            SpecialActionEffects = specialActionEffects ?? Array.Empty<RewardOp>();
        }

        /// <summary>是否已经录入宣告条件 —— 没录入的牌无法被宣告（骨架阶段的守卫）。</summary>
        public bool HasCondition => Condition != null && !Condition.IsEmpty;

        /// <summary>是否已经录入解锁的特殊行动 —— 没录入的牌不提供特殊行动。</summary>
        public bool HasSpecialAction => SpecialActionEffects.Count > 0;

        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Name) ? $"城市样式牌[{Id}]" : Name;
            return HasCondition
                ? $"{name}（{Level} 级·{Condition}）"
                : $"{name}（{Level} 级）";
        }
    }
}