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
    public sealed class FacilitySupplyPanel : MonoBehaviour
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 背景 / 牌位）。宿主自身必须保持激活，只切这一个。" +
                 "留空则面板一直显示（旧的常驻行为）。")]
        [SerializeField] private GameObject root;

        [Tooltip("6 个牌位按钮，顺序与设施供应区列表一致（下标 0 是最左边那张牌）")]
        [SerializeField] private Button[] cardButtons = Array.Empty<Button>();

        [Tooltip("每个牌位上的牌名文本，与 Card Buttons 一一对应（可选）")]
        [SerializeField] private TMP_Text[] cardLabels = Array.Empty<TMP_Text>();

        [Tooltip("每个牌位上的卡面 Image，与 Card Buttons 一一对应（可选；装饰用，Raycast Target 关掉）")]
        [SerializeField] private Image[] cardImages = Array.Empty<Image>();

        [Tooltip("设施牌卡面 sprite 库：下标 → 牌 id 的映射见 SpriteIndexOf（100–140 → 0–40，核心指挥塔 200–203 → 41–44，" +
                 "延伸枢纽 300–302 → 45–47）。按此顺序把 Assets/_Project/Art/Cards/Facility/card_facility.jpg 切出的子 Sprite 拖进来即可。")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

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

            viewOnly = false;
            if (root.activeSelf != open) root.SetActive(open);
        }

        /// <summary>面板是不是开着。</summary>
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>
        /// 只读查看开关，给常驻操作区的「设施」按钮用：
        /// 打开后六张设施牌只是资料 —— 牌位全部置为不可点，点上去不会触发建设，也不会发出 <see cref="CardClicked"/>；
        /// 再点一次关闭。建设阶段的「可操作」打开仍走 <see cref="SetOpen"/>，不受这里影响。
        /// </summary>
        public void ToggleView()
        {
            if (root == null) return;

            if (root.activeSelf && viewOnly)
            {
                SetOpen(false);
                return;
            }

            viewOnly = true;
            if (!root.activeSelf) root.SetActive(true);
            if (lastState != null) Refresh(lastState, lastInteractable);
        }

        private void Awake()
        {
            CachePlaceholders();

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
                // 只读查看时一律不可点：否则建设中点开「查看」还能顺手建设。
                button.interactable = interactable && !viewOnly && cardId >= 0;

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
