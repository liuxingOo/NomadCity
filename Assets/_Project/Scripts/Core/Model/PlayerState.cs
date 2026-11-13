using System;
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

        /// <summary>
        /// 开局入场时「从 7 张手牌里选 2 张放回游戏盒」是否已完成（EnterpriseExpansion.md §4.2）。
        /// 未启用企业扩展时恒为 true（保持旧行为）；完成后才能盖放角色牌。
        /// </summary>
        public bool SetupDiscardDone = true;

        /// <summary>本回合盖放的角色牌 id；未盖放为 -1。私有。</summary>
        public int PlacedCharacterCard = -1;

        /// <summary>本回合是否已使用过盖放的角色牌（每回合至多一次）。</summary>
        public bool UsedCharacterCardThisTurn;

        /// <summary>本回合是否已用掉盖放角色牌的「策略」（起始玩家可再用「计谋」，§10.1.2）。</summary>
        public bool UsedCharacterStrategyThisTurn;

        /// <summary>本回合是否已用掉盖放角色牌的「计谋」。</summary>
        public bool UsedCharacterTacticsThisTurn;

        /// <summary>
        /// 起始玩家用掉两种效果之一后置位：此时必须**立即**用掉另一种效果或结束使用，
        /// 期间不得插入其他主要 / 快速行动（§10.1.2）。非起始玩家恒为 false。
        /// </summary>
        public bool CharacterSecondEffectPending;

        /// <summary>已建设在自己城市面板上的设施牌 id。</summary>
        public readonly List<int> BuiltFacilities = new();

        /// <summary>城市面板的空位格数：3 列 × 4 行共 12 格（RuleSpec §4.2；素材 Art/CityPanel.png）。</summary>
        public const int CityPanelSlotCount = 12;

        /// <summary>核心指挥塔固定占用的格子：第 3 行中间（0 起下标 7）。</summary>
        public const int CityPanelCoreSlot = 7;

        /// <summary>
        /// 城市面板每个格子上放着的设施牌 id；-1 表示空格。
        /// 下标 0–11 按「从上到下、每行从左到右」排列，与 Art/CityPanel.png 的格子一一对应（§6.4e 步 2 的落点）。
        /// </summary>
        public readonly int[] CityPanelSlots = NewCityPanel();

        /// <summary>
        /// 每一格上的设施是否已经用于过一次城市样式宣告。
        /// §6.5b 步 1：宣告时把用到的设施**旋转 180°** 标识其已使用；§10.2：同一设施只能用于一个样式。
        /// 因此「已使用」与「已旋转」是同一件事，只记一个标记；宣告后不复位（§8.2 复位的是样式牌上的玩家标记）。
        /// </summary>
        public readonly bool[] CityPanelUsed = new bool[CityPanelSlotCount];

        /// <summary>把某一格标记为「已用于宣告」。</summary>
        public void MarkPanelSlotUsed(int slot)
        {
            if (slot >= 0 && slot < CityPanelSlotCount) CityPanelUsed[slot] = true;
        }

        /// <summary>城市面板上还有空的设施位（§6.4e：仍有空位才能执行建设）。</summary>
        public bool HasFreePanelSlot
        {
            get
            {
                foreach (int id in CityPanelSlots)
                    if (id < 0) return true;
                return false;
            }
        }

        private static int[] NewCityPanel()
        {
            var slots = new int[CityPanelSlotCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = -1;
            return slots;
        }

        /// <summary>移动城市停靠的资源点 id；不在任何资源点上为 -1。</summary>
        public int MobileCityNodeId = -1;

        /// <summary>本回合已使用过特殊行动的城市样式牌 id（每张样式牌每回合限一次）。</summary>
        public readonly HashSet<int> UsedPatternActionsThisTurn = new();

        /// <summary>
        /// 本回合尚待执行的「额外主要行动」次数（「源石工业中枢」特殊行动给 2 次）。
        /// 由效果写入、<see cref="Engine.TurnFlow.CompletePlayerAction"/> 在推进座位前递减并保持当前座位；
        /// 收尾阶段由 <see cref="ResetTurnScopedState"/> 清零。
        /// </summary>
        public int ExtraMainActionsThisTurn;

        /// <summary>
        /// 禁止使用角色牌的「轮」号（== <see cref="GameState.ActionRound"/> 时本玩家不能使用角色牌，0 = 无限制）。
        /// 「源石工业中枢」/「高效移动管理体系」特殊行动的「使用此行动的轮中不能使用角色牌」，
        /// 收尾阶段由 <see cref="ResetTurnScopedState"/> 清零。
        /// </summary>
        public int CharacterCardBanRound;

        public PlayerState(int seatIndex, PlayerColor color)
        {
            SeatIndex = seatIndex;
            Color = color;
        }

        /// <summary>深拷贝一份本玩家状态（供 <see cref="GameState.Clone"/> 的效果会话试跑用）。</summary>
        public PlayerState Clone()
        {
            var copy = new PlayerState(SeatIndex, Color)
            {
                Resources = Resources, // ResourcePool 是值类型结构体，赋值即拷贝
                Vouchers = Vouchers,
                Score = Score,
                MarkersInSupply = MarkersInSupply,
                PlacedCharacterCard = PlacedCharacterCard,
                UsedCharacterCardThisTurn = UsedCharacterCardThisTurn,
                UsedCharacterStrategyThisTurn = UsedCharacterStrategyThisTurn,
                UsedCharacterTacticsThisTurn = UsedCharacterTacticsThisTurn,
                CharacterSecondEffectPending = CharacterSecondEffectPending,
                MobileCityNodeId = MobileCityNodeId,
                ExtraMainActionsThisTurn = ExtraMainActionsThisTurn,
                CharacterCardBanRound = CharacterCardBanRound,
                SetupDiscardDone = SetupDiscardDone,
            };

            copy.HandCharacterCards.AddRange(HandCharacterCards);
            copy.DiscardCharacterCards.AddRange(DiscardCharacterCards);
            copy.BuiltFacilities.AddRange(BuiltFacilities);
            Array.Copy(CityPanelSlots, copy.CityPanelSlots, CityPanelSlots.Length);
            Array.Copy(CityPanelUsed, copy.CityPanelUsed, CityPanelUsed.Length);

            foreach (int cardId in UsedPatternActionsThisTurn) copy.UsedPatternActionsThisTurn.Add(cardId);

            return copy;
        }

        /// <summary>回合收尾时清空「本回合」作用域的标记。</summary>
        public void ResetTurnScopedState()
        {
            UsedCharacterCardThisTurn = false;
            UsedCharacterStrategyThisTurn = false;
            UsedCharacterTacticsThisTurn = false;
            CharacterSecondEffectPending = false;
            UsedPatternActionsThisTurn.Clear();
            ExtraMainActionsThisTurn = 0;
            CharacterCardBanRound = 0;
        }
    }
}
