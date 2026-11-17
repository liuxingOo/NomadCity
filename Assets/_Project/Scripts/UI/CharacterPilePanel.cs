using System.Collections.Generic;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 「已盖放角色卡 / 弃牌区」查看面板：全部玩家分组展示，不参与规则计算。
    ///
    /// 由一个运行时搭建的模态窗显示，卡片按状态里的顺序排列（弃牌区从旧到新），
    /// 所以它只读 <see cref="PlayerState.PlacedCharacterCard"/> 与
    /// <see cref="PlayerState.DiscardCharacterCards"/>，不会改动任何状态。
    /// </summary>
    public sealed class CharacterPilePanel : MonoBehaviour
    {
        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.98f);
        private static readonly Color RowColor = new Color(0.165f, 0.142f, 0.110f, 0.98f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color MutedColor = new Color(0.76f, 0.72f, 0.63f, 1f);

        private const float WindowWidth = 980f;
        private const float WindowHeight = 620f;
        private const float PadX = 22f;
        private const float CardWidth = 108f;
        private const float CardHeight = 150f;
        private const float CardGap = 10f;

        private TMP_FontAsset font;
        private Sprite[] faces = new Sprite[0];
        private GameObject root;
        private RectTransform window;
        private RectTransform content;
        private TMP_Text title;
        private GameState current;

        public bool IsOpen => root != null && root.activeSelf;

        public static CharacterPilePanel Create(Transform canvas, TMP_FontAsset font, Sprite[] faces)
        {
            GameObject host = new GameObject("CharacterPilePanel", typeof(RectTransform));
            host.transform.SetParent(canvas, false);

            var panel = host.AddComponent<CharacterPilePanel>();
            panel.font = font;
            panel.faces = faces ?? new Sprite[0];
            panel.Build();
            return panel;
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        // —— 两种视图 ——

        /// <summary>所有玩家当前盖放在场上的角色牌。</summary>
        public void ShowPlaced(GameState state)
        {
            if (state == null) return;
            current = state;
            Open("已盖放的角色卡");
            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                PlayerState player = state.Players[seat];
                var cards = new List<int>();
                if (player.PlacedCharacterCard >= 0) cards.Add(player.PlacedCharacterCard);
                AddGroup($"玩家 {seat + 1}", player.PlacedCharacterCard >= 0 ? null : "本回合还没有盖放角色牌", cards);
            }
        }

        /// <summary>所有玩家的角色牌弃牌区（按进入弃牌区的先后顺序）。</summary>
        public void ShowDiscards(GameState state)
        {
            if (state == null) return;
            current = state;
            Open("角色牌弃牌区");
            for (int seat = 0; seat < state.Players.Count; seat++)
            {
                PlayerState player = state.Players[seat];
                AddGroup($"玩家 {seat + 1}（{player.DiscardCharacterCards.Count} 张）",
                    player.DiscardCharacterCards.Count == 0 ? "还没有弃牌" : null,
                    new List<int>(player.DiscardCharacterCards));
            }
        }

        // —— 搭建与刷新 ——

        private void Open(string heading)
        {
            if (root == null) return;
            title.text = heading;
            ClearGroups();
            nextRowY = 0f;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
        }

        private void Build()
        {
            root = gameObject;
            Stretch((RectTransform)root.transform);

            Image dim = CreateImage("Dim", (RectTransform)root.transform, new Color(0f, 0f, 0f, 0.6f));
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);

            Image windowImage = CreateImage("Window", (RectTransform)root.transform, WindowColor);
            window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            title = CreateText("Title", window, string.Empty, 24f, TextAlignmentOptions.Left);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(PadX, -14f);
            title.rectTransform.sizeDelta = new Vector2(WindowWidth - PadX * 2f - 90f, 34f);

            Button close = CreateButton("Close", window, "关闭");
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-14f, -12f);
            closeRect.sizeDelta = new Vector2(76f, 34f);
            close.onClick.AddListener(Close);

            content = CreateRect("Content", window);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(PadX, PadX);
            content.offsetMax = new Vector2(-PadX, -56f);

            root.SetActive(false);
        }

        private void ClearGroups()
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }

        private float nextRowY;

        private void AddGroup(string heading, string emptyHint, IReadOnlyList<int> cards)
        {
            float y = nextRowY;
            float rows = 0f;

            Image row = CreateImage($"Row{y:F0}", content, RowColor);
            row.rectTransform.anchorMin = new Vector2(0f, 1f);
            row.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.rectTransform.pivot = new Vector2(0.5f, 1f);

            TMP_Text header = CreateText("Header", row.rectTransform, heading, 18f, TextAlignmentOptions.Left);
            header.color = TextColor;
            header.rectTransform.anchorMin = header.rectTransform.anchorMax = new Vector2(0f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(14f, -8f);
            header.rectTransform.sizeDelta = new Vector2(400f, 26f);

            float rowHeight = 44f;
            if (emptyHint != null)
            {
                TMP_Text hint = CreateText("Hint", row.rectTransform, emptyHint, 16f, TextAlignmentOptions.Left);
                hint.color = MutedColor;
                hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0f, 1f);
                hint.rectTransform.pivot = new Vector2(0f, 1f);
                hint.rectTransform.anchoredPosition = new Vector2(14f, -34f);
                hint.rectTransform.sizeDelta = new Vector2(600f, 24f);
                rowHeight = 64f;
            }
            else
            {
                int perRow = Mathf.Max(1, Mathf.FloorToInt((WindowWidth - PadX * 2f - 28f) / (CardWidth + CardGap)));
                for (int i = 0; i < cards.Count; i++)
                {
                    int column = i % perRow;
                    rows = Mathf.Floor(i / perRow);
                    AddCard(row.rectTransform, cards[i], i + 1,
                        new Vector2(14f + column * (CardWidth + CardGap), -34f - rows * (CardHeight + 8f)));
                }

                rowHeight = 42f + (rows + 1) * (CardHeight + 8f);
            }

            row.rectTransform.sizeDelta = new Vector2(0f, rowHeight);
            row.rectTransform.anchoredPosition = new Vector2(0f, y);
            nextRowY = y - rowHeight - 10f;
        }

        private void AddCard(RectTransform parent, int cardId, int order, Vector2 position)
        {
            Image image = CreateImage($"Card{cardId}_{order}", parent, new Color(1f, 1f, 1f, 0f));
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0f, 1f);
            image.rectTransform.pivot = new Vector2(0f, 1f);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = new Vector2(CardWidth, CardHeight);
            image.preserveAspect = true;
            image.raycastTarget = false;

            Sprite sprite = CardSprite(cardId);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                return;
            }

            Image back = CreateImage("Back", image.rectTransform, new Color(0.32f, 0.27f, 0.20f, 1f));
            Stretch(back.rectTransform);

            TMP_Text name = CreateText("Name", image.rectTransform, NameOf(cardId), 14f, TextAlignmentOptions.Center);
            name.color = TextColor;
            Stretch(name.rectTransform);
            name.margin = new Vector4(4f, 4f, 4f, 4f);
        }

        private Sprite CardSprite(int cardId)
        {
            Sprite operatorArt = OperatorCardArt.SpriteFor(cardId);
            if (operatorArt != null) return operatorArt;

            int index = CharacterHandPanel.SpriteIndexOf(cardId);
            return index >= 0 && index < faces.Length ? faces[index] : null;
        }

        private string NameOf(int cardId)
        {
            CharacterCardDef card = current != null && current.Catalog != null
                ? current.Catalog.Character(cardId)
                : null;
            return card != null ? card.Name : $"角色牌 {cardId}";
        }

        // —— 基础控件 ——

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text CreateText(string name, Transform parent, string text, float size,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = TextColor;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private Button CreateButton(string name, Transform parent, string text)
        {
            Image image = CreateImage(name, parent, new Color(0.42f, 0.34f, 0.24f, 1f));
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            TMP_Text label = CreateText("Label", image.rectTransform, text, 17f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            return button;
        }
    }
}
