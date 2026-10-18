using System;
using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    // PatternCell / PatternCondition 是纯数据，已移到 Model 层（CardDefinitions.cs），
    // 好让城市样式牌的定义（CityPatternCardDef）能直接持有宣告条件，不必让 Model 反过来依赖 Engine。

    /// <summary>一次成功匹配：用到的面板格子下标，以及这次匹配所用的旋转角度。</summary>
    public readonly struct PatternMatch
    {
        /// <summary>用到的格子下标（0–11），顺序与条件的 <see cref="PatternCondition.Cells"/> 一致。</summary>
        public readonly IReadOnlyList<int> Slots;

        /// <summary>这次匹配用的旋转角度：0 / 90 / 180 / 270。</summary>
        public readonly int RotationDegrees;

        public PatternMatch(IReadOnlyList<int> slots, int rotationDegrees)
        {
            Slots = slots;
            RotationDegrees = rotationDegrees;
        }

        public override string ToString() => $"旋转 {RotationDegrees}°：[{string.Join("、", Slots)}]";
    }

    /// <summary>
    /// 城市面板查询器（§6.5b / §10.2）：只看「位置 + 颜色」，做任何结算、不改任何状态。
    ///
    /// 两处用途：
    /// - 「宣告城市样式」前判断这 12 格能不能凑出某个样式条件，以及具体用哪几格；
    /// - 部分需要读面板的设施入场效果（「本格旁边有哪些颜色的设施」之类），先把「某格是什么」问清楚。
    ///
    /// 规则要点（都体现在这里的默认行为里）：
    /// - 条件可以整体**旋转**（90° 的倍数），**不能翻转**；
    /// - 彩色设施（<see cref="FacilityColor.Rainbow"/>）可以当成任意颜色；条件里的彩色格同理；
    /// - 每个设施只能用于一次宣告，用过的格子默认不再参与匹配（<see cref="PlayerState.CityPanelUsed"/>）；
    /// - 核心指挥塔虽然开局就占着第 8 格、不是建设出来的，但一样可以参与组合（已与规则持有者确认）。
    /// </summary>
    public static class CityPanelQuery
    {
        /// <summary>面板列数（3 列；素材 <c>Art/CityPanel.png</c>）。</summary>
        public const int Columns = 3;

        /// <summary>面板行数（4 行）。</summary>
        public const int Rows = 4;

        /// <summary>行列 → 格子下标。下标即 <see cref="PlayerState.CityPanelSlots"/> 的下标。</summary>
        public static int SlotOf(int row, int column) => row * Columns + column;

        public static int RowOf(int slot) => slot / Columns;

        public static int ColumnOf(int slot) => slot % Columns;

        public static bool IsInside(int slot) => slot >= 0 && slot < PlayerState.CityPanelSlotCount;

        /// <summary>是不是印着「核心指挥塔」的那一格（第 3 行中间）。</summary>
        public static bool IsCoreSlot(int slot) => slot == PlayerState.CityPanelCoreSlot;

        /// <summary>
        /// 核心指挥塔那一格的十字相邻格（上、下、左、右；均在面板内）。
        /// 供「城市化区域」这类「每有一个与核心指挥塔十字相邻的设施」的计数使用。
        /// </summary>
        public static readonly int[] CoreNeighbors =
        {
            PlayerState.CityPanelCoreSlot - Columns, // 上
            PlayerState.CityPanelCoreSlot + Columns, // 下
            PlayerState.CityPanelCoreSlot - 1,       // 左
            PlayerState.CityPanelCoreSlot + 1,       // 右
        };

        /// <summary>核心指挥塔十字相邻的 4 格上已经摆了几张设施（空格不算）。</summary>
        public static int AdjacentToCoreFacilityCount(PlayerState player)
        {
            int count = 0;
            foreach (int slot in CoreNeighbors)
            {
                if (CardAt(player, slot) >= 0) count++;
            }
            return count;
        }

        /// <summary>
        /// 面板上**非彩色**（颜色不是 <see cref="FacilityColor.Rainbow"/>）设施的**种数** —— 按不同牌名去重，
        /// 同名的多张只算 1 种（对应牌面「每有 1 种非彩色的设施」）。查不到定义的格子不计。
        /// </summary>
        public static int NonColorFacilityKindCount(PlayerState player, ICardCatalog catalog)
        {
            if (player == null || catalog == null) return 0;

            var names = new HashSet<string>();
            for (int slot = 0; slot < PlayerState.CityPanelSlotCount; slot++)
            {
                int cardId = CardAt(player, slot);
                if (cardId < 0) continue;

                FacilityCardDef card = catalog.Facility(cardId);
                if (card == null || card.Color == FacilityColor.Rainbow) continue;

                names.Add(card.Name);
            }
            return names.Count;
        }

        /// <summary>按 <see cref="CountSource"/> 统计某玩家面板上的数量（效果节点与建筑费用修正共用）。</summary>
        public static int Count(GameState state, int seat, CountSource source)
        {
            if (state == null || seat < 0 || seat >= state.Players.Count) return 0;

            PlayerState player = state.Players[seat];
            switch (source)
            {
                case CountSource.AdjacentCoreFacilities:
                    return AdjacentToCoreFacilityCount(player);
                case CountSource.NonColorFacilityKinds:
                    return NonColorFacilityKindCount(player, state.Catalog);
                default:
                    return 0;
            }
        }

        /// <summary>某格上的设施牌 id；空格为 -1。</summary>
        public static int CardAt(PlayerState player, int slot) =>
            player != null && IsInside(slot) ? player.CityPanelSlots[slot] : -1;

        /// <summary>
        /// 某格上的设施颜色；空格、或该格上的设施查不到定义时为 null（null = 这格不能参与样式组合）。
        /// </summary>
        public static FacilityColor? ColorAt(PlayerState player, ICardCatalog catalog, int slot)
        {
            if (catalog == null) return null;

            int cardId = CardAt(player, slot);
            if (cardId < 0) return null;

            FacilityCardDef card = catalog.Facility(cardId);
            return card != null ? card.Color : (FacilityColor?)null;
        }

        /// <summary>某格是否已经用于过一次宣告（用过的设施不能再进新的组合，§6.5b / §10.2）。</summary>
        public static bool IsUsed(PlayerState player, int slot) =>
            player != null && IsInside(slot) && player.CityPanelUsed[slot];

        /// <summary>
        /// 条件格要求的颜色与格子上实际的颜色是否相容：彩色（Rainbow）视为任意颜色，多色斜分格「其一即可」。
        /// </summary>
        public static bool MatchesColor(PatternCell cell, FacilityColor actual) => cell.Accepts(actual);

        /// <summary>
        /// 找出第一组合法匹配。尝试顺序固定：先按旋转 0° → 90° → 180° → 270°，
        /// 同角度下再从上到下、从左到右平移，因此同样的面板每次得到同一个结果（便于测试与复现）。
        /// </summary>
        public static bool TryFind(PlayerState player, ICardCatalog catalog, PatternCondition condition,
            out PatternMatch match)
        {
            match = default;

            List<PatternMatch> matches = FindAll(player, catalog, condition);
            if (matches.Count == 0) return false;

            match = matches[0];
            return true;
        }

        /// <summary>
        /// 列出所有合法匹配（同一次宣告里玩家可能有多组可用的格子，由上层让玩家挑一组）。
        /// **同一组格子只出现一次**：单格或旋转对称的条件会在不同角度下重复命中同样的格子，
        /// 那些重复项会被丢掉，否则界面会把同一组摆法列好几遍。
        /// </summary>
        public static List<PatternMatch> FindAll(PlayerState player, ICardCatalog catalog,
            PatternCondition condition)
        {
            var results = new List<PatternMatch>();
            if (player == null || catalog == null || condition == null || condition.IsEmpty) return results;

            Bounds(condition, out int minRow, out int minCol, out int height, out int width);

            foreach (int rotation in new[] { 0, 90, 180, 270 })
            {
                // 转 90° 或 270° 后宽高互换，才能算出「这个角度下占几行几列」。
                bool swapped = rotation == 90 || rotation == 270;
                int boxHeight = swapped ? width : height;
                int boxWidth = swapped ? height : width;

                for (int rowOffset = 0; rowOffset + boxHeight <= Rows; rowOffset++)
                {
                    for (int columnOffset = 0; columnOffset + boxWidth <= Columns; columnOffset++)
                    {
                        PatternMatch found = TryPlace(player, catalog, condition, minRow, minCol,
                            height, width, rotation, rowOffset, columnOffset);

                        if (found.Slots == null) continue;
                        if (ContainsSameSlots(results, found.Slots)) continue;

                        results.Add(found);
                    }
                }
            }

            return results;
        }

        /// <summary>已经收过「用同样这几格」的摆法了吗（顺序无关；用于过滤旋转带出来的重复项）。</summary>
        private static bool ContainsSameSlots(List<PatternMatch> found, IReadOnlyList<int> slots)
        {
            foreach (PatternMatch existing in found)
            {
                if (existing.Slots.Count != slots.Count) continue;

                bool same = true;
                for (int i = 0; i < slots.Count && same; i++)
                {
                    bool hit = false;
                    for (int j = 0; j < existing.Slots.Count; j++)
                    {
                        if (existing.Slots[j] == slots[i])
                        {
                            hit = true;
                            break;
                        }
                    }
                    same = hit;
                }

                if (same) return true;
            }
            return false;
        }

        /// <summary>条件占的矩形范围（先把坐标归一化到自身左上角为 0,0）。</summary>
        private static void Bounds(PatternCondition condition, out int minRow, out int minCol,
            out int height, out int width)
        {
            minRow = int.MaxValue;
            minCol = int.MaxValue;
            int maxRow = int.MinValue;
            int maxCol = int.MinValue;

            foreach (PatternCell cell in condition.Cells)
            {
                if (cell.Row < minRow) minRow = cell.Row;
                if (cell.Row > maxRow) maxRow = cell.Row;
                if (cell.Column < minCol) minCol = cell.Column;
                if (cell.Column > maxCol) maxCol = cell.Column;
            }

            height = maxRow - minRow + 1;
            width = maxCol - minCol + 1;
        }

        /// <summary>按给定旋转与平移试着把条件放到面板上；有一格不合格就整体不匹配（返回 default）。</summary>
        private static PatternMatch TryPlace(PlayerState player, ICardCatalog catalog, PatternCondition condition,
            int minRow, int minCol, int height, int width, int rotation,
            int rowOffset, int columnOffset)
        {
            var slots = new List<int>(condition.Cells.Count);

            foreach (PatternCell cell in condition.Cells)
            {
                int row;
                int column;

                switch (rotation)
                {
                    case 90:  row = cell.Column - minCol;  column = height - 1 - (cell.Row - minRow); break;
                    case 180: row = height - 1 - (cell.Row - minRow); column = width - 1 - (cell.Column - minCol); break;
                    case 270: row = width - 1 - (cell.Column - minCol); column = cell.Row - minRow; break;
                    default:  row = cell.Row - minRow; column = cell.Column - minCol; break;
                }

                int slot = SlotOf(rowOffset + row, columnOffset + column);
                if (!IsInside(slot)) return default;

                if (player.CityPanelUsed[slot]) return default; // §6.5b：每个设施只能用于一次宣告

                FacilityColor? actual = ColorAt(player, catalog, slot);
                if (actual == null) return default;
                if (!MatchesColor(cell, actual.Value)) return default;

                slots.Add(slot);
            }

            return new PatternMatch(slots, rotation);
        }
    }
}
