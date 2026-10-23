using System.Collections.Generic;

namespace NomadCity.Core.Model
{
    /// <summary>卡牌数量常量 —— 全部来自规则规格 §3 组件清单。</summary>
    public static class CardCounts
    {
        public const int CharacterRoles = 5;              // 每色 5 个角色
        public const int CharacterColors = 4;             // 绿 / 黄 / 红 / 蓝
        public const int CharacterCards = 20;             // 5 × 4

        public const int StandardFacilities = 41;         // 设施牌堆
        public const int CoreCommandTowers = 4;           // 核心指挥塔，每色 1
        public const int ExtensionHubs = 3;               // 延伸枢纽，红 / 黄 / 蓝

        public const int EventCards = 22;                 // 事件牌
        public const int EventGreen = 6;
        public const int EventYellow = 10;
        public const int EventRed = 6;

        public const int CityPatterns = 6;                // 城市样式牌（基础版）
        public const int CityPatternLevel1 = 4;
        public const int CityPatternLevel2 = 2;
        public const int CityPatternEnterprise = 6;       // 企业扩展城市样式牌：I 级 4 + II 级 2
    }

    /// <summary>
    /// 卡牌 id 分段约定：不同类型的牌各有独立 id 段，避免跨类型混淆。
    /// 引擎与联机同步只依赖这些 int，不依赖任何资源路径。
    /// </summary>
    public static class CardIds
    {
        /// <summary>角色牌：+color×5+role，共 20（0–19）。</summary>
        public const int CharacterBase = 0;

        /// <summary>普通设施牌：共 41（100–140）。</summary>
        public const int FacilityBase = 100;

        /// <summary>核心指挥塔：+color，共 4（200–203）。</summary>
        public const int CoreCommandTowerBase = 200;

        /// <summary>延伸枢纽：共 3（300–302）。</summary>
        public const int ExtensionHubBase = 300;

        /// <summary>事件牌：共 22（400–421）。</summary>
        public const int EventBase = 400;

        /// <summary>企业扩展专用事件牌：共 2（450–451），为同色基础牌的替换件。</summary>
        public const int EventEnterpriseBase = 450;
        public const int EventEnterpriseCount = 2;

        /// <summary>城市样式牌：共 6（500–505）。</summary>
        public const int CityPatternBase = 500;

        /// <summary>企业扩展城市样式牌：共 6（550–555），I 级 4 + II 级 2。</summary>
        public const int CityPatternEnterpriseBase = 550;
    }

    /// <summary>
    /// 卡牌目录：id → 定义 的唯一查询入口，并提供各类牌堆的 id 列表。
    /// 引擎只看这个接口，因此日后把它换成「从 Data 层 ScriptableObject 读取」的实现即可，
    /// 无需改动规则代码。当前由 <see cref="PlaceholderCardCatalog"/> 提供全部牌面数据。
    /// </summary>
    public interface ICardCatalog
    {
        CharacterCardDef Character(int id);
        FacilityCardDef Facility(int id);
        EventCardDef Event(int id);
        CityPatternCardDef CityPattern(int id);

        /// <summary>某颜色玩家的初始角色牌（基础版 5 张；扩展后为 7 张）。</summary>
        IReadOnlyList<int> CharacterCardIds(PlayerColor color);

        /// <summary>设施牌堆（41 张普通设施）。</summary>
        IReadOnlyList<int> FacilityDeckIds();

        /// <summary>
        /// 某一颜色的事件牌堆。启用企业扩展（<paramref name="enterpriseEnabled"/>）时，
        /// 用带「E」标记的替换件顶替对应基础牌（见 <c>Doc/EventCards.md</c> §6）。
        /// 不按人数裁剪：2／3 人局剔除标有「4」的牌由 <c>BoardSetup</c> 按
        /// <see cref="EventCardDef.FourPlayerOnly"/> 处理。
        /// </summary>
        IReadOnlyList<int> EventDeckIds(NodeColor backColor, bool enterpriseEnabled);

        /// <summary>场上的城市样式牌（I 级 4 张 + II 级 2 张）。</summary>
        IReadOnlyList<int> CityPatternIds();

        /// <summary>某颜色玩家拿取的核心指挥塔。</summary>
        int CoreCommandTowerId(PlayerColor color);

        /// <summary>延伸枢纽（红 / 黄 / 蓝各 1 张，放在一旁备用）。</summary>
        IReadOnlyList<int> ExtensionHubIds();
    }

    /// <summary>
    /// 内置卡牌目录：早期只是一个占位实现（只保证 id 分段、数量与颜色正确），现已填入全部
    /// 基础版牌面数据（角色 / 设施 / 事件 / 城市样式）——类名沿用早期实现，不再改动。
    /// 卡面切片完成后，可用 Data 层的 ScriptableObject 实现替换它。
    /// </summary>
    public sealed class PlaceholderCardCatalog : ICardCatalog
    {
        /// <summary>默认实例。</summary>
        public static readonly PlaceholderCardCatalog Default = new PlaceholderCardCatalog();

        // 基础版 5 个角色名（同一颜色的 5 张角色相同），规则规格 §14 已确认。
        private static readonly string[] RoleNames = { "雷蛇", "极境", "德克萨斯", "锡人", "坎诺特" };

        // 基础版 5 角色的策略 / 计谋（牌面名 + 效果序列），来源 Doc/CardEffects.md §3。
        // 顺序与 RoleNames 一一对应；同一颜色的 5 张角色共用同一套效果。
        private static readonly string[] RoleStrategyNames =
            { "安保协议", "后勤调配", "特别递送", "建立威信", "秘密渠道" };

        private static readonly string[] RoleTacticsNames =
            { "控制阵地", "导航通讯", "叙拉古人", "人员召集", "征收物资" };

        private static readonly RewardOp[][] RoleStrategyOps = BuildRoleStrategyOps();
        private static readonly RewardOp[][] RoleTacticsOps = BuildRoleTacticsOps();

