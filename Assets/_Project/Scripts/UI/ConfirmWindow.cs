using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 带窗口外框的确认 / 选项弹窗：深色半透明窗口 + 米色文字 + 棕色按钮，配色与操作区那些面板
    /// （设施供应区 / 城市面板 / 图鉴）一致。与 <see cref="ChoicePopup"/> 的区别是它是一整块窗口、
    /// 选项文字再长也不会溢出按钮，并且可以最小化成操作区上方的小按钮，让玩家一边看地图一边决定。
    ///
    /// 选项少且短时排成右下角一行；选项多或文字长时改成竖排列表（角色牌策略 / 计谋、企业特效都用这一种）。
    /// 整个窗口在运行时搭出来（同 <see cref="DeveloperPanel"/>），场景里不用摆任何东西。
    /// </summary>
    public sealed class ConfirmWindow : MonoBehaviour
    {
        // 与设施供应区 / 城市面板 / 图鉴 / 聊天 / 行动记录同一套配色。
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color ContentColor = new Color(0.12f, 0.10f, 0.08f, 0.85f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color ButtonColor = new Color(0.45f, 0.36f, 0.28f, 1f);

        private const float WindowWidth = 660f;
        private const float Pad = 24f;
        private const float TitleHeight = 40f;
        private const float ButtonHeight = 46f;
        private const float ButtonGap = 10f;
        private const float MinWindowHeight = 240f;
        private const float MaxWindowHeight = 820f;
        private const float DefaultWindowHeight = 300f;

        [SerializeField] private TMP_FontAsset font;

        private RectTransform rootRect;
        private Image dim;
        private RectTransform window;
        private TMP_Text titleLabel;
        private TMP_Text promptLabel;
        private RectTransform listRoot;
        private Button minimizeButton;
        private Button collapsedBar;
        private TMP_Text collapsedLabel;

        /// <summary>底部这一排 / 这一列选项按钮（每次 Show 按选项数量重建）。</summary>
        private readonly List<Button> optionButtons = new List<Button>();
        private Button cancelButton;

        private Action<int> onSelected;
        private IReadOnlyList<string> currentOptions;
        private bool currentAllowCancel = true;

        /// <summary>弹窗是不是还开着（展开或最小化都算）。</summary>
        public bool IsVisible => rootRect != null && rootRect.gameObject.activeSelf;

        /// <summary>当前是不是最小化状态。</summary>
        public bool IsMinimized => window != null && !window.gameObject.activeSelf && IsVisible;

        /// <summary>在画布上新建一个确认窗（运行时搭建，同一画布只建一次即可）。</summary>
        public static ConfirmWindow Create(Transform canvas, TMP_FontAsset font)
        {
            GameObject host = new GameObject("ConfirmWindow", typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var view = host.AddComponent<ConfirmWindow>();
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

            dim = CreateImage("Dim", rootRect, DimColor);
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);
            // 点遮罩不算取消：位置要靠确认或显式取消，误点不该丢掉已选的点。
            dim.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            Image windowImage = CreateImage("Window", rootRect, ContentColor);
            window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(WindowWidth, DefaultWindowHeight);

            titleLabel = CreateText("Title", window, "确认", 24f, TextAlignmentOptions.Left);
            titleLabel.rectTransform.anchorMin = titleLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
            titleLabel.rectTransform.pivot = new Vector2(0f, 1f);
            titleLabel.rectTransform.anchoredPosition = new Vector2(Pad, -14f);
            titleLabel.rectTransform.sizeDelta = new Vector2(WindowWidth - Pad * 2f - 110f, TitleHeight);
            titleLabel.textWrappingMode = TextWrappingModes.Normal;

            minimizeButton = CreateButton("Minimize", window, "最小化", 17f, ButtonColor);
            RectTransform minRect = minimizeButton.GetComponent<RectTransform>();
            minRect.anchorMin = minRect.anchorMax = new Vector2(1f, 1f);
            minRect.pivot = new Vector2(1f, 1f);
            minRect.anchoredPosition = new Vector2(-16f, -14f);
            minRect.sizeDelta = new Vector2(92f, 32f);
            minimizeButton.onClick.AddListener(Minimize);

            promptLabel = CreateText("Prompt", window, string.Empty, 20f, TextAlignmentOptions.TopLeft);
            promptLabel.textWrappingMode = TextWrappingModes.Normal;

            listRoot = CreateRect("Options", window);

            collapsedBar = CreateButton("CollapsedBar", rootRect, "展开", 16f, ButtonColor);
            collapsedLabel = collapsedBar.GetComponentInChildren<TMP_Text>();
            if (collapsedLabel != null)
            {
                collapsedLabel.textWrappingMode = TextWrappingModes.Normal;
                collapsedLabel.fontSize = 15f;
            }
            collapsedBar.onClick.AddListener(Expand);
            collapsedBar.gameObject.SetActive(false);
            LayoutCollapsedBar(null);

            rootRect.gameObject.SetActive(false);
        }

        /// <summary>
        /// 让最小化后的小按钮贴在操作区顶边外侧（行动记录那一列的上面）。
        /// <paramref name="operationBar"/> 传操作区（OperationBar）的 RectTransform；传 null 时退回底部居中。
        /// </summary>
        public void SetCollapseAnchor(Transform operationBar)
        {
            if (collapsedBar == null) return;
            LayoutCollapsedBar(operationBar as RectTransform);
        }

        private void LayoutCollapsedBar(RectTransform operationBar)
        {
            if (collapsedBar == null) return;

            var barRect = (RectTransform)collapsedBar.transform;
            barRect.SetAsLastSibling();
            barRect.sizeDelta = new Vector2(170f, 44f);

            if (operationBar != null && operationBar.parent is RectTransform parent)
            {
                // 操作区锚在右下角：小按钮贴在它的顶边外侧、和左边那一列（行动记录）对齐。
                barRect.SetParent(parent, false);
                float left = operationBar.anchoredPosition.x - operationBar.rect.width * operationBar.pivot.x;
                float top = operationBar.anchoredPosition.y + operationBar.rect.height * (1f - operationBar.pivot.y);
                barRect.anchorMin = barRect.anchorMax = new Vector2(1f, 0f);
                barRect.pivot = new Vector2(0f, 0f);
                barRect.anchoredPosition = new Vector2(left + 12f, top + 10f);
                barRect.SetAsLastSibling();
                return;
            }

            // 找不到操作区（例如场景被改过）：退回屏幕底部居中，至少不会挡住操作区。
            barRect.SetParent(rootRect, false);
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 120f);
            barRect.SetAsLastSibling();
        }

        /// <summary>打开窗口并展开（单个确认按钮 + 取消）。</summary>
        public void Show(string title, string prompt, string confirmLabel, Action onConfirm, Action onCancel)
        {
            Show(title, prompt, new[] { string.IsNullOrEmpty(confirmLabel) ? "确定" : confirmLabel },
                choice =>
                {
                    if (choice == 0) onConfirm?.Invoke();
                    else onCancel?.Invoke();
                },
                allowCancel: true);
        }

        /// <summary>
        /// 打开窗口并展开。<paramref name="options"/> 从主到次排列（第 0 个是主按钮）；
        /// 回调参数是被点中的下标，取消 / 返回时为 -1。<paramref name="allowCancel"/> 为 false 时不给取消按钮。
        /// </summary>
        public void Show(string title, string prompt, IReadOnlyList<string> options,
            Action<int> onSelected, bool allowCancel = true)
        {
            if (rootRect == null) return;

            this.onSelected = onSelected;
            currentOptions = options;
            currentAllowCancel = allowCancel;

            if (titleLabel != null) titleLabel.text = title;
            if (promptLabel != null) promptLabel.text = prompt;

            RebuildOptions();

            if (collapsedLabel != null)
            {
                string brief = string.IsNullOrEmpty(title) ? "确认" : title;
                if (brief.Length > 10) brief = brief.Substring(0, 10) + "…";
                collapsedLabel.text = $"展开 · {brief}";
            }

            if (transform.parent != null) transform.SetAsLastSibling();

            // 玩家把它最小化去点地图（改停靠点 / 增删采集点）时只更新文字，不要自作主张再弹回来。
            bool stayMinimized = IsMinimized;
            rootRect.gameObject.SetActive(true);
            if (stayMinimized) Minimize();
            else Expand();

            // 展开之后再量正文高度：TMP 要先被启用才能算出准确行高。
            LayoutWindow();
        }

        /// <summary>选项多或文字长时改竖排；否则排成右下角一行。</summary>
        private static bool UseVerticalList(IReadOnlyList<string> options)
        {
            if (options == null) return false;
            if (options.Count > 2) return true;

            foreach (string option in options)
            {
                if (!string.IsNullOrEmpty(option) && option.Length > 12) return true;
            }
            return false;
        }

        private void RebuildOptions()
        {
            foreach (Button button in optionButtons)
            {
                if (button == null) continue;
                // 先关掉再销毁：Destroy 要到帧末才生效，留着会和新建的按钮重叠一帧。
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            optionButtons.Clear();
            cancelButton = null;

            if (currentOptions != null)
            {
                for (int i = 0; i < currentOptions.Count; i++)
                {
                    int index = i;
                    string label = string.IsNullOrEmpty(currentOptions[i]) ? "确定" : currentOptions[i];
                    Button button = CreateButton($"Option{i}", listRoot, label, 19f, ButtonColor);
                    button.onClick.AddListener(() =>
                    {
                        Action<int> action = onSelected;
                        Hide();
                        action?.Invoke(index);
                    });
                    optionButtons.Add(button);
                }
            }

            if (!currentAllowCancel) return;

            cancelButton = CreateButton("Cancel", window, "取消", 20f, ButtonColor);
            cancelButton.onClick.AddListener(() =>
            {
                Action<int> action = onSelected;
                Hide();
                action?.Invoke(-1);
            });
            optionButtons.Add(cancelButton);
        }

        /// <summary>窗口高度与各块位置按正文 / 选项实际长度重排。</summary>
        private void LayoutWindow()
        {
            if (window == null || promptLabel == null || listRoot == null) return;

            const float titleTop = 14f;
            const float titleGap = 12f;
            float promptTop = titleTop + TitleHeight + titleGap;
            float textWidth = WindowWidth - Pad * 2f;

            float promptHeight = 0f;
            if (!string.IsNullOrEmpty(promptLabel.text))
                promptHeight = promptLabel.GetPreferredValues(promptLabel.text, textWidth, 0f).y;
            promptHeight = Mathf.Max(0f, promptHeight);

            var promptRect = (RectTransform)promptLabel.transform;
            promptRect.anchorMin = new Vector2(0f, 1f);
            promptRect.anchorMax = new Vector2(1f, 1f);
            promptRect.pivot = new Vector2(0.5f, 1f);
            promptRect.sizeDelta = new Vector2(-Pad * 2f, promptHeight);
            promptRect.anchoredPosition = new Vector2(0f, -promptTop);

            float listTop = promptTop + promptHeight + 16f;
            bool vertical = UseVerticalList(currentOptions);
            float listHeight = 0f;

            if (vertical)
            {
                float y = 0f;
                for (int i = 0; i < optionButtons.Count; i++)
                {
                    Button button = optionButtons[i];
                    if (button == null || button == cancelButton) continue;

                    var rect = (RectTransform)button.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    rect.sizeDelta = new Vector2(textWidth, 0f);

                    // 按文字实际高度定这一项的高，长效果说明也能完整折行显示。
                    TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                    float itemHeight = ButtonHeight;
                    if (label != null)
                    {
                        label.alignment = TextAlignmentOptions.Left;
                        label.rectTransform.offsetMin = new Vector2(14f, 8f);
                        label.rectTransform.offsetMax = new Vector2(-14f, -8f);
                        label.textWrappingMode = TextWrappingModes.Normal;
                        itemHeight = Mathf.Max(ButtonHeight, label.GetPreferredValues(label.text, textWidth - 32f, 0f).y + 18f);
                    }

                    rect.sizeDelta = new Vector2(textWidth, Mathf.Round(itemHeight));
                    rect.anchoredPosition = new Vector2(0f, -y);
                    y += itemHeight + 8f;
                }

                listHeight = Mathf.Max(0f, y - 8f);
            }
            else
            {
                float x = 0f;
                for (int i = 0; i < optionButtons.Count; i++)
                {
                    Button button = optionButtons[i];
                    if (button == null || button == cancelButton) continue;

                    string label = currentOptions != null && i < currentOptions.Count ? currentOptions[i] : null;
                    float width = Mathf.Clamp((label != null ? label.Length : 2) * 24f + 36f, 150f, 320f);

                    var rect = (RectTransform)button.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    rect.sizeDelta = new Vector2(width, ButtonHeight);
                    rect.anchoredPosition = new Vector2(-x, 0f);
                    x += width + ButtonGap;
                }

                listHeight = ButtonHeight;
            }

            var listRect = (RectTransform)listRoot.transform;
            listRect.anchorMin = listRect.anchorMax = new Vector2(0f, 1f);
            listRect.pivot = new Vector2(0f, 1f);
            listRect.sizeDelta = new Vector2(textWidth, listHeight);
            listRect.anchoredPosition = new Vector2(Pad, -listTop);

            if (cancelButton != null)
            {
                var cancelRect = (RectTransform)cancelButton.transform;
                cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(1f, 0f);
                cancelRect.pivot = new Vector2(1f, 0f);
                cancelRect.anchoredPosition = new Vector2(-Pad, Pad);
                cancelRect.sizeDelta = new Vector2(130f, ButtonHeight);
            }

            float height = listTop + listHeight + (cancelButton != null ? 14f + ButtonHeight : 0f) + Pad;
            height = Mathf.Clamp(height, MinWindowHeight, MaxWindowHeight);
            window.sizeDelta = new Vector2(WindowWidth, Mathf.Round(height));
        }

        /// <summary>关闭窗口（展开、最小化两种状态都关掉）。</summary>
        public void Hide()
        {
            if (rootRect == null) return;

            onSelected = null;
            // 最小化后的小按钮挂在操作区那一层，不在 rootRect 底下，得单独收掉。
            if (collapsedBar != null) collapsedBar.gameObject.SetActive(false);
            rootRect.gameObject.SetActive(false);
        }

        /// <summary>最小化：收起窗口，只留操作区上方的小按钮；遮罩放开，地图可以照常缩放 / 拖动。</summary>
        public void Minimize()
        {
            if (!IsVisible || window == null) return;

            window.gameObject.SetActive(false);
            if (collapsedBar != null) collapsedBar.gameObject.SetActive(true);
            if (dim != null)
            {
                Color color = dim.color;
                color.a = 0f;
                dim.color = color;
                dim.raycastTarget = false;
            }
        }

        /// <summary>展开窗口并恢复遮罩（点最小化后的小按钮回到这里）。</summary>
        public void Expand()
        {
            if (!IsVisible) return;

            if (window != null) window.gameObject.SetActive(true);
            if (collapsedBar != null) collapsedBar.gameObject.SetActive(false);
            if (dim != null)
            {
                dim.color = DimColor;
                dim.raycastTarget = true;
            }

            // 最小化期间可能换过正文（例如又点了另一个资源点），展开时按最新正文重排。
            LayoutWindow();
        }

        // —— 运行时搭 UI 的小工具（与 DeveloperPanel 同款） ——

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text CreateText(string name, RectTransform parent, string text, float size,
            TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = TextColor;
            label.raycastTarget = false;
            return label;
        }

        private Button CreateButton(string name, RectTransform parent, string label, float size, Color color)
        {
            Image background = CreateImage(name, parent, color);
            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            TMP_Text text = CreateText("Label", background.rectTransform, label, size,
                TextAlignmentOptions.Center);
            text.color = TextColor;
            Stretch(text.rectTransform);
            UiButtonTint.Ensure(button);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
