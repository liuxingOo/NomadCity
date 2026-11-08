using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 按钮的「亮 / 暗」只该由游戏状态决定，不能由 Unity 的「选中态」决定。
    ///
    /// uGUI 的 Button 在被点过之后会一直停在 Selected，颜色跟着 Selected Color 走，于是出现这种错位：
    /// 弹窗明明开着，点一下别处（EventSystem 取消选中）按钮就暗回去；再点按钮收起弹窗，
    /// 按钮反而因为刚被选中而亮着 —— 状态和颜色正好反了。
    ///
    /// 这里统一做两件事：
    /// 1. <see cref="Ensure"/>：把 Selected 拉平到 Normal，点过之后不会再自己发亮（启动时由
    ///    <see cref="EnsureAll"/> 把所有按钮过一遍）；
    /// 2. <see cref="SetLit"/>：确实「开着就该亮」的按钮（弹窗入口、行动模式键）由代码显式点亮。
    /// 悬停（Highlighted）与按下（Pressed）的反馈保留。
    /// </summary>
    public static class UiButtonTint
    {
        /// <summary>第一次碰到按钮时记下的原始配色；之后亮 / 暗都从它派生，避免反复改动互相污染。</summary>
        private static readonly Dictionary<Button, ColorBlock> Originals = new Dictionary<Button, ColorBlock>();

        /// <summary>进入播放模式时清一次缓存：上一轮场景里的按钮引用不能留到下一轮。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Originals.Clear();
        }

        /// <summary>记下原始配色，并让「被选中」不再比「普通」更亮。重复调用无副作用。</summary>
        public static void Ensure(Button button)
        {
            if (button == null || Originals.ContainsKey(button)) return;

            ColorBlock colors = button.colors;
            Originals[button] = colors;

            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        /// <summary>
        /// 点亮 / 熄灭一颗按钮。亮的时候用场景里配好的 Highlighted Color 当底色
        /// （此时悬停不再额外变化）；暗的时候恢复 Normal Color，并保留悬停提亮。
        /// </summary>
        public static void SetLit(Button button, bool lit)
        {
            if (button == null) return;

            Ensure(button);
            if (!Originals.TryGetValue(button, out ColorBlock original)) return;

            Color baseColor = lit ? original.highlightedColor : original.normalColor;

            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.selectedColor = baseColor;
            colors.highlightedColor = lit ? baseColor : original.highlightedColor;
            button.colors = colors;

            // 改 ColorBlock 不会立刻重绘当前状态，这里直接把颜色推给画面。
            if (button.targetGraphic != null)
                button.targetGraphic.CrossFadeColor(baseColor, colors.fadeDuration, true, true);
        }

        /// <summary>把场景里所有按钮（含未激活的弹窗里的）都过一遍，消除「点过就一直亮」。</summary>
        public static void EnsureAll()
        {
#if UNITY_6000_0_OR_NEWER
            // Unity 6 起 FindObjectsSortMode 版重载已过时，这里用不带排序的新重载。
            Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
#elif UNITY_2023_1_OR_NEWER
            Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            Button[] buttons = Object.FindObjectsOfType<Button>(true);
#endif
            for (int i = 0; i < buttons.Length; i++) Ensure(buttons[i]);
        }
    }
}
