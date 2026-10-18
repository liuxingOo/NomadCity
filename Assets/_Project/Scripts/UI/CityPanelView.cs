using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 城市面板（§6.4e 步 2 的落点）：3 列 × 4 行共 12 格，第 3 行中间那格印着「核心指挥塔」，开局就被占住。
    /// 显示**当前行动玩家**的面板（设施是公开信息）。
    /// 建设模式下点空格表示「把选中的设施牌建在这里」；其余时候点已建成的设施则会弹出放大卡面。
    /// 空格子不画按钮图形，露出面板底板自身的背景。
    ///
    /// 场景结构（建议）：与 Art/CityPanel.png 的格子一一对应
    /// Canvas
    ///   CityPanelView（空物体，挂本脚本，自身保持激活）
    ///     Root                 ← 面板本体（用 CityPanel.png 当背景）
    ///       格 1 … 12           ← 每个都是一个 Button，按「从上到下、每行从左到右」拖进 Slot Buttons
    ///         内容文本           ← 拖进 Slot Labels（可选）
    /// </summary>
    public sealed class CityPanelView : MonoBehaviour
    {
        [Tooltip("12 个格子按钮，下标 0–11 按「从上到下、每行从左到右」排列，与 CityPanel.png 一致")]
        [SerializeField] private Button[] slotButtons = Array.Empty<Button>();

        [Tooltip("每个格子上的内容文本，与 Slot Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] slotLabels = Array.Empty<TMP_Text>();

        [Tooltip("每个格子上的卡面 Image，与 Slot Buttons 一一对应（可选；装饰用，Raycast Target 关掉）")]
        [SerializeField] private Image[] slotImages = Array.Empty<Image>();

        [Tooltip("设施牌卡面 sprite 库，下标映射见 FacilitySupplyPanel.SpriteIndexOf（100–140 → 0–40，核心指挥塔 200–203 → 41–44，" +
                 "延伸枢纽 300–302 → 45–47）。")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

        [Tooltip("建设选位模式下，可放的空格子亮起来的颜色（空格子没有 Sprite，会画成一块这个颜色的半透明色块）。")]
        [SerializeField] private Color buildSlotHighlight = new Color(0.25f, 0.85f, 0.45f, 0.35f);

        /// <summary>点了第几格（0 起）。</summary>
        public event Action<int> SlotClicked;

        private void Awake()
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                Button button = slotButtons[i];
                if (button == null) continue;

                NeutralizeDisabledTint(button);

                int index = i; // 闭包捕获
                button.onClick.AddListener(() => SlotClicked?.Invoke(index));
            }

            if (slotButtons.Length != PlayerState.CityPanelSlotCount)
                Debug.LogWarning($"[城市面板] 格子按钮配了 {slotButtons.Length} 个，规则要求 {PlayerState.CityPanelSlotCount} 个（3 列 × 4 行）。" +
                                 "数量不一致时下标会错位，请对照 Art/CityPanel.png 把 12 个格子按顺序拖进「Slot Buttons」。", this);
        }

        /// <summary>
        /// 让「不可点」不再压暗目标图形。卡面位复用的就是这个 Image（Button 的 Target Graphic），
        /// Unity 默认的禁用色带 50% 透明，会把卡面蒙上一层灰。只动禁用色，按下/高亮反馈保留。
        /// 幂等：Awake 与 Refresh 各调一次 —— Refresh 可能比 Awake 先被调到，那边是保险。
        /// </summary>
        private static void NeutralizeDisabledTint(Button button)
        {
            if (button.colors.disabledColor == Color.white) return;

            ColorBlock colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
        }

        /// <summary>
        /// 刷新某个玩家的面板：写入每格内容。
        /// <paramref name="buildMode"/> 只在「建设」行动进行中为 true。
        /// 建设模式：只有空格可点（点了就把选中的设施建在这里）；非建设模式：只有已建成设施的格子可点（点了弹出放大卡面）。
        /// 空格子不画任何按钮图形、露出 CityPanelView 自身的底板背景，但保留 Raycast Target，否则建设模式下点不到。
        /// </summary>
        public void Refresh(GameState state, int seat, bool buildMode)
        {
            bool valid = state != null && seat >= 0 && seat < state.Players.Count;
            PlayerState player = valid ? state.Players[seat] : null;

            for (int i = 0; i < slotButtons.Length; i++)
            {
                Button button = slotButtons[i];
                if (button == null) continue;

                NeutralizeDisabledTint(button);

                int cardId = player != null && i < player.CityPanelSlots.Length ? player.CityPanelSlots[i] : -1;
                bool isCore = i == PlayerState.CityPanelCoreSlot;

                int faceIndex = FacilitySupplyPanel.SpriteIndexOf(cardId);
                Sprite face = faceIndex >= 0 && faceIndex < cardFaces.Length ? cardFaces[faceIndex] : null;
                bool hasFace = face != null;

                bool buildable = buildMode && player != null && !isCore && cardId < 0;

                button.interactable = buildMode ? buildable : hasFace;

                if (i < slotImages.Length && slotImages[i] != null)
                {
                    Image image = slotImages[i];
                    image.sprite = face;
                    // 空格子没有卡面：建设选位时亮成高亮色提示「可放这里」，
                    // 其余时候全透明（露出底板背景），但都保留 Raycast Target 以便建设时点得到。
                    image.color = hasFace ? Color.white : buildable ? buildSlotHighlight : new Color(1f, 1f, 1f, 0f);
                }

                // 卡面自带设施名，底板也印着「核心指挥塔」——一律不再叠文本。
                if (i < slotLabels.Length && slotLabels[i] != null)
                    slotLabels[i].text = string.Empty;
            }
        }
    }
}
