using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 设施供应区（§4.1 / §6.4e）：6 张设施牌，被建设拿走一张就立刻从牌堆补一张。
    /// 只负责显示与发出意图 —— 点第几个牌位就把下标交回给 GameScreenController，合法性与费用一概不问。
    ///
    /// 呈现方式：不常驻场上，由「建设」按钮弹出（<see cref="SetOpen"/>），选中一张牌即收起，
    /// 开关时机由 GameScreenController 按行动模式决定。
    ///
    /// 场景结构（建议）：
    /// Canvas
    ///   FacilitySupplyPanel（宿主容器，挂本脚本，自身永远激活）
    ///     Root                 ← 弹窗开关，由 SetOpen 控制显隐
    ///       Dim                ← 全屏半透明遮罩，Raycast Target 要勾上（挡住底下的地图）
    ///       背景 Image
    ///         牌位 1 … 6        ← 每个都是一个 Button，逐个拖进 Card Buttons
    ///           牌名文本        ← 拖进 Card Labels（可选）
    /// </summary>
    public sealed class FacilitySupplyPanel : MonoBehaviour, ICenteredPanel
    {
        // 与图鉴 / 聊天 / 行动记录同一套配色。
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color ContentColor = new Color(0.12f, 0.10f, 0.08f, 0.85f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color ButtonColor = new Color(0.45f, 0.36f, 0.28f, 1f);

        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 背景 / 牌位）。宿主自身必须保持激活，只切这一个。" +
                 "留空则面板一直显示（旧的常驻行为）。")]
        [SerializeField] private GameObject root;

        [Tooltip("操作区里常驻的「设施」按钮：面板开着时点亮它。留空则这颗按钮不跟着亮暗（不会出错）。")]
        [SerializeField] private Button toggleButton;

        [Tooltip("6 个牌位按钮，顺序与设施供应区列表一致（下标 0 是最左边那张牌）")]
        [SerializeField] private Button[] cardButtons = Array.Empty<Button>();

        [Tooltip("每个牌位上的牌名文本，与 Card Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] cardLabels = Array.Empty<TMP_Text>();

        [Tooltip("每个牌位上的卡面 Image，与 Card Buttons 一一对应（可选；装饰用，Raycast Target 关掉）")]
        [SerializeField] private Image[] cardImages = Array.Empty<Image>();

        [Tooltip("设施牌卡面 sprite 库：下标 → 牌 id 的映射见 SpriteIndexOf（100–140 → 0–40，核心指挥塔 200–203 → 41–44，" +
                 "延伸枢纽 300–302 → 45–47）。按此顺序把 Assets/_Project/Art/Cards/Facility/card_facility.jpg 切出的子 Sprite 拖进来即可。")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

        /// <summary>设施卡面库（只读）。图鉴的设施页直接复用这一份，避免在场景里重复拖 48 张图。</summary>
        public Sprite[] CardFaces => cardFaces;

        /// <summary>点了第几个牌位（0 起）。</summary>
        public event Action<int> CardClicked;

        /// <summary>
        /// 每个牌位 Image 在场景里原本的 sprite（占位贴图）。刷新时若该牌位没有卡面就退回它，
        /// 而不是把 Image 关掉 —— 关掉会连带关掉 Button 的 Target Graphic，那个牌位就点不动了。
        /// </summary>
        private Sprite[] placeholderSprites = Array.Empty<Sprite>();

        /// <summary>只读查看模式：面板开着，但六个牌位一律点不动（点卡片不会发出建设意图）。</summary>
        private bool viewOnly;

        /// <summary>最近一次刷新用的状态与可交互标记：切到只读 / 退出只读时立刻重算牌位，不用等下一次刷新。</summary>
        private GameState lastState;
        private bool lastInteractable;

        /// <summary>
        /// 开关弹窗。宿主容器始终激活（避免 Awake 里的按钮绑定失效），只切 Root 的显隐。
        /// 这是 GameScreenController 按行动模式调用的「可操作」打开方式，会把只读标记清掉。
        /// </summary>
        public void SetOpen(bool open)
        {
            if (root == null) return;

            // 三块同位置面板互斥：打开自己之前先把行动记录 / 城市样式收起来，
            // 否则两层遮罩叠着，后开的被先开的盖住，看着就像「点了按钮没反应」。
            if (open) CenteredPanelGroup.BeforeOpen(this);

            viewOnly = false;
            if (root.activeSelf != open) root.SetActive(open);
            UiButtonTint.SetLit(toggleButton, IsOpen);
        }

        /// <summary>面板是不是开着。</summary>
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>当前是不是「只读查看」：是的话牌位可点，但点上去只弹放大卡面，不会发出建设意图。</summary>
        public bool IsViewOnly => viewOnly;

        /// <summary>互斥组用：无条件收起，连只读标记一起清掉。</summary>
        public void ForceClose()
        {
            viewOnly = false;
            if (root != null && root.activeSelf) root.SetActive(false);
            UiButtonTint.SetLit(toggleButton, IsOpen);
        }

        /// <summary>
        /// 只读查看开关，给常驻操作区的「设施」按钮用：
        /// 打开后六张设施牌只是资料 —— 点牌位只会把卡面放大看一眼（<see cref="CardClicked"/> 照发，
        /// 由 GameScreenController 按「是不是只读」分流），不会走建设、不会扣费；
        /// 再点一次关闭。建设阶段的「可操作」打开仍走 <see cref="SetOpen"/>，不受这里影响。
        /// </summary>
        public void ToggleView()
        {
            if (root == null) return;

            // 开着就关 —— 不管是只读打开的，还是建设流程打开的，同一个按钮再点一次预期就是收起。
            if (root.activeSelf)
            {
                viewOnly = false;
                root.SetActive(false);
                UiButtonTint.SetLit(toggleButton, IsOpen);
                return;
            }

            // 关着就只读打开：先收起另外两块同位置面板，再亮自己。
            CenteredPanelGroup.BeforeOpen(this);
            viewOnly = true;
            root.SetActive(true);
            if (lastState != null) Refresh(lastState, lastInteractable);
            UiButtonTint.SetLit(toggleButton, IsOpen);
        }

        private void Awake()
        {
            CenteredPanelGroup.Register(this);
            CachePlaceholders();

            UiButtonTint.Ensure(toggleButton);

            if (root == null)
                Debug.LogWarning("[设施供应区] 没有接「Root」——面板会一直显示。要走「点建设才弹出」的话，" +
                                 "请在场景里新建弹窗根并拖进本组件的 Root 字段。", this);

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                NeutralizeDisabledTint(button);

                int index = i; // 闭包捕获：每个按钮绑定自己的下标
                button.onClick.AddListener(() => CardClicked?.Invoke(index));
            }

            ApplyPopupStyle();
        }

        /// <summary>
        /// 把设施弹窗的配色统一成图鉴 / 聊天 / 行动记录那一套：
        /// 遮罩更深、内容区加一层深色底、牌名用米白，并在右上角补一颗关闭按钮。
        /// </summary>
        private void ApplyPopupStyle()
        {
            if (root == null) return;

            Transform dim = root.transform.Find("Dim");
            if (dim != null && dim.TryGetComponent(out Image dimImage)) dimImage.color = DimColor;

            Transform panel = root.transform.Find("Panel");
            if (panel == null) return;

            // 内容区深色底：垫在牌位下面，牌位本身不动（设施牌位的 Image 就是卡面，不能染色）。
            if (panel.Find("ContentBg") == null)
            {
                var backdrop = new GameObject("ContentBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var backdropRect = (RectTransform)backdrop.transform;
                backdropRect.SetParent(panel, false);
                backdropRect.anchorMin = Vector2.zero;
                backdropRect.anchorMax = Vector2.one;
                backdropRect.offsetMin = new Vector2(12f, 12f);
                backdropRect.offsetMax = new Vector2(-12f, -12f);
                backdropRect.SetAsFirstSibling();

                var backdropImage = backdrop.GetComponent<Image>();
                backdropImage.color = ContentColor;
                backdropImage.raycastTarget = false;
            }

            AddCloseButton(panel);
        }

        /// <summary>右上角「关闭」按钮：点一下收起弹窗（等价于再点一次「设施」按钮）。</summary>
        private void AddCloseButton(Transform panel)
        {
            if (panel.Find("Close") != null) return;

            var go = new GameObject("Close", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-14f, -14f);
            rect.sizeDelta = new Vector2(72f, 32f);

            var image = go.GetComponent<Image>();
            image.color = ButtonColor;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            UiButtonTint.Ensure(button);
            button.onClick.AddListener(() => SetOpen(false));

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "关闭";
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = TextColor;
            label.raycastTarget = false;

            TMP_FontAsset font = null;
            foreach (TMP_Text cardLabel in cardLabels)
            {
                if (cardLabel != null && cardLabel.font != null)
                {
                    font = cardLabel.font;
                    break;
                }
            }

            if (font != null) label.font = font;
        }

        private void OnDestroy()
        {
            CenteredPanelGroup.Unregister(this);
        }

        /// <summary>
        /// 让「不可点」不再压暗目标图形。卡面位复用的就是这个 Image（Button 的 Target Graphic），
        /// Unity 默认的禁用色带 50% 透明，会把卡面蒙上一层灰。只动禁用色，按下/高亮反馈保留。
        /// 幂等：Awake 与 Refresh 各调一次 —— Refresh 可能比 Awake 先被调到，那边是保险。
        /// </summary>
        private static void NeutralizeDisabledTint(Button button)
        {
            if (button.colors.disabledColor == Color.white) return;

            ColorBlock colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
        }

        /// <summary>
        /// 记下每个牌位 Image 在场景里原本的 sprite（占位贴图）——只记一次。
        /// 不能只在 Awake 里做一次就完：GameBootstrap 的 Awake 会先一步调到 <see cref="Refresh"/>，
        /// 那时本组件的 Awake 可能还没执行，缓存仍是空的。这里按需补记，顺带保证下标对齐。
        /// </summary>
        private void CachePlaceholders()
        {
            if (placeholderSprites.Length == cardImages.Length) return;

            var cached = new Sprite[cardImages.Length];
            for (int i = 0; i < cardImages.Length; i++)
                cached[i] = cardImages[i] != null ? cardImages[i].sprite : null;
            placeholderSprites = cached;
        }

        /// <summary>
        /// 按当前供应区刷新 6 个牌位：写入牌名、按需显示。
        /// <paramref name="interactable"/> 只有「建设」行动进行中才为 true，其余时候牌位照样看得见但点不动。
        /// </summary>
        public void Refresh(GameState state, bool interactable)
        {
            CachePlaceholders();
            lastState = state;
            lastInteractable = interactable;

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                NeutralizeDisabledTint(button);

                int cardId = state != null && i < state.FacilitySupply.Count ? state.FacilitySupply[i] : -1;
                button.gameObject.SetActive(cardId >= 0);
                // 只读查看时牌位照样能点，但点上去只会弹放大卡面（控制器按 IsViewOnly 分流），
                // 不会走建设、不会扣费 —— 所以这里不能像以前那样直接把它锁死。
                button.interactable = cardId >= 0 && (viewOnly || interactable);

                int faceIndex = SpriteIndexOf(cardId);
                Sprite face = faceIndex >= 0 && faceIndex < cardFaces.Length ? cardFaces[faceIndex] : null;

                if (i < cardImages.Length && cardImages[i] != null)
                    cardImages[i].sprite = face != null ? face : placeholderSprites[i];

                // 卡面自带设施名，有卡面时就不再叠文本（否则名字压在图上）。
                if (i < cardLabels.Length && cardLabels[i] != null)
                    cardLabels[i].text = cardId >= 0 && face == null ? NameOf(state, cardId) : string.Empty;
            }
        }

        /// <summary>
        /// 设施牌 id → <see cref="cardFaces"/> 下标。
        /// 普通设施 100–140 → 0–40（按素材拼版图从左到右、从上到下编号，跳过其中的延伸枢纽槽位）；
        /// 核心指挥塔 200–203 → 41–44；延伸枢纽 300–302 → 45–47；其余 id 返回 -1。
        /// </summary>
        public static int SpriteIndexOf(int cardId)
        {
            if (cardId >= CardIds.FacilityBase && cardId < CardIds.FacilityBase + CardCounts.StandardFacilities)
                return cardId - CardIds.FacilityBase;

            if (cardId >= CardIds.CoreCommandTowerBase && cardId < CardIds.CoreCommandTowerBase + CardCounts.CoreCommandTowers)
                return CardCounts.StandardFacilities + (cardId - CardIds.CoreCommandTowerBase);

            if (cardId >= CardIds.ExtensionHubBase && cardId < CardIds.ExtensionHubBase + CardCounts.ExtensionHubs)
                return CardCounts.StandardFacilities + CardCounts.CoreCommandTowers + (cardId - CardIds.ExtensionHubBase);

            return -1;
        }

        private static string NameOf(GameState state, int cardId)
        {
            FacilityCardDef card = state.Catalog.Facility(cardId);
            return card != null ? card.Name : $"设施牌 {cardId}";
        }
    }
}
