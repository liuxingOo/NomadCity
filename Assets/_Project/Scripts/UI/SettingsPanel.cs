using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 游戏内右上角的「设置」入口与设置面板。
    ///
    /// 单独挂一层高 sortingOrder 的 Canvas：开局选企业家、企业抽选、角色牌盖放这些全屏面板
    /// 都会盖住主 Canvas，设置按钮必须始终压在最上面、任何阶段都点得到。
    /// 面板内容区用的是竖向布局，以后加设置项直接往里加，不用再动版面。
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.98f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color ButtonColor = new Color(0.42f, 0.34f, 0.24f, 1f);

        private const float PanelWidth = 720f;
        private const float PanelHeight = 520f;
        private const float PadX = 26f;

        private TMP_FontAsset font;
        private GameObject overlay;
        private GameObject confirmLayer;
        private RectTransform content;

        /// <summary>玩家在面板内的确认窗里点了「确定返回」。真正的断线与切场景交给外层。</summary>
        public event Action ReturnConfirmed;

        public static SettingsPanel Create(Canvas sourceCanvas, TMP_FontAsset font)
        {
            if (sourceCanvas == null) return null;

            var layer = new GameObject("SettingsLayer", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(sourceCanvas.transform, false);
            Stretch(layerRect);

            Canvas canvas = layer.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 800;

            CanvasScaler scaler = layer.GetComponent<CanvasScaler>();
            var sourceScaler = sourceCanvas.GetComponent<CanvasScaler>();
            if (sourceScaler != null)
            {
                scaler.uiScaleMode = sourceScaler.uiScaleMode;
                scaler.referenceResolution = sourceScaler.referenceResolution;
                scaler.screenMatchMode = sourceScaler.screenMatchMode;
                scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            }
            else
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(2560f, 1440f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            var panel = layer.AddComponent<SettingsPanel>();
            panel.font = font;
            panel.Build(layerRect);
            return panel;
        }

        public void Show()
        {
            if (overlay == null) return;
            overlay.SetActive(true);
            if (confirmLayer != null) confirmLayer.SetActive(false);
        }

        public void Hide()
        {
            if (confirmLayer != null) confirmLayer.SetActive(false);
            if (overlay != null) overlay.SetActive(false);
        }

        private void Build(RectTransform layer)
        {
            Button entry = CreateButton("SettingsButton", layer, "设置");
            var entryRect = entry.GetComponent<RectTransform>();
            entryRect.anchorMin = entryRect.anchorMax = new Vector2(1f, 1f);
            entryRect.pivot = new Vector2(1f, 1f);
            entryRect.anchoredPosition = new Vector2(-16f, -16f);
            entryRect.sizeDelta = new Vector2(96f, 34f);
            entry.onClick.AddListener(Show);

            overlay = new GameObject("Overlay", typeof(RectTransform));
            var overlayRect = (RectTransform)overlay.transform;
            overlayRect.SetParent(layer, false);
            Stretch(overlayRect);

            Image dim = CreateImage("Dim", overlayRect, new Color(0f, 0f, 0f, 0.6f));
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);

            // 点面板外的空白处收起。
            Button dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Hide);

            Image windowImage = CreateImage("Window", overlayRect, WindowColor);
            RectTransform window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            TMP_Text title = CreateText("Title", window, "设置", 24f, TextAlignmentOptions.Left);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(PadX, -14f);
            title.rectTransform.sizeDelta = new Vector2(PanelWidth - PadX * 2f - 90f, 34f);

            Button close = CreateButton("Close", window, "关闭");
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-14f, -12f);
            closeRect.sizeDelta = new Vector2(76f, 34f);
            close.onClick.AddListener(Hide);

            // 内容区：以后加设置项就往这里塞，竖向自动排。
            content = CreateRect("Content", window);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(PadX, PadX);
            content.offsetMax = new Vector2(-PadX, -60f);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;

            Button back = CreateButton("ReturnToMainMenu", content, "返回主菜单");
            back.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            back.onClick.AddListener(ShowReturnConfirm);

            BuildReturnConfirm(overlayRect);

            overlay.SetActive(false);
        }

        /// <summary>
        /// 「返回主菜单」的二次确认：贴在设置面板内部。
        /// 不再另开一个主 Canvas 上的弹窗 —— 那层排在设置层（sortingOrder 800）下面，
        /// 玩家只会看到设置面板，确认窗被完全盖住。
        /// </summary>
        private void BuildReturnConfirm(RectTransform parent)
        {
            confirmLayer = new GameObject("ReturnConfirm", typeof(RectTransform));
            var layerRect = (RectTransform)confirmLayer.transform;
            layerRect.SetParent(parent, false);
            Stretch(layerRect);

            Image dim = CreateImage("Dim", layerRect, new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);
            Button dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(HideReturnConfirm);

            Image windowImage = CreateImage("Window", layerRect, WindowColor);
            RectTransform window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(480f, 240f);

            TMP_Text title = CreateText("Title", window, "返回主菜单", 21f, TextAlignmentOptions.Left);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(24f, -18f);
            title.rectTransform.sizeDelta = new Vector2(432f, 34f);

            TMP_Text body = CreateText("Body", window,
                "这将退出当前对局并回到主菜单，未保存的进度会丢失。是否确定？",
                17f, TextAlignmentOptions.TopLeft);
            body.rectTransform.anchorMin = Vector2.zero;
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = new Vector2(24f, 82f);
            body.rectTransform.offsetMax = new Vector2(-24f, -62f);

            Button confirm = CreateButton("Confirm", window, "确定返回");
            var confirmRect = confirm.GetComponent<RectTransform>();
            confirmRect.anchorMin = confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.anchoredPosition = new Vector2(-92f, 22f);
            confirmRect.sizeDelta = new Vector2(168f, 42f);
            confirm.onClick.AddListener(() => ReturnConfirmed?.Invoke());

            Button cancel = CreateButton("Cancel", window, "取消");
            var cancelRect = cancel.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.anchoredPosition = new Vector2(92f, 22f);
            cancelRect.sizeDelta = new Vector2(168f, 42f);
            cancel.onClick.AddListener(HideReturnConfirm);

            confirmLayer.SetActive(false);
        }

        private void ShowReturnConfirm()
        {
            if (confirmLayer != null) confirmLayer.SetActive(true);
        }

        private void HideReturnConfirm()
        {
            if (confirmLayer != null) confirmLayer.SetActive(false);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ((RectTransform)go.transform).SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private TMP_Text CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            ((RectTransform)go.transform).SetParent(parent, false);
            var label = go.GetComponent<TMP_Text>();
            label.font = font;
            label.fontSize = size;
            label.color = TextColor;
            label.text = text;
            label.alignment = align;
            label.raycastTarget = false;
            return label;
        }

        private Button CreateButton(string name, Transform parent, string text)
        {
            Image image = CreateImage(name, parent, ButtonColor);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            TMP_Text label = CreateText("Label", image.rectTransform, text, 17f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            return button;
        }
    }
}
