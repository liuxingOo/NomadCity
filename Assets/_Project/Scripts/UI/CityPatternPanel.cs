using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 城市样式牌面板（§4.1 步 3 / §6.5b）：场上公开的 6 张城市样式牌，玩家从中挑一张宣告。
    /// 只负责显示与发出意图 —— 点第几个牌位就把下标交回给 GameScreenController，合法性与匹配一概不问。
    ///
    /// 呈现方式：不常驻场上，由「宣告城市样式」按钮弹出（<see cref="SetOpen"/>），选中一张牌即收起。
    ///
    /// 场景结构（建议，与 <see cref="FacilitySupplyPanel"/> 一致）：
    /// Canvas
    ///   CityPatternPanel（宿主容器，挂本脚本，自身永远激活）
    ///     Root                 ← 弹窗开关，由 SetOpen 控制显隐
    ///       Dim                ← 全屏半透明遮罩，Raycast Target 要勾上（挡住底下的地图）
    ///       背景 Image
    ///         牌位 1 … 6        ← 每个都是一个 Button，逐个拖进 Card Buttons
    ///           卡面 Image      ← 装饰用，Raycast Target 关闭，逐个拖进 Card Face Images
    ///           牌名文本        ← 拖进 Card Labels（可选）
    /// </summary>
    public sealed class CityPatternPanel : MonoBehaviour
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 背景 / 牌位）。宿主自身必须保持激活，只切这一个。" +
                 "留空则面板一直显示（旧的常驻行为）。")]
        [SerializeField] private GameObject root;

        [Tooltip("6 个牌位按钮，顺序与场上样式牌列表（CityPatternCards）一致（下标 0 是最左边那张牌）")]
        [SerializeField] private Button[] cardButtons = Array.Empty<Button>();

        [Tooltip("每个牌位上的牌名文本，与 Card Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] cardLabels = Array.Empty<TMP_Text>();

        [Header("卡面")]
        [Tooltip("每个牌位上的卡面 Image，与 Card Buttons 一一对应（装饰用，Raycast Target 关闭）")]
        [SerializeField] private Image[] cardFaceImages = Array.Empty<Image>();

        [Tooltip("按样式牌 id 顺序排列的卡面 sprite：基础牌 500–505 → 0–5，企业扩展牌 550–555 → 6–11")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

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
                Debug.LogWarning("[城市样式牌] 没有接「Root」——面板会一直显示。要走「点按钮才弹出」的话，" +
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
        /// 按当前场上的样式牌刷新 6 个牌位：写入牌名、按需显示。
        /// <paramref name="interactable"/> 只有「宣告」流程进行中才为 true，其余时候牌位照样看得见但点不动。
        /// </summary>
        public void Refresh(GameState state, bool interactable)
        {
            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                int cardId = state != null && i < state.CityPatternCards.Count ? state.CityPatternCards[i] : -1;
                button.gameObject.SetActive(cardId >= 0);
                button.interactable = interactable && cardId >= 0;

                if (i < cardLabels.Length && cardLabels[i] != null)
                    cardLabels[i].text = cardId >= 0 ? LabelOf(state, cardId) : string.Empty;

                if (i < cardFaceImages.Length && cardFaceImages[i] != null)
                {
                    int spriteIndex = SpriteIndexOf(cardId);
                    Sprite face = spriteIndex >= 0 && spriteIndex < cardFaces.Length ? cardFaces[spriteIndex] : null;
                    cardFaceImages[i].sprite = face;
                    cardFaceImages[i].enabled = face != null;
                }
            }
        }

        /// <summary>
        /// 样式牌 id → <see cref="cardFaces"/> 下标。
        /// 基础牌 500–505 → 0–5（I 级 500–503、II 级 504–505），企业扩展牌 550–555 → 6–11；其余 id 返回 -1。
        /// 注意：下标按 **id** 排，不是按拼版图从左到右、从上到下的顺序 —— 拼版图里 id 顺序是乱的，
        /// 而且 lv1 表中间还夹着一格要丢弃的重复样张。
        /// </summary>
        public static int SpriteIndexOf(int cardId)
        {
            if (cardId >= CardIds.CityPatternBase && cardId < CardIds.CityPatternBase + CardCounts.CityPatterns)
                return cardId - CardIds.CityPatternBase;
            if (cardId >= CardIds.CityPatternEnterpriseBase
                && cardId < CardIds.CityPatternEnterpriseBase + CardCounts.CityPatternEnterprise)
                return CardCounts.CityPatterns + (cardId - CardIds.CityPatternEnterpriseBase);
            return -1;
        }

        /// <summary>样式牌的显示名：优先用已录入的牌名，未录入时退回等级占位。</summary>
        private static string LabelOf(GameState state, int cardId)
        {
            CityPatternCardDef card = state.Catalog.CityPattern(cardId);
            if (card == null) return $"样式牌 {cardId}";
            if (!string.IsNullOrEmpty(card.Name)) return card.Name;
            return card.Level >= 2 ? "II 级样式牌" : "I 级样式牌";
        }
    }
}
