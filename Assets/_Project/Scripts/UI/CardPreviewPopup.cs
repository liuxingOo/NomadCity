using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 卡面放大弹窗：点已建成的设施时弹出这张牌的放大版，方便看清卡面细节。
    /// 只负责显示，不管规则 —— 由 <see cref="GameScreenController"/> 决定何时 Show / Hide。
    ///
    /// 场景结构（与 <see cref="CityPatternPanel"/> 一致）：
    /// Canvas
    ///   CardPreviewPopup（宿主容器，挂本脚本，自身永远激活）
    ///     Root                 ← 弹窗开关，由 SetOpen 控制显隐
    ///       Dim                ← 全屏半透明遮罩，Raycast Target 要勾上（挡住底下的地图）
    ///       放大卡面 Image     ← 拖进 Card Face Image（保持宽高比，否则卡面会被拉伸）
    ///       卡名文本           ← 拖进 Card Name Label（可选）
    ///       关闭按钮           ← 拖进 Close Button
    /// </summary>
    public sealed class CardPreviewPopup : MonoBehaviour
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 卡面 / 关闭按钮）。宿主自身必须保持激活，只切这一个。" +
                 "留空则弹窗会一直显示。")]
        [SerializeField] private GameObject root;

        [Tooltip("放大后的卡面 Image。要保持卡面原始宽高比，请把它的 Preserve Aspect 勾上。")]
        [SerializeField] private Image cardFaceImage;

        [Tooltip("卡名文本（可选）")]
        [SerializeField] private TMP_Text cardNameLabel;

        [Tooltip("关闭按钮")]
        [SerializeField] private Button closeButton;

        [Tooltip("关闭 / 取消按钮上的文案（可选）：查看卡面时写「关闭」，建设选费用时写「取消建设」。")]
        [SerializeField] private TMP_Text closeLabel;

        [Tooltip("建设（§6.4e 步 1）：「选项一 · 用资源费用建造」的按钮。只在建设选费用时出现，平时隐藏。")]
        [SerializeField] private Button resourcePayButton;

        [Tooltip("资源费用按钮上的文案（费用明细）；留空则不改按钮文字。")]
        [SerializeField] private TMP_Text resourcePayLabel;

        [Tooltip("建设：「选项二 · 用金券费用建造」的按钮。只在建设选费用时出现，平时隐藏。")]
        [SerializeField] private Button voucherPayButton;

        [Tooltip("金券费用按钮上的文案；留空则不改按钮文字。")]
        [SerializeField] private TMP_Text voucherPayLabel;

        [Tooltip("设施牌卡面 sprite 库，下标映射见 FacilitySupplyPanel.SpriteIndexOf（100–140 → 0–40，" +
                 "核心指挥塔 200–203 → 41–44，延伸枢纽 300–302 → 45–47）。与设施供应区、城市面板共用同一个库。")]
        [SerializeField] private Sprite[] cardFaces = System.Array.Empty<Sprite>();

        /// <summary>弹窗是否正开着。</summary>
        public bool IsVisible => root != null && root.activeSelf;

        /// <summary>关闭键当前该做什么：查看时收起弹窗，建设选费用时 = 取消建设（回建设选牌）。</summary>
        private Action closeAction;

        private Action payResourceAction;
        private Action payVoucherAction;

        /// <summary>开关弹窗。宿主容器始终激活（避免 Awake 里的按钮绑定失效），只切 Root 的显隐。</summary>
        public void SetOpen(bool open)
        {
            if (root == null) return;
            if (root.activeSelf != open) root.SetActive(open);
        }

        public void Hide() => SetOpen(false);

        private void Awake()
        {
            if (root == null)
                Debug.LogWarning("[卡面放大] 没有接「Root」——弹窗会一直显示。请新建弹窗根并拖进本组件的 Root 字段。", this);
            if (closeButton == null)
                Debug.LogWarning("[卡面放大] 没有接「关闭按钮」——只能靠脚本 Hide() 收起。请把关闭按钮拖进 Close Button。", this);

            if (closeButton != null)
            {
                if (closeButton.colors.disabledColor != Color.white)
                {
                    ColorBlock colors = closeButton.colors;
                    colors.disabledColor = Color.white;
                    closeButton.colors = colors;
                }

                closeButton.onClick.AddListener(OnCloseClicked);
            }

            if (resourcePayButton != null)
                resourcePayButton.onClick.AddListener(() => payResourceAction?.Invoke());
            if (voucherPayButton != null)
                voucherPayButton.onClick.AddListener(() => payVoucherAction?.Invoke());

            SetPayButtonsVisible(false);
            SetOpen(false);
        }

        private void OnDestroy()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
        }

        private void OnCloseClicked()
        {
            if (closeAction != null) closeAction();
            else Hide();
        }

        /// <summary>把牌 id 对应的卡面放大展示出来（查看用：隐藏两个付费按钮，只留关闭键）。</summary>
        public void Show(GameState state, int cardId)
        {
            ShowFace(state, cardId);

            closeAction = Hide;
            SetLabel(closeLabel, "关闭");
            SetPayButtonsVisible(false);

            SetOpen(true);
        }

        /// <summary>
        /// 建设第一步（§6.4e 步 1）：放大卡面 + 三个按钮 —— 选项一（资源付费）、选项二（金券付费）、取消建设。
        /// 只有一种费用时，另一个付费按钮自动隐藏；<paramref name="onCancel"/> = 取消建设（回建设选牌弹窗）。
        /// </summary>
        public void ShowForBuild(GameState state, int cardId,
            string resourceText, bool resourceEnabled, Action onResource,
            string voucherText, bool voucherEnabled, Action onVoucher,
            Action onCancel)
        {
            ShowFace(state, cardId);

            closeAction = onCancel;
            SetLabel(closeLabel, "取消建设");

            payResourceAction = onResource;
            payVoucherAction = onVoucher;

            if (resourcePayButton != null)
            {
                resourcePayButton.gameObject.SetActive(!string.IsNullOrEmpty(resourceText));
                resourcePayButton.interactable = resourceEnabled;
            }

            if (voucherPayButton != null)
            {
                voucherPayButton.gameObject.SetActive(!string.IsNullOrEmpty(voucherText));
                voucherPayButton.interactable = voucherEnabled;
            }

            SetLabel(resourcePayLabel, resourceText);
            SetLabel(voucherPayLabel, voucherText);

            SetOpen(true);
        }

        private void ShowFace(GameState state, int cardId)
        {
            int index = FacilitySupplyPanel.SpriteIndexOf(cardId);
            Sprite face = index >= 0 && index < cardFaces.Length ? cardFaces[index] : null;

            if (cardFaceImage != null)
            {
                cardFaceImage.sprite = face;
                cardFaceImage.enabled = face != null;
            }

            SetLabel(cardNameLabel, NameOf(state, cardId));
        }

        private void SetPayButtonsVisible(bool visible)
        {
            if (resourcePayButton != null) resourcePayButton.gameObject.SetActive(visible);
            if (voucherPayButton != null) voucherPayButton.gameObject.SetActive(visible);
        }

        private static void SetLabel(TMP_Text label, string text)
        {
            if (label != null) label.text = text ?? string.Empty;
        }

        private static string NameOf(GameState state, int cardId)
        {
            FacilityCardDef card = state != null ? state.Catalog.Facility(cardId) : null;
            return card != null ? card.Name : $"设施牌 {cardId}";
        }
    }
}
