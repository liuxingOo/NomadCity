using System;
using NomadCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.App
{
    /// <summary>
    /// 主菜单弹窗（开局设置 / 加入游戏）。四个入口——单机游戏、创建游戏、开发者模式、加入游戏——
    /// 都走这一套窗口：同样的尺寸、同样的位置、同样的标题与按钮规格，只是内容行不同。
    ///
    /// 为什么在运行时搭而不是用场景里的 StartPanel / JoinPanel：那两块是手工摆的，
    /// 字号、锚点、间距都已经歪了，改场景 YAML 既容易漏也难对齐。这里统一由代码生成，
    /// 以后要调尺寸只改这一个文件的常量，两个弹窗会一起变。
    /// </summary>
    public sealed class MainMenuDialog : MonoBehaviour
    {
        /// <summary>弹窗种类：开局设置（人数 / 扩展包 / 昵称）或加入游戏（房间码 / 昵称）。</summary>
        public enum Kind
        {
            Start = 0,
            Join = 1,
        }

        // 与游戏内操作区的弹窗同一套配色：深棕底、米色字、橙色主按钮。
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color WindowColor = new Color(0.145f, 0.118f, 0.086f, 0.97f);
        private static readonly Color FieldColor = new Color(0.09f, 0.075f, 0.058f, 0.95f);
        private static readonly Color BorderColor = new Color(0.42f, 0.34f, 0.25f, 0.9f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color MutedColor = new Color(0.72f, 0.67f, 0.58f, 1f);
        private static readonly Color PlaceholderColor = new Color(0.55f, 0.51f, 0.44f, 0.85f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.55f, 0.45f, 1f);
        private static readonly Color AccentColor = new Color(0.62f, 0.42f, 0.24f, 1f);
        private static readonly Color SecondaryColor = new Color(0.30f, 0.25f, 0.20f, 1f);

        private const float WindowWidth = 900f;
        private const float WindowHeight = 580f;
        private const float PadX = 48f;
        private const float ContentWidth = WindowWidth - PadX * 2f;
        private const float RowHeight = 56f;
        private const float ButtonHeight = 58f;
        private const float ButtonBottom = 36f;

        // 内容行统一从标题分隔线下面 30 像素开始排，两个弹窗共用同一套基准线。
        private const float FirstRowTop = -156f;

        [SerializeField] private TMP_FontAsset font;

        private Kind kind;
        private RectTransform rootRect;
        private RectTransform window;
        private TMP_Text subtitleLabel;
        private TMP_Text hintLabel;
        private TMP_Text errorLabel;
        private TMP_Text confirmLabel;

        // —— 开局设置 ——
        private GameObject hostRow;
        private Slider playerCountSlider;
        private TMP_Text playerCountValue;
        private Toggle enterpriseToggle;
        private Toggle infectedToggle;
        private TMP_InputField hostNicknameInput;

        // —— 加入游戏 ——
        private TMP_InputField joinCodeInput;
        private TMP_InputField clientNicknameInput;

        /// <summary>人数滑条变化（已取整成 2–4）。</summary>
        public Action<int> PlayerCountChanged;

        /// <summary>企业扩展包勾选变化。</summary>
        public Action<bool> EnterpriseChanged;

        /// <summary>感染者扩展勾选变化。</summary>
        public Action<bool> InfectedChanged;

        /// <summary>点了「返回」。</summary>
        public Action BackRequested;

        /// <summary>点了「进入游戏 / 创建联机房间 / 加入房间」。</summary>
        public Action ConfirmRequested;

        public bool IsVisible => rootRect != null && rootRect.gameObject.activeSelf;

        public int PlayerCount =>
            playerCountSlider != null ? Mathf.RoundToInt(playerCountSlider.value) : 4;

        public bool EnterpriseExpansion => enterpriseToggle != null && enterpriseToggle.isOn;

        public bool InfectedExpansion => infectedToggle != null && infectedToggle.isOn;

        public string HostNickname => hostNicknameInput != null ? hostNicknameInput.text : string.Empty;

        public string JoinCode => joinCodeInput != null ? joinCodeInput.text : string.Empty;

        public string ClientNickname => clientNicknameInput != null ? clientNicknameInput.text : string.Empty;

        /// <summary>在画布上建一个弹窗。同一个画布上可以先建开局设置、再建加入游戏，两者尺寸位置完全一致。</summary>
        public static MainMenuDialog Create(Transform canvas, TMP_FontAsset font, Kind kind)
        {
            GameObject host = new GameObject("MainMenuDialog_" + kind, typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var dialog = host.AddComponent<MainMenuDialog>();
            dialog.font = font;
            dialog.kind = kind;
            dialog.Build();
            return dialog;
        }

        // —— 对外显示 / 隐藏 ——

        /// <summary>显示开局设置。</summary>
        public void ShowStart(string subtitle, string hint, string confirmText, bool hostMode,
            int players, bool enterprise, bool infected, string nickname)
        {
            if (subtitleLabel != null) subtitleLabel.text = subtitle;
            if (hintLabel != null) hintLabel.text = hint;
            if (confirmLabel != null) confirmLabel.text = confirmText;
            if (hostRow != null) hostRow.SetActive(hostMode);
            LayoutFooter(hostMode);

            int count = Mathf.Clamp(players, 2, 4);
            if (playerCountSlider != null) playerCountSlider.SetValueWithoutNotify(count);
            if (playerCountValue != null) playerCountValue.text = count + " 人";
            if (enterpriseToggle != null) enterpriseToggle.SetIsOnWithoutNotify(enterprise);
            if (infectedToggle != null) infectedToggle.SetIsOnWithoutNotify(infected);
            // 昵称保留上次输入：只有在明确传入非空值时才覆盖，避免每次打开都把没保存的名字清空。
            if (hostNicknameInput != null && hostMode && !string.IsNullOrEmpty(nickname))
                hostNicknameInput.SetTextWithoutNotify(nickname);

            SetError(string.Empty);
            Show();
        }

        /// <summary>显示加入游戏。房间码留空时把光标放到房间码输入框。</summary>
        public void ShowJoin(string subtitle, string hint, string roomCode, string nickname)
        {
            if (subtitleLabel != null) subtitleLabel.text = subtitle;
            if (hintLabel != null) hintLabel.text = hint;

            string code = roomCode ?? string.Empty;
            if (joinCodeInput != null && code.Length > 0) joinCodeInput.SetTextWithoutNotify(code);
            if (clientNicknameInput != null && !string.IsNullOrEmpty(nickname))
                clientNicknameInput.SetTextWithoutNotify(nickname);

            SetError(string.Empty);
            Show();

            if (code.Length == 0) FocusJoinCode();
        }

        public void Hide()
        {
            if (rootRect == null) return;
            rootRect.gameObject.SetActive(false);
        }

        /// <summary>在状态行位置显示一条红色错误提示（空字符串则清空）。</summary>
        public void SetError(string message)
        {
            if (errorLabel == null) return;
            errorLabel.text = message ?? string.Empty;
        }

        public void FocusHostNickname()
        {
            Focus(hostNicknameInput);
        }

        public void FocusJoinCode()
        {
            Focus(joinCodeInput);
        }

        private static void Focus(TMP_InputField input)
        {
            if (input == null) return;
            input.Select();
            input.ActivateInputField();
        }

        private void Show()
        {
            if (rootRect == null) return;
            transform.SetAsLastSibling();
            rootRect.gameObject.SetActive(true);
        }

        // —— 搭窗口 ——

        private void Build()
        {
            rootRect = (RectTransform)transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image dim = CreateImage("Dim", rootRect, DimColor);
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);

            Image windowImage = CreateImage("Window", rootRect, WindowColor);
            windowImage.raycastTarget = true;
            window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            Outline border = windowImage.gameObject.AddComponent<Outline>();
            border.effectColor = BorderColor;
            border.effectDistance = new Vector2(2f, -2f);

            TMP_Text title = CreateText("Title", window,
                kind == Kind.Start ? "开局设置" : "加入游戏", 38f, TextAlignmentOptions.Left);
            Place((RectTransform)title.transform, PadX, -24f, 600f, 50f);

            subtitleLabel = CreateText("Subtitle", window, string.Empty, 19f, TextAlignmentOptions.Left);
            subtitleLabel.color = MutedColor;
            Place((RectTransform)subtitleLabel.transform, PadX, -82f, ContentWidth, 28f);

            Image divider = CreateImage("Divider", window, BorderColor);
            divider.raycastTarget = false;
            Place(divider.rectTransform, PadX, -122f, ContentWidth, 2f);

            if (kind == Kind.Start) BuildStartContent();
            else BuildJoinContent();

            Button back = CreateButton("Back", window, "返回", SecondaryColor, 180f, ButtonHeight);
            RectTransform backRect = (RectTransform)back.transform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0f);
            backRect.pivot = new Vector2(0f, 0f);
            backRect.anchoredPosition = new Vector2(PadX, ButtonBottom);
            back.onClick.AddListener(() => BackRequested?.Invoke());

            Button confirm = CreateButton("Confirm", window,
                kind == Kind.Start ? "进入游戏" : "加入房间", AccentColor, 280f, ButtonHeight);
            RectTransform confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(1f, 0f);
            confirmRect.anchoredPosition = new Vector2(-PadX, ButtonBottom);
            confirm.onClick.AddListener(() => ConfirmRequested?.Invoke());
            confirmLabel = confirm.GetComponentInChildren<TMP_Text>();

            errorLabel = CreateText("Error", window, string.Empty, 18f, TextAlignmentOptions.Left);
            errorLabel.color = ErrorColor;
            Place((RectTransform)errorLabel.transform, PadX, kind == Kind.Join ? -356f : -422f,
                ContentWidth, 28f);

            rootRect.gameObject.SetActive(false);
        }

        private void BuildStartContent()
        {
            TMP_Text playersLabel = CreateText("PlayersLabel", window, "玩家人数", 24f,
                TextAlignmentOptions.Left);
            Place((RectTransform)playersLabel.transform, PadX, FirstRowTop, 150f, RowHeight);

            playerCountSlider = CreateSlider(window, 2f, 4f, 4);
            Place((RectTransform)playerCountSlider.transform, PadX + 150f, FirstRowTop - 12f,
                ContentWidth - 150f - 120f, 32f);
            playerCountSlider.onValueChanged.AddListener(value =>
            {
                int count = Mathf.RoundToInt(value);
                if (playerCountValue != null) playerCountValue.text = count + " 人";
                PlayerCountChanged?.Invoke(count);
            });

            playerCountValue = CreateText("PlayersValue", window, "4 人", 24f, TextAlignmentOptions.Right);
            Place((RectTransform)playerCountValue.transform, WindowWidth - PadX - 110f, FirstRowTop,
                110f, RowHeight);

            // —— 扩展包 ——
            float expansionTop = FirstRowTop - 76f;
            TMP_Text expansionLabel = CreateText("ExpansionLabel", window, "扩展包", 24f,
                TextAlignmentOptions.Left);
            Place((RectTransform)expansionLabel.transform, PadX, expansionTop, 150f, 44f);

            enterpriseToggle = CreateToggle(window, "企业扩展包", false);
            Place((RectTransform)enterpriseToggle.transform, PadX + 150f, expansionTop, 250f, 44f);
            enterpriseToggle.onValueChanged.AddListener(value => EnterpriseChanged?.Invoke(value));

            infectedToggle = CreateToggle(window, "感染者扩展（暂未开放）", false);
            Place((RectTransform)infectedToggle.transform, PadX + 440f, expansionTop, 340f, 44f);
            infectedToggle.onValueChanged.AddListener(value => InfectedChanged?.Invoke(value));
            // 感染者扩展还没做：勾选框灰掉，避免开出一局用不了的模式。
            infectedToggle.interactable = false;
            TMP_Text infectedLabel = infectedToggle.GetComponentInChildren<TMP_Text>();
            if (infectedLabel != null) infectedLabel.color = MutedColor;

            // —— 房主昵称（只有「创建游戏」才显示）——
            hostRow = CreateRect("HostRow", window).gameObject;
            var hostRect = (RectTransform)hostRow.transform;
            Place(hostRect, PadX, expansionTop - 78f, ContentWidth, RowHeight);

            TMP_Text nicknameLabel = CreateText("NicknameLabel", hostRect, "房主昵称", 24f,
                TextAlignmentOptions.Left);
            Place((RectTransform)nicknameLabel.transform, 0f, 0f, 150f, RowHeight);

            hostNicknameInput = CreateInput(hostRect, "请输入昵称（最多 12 字）", 12,
                TMP_InputField.ContentType.Standard);
            Place((RectTransform)hostNicknameInput.transform, 150f, 0f, ContentWidth - 150f, RowHeight);

            hintLabel = CreateText("Hint", window, string.Empty, 18f, TextAlignmentOptions.Left);
            hintLabel.color = MutedColor;
            LayoutFooter(true);
        }

        private void BuildJoinContent()
        {
            TMP_Text codeLabel = CreateText("CodeLabel", window, "房间码", 24f, TextAlignmentOptions.Left);
            Place((RectTransform)codeLabel.transform, PadX, FirstRowTop, 150f, RowHeight);

            joinCodeInput = CreateInput(window, "6 位房间码", 6, TMP_InputField.ContentType.Alphanumeric);
            Place((RectTransform)joinCodeInput.transform, PadX + 150f, FirstRowTop,
                ContentWidth - 150f, RowHeight);

            float nicknameTop = FirstRowTop - 82f;
            TMP_Text nicknameLabel = CreateText("NicknameLabel", window, "昵称", 24f,
                TextAlignmentOptions.Left);
            Place((RectTransform)nicknameLabel.transform, PadX, nicknameTop, 150f, RowHeight);

            clientNicknameInput = CreateInput(window, "请输入昵称（最多 12 字）", 12,
                TMP_InputField.ContentType.Standard);
            Place((RectTransform)clientNicknameInput.transform, PadX + 150f, nicknameTop,
                ContentWidth - 150f, RowHeight);

            hintLabel = CreateText("Hint", window, string.Empty, 18f, TextAlignmentOptions.Left);
            hintLabel.color = MutedColor;
            Place((RectTransform)hintLabel.transform, PadX, -316f, ContentWidth, 32f);
        }

        /// <summary>
        /// 说明文字与错误提示按「有没有房主昵称行」落位：两种状态下都贴着最后一行内容下方，
        /// 不至于单机时中间空一大块。
        /// </summary>
        private void LayoutFooter(bool hostMode)
        {
            if (hintLabel == null) return;

            float hintTop = hostMode ? FirstRowTop - 218f : FirstRowTop - 140f;
            Place((RectTransform)hintLabel.transform, PadX, hintTop, ContentWidth, 32f);

            if (errorLabel != null)
                Place((RectTransform)errorLabel.transform, PadX, hintTop - 40f, ContentWidth, 32f);
        }

        // —— 运行时搭 UI 的小工具（与 ConfirmWindow 同款）——

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
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private Button CreateButton(string name, RectTransform parent, string label, Color color,
            float width, float height)
        {
            Image background = CreateImage(name, parent, color);
            RectTransform rect = background.rectTransform;
            rect.sizeDelta = new Vector2(width, height);

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            UiButtonTint.Ensure(button);

            TMP_Text text = CreateText("Label", rect, label, 24f, TextAlignmentOptions.Center);
            text.textWrappingMode = TextWrappingModes.Normal;
            Stretch(text.rectTransform);

            return button;
        }

        private Toggle CreateToggle(RectTransform parent, string label, bool isOn)
        {
            RectTransform rect = CreateRect("Toggle", parent);
            Toggle toggle = rect.gameObject.AddComponent<Toggle>();

            Image box = CreateImage("Box", rect, FieldColor);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            box.rectTransform.pivot = new Vector2(0f, 0.5f);
            box.rectTransform.anchoredPosition = Vector2.zero;
            box.rectTransform.sizeDelta = new Vector2(28f, 28f);

            Image check = CreateImage("Check", box.rectTransform, AccentColor);
            check.raycastTarget = false;
            check.rectTransform.anchorMin = Vector2.zero;
            check.rectTransform.anchorMax = Vector2.one;
            check.rectTransform.offsetMin = new Vector2(6f, 6f);
            check.rectTransform.offsetMax = new Vector2(-6f, -6f);

            TMP_Text text = CreateText("Label", rect, label, 22f, TextAlignmentOptions.Left);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(38f, 0f);
            text.rectTransform.offsetMax = Vector2.zero;

            toggle.targetGraphic = box;
            toggle.graphic = check;
            // graphic 是在 OnEnable 之后才挂上去的，Toggle 自己不会再刷一次初始状态，
            // 这里手动把勾选图按 isOn 显示，否则关闭状态也会显示成「已勾选」。
            check.canvasRenderer.SetAlpha(isOn ? 1f : 0f);
            toggle.isOn = isOn;
            toggle.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.85f, 0.82f, 0.76f, 1f),
                pressedColor = new Color(0.72f, 0.68f, 0.60f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
            return toggle;
        }

        private Slider CreateSlider(RectTransform parent, float min, float max, float value)
        {
            RectTransform rect = CreateRect("Slider", parent);
            Slider slider = rect.gameObject.AddComponent<Slider>();

            Image background = CreateImage("Background", rect, FieldColor);
            background.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            background.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            background.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            background.rectTransform.anchoredPosition = Vector2.zero;
            background.rectTransform.sizeDelta = new Vector2(0f, 12f);

            RectTransform fillArea = CreateRect("Fill Area", rect);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.pivot = new Vector2(0.5f, 0.5f);
            fillArea.anchoredPosition = Vector2.zero;
            fillArea.sizeDelta = new Vector2(-26f, 12f);

            Image fill = CreateImage("Fill", fillArea, AccentColor);
            fill.raycastTarget = false;
            Stretch(fill.rectTransform);

            RectTransform handleArea = CreateRect("Handle Slide Area", rect);
            handleArea.anchorMin = new Vector2(0f, 0f);
            handleArea.anchorMax = new Vector2(1f, 1f);
            handleArea.offsetMin = new Vector2(13f, 0f);
            handleArea.offsetMax = new Vector2(-13f, 0f);

            Image handle = CreateImage("Handle", handleArea, TextColor);
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            handle.rectTransform.sizeDelta = new Vector2(24f, 32f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(value);
            return slider;
        }

        private TMP_InputField CreateInput(RectTransform parent, string placeholder, int characterLimit,
            TMP_InputField.ContentType contentType)
        {
            Image background = CreateImage("Input", parent, FieldColor);
            // 先停用再装配：TMP_InputField 的 OnEnable 会去访问 textComponent / placeholder，
            // 组件刚加上时它们还没赋值。装配完再启用，OnEnable 才能拿到完整引用。
            GameObject host = background.gameObject;
            host.SetActive(false);

            TMP_InputField field = host.AddComponent<TMP_InputField>();
            field.targetGraphic = background;

            RectTransform area = CreateRect("Text Area", background.rectTransform);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(14f, 8f);
            area.offsetMax = new Vector2(-14f, -8f);
            area.gameObject.AddComponent<RectMask2D>();

            TMP_Text text = CreateText("Text", area, string.Empty, 22f, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);

            TMP_Text hint = CreateText("Placeholder", area, placeholder, 22f, TextAlignmentOptions.Left);
            hint.color = PlaceholderColor;
            Stretch(hint.rectTransform);

            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.contentType = contentType;
            if (contentType == TMP_InputField.ContentType.Alphanumeric)
                field.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;

            host.SetActive(true);
            return field;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>按窗口左上角定位：x 向右，y 向下（传负数）。</summary>
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
