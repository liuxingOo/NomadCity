using System.Collections.Generic;

namespace NomadCity.Core.Model
{
    /// <summary>
    /// 城市样式牌上的一个玩家标记。位置沿卡面轨道自上而下编号（0 起）：
    /// - I 级轨道：0 = 「未使用」区，1 = 「已使用」区；
    /// - II 级轨道：0 = 「2」（次数上限），1 = 「已使用」，2 = 「1」，3 = 「已使用」，4 = 「0」。
    /// 具体格位含义见规则规格 §10.2 / §8.2。
    /// </summary>
    public sealed class PatternMarker
    {
        public readonly int Seat;

        /// <summary>所在格位（轨道自上而下的格号）。</summary>
        public int Position;

        public PatternMarker(int seat, int position)
        {
            Seat = seat;
            Position = position;
        }
    }

    /// <summary>
    /// 场上一张城市样式牌的运行时状态：谁在上面放了标记、放在哪一格。
    /// 宣告（§6.3b / §10.2）与特殊行动的格位推进等机制待 §11 效果系统落地后接上，
    /// 这里先把「标记在哪一格」建模出来，供收尾阶段的复位（§8.2）使用。
    /// </summary>
    public sealed class CityPatternState
    {
        public readonly int CardId;

        /// <summary>样式等级：1 = I 级，2 = II 级。</summary>
        public readonly int Level;

        private readonly List<PatternMarker> markers = new();

        public CityPatternState(int cardId, int level)
        {
            CardId = cardId;
            Level = level;
        }

        /// <summary>轨道格数：I 级 2 格，II 级 5 格。</summary>
        public int TrackLength => Level >= 2 ? 5 : 2;

        /// <summary>深拷贝一份本样式牌的标记状态（供 <see cref="GameState.Clone"/> 的效果会话试跑用）。</summary>
        public CityPatternState Clone()
        {
            var copy = new CityPatternState(CardId, Level);
            foreach (PatternMarker marker in markers) copy.PlaceMarker(marker.Seat, marker.Position);
            return copy;
        }

        public IReadOnlyList<PatternMarker> Markers => markers;

        /// <summary>该玩家在这张样式牌上的标记数量。</summary>
        public int CountOf(int seat)
        {
            int count = 0;
            foreach (PatternMarker marker in markers) if (marker.Seat == seat) count++;
            return count;
        }

        /// <summary>放一个标记到指定格位（宣告时放到起点格 0，§10.2）。</summary>
        public PatternMarker PlaceMarker(int seat, int position)
        {
            var marker = new PatternMarker(seat, position);
            markers.Add(marker);
            return marker;
        }

        /// <summary>
        /// 该玩家在这张样式牌上**第一个**放下的标记（越早放下就越靠前，见 <see cref="Markers"/> 的追加顺序）。
        /// **剩余可用次数只认它**：同一玩家重复宣告同一张样式牌时，新标记照样落在「2」区（§6.5b 步 2），
        /// 但次数由本局第一次宣告时定下的那个标记决定，后面的标记不会把次数重置或累加
        /// （2026-10-19 与规则持有者确认）。
        /// </summary>
        public PatternMarker FirstMarkerOf(int seat)
        {
            foreach (PatternMarker marker in markers)
                if (marker.Seat == seat) return marker;
            return null;
        }

        /// <summary>
        /// 该玩家在这张样式牌上是否还有可用的特殊行动次数（§10.2-4）：只看
        /// <see cref="FirstMarkerOf"/> 停在哪一格 —— I 级的「未使用」（第 0 格）、
        /// II 级的「2」（第 0 格）与「1」（第 2 格）才算可用，停在「已使用」或「0」时不可用。
        /// 堆在「2」区的后续标记不计入次数。
        /// </summary>
        public bool HasUsableMarker(int seat)
        {
            PatternMarker marker = FirstMarkerOf(seat);
            if (marker == null) return false;

            if (Level >= 2) return marker.Position == 0 || marker.Position == 2;
            return marker.Position == 0;
        }

        /// <summary>
        /// 使用特殊行动后把该玩家的第一个标记**下移 1 格**（§10.2-4）：
        /// I 级 0→1（未使用→已使用）；II 级 0→1（「2」→已使用）、2→3（「1」→已使用）。
        /// 只动第一个标记，所以重复宣告堆上来的标记不会让可用次数变多。
        /// 返回是否真的移动了标记。
        /// </summary>
        public bool AdvanceMarker(int seat)
        {
            PatternMarker marker = FirstMarkerOf(seat);
            if (marker == null) return false;

            if (marker.Position == 0) { marker.Position = 1; return true; }
            if (Level >= 2 && marker.Position == 2) { marker.Position = 3; return true; }
            return false;
        }

        /// <summary>
        /// 收尾阶段复位（§8.2）：
        /// - I 级：位于「已使用」区（第 1 格）的标记放回「未使用」区（第 0 格）；
        /// - II 级：位于「已使用」格（第 1、3 格）的标记向下移动一格（落到 2、4 格）。
        /// </summary>
        public void ResetForCleanup()
        {
            foreach (PatternMarker marker in markers)
            {
                if (Level >= 2)
                {
                    if (marker.Position == 1) marker.Position = 2;
                    else if (marker.Position == 3) marker.Position = 4;
                }
                else
                {
                    if (marker.Position == 1) marker.Position = 0;
                }
            }
        }
    }
}