        private static readonly CharacterCardDef[] Characters = BuildCharacters();
        private static readonly FacilityCardDef[] Facilities = BuildFacilities();
        private static readonly EventCardDef[] Events = BuildEvents();
        private static readonly EventCardDef[] EnterpriseEvents = BuildEnterpriseEvents();
        private static readonly CityPatternCardDef[] CityPatterns = BuildCityPatterns();
        private static readonly CityPatternCardDef[] EnterpriseCityPatterns = BuildEnterpriseCityPatterns();

        public CharacterCardDef Character(int id) => Characters[id - CardIds.CharacterBase];

        public FacilityCardDef Facility(int id)
        {
            if (id >= CardIds.FacilityBase && id < CardIds.CoreCommandTowerBase)
                return Facilities[id - CardIds.FacilityBase];
            if (id >= CardIds.CoreCommandTowerBase && id < CardIds.ExtensionHubBase)
                return Facilities[TowersOffset + (id - CardIds.CoreCommandTowerBase)];
            if (id >= CardIds.ExtensionHubBase && id < CardIds.EventBase)
                return Facilities[HubsOffset + (id - CardIds.ExtensionHubBase)];
            throw new System.ArgumentOutOfRangeException(nameof(id), id, "不是有效的设施牌 id。");
        }

        public EventCardDef Event(int id)
        {
            if (id >= CardIds.EventBase && id < CardIds.EventBase + CardCounts.EventCards)
                return Events[id - CardIds.EventBase];
            if (id >= CardIds.EventEnterpriseBase && id < CardIds.EventEnterpriseBase + CardIds.EventEnterpriseCount)
                return EnterpriseEvents[id - CardIds.EventEnterpriseBase];
            throw new System.ArgumentOutOfRangeException(nameof(id), id, "不是有效的事件牌 id。");
        }

        public CityPatternCardDef CityPattern(int id)
        {
            if (id >= CardIds.CityPatternBase && id < CardIds.CityPatternBase + CardCounts.CityPatterns)
                return CityPatterns[id - CardIds.CityPatternBase];
            if (id >= CardIds.CityPatternEnterpriseBase
                && id < CardIds.CityPatternEnterpriseBase + CardCounts.CityPatternEnterprise)
                return EnterpriseCityPatterns[id - CardIds.CityPatternEnterpriseBase];
            throw new System.ArgumentOutOfRangeException(nameof(id), id, "不是有效的城市样式牌 id。");
        }

        public IReadOnlyList<int> CharacterCardIds(PlayerColor color)
        {
            int first = CardIds.CharacterBase + (int)color * CardCounts.CharacterRoles;
            var ids = new int[CardCounts.CharacterRoles];
            for (int i = 0; i < ids.Length; i++) ids[i] = first + i;
            return ids;
        }

        public IReadOnlyList<int> FacilityDeckIds()
        {
            var ids = new int[CardCounts.StandardFacilities];
            for (int i = 0; i < ids.Length; i++) ids[i] = CardIds.FacilityBase + i;
            return ids;
        }

        public IReadOnlyList<int> EventDeckIds(NodeColor backColor, bool enterpriseEnabled)
        {
            var ids = new List<int>();
            foreach (EventCardDef def in Events)
            {
                if (def.BackColor != backColor) continue;
                if (enterpriseEnabled && IsReplacedByEnterprise(def.Id)) continue; // 被「E」替换件顶替
                ids.Add(def.Id);
            }
            if (enterpriseEnabled)
            {
                foreach (EventCardDef def in EnterpriseEvents)
                {
                    if (def.BackColor == backColor) ids.Add(def.Id);
                }
            }
            return ids;
        }

        /// <summary>基础牌 id 是否被某张企业扩展替换件顶替。</summary>
        private static bool IsReplacedByEnterprise(int id)
        {
            foreach (EventCardDef def in EnterpriseEvents)
            {
                if (def.VariantOfId == id) return true;
            }
            return false;
        }

