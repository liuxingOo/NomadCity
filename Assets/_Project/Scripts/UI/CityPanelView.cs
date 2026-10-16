using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 城市面板（§6.4e 步 2 的落点）：3 列 × 4 行共 12 格，第 3 行中间那格印着「核心指挥塔」，开局就被占住。
    /// 显示**当前行动玩家**的面板（设施是公开信息），点空格表示「把选中的设施牌建在这里」。
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

        /// <summary>点了第几格（0 起）。</summary>
        public event Action<int> SlotClicked;

        private void Awake()
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                Button button = slotButtons[i];
                if (button == null) continue;

                int index = i; // 闭包捕获
                button.onClick.AddListener(() => SlotClicked?.Invoke(index));
            }

            if (slotButtons.Length != PlayerState.CityPanelSlotCount)
                Debug.LogWarning($"[城市面板] 格子按钮配了 {slotButtons.Length} 个，规则要求 {PlayerState.CityPanelSlotCount} 个（3 列 × 4 行）。" +
                                 "数量不一致时下标会错位，请对照 Art/CityPanel.png 把 12 个格子按顺序拖进「Slot Buttons」。", this);
        }

        /// <summary>
        /// 刷新某个玩家的面板：写入每格内容。
        /// <paramref name="interactable"/> 只在「建设」行动进行中为 true；核心指挥塔那格与已有设施的格子永远点不动。
        /// </summary>
        public void Refresh(GameState state, int seat, bool interactable)
        {
            bool valid = state != null && seat >= 0 && seat < state.Players.Count;
            PlayerState player = valid ? state.Players[seat] : null;

            for (int i = 0; i < slotButtons.Length; i++)
            {
                Button button = slotButtons[i];
                if (button == null) continue;

                int cardId = player != null && i < player.CityPanelSlots.Length ? player.CityPanelSlots[i] : -1;
                bool isCore = i == PlayerState.CityPanelCoreSlot;

                button.interactable = interactable && player != null && !isCore && cardId < 0;

                if (i < slotLabels.Length && slotLabels[i] != null)
                    slotLabels[i].text = LabelOf(state, cardId, isCore);
            }
        }

        private static string LabelOf(GameState state, int cardId, bool isCore)
        {
            if (isCore) return "核心指挥塔";
            if (cardId < 0) return "空位";

            FacilityCardDef card = state.Catalog.Facility(cardId);
            return card != null ? card.Name : $"设施牌 {cardId}";
        }
    }
}
