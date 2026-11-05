using System;
using NomadCity.Core.Model;
using NomadCity.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 城市样式牌面板（§4.1 步 3 / §6.5b）：场上公开的 6 张城市样式牌，玩家从中挑一张宣告。
    /// 只负责显示与发出意图 —— 点第几个牌位就把下标交回给 GameScreenController，合法性与匹配一概不问。
    ///
    /// 呈现方式：不常驻场上，由「宣告城市样式」按钮弹出（<see cref="SetOpen"/>），选中一张牌即收起。
    ///
    /// 场景结构（建议，与 <see cref="FacilitySupplyPanel"/> 一致）：
    /// Canvas
    ///   CityPatternPanel（宿主容器，挂本脚本，自身永远激活）
    ///     Root                 ← 弹窗开关，由 SetOpen 控制显隐
    ///       Dim                ← 全屏半透明遮罩，Raycast Target 要勾上（挡住底下的地图）
    ///       背景 Image
    ///         牌位 1 … 6        ← 每个都是一个 Button，逐个拖进 Card Buttons
    ///           卡面 Image      ← 装饰用，Raycast Target 关闭，逐个拖进 Card Face Images
    ///           牌名文本        ← 拖进 Card Labels（可选）
    /// </summary>
    public sealed class CityPatternPanel : MonoBehaviour, ICenteredPanel
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 背景 / 牌位）。宿主自身必须保持激活，只切这一个。" +
                 "留空则面板一直显示（旧的常驻行为）。")]
        [SerializeField] private GameObject root;

        [Tooltip("6 个牌位按钮，顺序与场上样式牌列表（CityPatternCards）一致（下标 0 是最左边那张牌）")]
        [SerializeField] private Button[] cardButtons = Array.Empty<Button>();

        [Tooltip("每个牌位上的牌名文本，与 Card Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] cardLabels = Array.Empty<TMP_Text>();

        [Header("卡面")]
        [Tooltip("每个牌位上的卡面 Image，与 Card Buttons 一一对应（装饰用，Raycast Target 关闭）")]
        [SerializeField] private Image[] cardFaceImages = Array.Empty<Image>();

        [Tooltip("按样式牌 id 顺序排列的卡面 sprite：基础牌 500–505 → 0–5，企业扩展牌 550–555 → 6–11")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

        [Header("玩家标记（宣告后落在牌面轨道上）")]
        [Tooltip("玩家标记素材，下标 = PlayerColor（绿 0 / 黄 1 / 红 2 / 蓝 3）。留空则用玩家色方块占位。")]
        [SerializeField] private Sprite[] playerMarkerSprites = new Sprite[4];

        [Tooltip("牌面轨道区的左右边界（相对卡面宽度的 0~1；样式牌右侧那一竖条就是轨道区）")]
        [SerializeField, Range(0f, 1f)] private float markerAreaLeft = 0.55f;
        [SerializeField, Range(0f, 1f)] private float markerAreaRight = 0.90f;

        [Tooltip("牌面轨道区的上下边界（相对卡面高度的 0~1，0 = 卡面底部）。" +
                 "照 card_cp_lv1（未使用/已使用 两格中点在 0.70/0.30）与 card_cp_lv2（五格等分）量出来的。")]
        [SerializeField, Range(0f, 1f)] private float markerAreaBottom = 0.10f;
        [SerializeField, Range(0f, 1f)] private float markerAreaTop = 0.90f;

        [Tooltip("标记直径占卡面宽度的比例")]
        [SerializeField, Range(0.01f, 0.5f)] private float markerSizeRatio = 0.072f;

        [Tooltip("同一格里有多个玩家的标记时，横向并排的间距（× 标记直径）")]
        [SerializeField, Range(0f, 2f)] private float markerSpacingRatio = 0.88f;

        /// <summary>标记用的子物体名（每次刷新重建，方便直接按名字清掉上一次的）。</summary>
        private const string MarkerRootName = "PatternMarkers";

        /// <summary>点了第几个牌位（0 起）。</summary>
        public event Action<int> CardClicked;

        /// <summary>只读查看模式：面板开着，但六个牌位一律点不动（点卡片不会发出宣告意图）。</summary>
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

            // 三块同位置面板互斥：打开自己之前先把行动记录 / 设施供应区收起来。
            if (open) CenteredPanelGroup.BeforeOpen(this);

            viewOnly = false;
            if (root.activeSelf != open) root.SetActive(open);
        }

        /// <summary>面板是不是开着。</summary>
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>当前是不是「只读查看」：是的话牌位可点，但点上去只弹放大卡面，不会发出宣告意图。</summary>
        public bool IsViewOnly => viewOnly;

        /// <summary>互斥组用：无条件收起，连只读标记一起清掉。</summary>
        public void ForceClose()
        {
            viewOnly = false;
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        /// <summary>
        /// 只读查看开关，给常驻操作区的「城市样式」按钮用：
        /// 打开后六张样式牌只是资料 —— 牌位全部置为不可点，点上去不会触发宣告，也不会发出 <see cref="CardClicked"/>；
        /// 再点一次关闭。宣告 / 特殊行动阶段的「可操作」打开仍走 <see cref="SetOpen"/>，不受这里影响。
        /// </summary>
        public void ToggleView()
        {
            if (root == null) return;

            // 开着就关 —— 不管是只读打开的，还是宣告流程打开的，同一个按钮再点一次预期就是收起。
            if (root.activeSelf)
            {
                viewOnly = false;
                root.SetActive(false);
                return;
            }

            // 关着就只读打开：先收起另外两块同位置面板，再亮自己。
            CenteredPanelGroup.BeforeOpen(this);
            viewOnly = true;
            root.SetActive(true);
            if (lastState != null) Refresh(lastState, lastInteractable);
        }

        private void Awake()
        {
            CenteredPanelGroup.Register(this);

            if (root == null)
                Debug.LogWarning("[城市样式牌] 没有接「Root」——面板会一直显示。要走「点按钮才弹出」的话，" +
                                 "请在场景里新建弹窗根并拖进本组件的 Root 字段。", this);

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                int index = i; // 闭包捕获：每个按钮绑定自己的下标
                button.onClick.AddListener(() => CardClicked?.Invoke(index));
            }
        }

        private void OnDestroy()
        {
            CenteredPanelGroup.Unregister(this);
        }

        /// <summary>
        /// 按当前场上的样式牌刷新 6 个牌位：写入牌名、按需显示。
        /// <paramref name="interactable"/> 只有「宣告」流程进行中才为 true，其余时候牌位照样看得见但点不动。
        /// </summary>
        public void Refresh(GameState state, bool interactable)
        {
            lastState = state;
            lastInteractable = interactable;

            for (int i = 0; i < cardButtons.Length; i++)
            {
                Button button = cardButtons[i];
                if (button == null) continue;

                int cardId = state != null && i < state.CityPatternCards.Count ? state.CityPatternCards[i] : -1;
                button.gameObject.SetActive(cardId >= 0);
                // 只读查看时照样能点，但点上去只会弹放大卡面（控制器按 IsViewOnly 分流）：
                // 既不会顺手宣告，也不会像以前那样把牌位锁死变成「点了没反应」。
                button.interactable = cardId >= 0 && (viewOnly || interactable);

                if (i < cardLabels.Length && cardLabels[i] != null)
                    cardLabels[i].text = cardId >= 0 ? LabelOf(state, cardId) : string.Empty;

                if (i < cardFaceImages.Length && cardFaceImages[i] != null)
                {
                    int spriteIndex = SpriteIndexOf(cardId);
                    Sprite face = spriteIndex >= 0 && spriteIndex < cardFaces.Length ? cardFaces[spriteIndex] : null;
                    cardFaceImages[i].sprite = face;
                    cardFaceImages[i].enabled = face != null;
                }

                RefreshMarkers(state, i, button, cardId);
            }
        }

        // —— 牌面上的玩家标记（§10.2：宣告后落在「未使用 / 2」格，用特殊行动后下移一格） ——

        /// <summary>
        /// 把这张样式牌上的玩家标记按格位画到牌面轨道区。
        /// 格位编号自上而下（<see cref="CityPatternState.TrackLength"/>：I 级 2 格、II 级 5 格），
        /// 同一格里有多个玩家的标记时横向并排（按 <see cref="CityPatternState.Markers"/> 的先后）。
        /// 位置由几个 0~1 的比例控制，牌面美术换了对不上时在 Inspector 里微调即可，不用改代码。
        /// </summary>
        private void RefreshMarkers(GameState state, int slot, Button button, int cardId)
        {
            RectTransform host = MarkerHost(slot, button);
            if (host == null) return;

            // 每次刷新重建：牌位上的牌会变，轨道区也就跟着变，重建比逐个回收简单可靠。
            Transform previous = host.Find(MarkerRootName);
            if (previous != null) DestroyNow(previous.gameObject);

            if (state == null || cardId < 0) return;

            CityPatternState pattern = state.PatternStateOf(cardId);
            if (pattern == null || pattern.Markers.Count == 0) return;

            float width = host.rect.width;
            float height = host.rect.height;
            if (width <= 1f || height <= 1f) return;   // 布局还没算出来，等下一次刷新再画

            RectTransform markerRoot = CreateRect(host, MarkerRootName);
            Stretch(markerRoot);

            float size = width * markerSizeRatio;
            float centerX = (markerAreaLeft + markerAreaRight) * 0.5f * width - width * 0.5f;

            int trackLength = Mathf.Max(1, pattern.TrackLength);
            float cellHeight = (markerAreaTop - markerAreaBottom) * height / trackLength;

            for (int position = 0; position < trackLength; position++)
            {
                var seats = new System.Collections.Generic.List<int>();
                foreach (PatternMarker marker in pattern.Markers)
                    if (marker.Position == position) seats.Add(marker.Seat);

                if (seats.Count == 0) continue;

                // 格心 y（相对卡面中心）：从上往下数第 position 格。
                float cellCenterY = markerAreaTop * height - (position + 0.5f) * cellHeight - height * 0.5f;

                for (int i = 0; i < seats.Count; i++)
                {
                    float x = centerX + (i - (seats.Count - 1) * 0.5f) * size * markerSpacingRatio;
                    Image marker = CreateMarker(markerRoot, state, seats[i], size);
                    ((RectTransform)marker.transform).anchoredPosition = new Vector2(x, cellCenterY);
                }
            }
        }

        /// <summary>标记的父物体：优先挂在卡面 Image 上（跟着卡面缩放），没有就退回牌位按钮。</summary>
        private RectTransform MarkerHost(int slot, Button button)
        {
            if (slot < cardFaceImages.Length && cardFaceImages[slot] != null)
                return cardFaceImages[slot].rectTransform;
            return button != null ? button.transform as RectTransform : null;
        }

        private Image CreateMarker(RectTransform parent, GameState state, int seat, float size)
        {
            PlayerColor color = state.Player(seat).Color;
            int index = (int)color;
            Sprite sprite = playerMarkerSprites != null && index < playerMarkerSprites.Length ? playerMarkerSprites[index] : null;

            RectTransform rect = CreateRect(parent, $"Marker_{seat}");
            rect.sizeDelta = new Vector2(size, size);

            Image image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
            }
            else
            {
                // 没接素材时退回玩家色方块，至少看得出是谁。
                image.color = MapPalette.Player(color);
            }

            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void DestroyNow(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        /// <summary>
        /// 样式牌 id → <see cref="cardFaces"/> 下标。
        /// 基础牌 500–505 → 0–5（I 级 500–503、II 级 504–505），企业扩展牌 550–555 → 6–11；其余 id 返回 -1。
        /// 注意：下标按 **id** 排，不是按拼版图从左到右、从上到下的顺序 —— 拼版图里 id 顺序是乱的，
        /// 而且 lv1 表中间还夹着一格要丢弃的重复样张。
        /// </summary>
        public static int SpriteIndexOf(int cardId)
        {
            if (cardId >= CardIds.CityPatternBase && cardId < CardIds.CityPatternBase + CardCounts.CityPatterns)
                return cardId - CardIds.CityPatternBase;
            if (cardId >= CardIds.CityPatternEnterpriseBase
                && cardId < CardIds.CityPatternEnterpriseBase + CardCounts.CityPatternEnterprise)
                return CardCounts.CityPatterns + (cardId - CardIds.CityPatternEnterpriseBase);
            return -1;
        }

        /// <summary>样式牌的显示名：优先用已录入的牌名，未录入时退回等级占位。</summary>
        private static string LabelOf(GameState state, int cardId)
        {
            CityPatternCardDef card = state.Catalog.CityPattern(cardId);
            if (card == null) return $"样式牌 {cardId}";
            if (!string.IsNullOrEmpty(card.Name)) return card.Name;
            return card.Level >= 2 ? "II 级样式牌" : "I 级样式牌";
        }
    }
}
