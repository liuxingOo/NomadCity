using System.Collections.Generic;

namespace NomadCity.Core.Model
{
    /// <summary>
    /// 单个玩家的状态。座位号（SeatIndex）是引擎内的唯一标识，颜色只是它的一个属性。
    /// 手牌与盖放的角色牌属于私有信息，只有所有者可见。
    /// </summary>
    public sealed class PlayerState
    {
        public readonly int SeatIndex;
        public readonly PlayerColor Color;

        /// <summary>持有的资源（源岩／源石／异铁／至纯源石）。</summary>
        public ResourcePool Resources;

        /// <summary>持有的哥伦比亚金券总值（面额 1/5/10，引擎只关心总额）。</summary>
        public int Vouchers;

        /// <summary>分数（地图分数轨道上的位置）。</summary>
        public int Score;

        /// <summary>玩家供应堆里剩余的玩家标记数量（总量 30）。为 0 时不能再放置影响力或宣告样式。</summary>
        public int MarkersInSupply;

        /// <summary>手牌（角色牌 id）。私有，不公开。</summary>
        public readonly List<int> HandCharacterCards = new();

        /// <summary>角色牌弃牌区。</summary>
        public readonly List<int> DiscardCharacterCards = new();

        /// <summary>本回合盖放的角色牌 id；未盖放为 -1。私有。</summary>
        public int PlacedCharacterCard = -1;

        /// <summary>本回合是否已使用过盖放的角色牌（每回合至多一次）。</summary>
        public bool UsedCharacterCardThisTurn;

        /// <summary>已建设在自己城市面板上的设施牌 id。</summary>
        public readonly List<int> BuiltFacilities = new();

        /// <summary>移动城市停靠的资源点 id；不在任何资源点上为 -1。</summary>
        public int MobileCityNodeId = -1;

        /// <summary>本回合已使用过特殊行动的城市样式牌 id（每张样式牌每回合限一次）。</summary>
        public readonly HashSet<int> UsedPatternActionsThisTurn = new();

        public PlayerState(int seatIndex, PlayerColor color)
        {
            SeatIndex = seatIndex;
            Color = color;
        }

        /// <summary>回合收尾时清空「本回合」作用域的标记。</summary>
        public void ResetTurnScopedState()
        {
            UsedCharacterCardThisTurn = false;
            UsedPatternActionsThisTurn.Clear();
        }
    }
}
