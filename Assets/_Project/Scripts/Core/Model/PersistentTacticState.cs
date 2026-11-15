using System.Collections.Generic;

namespace NomadCity.Core.Model
{
    /// <summary>4 张「持续留在场上（∞）」的干员计谋（Doc/EnterpriseExpansion.md §9.9）。</summary>
    public enum PersistentTacticKind
    {
        None = 0,

        /// <summary>泥岩·泥岩小队：收尾阶段一次，支付 2 源岩 → 放置 1 个影响力。永久留场。</summary>
        MudrockSquad = 1,

        /// <summary>可露希尔·工程订购：打出时把至多 2 张异名设施牌压在牌下；建设时可选用；牌下空了进弃牌区。</summary>
        EngineeringOrder = 2,

        /// <summary>多萝西·为了大家：打出时放至多 4 个标记；自己的影响力被移除/替换时可改放在此牌上；
        /// 收尾阶段一次，移除 2 个标记 → 放置 1 个影响力。</summary>
        ForEveryone = 3,

        /// <summary>异客·隐蔽仓库：打出时放至多 5 个标记；每次支付资源时可用至多 2 个标记代替等量资源；
        /// 标记用完后进弃牌区。</summary>
        HiddenWarehouse = 4,
    }

    /// <summary>
    /// 一张持续留在场上的计谋牌。
    /// 牌本身不再属于任何牌堆 / 弃牌区，而是挂在这里；<see cref="CardId"/> 用于查牌面名，
    /// <see cref="Seat"/> 是它的所有者（只影响「自己的」类被动与收尾操作）。
    /// </summary>
    public sealed class PersistentTacticState
    {
        /// <summary>这张持续牌对应的干员角色牌 id（700–707 段）。</summary>
        public int CardId;

        /// <summary>所有者座位。</summary>
        public int Seat;

        /// <summary>持续计谋种类。</summary>
        public PersistentTacticKind Kind;

        /// <summary>工程订购：压在牌下的设施牌 id（打出时从供应区取，建设时可选用）。</summary>
        public readonly List<int> AttachedFacilityIds = new();

        /// <summary>为了大家 / 隐蔽仓库：牌上的玩家标记数。</summary>
        public int Markers;

        /// <summary>本收尾阶段是否已经用过「每收尾阶段一次」的能力。</summary>
        public bool UsedCleanupThisTurn;

        public PersistentTacticState Clone()
        {
            var copy = new PersistentTacticState
            {
                CardId = CardId,
                Seat = Seat,
                Kind = Kind,
                Markers = Markers,
                UsedCleanupThisTurn = UsedCleanupThisTurn,
            };
            copy.AttachedFacilityIds.AddRange(AttachedFacilityIds);
            return copy;
        }

        public override string ToString() =>
            $"持续计谋[{CardId}] {Kind}（座位 {Seat}，标记 {Markers}，牌下设施 {AttachedFacilityIds.Count}）";
    }
}
