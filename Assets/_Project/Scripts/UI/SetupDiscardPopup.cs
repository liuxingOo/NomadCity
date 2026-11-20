using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 企业扩展开局「7 选 5」的专用弹窗：一整排 7 张角色牌卡面。
    ///
    /// 为什么不做在手牌托盘里：这一步发生在**开局准备**，玩家还没有正式手牌，
    /// 手牌区此时应该是空的；把 7 张临时摊在手牌区会让人以为已经拿到手牌。
    ///
    /// 交互：点一张卡 → 由控制器弹放大卡面（确定移除 / 取消）→ 选满 2 张后
    /// 底部「确认移除」才可点；「重新选择」把两张清空。配色与其它弹窗一致。
    /// </summary>
    public sealed class SetupDiscardPopup : MonoBehaviour
    {
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color ContentColor = new Color(0.12f, 0.10f, 0.08f, 0.96f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color ButtonColor = new Color(0.45f, 0.36f, 0.28f, 1f);
        private static readonly Color SelectedColor = new Color(0.88f, 0.68f, 0.30f, 1f);
        private static readonly Color EmptyCardColor = new Color(0.22f, 0.20f, 0.18f, 1f);

        private const float CardWidth = 132f;
        private const float CardHeight = 184f;
        private const float CardGap = 16f;
        private const float Pad = 26f;
        private const float BottomBar = 64f;

        [SerializeField] private TMP_FontAsset font;

        private RectTransform rootRect;
        private RectTransform window;
        private TMP_Text titleLabel;
        private TMP_Text hintLabel;
        private RectTransform cardsRoot;
        private Button confirmButton;
        private Button resetButton;

        private readonly List<Button> cardButtons = new List<Button>();
        private readonly List<Image> cardImages = new List<Image>();
        private readonly List<Image> cardFrames = new List<Image>();
        private readonly List<Image> cardDims = new List<Image>();

        private Action<int> onCardClicked;
        private Action onConfirmed;
        private Action onReset;

        /// <summary>弹窗是不是还开着。</summary>
        public bool IsVisible => rootRect != null && rootRect.gameObject.activeSelf;

        /// <summary>在画布上新建一个「7 选 5」弹窗（运行时搭建，只建一次）。</summary>
        public static SetupDiscardPopup Create(Transform canvas, TMP_FontAsset font)
        {
            var host = new GameObject("SetupDiscardPopup", typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var view = host.AddComponent<SetupDiscardPopup>();
            view.font = font;
            view.Build();
            return view;
        }

        private void Build()
        {
            rootRect = (RectTransform)transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image dim = CreateImage("Dim", rootRect, DimColor);
            dim.raycastTarget = true;

            float width = CardWidth * 7f + CardGap * 6f + Pad * 2f;
            const float height = 372f;

            window = CreateImage("Window", rootRect, ContentColor).rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(width, height);
            window.anchoredPosition = Vector2.zero;

            titleLabel = CreateText("Title", window, 30f, TextAlignmentOptions.Left);
            var titleRect = (RectTransform)titleLabel.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(Pad, -Pad);
            titleRect.sizeDelta = new Vector2(width - Pad * 2f, 42f);

            hintLabel = CreateText("Hint", window, 20f, TextAlignmentOptions.Left);
            hintLabel.color = new Color(0.80f, 0.76f, 0.68f, 1f);
            var hintRect = (RectTransform)hintLabel.transform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0f, 1f);
            hintRect.pivot = new Vector2(0f, 1f);
            hintRect.anchoredPosition = new Vector2(Pad, -Pad - 44f);
            hintRect.sizeDelta = new Vector2(width - Pad * 2f, 32f);

            // 卡面一排：左对齐、垂直居中放在标题与底栏之间。
            cardsRoot = new GameObject("Cards", typeof(RectTransform)).GetComponent<RectTransform>();
            cardsRoot.SetParent(window, false);
            cardsRoot.anchorMin = new Vector2(0f, 0f);
            cardsRoot.anchorMax = new Vector2(1f, 1f);
            cardsRoot.offsetMin = new Vector2(Pad, BottomBar);
            cardsRoot.offsetMax = new Vector2(-Pad, -Pad - 84f);

            for (int i = 0; i < 7; i++)
            {
                Image image = CreateImage($"Card{i + 1}", cardsRoot, EmptyCardColor);
                image.preserveAspect = false;
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(CardWidth, CardHeight);
                rect.anchoredPosition = new Vector2(i * (CardWidth + CardGap), 0f);

                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                UseHoverFeedbackOnly(button);
                int index = i;
                button.onClick.AddListener(() => onCardClicked?.Invoke(index));
                cardButtons.Add(button);
                cardImages.Add(image);

                // 选中时的金框 / 压暗层（都盖在卡面上、不挡点击）。
                Image frame = CreateImage("Frame", rect, SelectedColor);
                frame.raycastTarget = false;
                frame.rectTransform.anchorMin = Vector2.zero;
                frame.rectTransform.anchorMax = Vector2.one;
                frame.rectTransform.offsetMin = new Vector2(-3f, -3f);
                frame.rectTransform.offsetMax = new Vector2(3f, 3f);
                var frameOutline = frame.gameObject.AddComponent<Outline>();
                frameOutline.effectDistance = new Vector2(2f, 2f);
                cardFrames.Add(frame);

                Image dimCard = CreateImage("Dim", rect, new Color(0f, 0f, 0f, 0.45f));
                dimCard.raycastTarget = false;
                dimCard.rectTransform.anchorMin = Vector2.zero;
                dimCard.rectTransform.anchorMax = Vector2.one;
                dimCard.rectTransform.offsetMin = Vector2.zero;
                dimCard.rectTransform.offsetMax = Vector2.zero;
                cardDims.Add(dimCard);
            }

            confirmButton = CreateButton("Confirm", window, "确认移除（选满 2 张）", new Vector2(260f, 48f));
            var confirmRect = (RectTransform)confirmButton.transform;
            confirmRect.anchorMin = confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.anchoredPosition = new Vector2(80f, Pad);
            confirmButton.onClick.AddListener(() => onConfirmed?.Invoke());

            resetButton = CreateButton("Reset", window, "重新选择", new Vector2(160f, 48f));
            var resetRect = (RectTransform)resetButton.transform;
            resetRect.anchorMin = resetRect.anchorMax = new Vector2(0.5f, 0f);
            resetRect.pivot = new Vector2(0.5f, 0f);
            resetRect.anchoredPosition = new Vector2(-140f, Pad);
            resetButton.onClick.AddListener(() => onReset?.Invoke());

            Hide();
        }

        /// <summary>刷新并显示弹窗。选满 2 张时「确认移除」才可点。</summary>
        public void Show(string title, string hint, IReadOnlyList<int> cardIds, IReadOnlyList<Sprite> sprites,
            IReadOnlyList<int> picked, Action<int> cardClicked, Action confirmed, Action reset)
        {
            if (rootRect == null) return;

            onCardClicked = cardClicked;
            onConfirmed = confirmed;
            onReset = reset;

            rootRect.gameObject.SetActive(true);
            rootRect.SetAsLastSibling();
            titleLabel.text = title;
            hintLabel.text = hint;

            for (int i = 0; i < cardButtons.Count; i++)
            {
                bool used = cardIds != null && i < cardIds.Count;
                cardButtons[i].gameObject.SetActive(used);
                if (!used) continue;

                cardImages[i].sprite = sprites != null && i < sprites.Count ? sprites[i] : null;
                cardImages[i].color = cardImages[i].sprite != null ? Color.white : EmptyCardColor;

                bool selected = picked != null && picked.Contains(cardIds[i]);
                cardFrames[i].gameObject.SetActive(selected);
                cardDims[i].gameObject.SetActive(selected);
            }

            bool ready = picked != null && picked.Count >= 2;
            confirmButton.interactable = ready;
            resetButton.interactable = picked != null && picked.Count > 0;
        }

        public void Hide()
        {
            if (rootRect != null) rootRect.gameObject.SetActive(false);
        }

        /// <summary>
        /// 只保留「悬停 / 按下」的反馈。
        ///
        /// 默认 ColorTint 还有两档会自己变色：
        /// - 选中色：按钮从不可点变回可点时被 EventSystem 选中，会常亮；改为与正常色一致。
        /// - 不可点色：默认是浅灰半透明，看起来像「突然亮了」；同样对齐正常色，
        ///   能不能点是靠逻辑拦的，不靠颜色。
        /// </summary>
        private static void UseHoverFeedbackOnly(Button button)
        {
            ColorBlock colors = button.colors;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = colors.normalColor;
            button.colors = colors;

            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
        }

        // —— 运行时搭控件的小工具（配色与 ConfirmWindow 一致） ——

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text CreateText(string name, Transform parent, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = TextColor;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private Button CreateButton(string name, Transform parent, string label, Vector2 size)
        {
            Image image = CreateImage(name, parent, ButtonColor);
            image.rectTransform.sizeDelta = size;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            UseHoverFeedbackOnly(button);

            TMP_Text text = CreateText("Label", image.transform, 20f, TextAlignmentOptions.Center);
            text.text = label;
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return button;
        }
    }
}
