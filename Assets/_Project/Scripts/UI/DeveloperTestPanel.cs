using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 开发者测试弹窗（只在开发者模式下打开，快捷键 F2）。
    ///
    /// 正常对局里企业特效只能由「角色牌 / 设施 / 城市样式特殊行动 / 事件牌」的效果解锁触发，
    /// 这里给测试留一个后门：把指定企业板直接提到某一等级，再走和正式流程完全相同的触发界面，
    /// 不在正式界面里加任何无条件触发按钮。
    /// </summary>
    public sealed class DeveloperTestPanel : MonoBehaviour
    {
        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.97f);
        private static readonly Color RowColor = new Color(0.165f, 0.142f, 0.110f, 0.98f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color MutedColor = new Color(0.76f, 0.72f, 0.63f, 1f);

        private const float WindowWidth = 820f;
        private const float WindowHeight = 620f;
        private const float RowHeight = 56f;
        private const float RowGap = 6f;

        /// <summary>点了某块企业板的某个等级：控制器负责先设等级、再走正式触发流程。</summary>
        public Action<int, int> EnterpriseLevelPicked;

        private TMP_FontAsset font;
        private RectTransform content;
        private TMP_Text title;
        private TMP_Text hint;

        public static DeveloperTestPanel Create(Transform canvas, TMP_FontAsset font)
        {
            GameObject host = new GameObject("DeveloperTestPanel", typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var panel = host.AddComponent<DeveloperTestPanel>();
            panel.font = font;
            panel.Build();
            host.SetActive(false);
            return panel;
        }

        public bool IsOpen => gameObject.activeSelf;

        public void Toggle(GameState state, int seat)
        {
            if (IsOpen) Close();
            else Open(state, seat);
        }

        public void Open(GameState state, int seat)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Rebuild(state, seat);
        }

        public void Close() => gameObject.SetActive(false);

        private void Build()
        {
            RectTransform root = (RectTransform)transform;
            Stretch(root);

            Image dim = CreateImage("Dim", root, new Color(0f, 0f, 0f, 0.6f));
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);

            Image windowImage = CreateImage("Window", root, WindowColor);
            RectTransform window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            title = CreateText("Title", window, "开发者测试", 24f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            title.rectTransform.sizeDelta = new Vector2(WindowWidth - 260f, 34f);

            hint = CreateText("Hint", window, string.Empty, 13f, TextAlignmentOptions.Center);
            hint.color = MutedColor;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -46f);
            hint.rectTransform.sizeDelta = new Vector2(WindowWidth - 60f, 22f);

            Button close = CreateButton("Close", window, "关闭", 18f);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-14f, -12f);
            closeRect.sizeDelta = new Vector2(72f, 32f);
            close.onClick.AddListener(Close);

            content = CreateRect("Content", window);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.offsetMin = new Vector2(14f, 14f);
            content.offsetMax = new Vector2(-14f, -76f);
        }

        private void Rebuild(GameState state, int seat)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            title.text = $"开发者测试 · 玩家 {seat + 1}";
            hint.text = "● 企业一栏：点某一级会先把合作等级直接写成该级，再用与正式流程相同的界面触发该级特效。\n" +
                        "● 这里只是测试后门；正常对局的企业特效仍只能由角色牌 / 设施 / 城市样式特殊行动 / 事件牌解锁触发。";

            List<int> boards = state?.Enterprises?.ActiveBoards;
            if (boards == null || boards.Count == 0)
            {
                TMP_Text empty = CreateText("Empty", content, "本局没有在场的合作企业板。", 15f,
                    TextAlignmentOptions.TopLeft);
                empty.color = MutedColor;
                empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(0f, 1f);
                empty.rectTransform.pivot = new Vector2(0f, 1f);
                empty.rectTransform.anchoredPosition = Vector2.zero;
                empty.rectTransform.sizeDelta = new Vector2(WindowWidth - 60f, 24f);
                return;
            }

            for (int i = 0; i < boards.Count; i++)
                BuildBoardRow(state, seat, boards[i], i);
        }

        private void BuildBoardRow(GameState state, int seat, int boardId, int index)
        {
            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);

            Image row = CreateImage($"Board{boardId}", content, RowColor);
            RectTransform rowRect = row.rectTransform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowGap));
            rowRect.sizeDelta = new Vector2(0f, RowHeight);

            int level = state.Enterprises.LevelOf(boardId, seat);

            TMP_Text name = CreateText("Name", rowRect,
                $"{def?.Name ?? $"企业板 {boardId}"}（当前 Lv{Mathf.Max(level, 0)}）",
                16f, TextAlignmentOptions.Left);
            name.color = TextColor;
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            name.rectTransform.pivot = new Vector2(0f, 0.5f);
            name.rectTransform.anchoredPosition = new Vector2(12f, 0f);
            name.rectTransform.sizeDelta = new Vector2(210f, RowHeight);

            for (int lv = 1; lv <= 5; lv++)
            {
                int pickedLevel = lv;
                EnterpriseLevel entry = def?.Level(lv);
                string label = entry != null && entry.IsEffect ? $"Lv{lv}" : $"Lv{lv}·奖励";

                Button button = CreateButton($"Lv{lv}", rowRect, label, 15f);
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(-12f - (5 - lv) * 92f, 0f);
                rect.sizeDelta = new Vector2(86f, 36f);
                button.onClick.AddListener(() =>
                {
                    Close();
                    EnterpriseLevelPicked?.Invoke(boardId, pickedLevel);
                });
            }
        }

        // —— 构建小工具（和 EnterpriseBoardPanel 同一套规格）——

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            return r;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            return img;
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

        private Button CreateButton(string name, RectTransform parent, string text, float size)
        {
            Image bg = CreateImage(name, parent, new Color(0.45f, 0.36f, 0.28f, 1f));
            Button btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            TMP_Text label = CreateText("Label", bg.rectTransform, text, size, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            UiButtonTint.Ensure(btn);
            return btn;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
