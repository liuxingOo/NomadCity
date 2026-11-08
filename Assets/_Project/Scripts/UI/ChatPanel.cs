using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 操作区「聊天」按钮的弹窗：房间里的人打字互相发消息。
    ///
    /// 尺寸与位置跟设施供应区 / 城市样式一致（680×480 居中），并加入
    /// <see cref="CenteredPanelGroup"/>：打开它会把另外两块同位置面板收起来，避免叠在一起。
    ///
    /// 本组件只管界面与会话内的收发显示 —— 消息怎么送到别人机器由上层接线
    /// （<see cref="MessageSubmitted"/> 发出，<see cref="AppendMessage"/> 收进）。
    /// 弹窗内容在运行时搭出来，场景里只需要把组件挂在「聊天」按钮上并指定字体。
    /// </summary>
    public sealed class ChatPanel : MonoBehaviour, ICenteredPanel
    {
        /// <summary>单条消息最长字数（Relay 单包上限 1024 字节，中文 3 字节/字）。</summary>
        private const int MaxMessageLength = 120;

        /// <summary>最多保留多少条历史，防止长时间聊天把文本撑爆。</summary>
        private const int MaxKeptLines = 200;

        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.78f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);

        [Tooltip("操作区里的「聊天」按钮（本组件通常就挂在它身上）")]
        [SerializeField] private Button toggleButton;

        [Tooltip("聊天文字用的中文字体（留空会用 TMP 默认字体，中文可能缺字）")]
        [SerializeField] private TMP_FontAsset font;

        private GameObject root;
        private TMP_InputField input;
        private TMP_Text logText;
        private ScrollRect scroll;

        private readonly List<string> lines = new List<string>();

        /// <summary>玩家按下「发送」或回车：内容已去掉首尾空白并限长。</summary>
        public event Action<string> MessageSubmitted;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            CenteredPanelGroup.Register(this);
            Build();

            if (toggleButton != null)
            {
                UiButtonTint.Ensure(toggleButton);
                toggleButton.onClick.AddListener(Toggle);
            }

            SetOpen(false);
        }

        private void OnDestroy()
        {
            CenteredPanelGroup.Unregister(this);
        }

        /// <summary>操作区按钮：开 / 关弹窗。</summary>
        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            if (open) CenteredPanelGroup.BeforeOpen(this);
            if (root != null) root.SetActive(open);
            UiButtonTint.SetLit(toggleButton, open);

            if (open && input != null)
            {
                input.text = string.Empty;
                input.ActivateInputField();
                ScrollToBottom();
            }
        }

        public void ForceClose() => SetOpen(false);

        /// <summary>往聊天记录里追加一条并滚到底部。<paramref name="from"/> 是发送者昵称。</summary>
        public void AppendMessage(string from, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            string line = string.IsNullOrEmpty(from) ? text : $"{from}：{text}";
            lines.Add(line);
            if (lines.Count > MaxKeptLines) lines.RemoveRange(0, lines.Count - MaxKeptLines);

            if (logText == null) return;
            logText.text = string.Join("\n", lines);
            LayoutRebuilder.ForceRebuildLayoutImmediate(logText.rectTransform);
            ScrollToBottom();
        }

        // —— 运行时搭 UI ——

        private void Build()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[聊天] 找不到 Canvas，聊天弹窗无法显示。", this);
                return;
            }

            root = new GameObject("ChatPanel", typeof(RectTransform));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvas.transform, false);
            Stretch(rootRect);
            rootRect.SetAsLastSibling(); // 压在其他面板之上

            Image dim = CreateImage("Dim", rootRect, new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);

            // 遮罩本身也能点：点窗口外面任意处收起（否则它会挡住操作区的「聊天」按钮，再点关不掉）。
            Button dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.targetGraphic = dim;
            dimButton.onClick.AddListener(() => SetOpen(false));

            Image window = CreateImage("Window", rootRect, WindowColor);
            RectTransform windowRect = window.rectTransform;
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;
            windowRect.sizeDelta = new Vector2(680f, 480f);

            TMP_Text title = CreateText("Title", windowRect, "聊天", 26f, TextAlignmentOptions.Center);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -12f);
            titleRect.sizeDelta = new Vector2(-140f, 40f);

            Button close = CreateButton("Close", windowRect, "关闭", 20f, out _);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-16f, -14f);
            closeRect.sizeDelta = new Vector2(72f, 32f);
            close.onClick.AddListener(() => SetOpen(false));

            BuildLog(windowRect);
            BuildInputRow(windowRect);
        }

        private void BuildLog(RectTransform windowRect)
        {
            Image log = CreateImage("Log", windowRect, new Color(0.12f, 0.10f, 0.08f, 0.85f));
            RectTransform logRect = log.rectTransform;
            logRect.anchorMin = Vector2.zero;
            logRect.anchorMax = Vector2.one;
            logRect.offsetMin = new Vector2(20f, 66f);
            logRect.offsetMax = new Vector2(-20f, -56f);

            scroll = log.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            RectTransform viewport = CreateRect("Viewport", logRect);
            Stretch(viewport);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            logText = CreateText("LogText", viewport, string.Empty, 20f, TextAlignmentOptions.TopLeft);
            RectTransform textRect = logText.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -6f);
            textRect.sizeDelta = new Vector2(-24f, 0f);

            ContentSizeFitter fitter = logText.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = textRect;
        }

        private void BuildInputRow(RectTransform windowRect)
        {
            Image inputBg = CreateImage("Input", windowRect, new Color(0.16f, 0.13f, 0.10f, 0.95f));
            RectTransform inputRect = inputBg.rectTransform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.offsetMin = new Vector2(20f, 16f);
            inputRect.offsetMax = new Vector2(-116f, 56f);

            RectTransform textArea = CreateRect("Text Area", inputRect);
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(12f, 6f);
            textArea.offsetMax = new Vector2(-12f, -6f);
            textArea.gameObject.AddComponent<RectMask2D>();

            TMP_Text text = CreateText("Text", textArea, string.Empty, 20f, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);

            TMP_Text placeholder = CreateText("Placeholder", textArea, "输入消息…", 20f, TextAlignmentOptions.Left);
            Stretch(placeholder.rectTransform);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            input = inputBg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = MaxMessageLength;
            input.onSubmit.AddListener(_ => Submit());

            Button send = CreateButton("Send", windowRect, "发送", 20f, out _);
            RectTransform sendRect = send.GetComponent<RectTransform>();
            sendRect.anchorMin = new Vector2(1f, 0f);
            sendRect.anchorMax = new Vector2(1f, 0f);
            sendRect.pivot = new Vector2(1f, 0f);
            sendRect.anchoredPosition = new Vector2(-20f, 16f);
            sendRect.sizeDelta = new Vector2(84f, 40f);
            send.onClick.AddListener(Submit);
        }

        private void Submit()
        {
            if (input == null) return;

            string text = input.text != null ? input.text.Trim() : string.Empty;
            if (text.Length == 0) return;
            if (text.Length > MaxMessageLength) text = text.Substring(0, MaxMessageLength);

            input.text = string.Empty;
            input.ActivateInputField();
            MessageSubmitted?.Invoke(text);
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        }

        // —— 小工具 ——

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

        private TMP_Text CreateText(string name, RectTransform parent, string content, float size,
            TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = TextColor;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(string name, RectTransform parent, string label, float size, out TMP_Text labelText)
        {
            Image background = CreateImage(name, parent, new Color(0.45f, 0.36f, 0.28f, 1f));
            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            labelText = CreateText("Label", background.rectTransform, label, size, TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform);

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
