using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 设施供应区（§4.1 / §6.4e）：6 张设施牌，被建设拿走一张就立刻从牌堆补一张。
    /// 只负责显示与发出意图 —— 点第几个牌位就把下标交回给 GameScreenController，合法性与费用一概不问。
    ///
    /// 呈现方式：不常驻场上，由「建设」按钮弹出（<see cref="SetOpen"/>），选中一张牌即收起，
    /// 开关时机由 GameScreenController 按行动模式决定。
    ///
    /// 场景结构（建议）：
    /// Canvas
    ///   FacilitySupplyPanel（宿主容器，挂本脚本，自身永远激活）
    ///     Root                 ← 弹窗开关，由 SetOpen 控制显隐
    ///       Dim                ← 全屏半透明遮罩，Raycast Target 要勾上（挡住底下的地图）
    ///       背景 Image
    ///         牌位 1 … 6        ← 每个都是一个 Button，逐个拖进 Card Buttons
    ///           牌名文本        ← 拖进 Card Labels（可选）
    /// </summary>
    public sealed class FacilitySupplyPanel : MonoBehaviour
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 背景 / 牌位）。宿主自身必须保持激活，只切这一个。" +
                 "留空则面板一直显示（旧的常驻行为）。")]
        [SerializeField] private GameObject root;

        [Tooltip("6 个牌位按钮，顺序与设施供应区列表一致（下标 0 是最左边那张牌）")]
        [SerializeField] private Button[] cardButtons = Array.Empty<Button>();

        [Tooltip("每个牌位上的牌名文本，与 Card Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] cardLabels = Array.Empty<TMP_Text>();

        /// <summary>点了第几个牌位（0 起）。</summary>
        public event Action<int> CardClicked;

        /// <summary>
        /// 开关弹窗。宿主容器始终激活（避免 Awake 里的按钮绑定失效），只切 Root 的显隐。
        /// </summary>
        public void SetOpen(bool open)
        {
            if (root == null) return;
            if (root.activeSelf != open) root.SetActive(open);
        }

        private void Awake()
        {
            if (root == null)
                Debug.LogWarning("[设施供应区] 没有接「Root」——面板会一直显示。要走「点建设才弹出」的话，" +
                                 "请在场景里新建弹窗根并拖进本组件的 Root 字段。", this);

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                int index = i; // 闭包捕获：每个按钮绑定自己的下标
                button.onClick.AddListener(() => CardClicked?.Invoke(index));
            }
        }

        /// <summary>
        /// 按当前供应区刷新 6 个牌位：写入牌名、按需显示。
        /// <paramref name="interactable"/> 只有「建设」行动进行中才为 true，其余时候牌位照样看得见但点不动。
        /// </summary>
        public void Refresh(GameState state, bool interactable)
        {
            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                int cardId = state != null && i < state.FacilitySupply.Count ? state.FacilitySupply[i] : -1;
                button.gameObject.SetActive(cardId >= 0);
                button.interactable = interactable && cardId >= 0;

                if (i < cardLabels.Length && cardLabels[i] != null)
                    cardLabels[i].text = cardId >= 0 ? NameOf(state, cardId) : string.Empty;
            }
        }

        private static string NameOf(GameState state, int cardId)
        {
            FacilityCardDef card = state.Catalog.Facility(cardId);
            return card != null ? card.Name : $"设施牌 {cardId}";
        }
    }
}
