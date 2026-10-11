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

        public const int CityPatterns = 6;                // 城市样式牌
        public const int CityPatternLevel1 = 4;
        public const int CityPatternLevel2 = 2;
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

        /// <summary>城市样式牌：共 6（500–505）。</summary>
        public const int CityPatternBase = 500;
    }

    /// <summary>
    /// 卡牌目录：id → 定义 的唯一查询入口，并提供各类牌堆的 id 列表。
    /// 引擎只看这个接口，因此日后把它换成「从 Data 层 ScriptableObject 读取」的实现即可，
    /// 无需改动规则代码。当前由 <see cref="PlaceholderCardCatalog"/> 提供占位数据。
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

        /// <summary>某一颜色的事件牌堆。</summary>
        IReadOnlyList<int> EventDeckIds(NodeColor backColor);

        /// <summary>场上的城市样式牌（I 级 4 张 + II 级 2 张）。</summary>
        IReadOnlyList<int> CityPatternIds();

        /// <summary>某颜色玩家拿取的核心指挥塔。</summary>
        int CoreCommandTowerId(PlayerColor color);

        /// <summary>延伸枢纽（红 / 黄 / 蓝各 1 张，放在一旁备用）。</summary>
        IReadOnlyList<int> ExtensionHubIds();
    }

    /// <summary>
    /// 占位卡牌目录：只保证 id 分段、数量与颜色正确，牌面属性（费用 / 分数 / 效果）留空。
    /// 卡面切片完成后，用 Data 层的实现替换它，本类即可退役。
    /// </summary>
    public sealed class PlaceholderCardCatalog : ICardCatalog
    {
        /// <summary>默认实例。</summary>
        public static readonly PlaceholderCardCatalog Default = new PlaceholderCardCatalog();

        // 基础版 5 个角色名（同一颜色的 5 张角色相同），规则规格 §14 已确认。
        private static readonly string[] RoleNames = { "雷蛇", "极境", "德克萨斯", "锡人", "坎诺特" };

        private static readonly CharacterCardDef[] Characters = BuildCharacters();
        private static readonly FacilityCardDef[] Facilities = BuildFacilities();
        private static readonly EventCardDef[] Events = BuildEvents();
        private static readonly CityPatternCardDef[] CityPatterns = BuildCityPatterns();

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

        public EventCardDef Event(int id) => Events[id - CardIds.EventBase];

        public CityPatternCardDef CityPattern(int id) => CityPatterns[id - CardIds.CityPatternBase];

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

        public IReadOnlyList<int> EventDeckIds(NodeColor backColor)
        {
            var ids = new List<int>();
            foreach (EventCardDef def in Events)
            {
                if (def.BackColor == backColor) ids.Add(def.Id);
            }
            return ids;
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
                    defs[id - CardIds.CharacterBase] = new CharacterCardDef(id, (PlayerColor)color, RoleNames[role]);
                }
            }
            return defs;
        }

        private static FacilityCardDef[] BuildFacilities()
        {
            var defs = new FacilityCardDef[CardCounts.StandardFacilities + CardCounts.CoreCommandTowers + CardCounts.ExtensionHubs];

            for (int i = 0; i < CardCounts.StandardFacilities; i++)
            {
                int id = CardIds.FacilityBase + i;
                defs[i] = new FacilityCardDef(id, FacilityKind.Standard, $"未命名设施 {i + 1:00}");
            }
            for (int color = 0; color < CardCounts.CoreCommandTowers; color++)
            {
                defs[TowersOffset + color] = new FacilityCardDef(
                    CardIds.CoreCommandTowerBase + color, FacilityKind.CoreCommandTower, $"核心指挥塔（{(PlayerColor)color}）");
            }

            var hubColors = new[] { PlayerColor.Red, PlayerColor.Yellow, PlayerColor.Blue }; // 延伸枢纽没有绿色
            for (int i = 0; i < hubColors.Length; i++)
            {
                defs[HubsOffset + i] = new FacilityCardDef(
                    CardIds.ExtensionHubBase + i, FacilityKind.ExtensionHub, $"延伸枢纽（{hubColors[i]}）");
            }
            return defs;
        }

        private static EventCardDef[] BuildEvents()
        {
            var defs = new EventCardDef[CardCounts.EventCards];
            for (int i = 0; i < defs.Length; i++)
            {
                NodeColor back = i < CardCounts.EventGreen ? NodeColor.Green
                    : i < CardCounts.EventGreen + CardCounts.EventYellow ? NodeColor.Yellow
                    : NodeColor.Red;
                defs[i] = new EventCardDef(CardIds.EventBase + i, back);
            }
            return defs;
        }

        private static CityPatternCardDef[] BuildCityPatterns()
        {
            var defs = new CityPatternCardDef[CardCounts.CityPatterns];
            for (int i = 0; i < defs.Length; i++)
            {
                int level = i < CardCounts.CityPatternLevel1 ? 1 : 2;
                defs[i] = new CityPatternCardDef(CardIds.CityPatternBase + i, level);
            }
            return defs;
        }
    }
}