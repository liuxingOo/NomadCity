using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 本机玩家的角色牌手牌（§4.2 阶段 1 / §10.1.1）：点小标签展开后，盖放阶段点一张牌即把它背面朝上盖放，
    /// 其余时候只显示、不可点。只负责显示与发出意图 —— 点第几个牌位就把下标交回 GameScreenController，
    /// 合法性与费用一概不问（可点与否由控制器按当前阶段决定）。
    ///
    /// 与 <see cref="FacilitySupplyPanel"/> 不同，这一块**不铺遮罩**：盖放阶段与开局入场（§4.3 决定初始资源点）
    /// 同时发生在第 1 回合的阶段 1，若用模态弹窗挡住地图，就点不到绿色资源点了。因此它只做一条可开关的小托盘。
    ///
    /// 场景结构（建议）：
    /// Canvas
    ///   CharacterHandPanel（宿主容器，挂本脚本，自身永远激活）
    ///     小标签按钮         ← 拖进 Toggle Button（与 Root 平级；点它展开 / 收起，开局默认收起）
    ///     Root               ← 托盘本体，由「玩家手里有牌」＋「已展开」两个条件控制显隐
    ///       牌位 1 … 5        ← 每个都是一个 Button，逐个拖进 Card Buttons（脚本会自动叠放排列，别挂 Layout Group）
    ///         牌名文本        ← 拖进 Card Labels（可选）
    ///         卡面 Image      ← 拖进 Card Images（可选；装饰用，Raycast Target 关掉）
    /// </summary>
    public sealed class CharacterHandPanel : MonoBehaviour
    {
        [Tooltip("托盘本体（宿主 → Root → 牌位）。宿主自身必须保持激活，只切这一个。")]
        [SerializeField] private GameObject root;

        [Tooltip("展开 / 收起手牌的小标签按钮。**要跟 Root 平级**放在宿主下，不能放进 Root —— " +
                 "否则一收起就连按钮一起消失。留空则手牌区不能收起（一直显示）。")]
        [SerializeField] private Button toggleButton;

        [Tooltip("小标签上的文字（可选）：收起时显示「手牌」，展开时显示「收起手牌」。")]
        [SerializeField] private TMP_Text toggleLabel;

        [Tooltip("手牌牌位按钮，顺序与手牌列表一致（下标 0 是最左边那张牌）。最多按手牌上限（5 张）摆。")]
        [SerializeField] private Button[] cardButtons = Array.Empty<Button>();

        [Tooltip("每个牌位上的牌名文本，与 Card Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] cardLabels = Array.Empty<TMP_Text>();

        [Tooltip("每个牌位上的卡面 Image，与 Card Buttons 一一对应（可选；装饰用，Raycast Target 关掉）")]
        [SerializeField] private Image[] cardImages = Array.Empty<Image>();

        [Tooltip("角色牌卡面 sprite 库：四个颜色共用同一张正面，所以这里只存 5 张角色图，下标 = 角色" +
                 "（0 雷蛇 Liskarm / 1 极境 Elysium / 2 德克萨斯 Texas / 3 锡人 Tin Man / 4 坎诺特 Canot）。" +
                 "映射见 SpriteIndexOf。用菜单 NomadCity / 卡面：填充卡面库（Card Faces）会自动按切片名填好，无需手动拖。")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

        [Tooltip("相邻两张手牌的水平位移（像素）。小于卡面宽度就会叠放：卡宽约 150 时填 70，就是叠掉一半。")]
        [SerializeField] private float cardStep = 70f;

        [Tooltip("整组手牌在 Root 里居中排列；关掉则从 Root 左侧开始向右排。" +
                 "手牌数变化时都会自动重新分布位置（叠放布局由脚本算，不要用 Horizontal Layout Group）。")]
        [SerializeField] private bool centerGroup = true;

        /// <summary>点了第几个牌位（0 起）。</summary>
        public event Action<int> CardClicked;

        /// <summary>
        /// 每个牌位 Image 在场景里原本的 sprite（占位贴图）。刷新时若该牌位没有卡面就退回它，
        /// 而不是把 Image 关掉 —— 关掉会连带关掉 Button 的 Target Graphic，那个牌位就点不动了。
        /// </summary>
        private Sprite[] placeholderSprites = Array.Empty<Sprite>();

        /// <summary>本机玩家手里还有没有牌（由控制器告知）；没有牌时自动收起。</summary>
        private bool available;

        /// <summary>玩家有没有点小标签把手牌展开。默认 false = 收起 —— 点击才显示。</summary>
        private bool expanded;

        /// <summary>由控制器告知「本机玩家是否还有手牌」。没牌时自动收起；有牌时保持玩家上次的展开状态。</summary>
        public void SetAvailable(bool hasHand)
        {
            available = hasHand;
            if (!hasHand) expanded = false;
            ApplyVisibility();
        }

        /// <summary>点小标签：展开 / 收起。</summary>
        public void ToggleExpanded()
        {
            expanded = !expanded;
            ApplyVisibility();
        }

        /// <summary>真正切显隐。宿主容器始终激活（避免 Awake 里的按钮绑定失效），只切 Root 的显隐。</summary>
        private void ApplyVisibility()
        {
            if (root != null && root.activeSelf != (available && expanded))
                root.SetActive(available && expanded);
            if (toggleLabel != null)
                toggleLabel.text = expanded ? "收起手牌" : "手牌";
        }

        private void Awake()
        {
            CachePlaceholders();

            if (toggleButton != null)
            {
                NeutralizeDisabledTint(toggleButton);
                toggleButton.onClick.AddListener(ToggleExpanded);
            }
            else
            {
                Debug.LogWarning("[角色牌手牌] 没接「Toggle Button」——手牌区无法收起，会一直显示。" +
                                 "要「点击才显示」的话，请在宿主下加一个小标签按钮并拖进本字段。", this);
            }

            ApplyVisibility();   // 开局默认收起：点击小标签才显示

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                NeutralizeDisabledTint(button);

                int index = i; // 闭包捕获：每个按钮绑定自己的下标
                button.onClick.AddListener(() => CardClicked?.Invoke(index));
            }
        }

        /// <summary>
        /// 让「不可点」不再压暗目标图形。卡面位复用的就是这个 Image（Button 的 Target Graphic），
        /// Unity 默认的禁用色带 50% 透明，会把卡面蒙上一层灰。只动禁用色，按下/高亮反馈保留。
        /// </summary>
        private static void NeutralizeDisabledTint(Button button)
        {
            if (button.colors.disabledColor == Color.white) return;

            ColorBlock colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
        }

        /// <summary>记下每个牌位 Image 在场景里原本的 sprite（占位贴图）——只记一次，按需补记。</summary>
        private void CachePlaceholders()
        {
            if (placeholderSprites.Length == cardImages.Length) return;

            var cached = new Sprite[cardImages.Length];
            for (int i = 0; i < cardImages.Length; i++)
                cached[i] = cardImages[i] != null ? cardImages[i].sprite : null;
            placeholderSprites = cached;
        }

        /// <summary>
        /// 按当前手牌刷新牌位：写入牌名、按需显示。
        /// <paramref name="interactable"/> 只有盖放阶段才为 true；但牌位**始终可点** ——
        /// 不能盖放的时候点上去只是打开放大卡面看一眼，由 GameScreenController 按这个标记分流。
        /// </summary>
        public void Refresh(GameState state, int seat, bool interactable)
        {
            CachePlaceholders();

            PlayerState player = state != null && seat >= 0 && seat < state.Players.Count
                ? state.Players[seat]
                : null;
            IReadOnlyList<int> hand = player != null ? player.HandCharacterCards : null;

            // §10.1.1：手牌为空时先把自己弃牌区的角色牌全部回收再盖放，
            // 所以盖放阶段（interactable）的候选池可以退回弃牌区。
            IReadOnlyList<int> pool = hand != null && hand.Count > 0
                ? hand
                : (interactable && player != null ? player.DiscardCharacterCards : hand);

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                NeutralizeDisabledTint(button);

                int cardId = pool != null && i < pool.Count ? pool[i] : -1;
                button.gameObject.SetActive(cardId >= 0);
                // 有牌就能点：盖放阶段点=盖放，其余时候点=放大看一眼（由控制器分流），不要在这里锁死。
                button.interactable = cardId >= 0;

                int faceIndex = SpriteIndexOf(cardId);
                Sprite face = faceIndex >= 0 && faceIndex < cardFaces.Length ? cardFaces[faceIndex] : null;

                if (i < cardImages.Length && cardImages[i] != null)
                    cardImages[i].sprite = face != null ? face : placeholderSprites[i];

                // 卡面自带角色名，有卡面时就不再叠文本（否则名字压在图上）。
                if (i < cardLabels.Length && cardLabels[i] != null)
                    cardLabels[i].text = cardId >= 0 && face == null ? NameOf(state, cardId) : string.Empty;
            }

            LayoutCards();
        }

        /// <summary>
        /// 把手牌叠着排成一行：相邻两张按 <see cref="cardStep"/> 错开（小于卡宽即产生叠放），下标越大的牌压在上面。
        /// 手牌数变化时整组自动重排（居中则重新居中）。不用 Horizontal Layout Group —— 它做不了叠放，还会把超宽的牌裁掉。
        /// </summary>
        private void LayoutCards()
        {
            int visible = 0;
            for (int i = 0; i < cardButtons.Length; i++)
                if (cardButtons[i] != null && cardButtons[i].gameObject.activeSelf) visible++;

            int placed = 0;
            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null || !button.gameObject.activeSelf) continue;

                // 位置一律相对 Root 中心算，免得受各牌位在场景里设的锚点影响。
                if (button.transform is RectTransform rect)
                {
                    Vector2 size = rect.rect.size;   // 切换锚点前先记下实际大小，避免原本是拉伸锚点的牌被拉变形
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    if (size.x > 1f && size.y > 1f) rect.sizeDelta = size;

                    float x = centerGroup ? (placed - (visible - 1) * 0.5f) * cardStep : placed * cardStep;
                    rect.anchoredPosition = new Vector2(x, 0f);
                }

                button.transform.SetSiblingIndex(placed);
                placed++;
            }
        }

        /// <summary>
        /// 角色牌 id → <see cref="cardFaces"/> 下标。四个颜色共用同一张正面，所以下标是**角色**（0–4）：
        /// (id - 角色牌起始) 对角色数取模（Liskarm 0 / Elysium 1 / Texas 2 / Tin Man 3 / Canot 4）；越界返回 -1。
        /// </summary>
        public static int SpriteIndexOf(int cardId)
        {
            if (cardId >= CardIds.CharacterBase && cardId < CardIds.CharacterBase + CardCounts.CharacterCards)
                return (cardId - CardIds.CharacterBase) % CardCounts.CharacterRoles;

            return -1;
        }

        private static string NameOf(GameState state, int cardId)
        {
            CharacterCardDef card = state.Catalog.Character(cardId);
            return card != null ? card.Name : $"角色牌 {cardId}";
        }
    }
}
