using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Model.Enterprise
{
    /// <summary>
    /// 合作企业板 / 莱茵生命科室板的静态定义。
    ///
    /// 数据来源：<c>Assets/_Project/Art/Cards/Enterprise/card_enterprise.jpg</c>（8 家企业板）
    /// 与 <c>laiyin1.png</c>–<c>laiyin3.png</c>（3 张科室板），规则说明见
    /// <c>Doc/EnterpriseExpansion.md</c>。
    ///
    /// 每块板有 0–5 级：0 级只是标记起点，1–5 级各有一条「企业特效」或「即时奖励」。
    /// - <see cref="EnterpriseLevel.IsEffect"/> = true：升级只解锁，不自动结算，由玩家另行触发。
    /// - <see cref="EnterpriseLevel.IsEffect"/> = false：升级时立刻结算 <see cref="EnterpriseLevel.Option"/>。
    /// - <see cref="EnterpriseSpecial"/>：现有奖励系统表达不了的特效，交给命令层单独处理。
    /// </summary>
    public enum EnterpriseId
    {
        RhodesIsland = 0, // 罗德岛
        Hyde = 1,         // 海德兄弟
        RhineLab = 2,     // 莱茵生命
        FusenBank = 3,    // 富森银行
        Raythean = 4,     // 雷神工业
        MamaJohn = 5,     // 约翰老妈
        Blacksteel = 6,   // 黑钢国际
        Arim = 7,         // 雷姆必拓
    }

    /// <summary>现有奖励系统（<see cref="RewardOp"/>）表达不了、需要命令层单独处理的特效。</summary>
    public enum EnterpriseSpecial
    {
        None = 0,

        /// <summary>干员特派（罗德岛 3 / 5 级）：尚需干员牌数据，暂不结算。</summary>
        OperatorDispatch = 1,

        /// <summary>选择 1 家其他企业，提升 1 级（罗德岛 1 级）。</summary>
        AdvanceOtherEnterprise = 2,

        /// <summary>从弃牌区取回 1 张角色牌，其余手牌弃掉，获得 10 金券（罗德岛 4 级）。</summary>
        RecallDiscardAndDiscardHand = 3,

        /// <summary>支付源岩 → 设施供应区洗回牌堆并补满，然后免费建造 1 次（海德兄弟 4 级）。</summary>
        SupplyReshuffleAndFreeBuild = 4,

        /// <summary>切换莱茵生命科室板，并结算新科室板对应合作等级的奖励（莱茵生命 1 级）。</summary>
        SwitchDepartment = 5,

        /// <summary>升级奖励来自当前科室板（莱茵生命 2–5 级）。</summary>
        DepartmentReward = 6,

        /// <summary>从设施牌堆选 1 张设施建设，然后洗混牌堆（雷神工业 5 级）。</summary>
        BuildFromDeckAndShuffle = 7,

        /// <summary>进行 1 次不占用主要行动的建设（雷神工业 3 级、工程科 4 级）。</summary>
        BuildWithoutAction = 8,

        /// <summary>从设施供应区免费建造 1 张金券费用不高于 <see cref="EnterpriseLevel.Amount"/> 的设施（工程科 5 级）。</summary>
        BuildFreeFromSupply = 12,

        /// <summary>进行 1 次采集，规则同采集阶段（雷姆必拓 4 级）。</summary>
        ExtraHarvest = 9,

        /// <summary>1 位对手失去 1 分（富森银行 4 级）。</summary>
        OpponentLoseScore = 10,

        /// <summary>降低 1 级该企业的合作等级，并获得该等级的奖励（结构科 5 级；已按卡面核对）。</summary>
        LowerLevelAndSettle = 11,
    }

    /// <summary>企业板上的一级。</summary>
    public sealed class EnterpriseLevel
    {
        /// <summary>0–5。</summary>
        public readonly int Level;

        /// <summary>牌面文字（UI 原样显示）。</summary>
        public readonly string Text;

        /// <summary>是否为企业特效（升级只解锁，不自动结算）。</summary>
        public readonly bool IsEffect;

        /// <summary>自动结算 / 触发时执行的效果；为空表示只有文字。</summary>
        public readonly EventOption Option;

        /// <summary>现有奖励系统表达不了的特效。</summary>
        public readonly EnterpriseSpecial Special;

        /// <summary>附加数值：目前只用于「免费建造的金券费用上限」（0 = 不限）。</summary>
        public readonly int Amount;

        public EnterpriseLevel(int level, string text, bool isEffect, EventOption option = null,
            EnterpriseSpecial special = EnterpriseSpecial.None, int amount = 0)
        {
            Level = level;
            Text = text ?? string.Empty;
            IsEffect = isEffect;
            Option = option;
            Special = special;
            Amount = amount;
        }

        public override string ToString() => $"Lv{Level}：{Text}";
    }

    /// <summary>一块合作企业板。</summary>
    public sealed class EnterpriseBoardDef
    {
        public readonly EnterpriseId Id;
        public readonly string Name;
        public readonly string Subtitle;
        public readonly string Rule;

        private readonly EnterpriseLevel[] levels;

        public EnterpriseBoardDef(EnterpriseId id, string name, string subtitle, string rule,
            EnterpriseLevel[] levels)
        {
            Id = id;
            Name = name;
            Subtitle = subtitle;
            Rule = rule;
            this.levels = levels ?? throw new ArgumentNullException(nameof(levels));
        }

        public int MaxLevel => 5;

        public EnterpriseLevel Level(int level)
        {
            if (level < 0) level = 0;
            if (level >= levels.Length) level = levels.Length - 1;
            return levels[level];
        }
    }

    /// <summary>莱茵生命科室板（4 行奖励对应合作等级 5→2）。</summary>
    public sealed class RhineDepartmentDef
    {
        public readonly int Id;
        public readonly string Name;

        /// <summary>下标即合作等级（2–5）；0／1 级留空。</summary>
        private readonly EnterpriseLevel[] levels = new EnterpriseLevel[6];

        public RhineDepartmentDef(int id, string name, EnterpriseLevel level2, EnterpriseLevel level3,
            EnterpriseLevel level4, EnterpriseLevel level5)
        {
            Id = id;
            Name = name;
            // ⚠️ 级序按科室板从上到下录入为 5→2（扫描件未印等级数字）。
            levels[2] = level2;
            levels[3] = level3;
            levels[4] = level4;
            levels[5] = level5;
        }

        public EnterpriseLevel Level(int level) =>
            level >= 2 && level < levels.Length ? levels[level] : null;
    }

    /// <summary>8 张企业板 + 3 张莱茵生命科室板的查表数据。</summary>
    public static class EnterpriseCatalog
    {
        public const int BoardCount = 8;

        public const int DepartmentCount = 3;

        private static readonly EnterpriseBoardDef[] boards = BuildBoards();

        private static readonly RhineDepartmentDef[] departments = BuildDepartments();

        public static IReadOnlyList<EnterpriseBoardDef> Boards => boards;

        public static IReadOnlyList<RhineDepartmentDef> Departments => departments;

        public static EnterpriseBoardDef Board(int id)
        {
            if (id < 0 || id >= boards.Length) return null;
            return boards[id];
        }

        public static EnterpriseBoardDef Board(EnterpriseId id) => Board((int)id);

        public static RhineDepartmentDef Department(int id)
        {
            if (id < 0 || id >= departments.Length) return null;
            return departments[id];
        }

        // —— 词条构造小工具 ——

        private static EnterpriseLevel Unlock(int level, string text, params RewardOp[] ops) =>
            new(level, text, true, ops.Length == 0 ? null : new EventOption(text, ops));

        private static EnterpriseLevel UnlockCost(int level, string text,
            IReadOnlyList<RewardOp> cost, params RewardOp[] rewards) =>
            new(level, text, true, new EventOption(text, cost, rewards));

        private static EnterpriseLevel UnlockSpecial(int level, string text, EnterpriseSpecial special) =>
            new(level, text, true, null, special);

        /// <summary>企业特效 + 需先支付的费用（如雷姆必拓 4 级「失去 1 分」）。</summary>
        private static EnterpriseLevel UnlockSpecialCost(int level, string text,
            IReadOnlyList<RewardOp> cost, EnterpriseSpecial special) =>
            new(level, text, true, new EventOption(text, cost, Array.Empty<RewardOp>()), special);

        private static EnterpriseLevel Instant(int level, string text, params RewardOp[] ops) =>
            new(level, text, false, new EventOption(text, ops));

        private static EnterpriseLevel InstantSpecial(int level, string text,
            IReadOnlyList<RewardOp> cost, EnterpriseSpecial special) =>
            new(level, text, false, new EventOption(text, cost, Array.Empty<RewardOp>()), special);

        private static EnterpriseLevel Note(int level, string text) => new(level, text, false);

        private static EnterpriseBoardDef[] BuildBoards()
        {
            return new[]
            {
                // ————————————————— 罗德岛 —————————————————
                new EnterpriseBoardDef(EnterpriseId.RhodesIsland, "罗德岛", "RHODES ISLAND",
                    "3／5 级：干员特派。1 级：选 1 家其他企业提升 1 级。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        UnlockSpecial(1, "企业特效：选择 1 家其他企业，提升 1 级该企业的合作等级。",
                            EnterpriseSpecial.AdvanceOtherEnterprise),
                        Unlock(2, "企业特效：在航道上，你每有 1 个自己的影响力，获得 1 金券。",
                            RewardOp.VouchersPerCount(CountSource.InfluenceOnRoutes, 1)),
                        UnlockSpecial(3, "干员特派。", EnterpriseSpecial.OperatorDispatch),
                        UnlockSpecial(4,
                            "企业特效：从角色牌弃牌区选择 1 张牌加入手牌，然后把你其余的手牌放到弃牌区，获得 10 金券。",
                            EnterpriseSpecial.RecallDiscardAndDiscardHand),
                        UnlockSpecial(5, "干员特派。", EnterpriseSpecial.OperatorDispatch),
                    }),

                // ————————————————— 海德兄弟 —————————————————
                new EnterpriseBoardDef(EnterpriseId.Hyde, "海德兄弟", "HYDE BROTHERS",
                    "4 级：把供应区洗回牌堆、补满，然后免费建造 1 次。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        Unlock(1, "企业特效：获得 4 源岩。", RewardOp.Gain(ResourceType.Orirock, 4)),
                        UnlockCost(2, "企业特效：支付 2 源岩，获得 15 金券。",
                            new[] { RewardOp.Pay(ResourceType.Orirock, 2) }, RewardOp.Vouchers(15)),
                        Instant(3, "获得 1 分。", RewardOp.Score(1)),
                        UnlockCost(4,
                            "企业特效：支付 6 源岩，将设施供应区中所有设施牌洗回设施牌堆，之后补满供应区，然后你可进行 1 次免费建造。",
                            new[] { RewardOp.Pay(ResourceType.Orirock, 6) }),
                        Instant(5, "获得 2 分。", RewardOp.Score(2)),
                    }),

                // ————————————————— 莱茵生命 —————————————————
                new EnterpriseBoardDef(EnterpriseId.RhineLab, "莱茵生命", "RHINE LAB",
                    "2–5 级奖励由当前科室板决定；科室板上的内容皆为奖励而非企业特效。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        UnlockSpecial(1,
                            "企业特效：切换至另一张科室板，你获得与该合作等级对应的科室板奖励。",
                            EnterpriseSpecial.SwitchDepartment),
                        new(2, "科室板放置区：2 级奖励按当前科室板结算。", false, null,
                            EnterpriseSpecial.DepartmentReward),
                        new(3, "科室板放置区：3 级奖励按当前科室板结算。", false, null,
                            EnterpriseSpecial.DepartmentReward),
                        new(4, "科室板放置区：4 级奖励按当前科室板结算。", false, null,
                            EnterpriseSpecial.DepartmentReward),
                        new(5, "科室板放置区：5 级奖励按当前科室板结算。", false, null,
                            EnterpriseSpecial.DepartmentReward),
                    }),

                // ————————————————— 富森银行 —————————————————
                new EnterpriseBoardDef(EnterpriseId.FusenBank, "富森银行", "FUSEN BANK",
                    "4 级：你 +1 分，1 位对手 -1 分。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        Unlock(1, "企业特效：获得 8 金券。", RewardOp.Vouchers(8)),
                        Note(2, "（该等级没有奖励。）"),
                        Unlock(3, "企业特效：你获得 20 金券，每位对手获得 4 金券。",
                            RewardOp.Vouchers(20), RewardOp.OpponentsGainVouchers(4)),
                        new(4, "企业特效：你获得 1 分，1 位对手失去 1 分。", true,
                            new EventOption("获得 1 分", new[] { RewardOp.Score(1) }),
                            EnterpriseSpecial.OpponentLoseScore),
                        Instant(5, "获得 3 分。", RewardOp.Score(3)),
                    }),

                // ————————————————— 雷神工业 —————————————————
                new EnterpriseBoardDef(EnterpriseId.Raythean, "雷神工业", "RAYTHEAN",
                    "3 级：不占用主要行动的建设。5 级：从设施牌堆选 1 张建设后洗混牌堆。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        Unlock(1, "企业特效：获得 3 源石。", RewardOp.Gain(ResourceType.Originium, 3)),
                        Instant(2, "获得 1 分。", RewardOp.Score(1)),
                        new(3, "企业特效：支付 2 源石，进行 1 次建造（不占用主要行动）。", true,
                            new EventOption("支付 2 源石 → 建造", new[] { RewardOp.Pay(ResourceType.Originium, 2) },
                                Array.Empty<RewardOp>()),
                            EnterpriseSpecial.BuildWithoutAction),
                        Instant(4, "获得 1 分。", RewardOp.Score(1)),
                        UnlockSpecial(5,
                            "企业特效：从设施牌堆选择 1 张设施，建设该设施，然后你必须洗混设施牌堆。",
                            EnterpriseSpecial.BuildFromDeckAndShuffle),
                    }),

                // ————————————————— 约翰老妈 —————————————————
                new EnterpriseBoardDef(EnterpriseId.MamaJohn, "约翰老妈", "MAMA JOHN'S",
                    "2 级：失去 1 分换 20 金券；4 级：支付 25 金券换 3 分。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        Note(1, "（该等级没有奖励。）"),
                        UnlockCost(2, "企业特效：失去 1 分，获得 20 金券。",
                            new[] { RewardOp.LoseScore(1) }, RewardOp.Vouchers(20)),
                        Unlock(3, "企业特效：获得 1 分。", RewardOp.Score(1)),
                        UnlockCost(4, "企业特效：支付 25 金券，获得 3 分。",
                            new[] { RewardOp.PayVouchers(25) }, RewardOp.Score(3)),
                        Instant(5, "获得 3 分。", RewardOp.Score(3)),
                    }),

                // ————————————————— 黑钢国际 —————————————————
                new EnterpriseBoardDef(EnterpriseId.Blacksteel, "黑钢国际", "BLACKSTEEL WORLDWIDE",
                    "4 级：执行 2 次（放置或移除 1 个影响力）。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        Unlock(1, "企业特效：放置 或 移除 1 个影响力。",
                            RewardOp.Alternative(
                                new EffectBranch("放置 1 个影响力", RewardOp.Influence(InfluencePlacementScope.DeployRules)),
                                new EffectBranch("移除 1 个影响力", RewardOp.RemoveInfluence(1)))),
                        Unlock(2, "企业特效：移动 2 次影响力。", RewardOp.RelocateInfluence(2)),
                        Instant(3, "获得 1 分。", RewardOp.Score(1)),
                        Unlock(4, "企业特效：执行 2 次「放置 或 移除 1 个影响力」。",
                            RewardOp.Alternative(
                                new EffectBranch("第 1 次：放置", RewardOp.Influence(InfluencePlacementScope.DeployRules)),
                                new EffectBranch("第 1 次：移除", RewardOp.RemoveInfluence(1))),
                            RewardOp.Alternative(
                                new EffectBranch("第 2 次：放置", RewardOp.Influence(InfluencePlacementScope.DeployRules)),
                                new EffectBranch("第 2 次：移除", RewardOp.RemoveInfluence(1)))),
                        Instant(5, "获得 1 分。", RewardOp.Score(1)),
                    }),

                // ————————————————— 雷姆必拓 —————————————————
                new EnterpriseBoardDef(EnterpriseId.Arim, "雷姆必拓", "ARIM SOLUTIONS",
                    "2 级：付 4 金券替换资源点上的影响力；4 级：失去 1 分换取 1 次采集。",
                    new[]
                    {
                        Note(0, "合作等级 0（标记起点）。"),
                        Unlock(1, "企业特效：获得 1 源岩、1 源石、1 异铁。",
                            RewardOp.Gain(ResourceType.Orirock, 1),
                            RewardOp.Gain(ResourceType.Originium, 1),
                            RewardOp.Gain(ResourceType.Oriron, 1)),
                        UnlockCost(2, "企业特效：支付 4 金券，替换 1 个在资源点上的影响力。",
                            new[] { RewardOp.PayVouchers(4) }, RewardOp.ReplaceInfluence(1)),
                        Instant(3, "获得 1 分。", RewardOp.Score(1)),
                        UnlockSpecialCost(4, "企业特效：失去 1 分，你进行 1 次采集（其规则和采集阶段相同）。",
                            new[] { RewardOp.LoseScore(1) }, EnterpriseSpecial.ExtraHarvest),
                        Instant(5, "获得 2 分。", RewardOp.Score(2)),
                    }),
            };
        }

        private static RhineDepartmentDef[] BuildDepartments()
        {
            return new[]
            {
                // 防御科
                new RhineDepartmentDef(0, "防御科",
                    new EnterpriseLevel(2, "获得 7 金券。", false,
                        new EventOption("获得 7 金券", new[] { RewardOp.Vouchers(7) })),
                    new EnterpriseLevel(3, "支付 3 金券，放置 1 个影响力，或移动 2 次影响力。", false,
                        new EventOption("支付 3 金券",
                            new[] { RewardOp.PayVouchers(3) },
                            new[]
                            {
                                RewardOp.Alternative(
                                    new EffectBranch("放置 1 个影响力", RewardOp.Influence(InfluencePlacementScope.DeployRules)),
                                    new EffectBranch("移动 2 次影响力", RewardOp.RelocateInfluence(2))),
                            })),
                    new EnterpriseLevel(4, "支付 12 金券，执行 2 次「放置 或 移除 1 个影响力」。", false,
                        new EventOption("支付 12 金券",
                            new[] { RewardOp.PayVouchers(12) },
                            new[]
                            {
                                RewardOp.Alternative(
                                    new EffectBranch("第 1 次：放置", RewardOp.Influence(InfluencePlacementScope.DeployRules)),
                                    new EffectBranch("第 1 次：移除", RewardOp.RemoveInfluence(1))),
                                RewardOp.Alternative(
                                    new EffectBranch("第 2 次：放置", RewardOp.Influence(InfluencePlacementScope.DeployRules)),
                                    new EffectBranch("第 2 次：移除", RewardOp.RemoveInfluence(1))),
                            })),
                    new EnterpriseLevel(5, "支付 25 金券，获得 3 分。", false,
                        new EventOption("支付 25 金券", new[] { RewardOp.PayVouchers(25) }, new[] { RewardOp.Score(3) }))),

                // 工程科
                new RhineDepartmentDef(1, "工程科",
                    new EnterpriseLevel(2, "获得 3 源岩。", false,
                        new EventOption("获得 3 源岩", new[] { RewardOp.Gain(ResourceType.Orirock, 3) })),
                    new EnterpriseLevel(3, "获得 2 异铁。", false,
                        new EventOption("获得 2 异铁", new[] { RewardOp.Gain(ResourceType.Oriron, 2) })),
                    new EnterpriseLevel(4, "支付 3 源岩，进行 1 次建造（不占用主要行动）。", false,
                        new EventOption("支付 3 源岩", new[] { RewardOp.Pay(ResourceType.Orirock, 3) },
                            Array.Empty<RewardOp>()),
                        EnterpriseSpecial.BuildWithoutAction),
                    new EnterpriseLevel(5, "支付 1 至纯源石，从设施供应区免费建造 1 张金券费用不高于 23 的设施。", false,
                        new EventOption("支付 1 至纯源石", new[] { RewardOp.Pay(ResourceType.OriginitePrime, 1) },
                            Array.Empty<RewardOp>()),
                        EnterpriseSpecial.BuildFreeFromSupply, 23)),

                // 结构科
                new RhineDepartmentDef(2, "结构科",
                    new EnterpriseLevel(2, "获得 3 源石。", false,
                        new EventOption("获得 3 源石", new[] { RewardOp.Gain(ResourceType.Originium, 3) })),
                    new EnterpriseLevel(3, "获得 1 分。", false,
                        new EventOption("获得 1 分", new[] { RewardOp.Score(1) })),
                    new EnterpriseLevel(4, "支付 3 源石，获得 1 至纯源石。", false,
                        new EventOption("支付 3 源石", new[] { RewardOp.Pay(ResourceType.Originium, 3) },
                            new[] { RewardOp.Gain(ResourceType.OriginitePrime, 1) })),
                    new EnterpriseLevel(5, "你降低 1 级该企业的合作等级，并获得该等级的奖励。", false, null,
                        EnterpriseSpecial.LowerLevelAndSettle)),
            };
        }
    }
}