        public IReadOnlyList<int> CityPatternIds()
        {
            var ids = new int[CityPatterns.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = CityPatterns[i].Id;
            return ids;
        }

        public int CoreCommandTowerId(PlayerColor color) => CardIds.CoreCommandTowerBase + (int)color;

        public IReadOnlyList<int> ExtensionHubIds()
        {
            var ids = new int[CardCounts.ExtensionHubs];
            for (int i = 0; i < ids.Length; i++) ids[i] = CardIds.ExtensionHubBase + i;
            return ids;
        }

        // —— 静态表构建 ——

        private const int TowersOffset = CardCounts.StandardFacilities;
        private const int HubsOffset = TowersOffset + CardCounts.CoreCommandTowers;

        private static CharacterCardDef[] BuildCharacters()
        {
            var defs = new CharacterCardDef[CardCounts.CharacterCards];
            for (int color = 0; color < CardCounts.CharacterColors; color++)
            {
                for (int role = 0; role < CardCounts.CharacterRoles; role++)
                {
                    int id = CardIds.CharacterBase + color * CardCounts.CharacterRoles + role;
                    defs[id - CardIds.CharacterBase] = new CharacterCardDef(
                        id, (PlayerColor)color, RoleNames[role],
                        RoleStrategyNames[role], RoleTacticsNames[role],
                        RoleStrategyOps[role], RoleTacticsOps[role]);
                }
            }
            return defs;
        }

        // —— 角色牌效果数据（来源：Doc/CardEffects.md §3；顺序与 RoleNames 一致）——

        private static RewardOp[][] BuildRoleStrategyOps() => new[]
        {
            // 雷蛇·策略「安保协议」：放置 2 次影响力；本回合收尾阶段移除 1 个你的影响力（延迟结算）。
            new[]
            {
                RewardOp.Influence(InfluencePlacementScope.DeployRules, 2),
                RewardOp.Deferred(RewardOp.RemoveOwnInfluence(1)),
            },

            // 极境·策略「后勤调配」：从源岩／源石／异铁中选你拥有数量最少的一种，获得 4 个。
            new[] { RewardOp.ChooseLeastHeldResource(4, ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron) },

            // 德克萨斯·策略「特别递送」：获得 12 金券；选 1 张设施供应区的设施牌放回设施牌堆底部。
            new[]
            {
                RewardOp.Vouchers(12),
                RewardOp.SupplyCardToDeckBottom(),
            },

            // 锡人·策略「建立威信」：获得 1 分；然后可选支付 12 金券得 1 至纯源石，再可选支付 15 金券得 1 至纯源石。
            new[]
            {
                RewardOp.Score(1),
                RewardOp.OptionalChain(
                    new EffectBranch("支付 12 金券，获得 1 个至纯源石",
                        RewardOp.PayVouchers(12), RewardOp.Gain(ResourceType.OriginitePrime, 1)),
                    new EffectBranch("支付 15 金券，获得 1 个至纯源石",
                        RewardOp.PayVouchers(15), RewardOp.Gain(ResourceType.OriginitePrime, 1))),
            },

            // 坎诺特·策略「秘密渠道」：把任意资源按单价出售（源岩 3 / 源石 3 / 异铁 4 / 至纯源石 15）。
            new[] { RewardOp.SellResources() },
        };

        private static RewardOp[][] BuildRoleTacticsOps() => new[]
        {
            // 雷蛇·计谋「控制阵地」：支付 3 金券 → 替换 1 个影响力（同格移除后放置，放不下则只移除）。
            new[]
            {
                RewardOp.PayVouchers(3),
                RewardOp.ReplaceInfluence(1),
            },

            // 极境·计谋「导航通讯」：支付 3 至纯源石 → 以「相邻且有自己的影响力」的资源点为目标免费城市移动。
            new[]
            {
                RewardOp.Pay(ResourceType.OriginitePrime, 3),
                RewardOp.FreeCityMoveOwnInfluenced(1),
            },

            // 德克萨斯·计谋「叙拉古人」：支付 3 金券 → 移动 3 个自己的影响力。
            new[]
            {
                RewardOp.PayVouchers(3),
                RewardOp.RelocateInfluence(3),
            },

            // 锡人·计谋「人员召集」：把弃牌区的牌全部收回手牌；每收回 1 张在「获得 5 金券 / 移动 1 个影响力」中二选一。
            new[]
            {
                RewardOp.RecallDiscardPerCard(
                    new EffectBranch("获得 5 金券", RewardOp.Vouchers(5)),
                    new EffectBranch("移动 1 个自己的影响力", RewardOp.RelocateInfluence(1))),
            },

            // 坎诺特·计谋「征收物资」：出牌者选 1 种基础资源；所有玩家按 2 金券 / 个卖光该资源（各自收取），出牌者 +1 分。
            new[]
            {
                RewardOp.LevyResourceSale(2),
                RewardOp.Score(1),
            },
        };

        // —— 设施牌数据（来源：Doc/FacilityCards.md）——
        // 普通设施牌按素材拼版图从左到右、从上到下编号（素材槽位 22／33／44 是延伸枢纽，跳过）。

        private static FacilityCardDef[] BuildFacilities()
        {
            var defs = new FacilityCardDef[CardCounts.StandardFacilities + CardCounts.CoreCommandTowers + CardCounts.ExtensionHubs];

            BuildStandardFacilities(defs);

            // 核心指挥塔牌面为**彩色**描边（可视为任意颜色，§6.5b），id 仍按玩家颜色分段（200–203）。
            for (int color = 0; color < CardCounts.CoreCommandTowers; color++)
            {
                defs[TowersOffset + color] = new FacilityCardDef(
                    CardIds.CoreCommandTowerBase + color, FacilityKind.CoreCommandTower, "核心指挥塔",
                    FacilityColor.Rainbow, 0, null, 0, FacilityKeyword.Unique);
            }

            // 延伸枢纽没有绿色；顺序沿用既有 {红, 黄, 蓝}
            var hubColors = new[] { FacilityColor.Red, FacilityColor.Yellow, FacilityColor.Blue };
            for (int i = 0; i < hubColors.Length; i++)
            {
                defs[HubsOffset + i] = new FacilityCardDef(
                    CardIds.ExtensionHubBase + i, FacilityKind.ExtensionHub, "延伸枢纽", hubColors[i], -1);
            }
            return defs;
        }

        /// <summary>
        /// 41 张普通设施牌，按 id 顺序（100–140）写入 <paramref name="defs"/> 的前 41 个槽位。
        /// 同名多张的牌逐张登记，数值完全相同。
        /// 费用 / 分数 / 关键词与入场效果（含以单价售卖资源、结算他牌入场效果、传递起始标记、
        /// 图标型效果等专用节点）均已录入，详见 FacilityCards.md。
        /// </summary>
        private static void BuildStandardFacilities(FacilityCardDef[] defs)
        {
            int i = 0;

            // —— 彩色（11 张，100–110）——
            i = AddStandard(defs, i, "城邦行政区", FacilityColor.Rainbow, 3, 3, 30, FacilityKeyword.Unique,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 3),
                    RewardOp.Pay(ResourceType.Originium, 3),
                    RewardOp.Pay(ResourceType.Oriron, 3),
                });
            i = AddStandard(defs, i, "附属能源设施", FacilityColor.Rainbow, 1, 3, 23, FacilityKeyword.Unique,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 2),
                    RewardOp.Pay(ResourceType.Originium, 2),
                    RewardOp.Pay(ResourceType.Oriron, 3),
                },
                // 入场效果：选一个十字相邻的非彩色设施，结算它的入场效果。
                new[] { RewardOp.SettleAnotherEntryEffect() });
            i = AddStandard(defs, i, "联邦理事会", FacilityColor.Rainbow, 0, 3, 17, FacilityKeyword.Unique,
                new[] { RewardOp.Pay(ResourceType.Orirock, 4), RewardOp.Pay(ResourceType.Originium, 2) },
                // 入场效果：本回合收尾阶段「传递起始标记」时，改由本玩家获得。
                new[] { RewardOp.PassStartMarker() });
            i = AddStandard(defs, i, "简陋工程营", FacilityColor.Rainbow, -1, 2, 8, FacilityKeyword.Unique,
                new[] { RewardOp.Pay(ResourceType.Orirock, 2), RewardOp.Pay(ResourceType.Originium, 1) },
                // 入场效果：进行一次建造（照牌面费用付费）。
                new[] { RewardOp.BuildOnce() });

            // —— 蓝色（10 张，111–120）——
            // 资源费用 = 至纯源石1 + 金券2（费用列可混入金券 / 至纯源石，见 Doc/FacilityCards.md §1）
            i = AddStandard(defs, i, "物流枢纽", FacilityColor.Blue, 2, 2, 24, FacilityKeyword.Unique,
                new[] { RewardOp.Pay(ResourceType.OriginitePrime, 1), RewardOp.PayVouchers(2) },
                // 入场效果：从余下的延伸枢纽中选 1 张并建设。
                new[] { RewardOp.BuildExtensionHub() });
            i = AddStandard(defs, i, "源石精炼厂", FacilityColor.Blue, 0, 1, 10, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 1),
                    RewardOp.Pay(ResourceType.Originium, 1),
                    RewardOp.Pay(ResourceType.Oriron, 1),
                },
                new[] { RewardOp.Gain(ResourceType.Originium, 6) });
            i = AddStandard(defs, i, "城市化区域", FacilityColor.Blue, 1, 3, 16, FacilityKeyword.None,
                new[] { RewardOp.Pay(ResourceType.Orirock, 3), RewardOp.Pay(ResourceType.Oriron, 2) },
                // 入场效果：每有一个与核心指挥塔十字相邻的设施，获得 4 金券。
                new[] { RewardOp.VouchersPerCount(CountSource.AdjacentCoreFacilities, 4) });
            i = AddStandard(defs, i, "异铁冶炼厂", FacilityColor.Blue, 1, 1, 15, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 2),
                    RewardOp.Pay(ResourceType.Originium, 2),
                    RewardOp.Pay(ResourceType.Oriron, 1),
                },
                new[] { RewardOp.Gain(ResourceType.Oriron, 4) });
            i = AddStandard(defs, i, "贸易街区", FacilityColor.Blue, 0, 3, 6, FacilityKeyword.None,
                new[] { RewardOp.Pay(ResourceType.Orirock, 1), RewardOp.Pay(ResourceType.Oriron, 1) },
                // 入场效果：可以按单价把任意资源换成金券（源岩3／源石3／异铁4／至纯源石15）。
                new[] { RewardOp.SellResources() });

            // —— 黄色（10 张，121–130）——
            i = AddStandard(defs, i, "城邦工业区", FacilityColor.Yellow, 2, 3, 24, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 4),
                    RewardOp.Pay(ResourceType.Originium, 3),
                    RewardOp.PayVouchers(2),
                },
                // 建筑费用修正（非入场效果）：自己面板上每有 1 种非彩色设施，少付 1 源岩。
                null,
                new FacilityCostModifier(CountSource.NonColorFacilityKinds, ResourceType.Orirock, 1));
            i = AddStandard(defs, i, "高性能动力设施", FacilityColor.Yellow, 0, 2, 20, FacilityKeyword.None,
                new[] { RewardOp.Pay(ResourceType.Originium, 4), RewardOp.Pay(ResourceType.Oriron, 2) },
                // 牌面关键词栏的「免费」紧邻城市移动图标，只表示「进行一次免费的城市移动」（免付 3 源石）；
                // 建设本身照常按上面的两组费用二选一支付（已与规则持有者确认）。
                // 入场效果：免费城市移动（§6.4d）；另按牌面另一行，在所经航道**额外**放置 1 个影响力
                //（出发点的放置由城市移动本身完成，故这次合计放 2 个）。
                new[] { RewardOp.FreeCityMove(), RewardOp.PlaceInfluenceOnCityMoveRoute() });
            i = AddStandard(defs, i, "固源岩提纯厂", FacilityColor.Yellow, 0, 1, 10, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 1),
                    RewardOp.Pay(ResourceType.Originium, 1),
                    RewardOp.Pay(ResourceType.Oriron, 1),
                },
                new[] { RewardOp.Gain(ResourceType.Orirock, 7) });
            i = AddStandard(defs, i, "源石精炼厂", FacilityColor.Yellow, 0, 1, 10, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 1),
                    RewardOp.Pay(ResourceType.Originium, 1),
                    RewardOp.Pay(ResourceType.Oriron, 1),
                },
                new[] { RewardOp.Gain(ResourceType.Originium, 6) });
            i = AddStandard(defs, i, "开采电铲", FacilityColor.Yellow, 0, 3, 10, FacilityKeyword.None,
                new[] { RewardOp.Pay(ResourceType.Originium, 2), RewardOp.Pay(ResourceType.Oriron, 1) },
                // 入场效果：合计获得 5 个资源，由玩家在源岩／源石／异铁三者之间任意分配。
                new[] { RewardOp.GainSpread(5, ResourceType.Orirock, ResourceType.Originium, ResourceType.Oriron) });

            // —— 红色（10 张，131–140）——
            i = AddStandard(defs, i, "异铁冶炼厂", FacilityColor.Red, 1, 1, 15, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 2),
                    RewardOp.Pay(ResourceType.Originium, 2),
                    RewardOp.Pay(ResourceType.Oriron, 1),
                },
                new[] { RewardOp.Gain(ResourceType.Oriron, 4) });
            i = AddStandard(defs, i, "固源岩提纯厂", FacilityColor.Red, 0, 1, 10, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 1),
                    RewardOp.Pay(ResourceType.Originium, 1),
                    RewardOp.Pay(ResourceType.Oriron, 1),
                },
                new[] { RewardOp.Gain(ResourceType.Orirock, 7) });
            i = AddStandard(defs, i, "佣兵指挥部", FacilityColor.Red, 1, 3, 18, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 2),
                    RewardOp.Pay(ResourceType.Oriron, 2),
                    RewardOp.PayVouchers(4),
                },
                // 入场效果为「或」二选一：替换 1 个影响力，或放置 1 个影响力。
                new[]
                {
                    RewardOp.Alternative(
                        new EffectBranch("替换 1 个影响力", RewardOp.ReplaceInfluence()),
                        new EffectBranch("放置 1 个影响力",
                            RewardOp.Influence(InfluencePlacementScope.DeployRules, 1))),
                });
            i = AddStandard(defs, i, "护航调度中心", FacilityColor.Red, 0, 2, 18, FacilityKeyword.None,
                new[]
                {
                    RewardOp.Pay(ResourceType.Orirock, 2),
                    RewardOp.Pay(ResourceType.Originium, 2),
                    RewardOp.Pay(ResourceType.Oriron, 2),
                },
                new[] { RewardOp.Influence(InfluencePlacementScope.DeployRules, 2) });
            i = AddStandard(defs, i, "载具仓库", FacilityColor.Red, 0, 3, 9, FacilityKeyword.None,
                new[] { RewardOp.Pay(ResourceType.Orirock, 1), RewardOp.Pay(ResourceType.Originium, 2) },
                // 入场效果为「或」二选一：拿走 1 个别人的影响力并移动 1 个自己的影响力，或执行 1 次探索。
                new[]
                {
                    RewardOp.Alternative(
                        new EffectBranch("拿走 1 个影响力并移动自己的 1 个影响力",
                            RewardOp.RemoveInfluence(), RewardOp.RelocateInfluence()),
                        new EffectBranch("执行 1 次探索", RewardOp.ExploreOnce())),
                });
        }

        /// <summary>登记 <paramref name="copies"/> 张同名牌（连号 id），返回下一个起始下标。</summary>
        private static int AddStandard(FacilityCardDef[] defs, int start, string name, FacilityColor color, int score,
            int copies, int voucherCost = 0, FacilityKeyword keywords = FacilityKeyword.None,
            IReadOnlyList<RewardOp> resourceCost = null, IReadOnlyList<RewardOp> entryEffects = null,
            FacilityCostModifier costModifier = null)
        {
            for (int i = 0; i < copies; i++)
            {
                defs[start + i] = new FacilityCardDef(CardIds.FacilityBase + start + i, FacilityKind.Standard, name,
                    color, score, resourceCost, voucherCost, keywords, entryEffects, costModifier);
            }
            return start + copies;
        }

        // —— 事件牌数据（来源：Doc/EventCards.md；id 按素材拼版图从左到右、从上到下编号）——

        private static readonly RewardOp Orirock1 = RewardOp.Gain(ResourceType.Orirock, 1);
        private static readonly RewardOp Originium1 = RewardOp.Gain(ResourceType.Originium, 1);
        private static readonly RewardOp Oriron1 = RewardOp.Gain(ResourceType.Oriron, 1);
        private static readonly RewardOp OriginitePrime1 = RewardOp.Gain(ResourceType.OriginitePrime, 1);
        private static readonly RewardOp Score1 = RewardOp.Score(1);

        private static EventCardDef[] BuildEvents()
        {
            return new[]
            {
                // —— 绿色（6 张，400–405）——
                Event(400, "中立采石场", NodeColor.Green, ResourceType.Orirock, 1,
                    Opt("我们只是过来兑换一些燃料，并谈谈生意", RewardOp.Gain(ResourceType.Originium, 3)),
                    Opt("今天起这里不再由你说了算了", RewardOp.Gain(ResourceType.Oriron, 2), RewardOp.Vouchers(2))),

                Event(401, "清理虫巢", NodeColor.Green, ResourceType.Oriron, 1,
                    Opt("向当地治安官邀功", RewardOp.Vouchers(7)),
                    Opt("附近聚落居民向你表达感谢", RewardOp.Gain(ResourceType.Originium, 3))),

                Event(402, "外露矿床", NodeColor.Green, ResourceType.Originium, 1,
                    Opt("清理出一片场地来建设矿区", RewardOp.Gain(ResourceType.Orirock, 4)),
                    Opt("把成堆的粗劣源石带回城", RewardOp.Gain(ResourceType.Originium, 3))),

                Event(403, "富饶岩层", NodeColor.Green, ResourceType.Orirock, 1,
                    Opt("现在就装一车源岩回城", RewardOp.Gain(ResourceType.Orirock, 4)),
                    Opt("找卖你地图的家伙弥补损失", RewardOp.Gain(ResourceType.Originium, 3))),

                Event(404, "矿业聚落", NodeColor.Green, ResourceType.Originium, 1,
                    Opt("用约翰老妈提供的抑制剂换取开采合同", RewardOp.Gain(ResourceType.Originium, 3)),
                    Opt("收取费用，并为他们提供一片相对安全的聚居区", RewardOp.Vouchers(7))),

                Event(405, "废弃矿坑", NodeColor.Green, ResourceType.Originium, 1, fourPlayerOnly: true,
                    Opt("先把矿井外留下的资源设备收走", RewardOp.Gain(ResourceType.Oriron, 2), RewardOp.Vouchers(2)),
                    Opt("看看你们干的好事！天黑前要是不把这挖通我就把你们活埋在源岩里！", RewardOp.Gain(ResourceType.Orirock, 4))),

                // —— 黄色（10 张，406–415）——
                Event(406, "大型源岩场", NodeColor.Yellow, ResourceType.Orirock, 1,
                    Opt("独享这片矿区，趁其他人还未发现这里", RewardOp.Gain(ResourceType.Orirock, 5)),
                    Opt("以部分开采权为代价，与聚合剂厂商合作", RewardOp.Vouchers(4), Score1)),

                Event(407, "洞穴遗迹", NodeColor.Yellow, ResourceType.Orirock, 1,
                    Opt("爆破！我们不是来考古的", RewardOp.Gain(ResourceType.Orirock, 5)),
                    Opt("给「梅兰德历史协会」写一封信", RewardOp.Vouchers(10))),

                Event(408, "荒地人村落", NodeColor.Yellow, ResourceType.Orirock, 1,
                    Opt("换取荒地人的服务", RewardOp.Gain(ResourceType.Orirock, 3), Oriron1),
                    Opt("你找到的源石矿储量不多，不过多少有点收获", RewardOp.Gain(ResourceType.Originium, 4))),

                Event(409, "锈锤领地", NodeColor.Yellow, ResourceType.Originium, 1,
                    Opt("名为岩蹄安保的组织愿意保护矿区，但价格低廉得可疑……",
                        new[] { RewardOp.PayVouchers(4) },
                        new[] { RewardOp.Influence(InfluencePlacementScope.DeployRules) }),
                    Opt("真是中了大奖！向治安官禀报此事", RewardOp.Vouchers(10)),
                    Opt("别招惹这些硬茬，埋头做我们的事", RewardOp.Gain(ResourceType.Originium, 4))),

                Event(410, "情报交换", NodeColor.Yellow, ResourceType.Originium, 1, fourPlayerOnly: true,
                    Opt("杰弗里为你提供了一些当地的人脉", RewardOp.Influence(InfluencePlacementScope.ThisNodeOrAdjacentRoutes)),
                    Opt("源石矿脉的位置可比美酒和伙计重要", Orirock1, RewardOp.Gain(ResourceType.Originium, 3)),
                    Opt("「杰弗里，这儿归我了，把酒馆开到城里去吧！」", RewardOp.Gain(ResourceType.Oriron, 3))),

                Event(411, "风险任务", NodeColor.Yellow, ResourceType.Originium, 1,
                    Opt("在外围挖掘地址样本，谨慎点，我不需要死掉的手下！", RewardOp.Gain(ResourceType.Oriron, 3)),
                    Opt("叫你的手下开始搭建矿井，他们之后会有很长的假期", RewardOp.Gain(ResourceType.Originium, 4))),

                Event(412, "异铁开采权", NodeColor.Yellow, ResourceType.Oriron, 1,
                    Opt("「我可没有义务分享成果」你打发走了治安官", Oriron1, RewardOp.Gain(ResourceType.Orirock, 3)),
                    Opt("「联邦万岁！」你向治安官伸出了手", RewardOp.Vouchers(13), RewardOp.OpponentsGain(ResourceType.Oriron, 1))),

                Event(413, "敌对生态圈", NodeColor.Yellow, ResourceType.Oriron, 1, fourPlayerOnly: true,
                    Opt("派一队佣兵和磐蟹「谈判」", RewardOp.Gain(ResourceType.Oriron, 3)),
                    Opt("雇佣萨尔贡术士役使这些大块头", RewardOp.Gain(ResourceType.Orirock, 2), Score1)),

                Event(414, "富异铁区", NodeColor.Yellow, ResourceType.Oriron, 1,
                    Opt("你正好有一批军方订单，先去找找矿脉的位置", RewardOp.Gain(ResourceType.Oriron, 3)),
                    Opt("向荒地人村落索要赔偿金", RewardOp.Gain(ResourceType.Originium, 2), RewardOp.Vouchers(5))),

                Event(415, "遗弃矿场", NodeColor.Yellow, ResourceType.Originium, 1,
                    Opt("拆除这里的设备，带走有用的材料", RewardOp.Gain(ResourceType.Oriron, 3)),
                    Opt("恢复矿场的运行", RewardOp.Gain(ResourceType.Originium, 4))),

                // —— 红色（6 张，416–421）——
                Event(416, "险中净土", NodeColor.Red, ResourceType.Orirock, 2,
                    Opt("在更多人遇害前开始清理源石", RewardOp.Gain(ResourceType.Originium, 6)),
                    Opt("开采源岩，那些学者自会有人去救", RewardOp.Gain(ResourceType.Orirock, 7)),
                    Opt("你的人搭救了遇险的考察队，他们愿意还这份人情",
                        RewardOp.Vouchers(6), RewardOp.Influence(InfluencePlacementScope.ThisNodeOrAdjacentRoutes))),

                Event(417, "裂谷矿脉", NodeColor.Red, ResourceType.OriginitePrime, 1,
                    Opt("高纯度源石矿的发现将为你的城邦拉开新的篇章", Oriron1, OriginitePrime1),
                    Opt("收集裂隙层的源石碎片", RewardOp.Gain(ResourceType.Orirock, 3), RewardOp.Gain(ResourceType.Originium, 3))),

                Event(418, "采集平台残骸", NodeColor.Red, ResourceType.Oriron, 1,
                    Opt("他们没救了，继续回收作业", RewardOp.Gain(ResourceType.Oriron, 4), RewardOp.Vouchers(5)),
                    Opt("用设备刨开掩埋残骸的障碍，也许有更多的幸存者", RewardOp.Gain(ResourceType.Orirock, 4), RewardOp.Gain(ResourceType.Originium, 3))),

                Event(419, "高污染环境", NodeColor.Red, ResourceType.Orirock, 2,
                    Opt("恳求赞助商协助建设矿区", RewardOp.Gain(ResourceType.Orirock, 2), RewardOp.Gain(ResourceType.Originium, 2), RewardOp.Gain(ResourceType.Oriron, 2)),
                    Opt("把报告给治安官看看，希望他也会中招", RewardOp.Vouchers(18)),
                    Opt("工人冒着风险为你提炼出了高纯度源石矿", OriginitePrime1, RewardOp.Vouchers(4))),

                Event(420, "深层矿床", NodeColor.Red, ResourceType.OriginitePrime, 1, fourPlayerOnly: true,
                    Opt("先试着采集一些深层矿物样本", OriginitePrime1, Orirock1),
                    Opt("让矿业机械逐步挖开坚硬岩层", RewardOp.Gain(ResourceType.Originium, 3), RewardOp.Gain(ResourceType.Oriron, 2)),
                    Opt("向联邦提出拨款请求以协助建设", RewardOp.Vouchers(18), RewardOp.OpponentsGainVouchers(3))),

                Event(421, "地质瑰宝", NodeColor.Red, ResourceType.OriginitePrime, 1,
                    Opt("何必多想，这里的宝贝足够帮你解决眼前更棘手的问题", Originium1, OriginitePrime1),
                    Opt("帮助地质学家清除矿脉上的杂质，弄不好会有其他有价值的发现？", RewardOp.Gain(ResourceType.Orirock, 3), RewardOp.Gain(ResourceType.Oriron, 3))),
            };
        }

        /// <summary>企业扩展专用事件牌（带「E」标记）：替换同色的对应基础牌，本身不改变牌堆总数。</summary>
        private static EventCardDef[] BuildEnterpriseEvents()
        {
            return new[]
            {
                Event(CardIds.EventEnterpriseBase, "洞穴遗迹ᴱ", NodeColor.Yellow, ResourceType.Orirock, 1,
                    isEnterpriseCard: true, variantOfId: 407,
                    Opt("爆破！我们不是来考古的", RewardOp.Gain(ResourceType.Orirock, 5)),
                    Opt("给「梅兰德历史协会」写一封信", RewardOp.Vouchers(10)),
                    Opt("我的赞助商里会有人感兴趣的",
                        new[] { RewardOp.Pay(ResourceType.Oriron, 1) },
                        new[] { RewardOp.Enterprise() })),

                Event(CardIds.EventEnterpriseBase + 1, "采集平台残骸ᴱ", NodeColor.Red, ResourceType.Oriron, 1,
                    isEnterpriseCard: true, variantOfId: 418,
                    Opt("即使拖慢工期也应先将幸存者妥善安置，幸运的是其中竟有一位企业代表",
                        new[] { RewardOp.LoseScore(1) },
                        new[] { RewardOp.Enterprise() }),
                    Opt("用设备刨开掩埋残骸的障碍，也许有更多的幸存者", RewardOp.Gain(ResourceType.Orirock, 4), RewardOp.Gain(ResourceType.Originium, 3)),
                    Opt("他们没救了，继续回收作业", RewardOp.Gain(ResourceType.Oriron, 4), RewardOp.Vouchers(5))),
            };
        }

        // —— 事件牌数据录入辅助 ——

        private static EventCardDef Event(int id, string name, NodeColor backColor, ResourceType tokenResource, int tokenAmount,
            params EventOption[] options) =>
            new(id, name, backColor, tokenResource, tokenAmount, false, false, -1, options);

        private static EventCardDef Event(int id, string name, NodeColor backColor, ResourceType tokenResource, int tokenAmount,
            bool fourPlayerOnly, params EventOption[] options) =>
            new(id, name, backColor, tokenResource, tokenAmount, fourPlayerOnly, false, -1, options);

        private static EventCardDef Event(int id, string name, NodeColor backColor, ResourceType tokenResource, int tokenAmount,
            bool isEnterpriseCard, int variantOfId, params EventOption[] options) =>
            new(id, name, backColor, tokenResource, tokenAmount, false, isEnterpriseCard, variantOfId, options);

        private static EventOption Opt(string text, params RewardOp[] rewards) => new(text, rewards);

        private static EventOption Opt(string text, RewardOp[] cost, RewardOp[] rewards) =>
            new(text, cost, rewards);

        // —— 城市样式牌数据（来源：Doc/RuleSpec.md §6.5b / §10.2 与牌面切片 card_cp_lv1.jpg / card_cp_lv2.jpg）——
        // 坐标是**相对**格阵，匹配时可整体平移 + 按 90° 倍数旋转（不可翻转，§10.2）；
        // 斜分格记成多个颜色（其一即可）；彩色设施任何时候都能当作任意颜色（§10.2），由 PatternCell.Accepts 统一处理。
        // 牌面上「解锁特殊行动」的费用与效果均已录入（§6.3f / §10.2-4，来源 card_cp_lv1.jpg / card_cp_lv2.jpg）。

        private static CityPatternCardDef[] BuildCityPatterns()
        {
            return new[]
            {
                // —— I 级（4 张，500–503）——
                // 军工业化区域：放置 X 个影响力（X = 该样式牌上自己的标记数，至多 3 个）。
                Pattern(500, 1, "军工业化区域", 2,
                    null, new[] { RewardOp.InfluencePerPatternMarkers(3) },
                    new PatternCell(0, 0, FacilityColor.Blue, FacilityColor.Yellow), // 斜分：蓝 / 黄
                    new PatternCell(0, 1, FacilityColor.Red)),

                // 动员配套体系：替换 1 个影响力（移除一个后在同一空格放置一个自己的）。
                Pattern(501, 1, "动员配套体系", 3,
                    null, new[] { RewardOp.ReplaceInfluence(1) },
                    new PatternCell(0, 0, FacilityColor.Yellow),
                    new PatternCell(0, 1, FacilityColor.Yellow),
                    new PatternCell(0, 2, FacilityColor.Red)),

                // 复合动力系统：支付 1 源石 + 合计 3 个（源岩／异铁任意分配）→ 1 次免费城市移动 + 在经过的航道上放置 1 影响力。
                Pattern(502, 1, "复合动力系统", 3,
                    new[]
                    {
                        RewardOp.Pay(ResourceType.Originium, 1),
                        RewardOp.PaySpread(3, ResourceType.Orirock, ResourceType.Oriron),
                    },
                    new[] { RewardOp.FreeCityMove(1), RewardOp.PlaceInfluenceOnCityMoveRoute(1) },
                    new PatternCell(0, 0, FacilityColor.Yellow),
                    new PatternCell(1, 0, FacilityColor.Red),
                    new PatternCell(1, 1, FacilityColor.Yellow)),

                // 物资中继站：获得 源岩×1、源石×1、异铁×1。
                Pattern(503, 1, "物资中继站", 2,
                    null, new[]
                    {
                        RewardOp.Gain(ResourceType.Orirock, 1),
                        RewardOp.Gain(ResourceType.Originium, 1),
                        RewardOp.Gain(ResourceType.Oriron, 1),
                    },
                    new PatternCell(0, 0, FacilityColor.Blue, FacilityColor.Red), // 斜分：蓝 / 红
                    new PatternCell(0, 1, FacilityColor.Yellow)),

                // —— II 级（2 张，504–505）——
                // 源石工业中枢：支付 6 金券 → 本轮额外 2 次主要行动；本轮不能用角色牌。
                Pattern(504, 2, "源石工业中枢", 6,
                    new[] { RewardOp.PayVouchers(6) },
                    new[] { RewardOp.ExtraMainActions(2), RewardOp.ForbidCharacterCardsThisRound() },
                    new PatternCell(0, 0, FacilityColor.Blue),
                    new PatternCell(1, 0, FacilityColor.Red),
                    new PatternCell(1, 1, FacilityColor.Blue),
                    new PatternCell(2, 0, FacilityColor.Yellow),
                    new PatternCell(2, 1, FacilityColor.Yellow),
                    new PatternCell(2, 2, FacilityColor.Red)),

                // 高效移动管理体系：支付 3 源石 → 连续 2 次免费城市移动；本轮不能用角色牌。
                Pattern(505, 2, "高效移动管理体系", 7,
                    new[] { RewardOp.Pay(ResourceType.Originium, 3) },
                    new[] { RewardOp.ForbidCharacterCardsThisRound(), RewardOp.FreeCityMove(2) },
                    new PatternCell(0, 0, FacilityColor.Yellow),
                    new PatternCell(1, 0, FacilityColor.Red),
                    new PatternCell(1, 1, FacilityColor.Yellow),
                    new PatternCell(2, 0, FacilityColor.Blue),
                    new PatternCell(2, 1, FacilityColor.Blue),
                    new PatternCell(2, 2, FacilityColor.Red)),
            };
        }

        /// <summary>企业扩展城市样式牌（带「E」标记）：I 级 4 张 + II 级 2 张（Doc/EnterpriseExpansion.md §2 / §3.1）。</summary>
        private static CityPatternCardDef[] BuildEnterpriseCityPatterns()
        {
            return new[]
            {
                // —— I 级（4 张，550–553）——
                Pattern(550, 1, "城建开拓区ᴱ", 5,
                    new PatternCell(0, 1, FacilityColor.Yellow),
                    new PatternCell(1, 0, FacilityColor.Blue),
                    new PatternCell(1, 1, FacilityColor.Blue),
                    new PatternCell(1, 2, FacilityColor.Red)),

                Pattern(551, 1, "拓荒者据点ᴱ", 2,
                    new PatternCell(0, 1, FacilityColor.Yellow),
                    new PatternCell(1, 0, FacilityColor.Blue)),

                Pattern(552, 1, "核心商业区ᴱ", 3,
                    new PatternCell(0, 0, FacilityColor.Blue),
                    new PatternCell(0, 1, FacilityColor.Blue),
                    new PatternCell(0, 2, FacilityColor.Red)),

                Pattern(553, 1, "大型企业园ᴱ", 3,
                    new PatternCell(0, 0, FacilityColor.Blue),
                    new PatternCell(1, 0, FacilityColor.Red),
                    new PatternCell(1, 1, FacilityColor.Blue)),

                // —— II 级（2 张，554–555，与基础版 504 / 505 同阵）——
                Pattern(554, 2, "警备队辖区ᴱ", 6,
                    new PatternCell(0, 0, FacilityColor.Blue),
                    new PatternCell(1, 0, FacilityColor.Red),
                    new PatternCell(1, 1, FacilityColor.Blue),
                    new PatternCell(2, 0, FacilityColor.Yellow),
                    new PatternCell(2, 1, FacilityColor.Yellow),
                    new PatternCell(2, 2, FacilityColor.Red)),

                Pattern(555, 2, "拓荒指挥中枢ᴱ", 7,
                    new PatternCell(0, 0, FacilityColor.Yellow),
                    new PatternCell(1, 0, FacilityColor.Red),
                    new PatternCell(1, 1, FacilityColor.Yellow),
                    new PatternCell(2, 0, FacilityColor.Blue),
                    new PatternCell(2, 1, FacilityColor.Blue),
                    new PatternCell(2, 2, FacilityColor.Red)),
            };
        }

        private static CityPatternCardDef Pattern(int id, int level, string name, int score,
            params PatternCell[] cells) =>
            new CityPatternCardDef(id, level, name, score, new PatternCondition(cells));

        /// <summary>带「解锁特殊行动」费用与效果的城市样式牌（基础版 6 张，§6.3f / §10.2-4）。</summary>
        private static CityPatternCardDef Pattern(int id, int level, string name, int score,
            RewardOp[] specialActionCost, RewardOp[] specialActionEffects, params PatternCell[] cells) =>
            new CityPatternCardDef(id, level, name, score, new PatternCondition(cells),
                specialActionCost, specialActionEffects);
    }
}