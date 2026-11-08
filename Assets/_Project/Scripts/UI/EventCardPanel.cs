using System;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 事件卡面板：中央模态弹窗，展示翻开的事件卡卡面，选项热区叠在卡面「事件区」的选项行上。
    /// 与 <see cref="ActionPanel"/> 一样只负责展示与发出意图，不做任何规则判断 ——
    /// 玩家点了哪一行，通过 <see cref="OptionSelected"/> 交给 GameScreenController 去派发命令。
    ///
    /// 卡面上的卡名、描述文、选项文案与奖励图标都印在贴图里，所以这里只贴一张整卡 sprite，
    /// 不再单独渲染文字（也因此不会踩 TMP 缺字）。
    /// </summary>
    public sealed class EventCardPanel : MonoBehaviour, IDragHandler
    {
        [Tooltip("整块弹窗的根节点；Show/Hide 直接开关它")]
        [SerializeField] private GameObject root;

        [Tooltip("半透明遮罩：展开时是功能性元素，保留 Raycast Target 用来挡住地图点击；最小化后会被放开，好让地图能缩放 / 拖动")]
        [SerializeField] private Image dim;

        [Header("卡面")]
        [Tooltip("显示卡面的 Image（装饰用，Raycast Target 关闭）")]
        [SerializeField] private Image cardFace;

        [Tooltip("按事件牌 id 顺序排列的卡面 sprite：400–421 → 0–21，企业替换牌 450/451 → 22/23")]
        [SerializeField] private Sprite[] cardFaces = Array.Empty<Sprite>();

        [Header("选项热区（两套独立布局）")]
        [Tooltip("2 选项卡片的选项热区，共 2 个按钮。位置直接在场景里拖好，运行时不改位置。")]
        [SerializeField] private Button[] twoOptionHotzones = Array.Empty<Button>();

        [Tooltip("3 选项卡片的选项热区，共 3 个按钮。位置直接在场景里拖好，运行时不改位置。")]
        [SerializeField] private Button[] threeOptionHotzones = Array.Empty<Button>();

        [Tooltip("可选：面板上的提示行")]
        [SerializeField] private TMP_Text statusText;

        [Header("悬停反馈")]
        [Tooltip("鼠标停在选项行上时的叠加色。热区平时是 A=0 的全透明，所以这里靠直接改 Image 的 Color 来发亮" +
                 "（Button 自带的颜色过渡是乘法运算，0 乘任何颜色还是 0，对全透明热区无效）。把 A 改成 0 就等于关掉这个效果。")]
        [SerializeField] private Color hoverTint = new Color(1f, 1f, 1f, 0.18f);

        private Image[] hotzoneGraphics = Array.Empty<Image>();
        private Color[] hotzoneIdleColors = Array.Empty<Color>();

        /// <summary>每个热区对应的「选项下标」（两组各自从 0 起），用于判断某一行是不是当前选中行。</summary>
        private int[] hotzoneOptionIndices = Array.Empty<int>();

        /// <summary>已选中、等待第二次点击确认的选项下标；-1 表示没选。</summary>
        private int selectedIndex = -1;

        private Canvas canvas;

        /// <summary>场景里摆好的遮罩颜色（展开态），最小化时只是临时改透明度，展开要还原成它。</summary>
        private Color dimIdleColor;

        [Header("拖动与最小化")]
        [Tooltip("会被拖动 / 折叠的卡面物体（Card）。拖的只是它的 Anchored Position，满屏遮罩不动。")]
        [SerializeField] private RectTransform cardRect;

        [Tooltip("卡面角落的最小化按钮。做成 Card 的子物体，折叠时会跟着卡面一起隐藏。")]
        [SerializeField] private Button minimizeButton;

        [Tooltip("折叠后显示的小横条，自带 Button，点它展开。做成 Root 的子物体，位置按你想让它出现的地方摆。")]
        [SerializeField] private Button collapsedBar;

        [Tooltip("选项「选中但还没确认」时那一行的高亮色。")]
        [SerializeField] private Color selectedTint = new Color(1f, 1f, 1f, 0.35f);

        [Tooltip("最小化后遮罩的透明度：0 = 完全透明（地图最清楚），0.5 = 还留一层暗色。")]
        [SerializeField, Range(0f, 1f)] private float collapsedDimAlpha;

        /// <summary>玩家确认了第几个选项（0 起）；由 GameScreenController 接住并派发命令。</summary>
        public event Action<int> OptionSelected;

        private void Awake()
        {
            // 展开时遮罩是功能性 UI，必须挡住点击（卡面之类的装饰元素才取消 Raycast Target）。
            if (dim != null)
            {
                dimIdleColor = dim.color;
                dim.raycastTarget = true;
            }

            canvas = GetComponentInParent<Canvas>();

            if (minimizeButton != null) minimizeButton.onClick.AddListener(Collapse);
            if (collapsedBar != null) collapsedBar.onClick.AddListener(Expand);

            int total = twoOptionHotzones.Length + threeOptionHotzones.Length;
            hotzoneGraphics = new Image[total];
            hotzoneIdleColors = new Color[total];
            hotzoneOptionIndices = new int[total];

            int next = WireGroup(twoOptionHotzones, 0);
            WireGroup(threeOptionHotzones, next);

            Expand(); // 先归位到「展开」，避免场景里残留的折叠状态带进运行期
            Hide();
        }

        /// <summary>
        /// 给一组热区装点击与悬停：组内第 i 个按钮对应选项下标 i（两组各自从 0 起），
        /// 悬停用的 Image 存进按「组拼接」的公共下标 <paramref name="storageOffset"/>。
        /// 返回下一组的起始存储下标。
        /// </summary>
        private int WireGroup(Button[] hotzones, int storageOffset)
        {
            for (int i = 0; i < hotzones.Length; i++)
            {
                Button button = hotzones[i];
                if (button == null) continue;

                int optionIndex = i; // 闭包捕获：每个热区绑定自己的选项下标
                button.onClick.AddListener(() => OnOptionClicked(optionIndex));

                SetUpHover(storageOffset + i, optionIndex, button);
            }

            return storageOffset + hotzones.Length;
        }

        /// <summary>
        /// 激活一组热区中的前 <paramref name="count"/> 个，其余隐藏（<paramref name="count"/> 传 0 即整组不用）。
        /// </summary>
        private static void ActivateGroup(Button[] hotzones, int count)
        {
            for (int i = 0; i < hotzones.Length; i++)
            {
                if (hotzones[i] != null) hotzones[i].gameObject.SetActive(i < count);
            }
        }

        /// <summary>
        /// 给热区装悬停反馈：鼠标停在选项行上时把热区自己的 Image 染成 <see cref="hoverTint"/>，离开时还原成原样。
        /// 不用 Button 自带的「颜色过渡」，因为那是把颜色<b>乘</b>到画面上，而热区平时 A=0，乘完还是 0。
        /// </summary>
        private void SetUpHover(int index, int optionIndex, Button button)
        {
            Image graphic = button.targetGraphic as Image;
            if (graphic == null) graphic = button.GetComponent<Image>();
            if (graphic == null) return;

            button.transition = Selectable.Transition.None; // 高亮改由这里接管，避免两套状态互相打架

            Color idle = graphic.color;
            hotzoneGraphics[index] = graphic;
            hotzoneIdleColors[index] = idle;
            hotzoneOptionIndices[index] = optionIndex;

            EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => graphic.color = RowColor(index, true));
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => graphic.color = RowColor(index, false));
            trigger.triggers.Add(exit);
        }

        /// <summary>
        /// 某一行的颜色：选中态优先（再点一次就能确认的那一行），否则鼠标悬停时发亮，其余为原样。
        /// </summary>
        private Color RowColor(int index, bool hovered)
        {
            if (index == selectedIndex) return selectedTint;
            return hovered ? hoverTint : hotzoneIdleColors[index];
        }

        /// <summary>把所有热区还原成「未悬停、但保留选中」的样子。</summary>
        private void RefreshRowColors()
        {
            for (int i = 0; i < hotzoneGraphics.Length; i++)
            {
                if (hotzoneGraphics[i] != null) hotzoneGraphics[i].color = RowColor(i, false);
            }
        }

        /// <summary>弹出指定事件卡，并只启用前 <paramref name="optionCount"/> 个选项热区。</summary>
        public void Show(int cardId, int optionCount)
        {
            if (root != null) root.SetActive(true);

            Expand();              // 每张新卡都从展开状态开始
            selectedIndex = -1;    // 并且没有预选项

            if (cardFace != null)
            {
                int index = SpriteIndexOf(cardId);
                Sprite face = index >= 0 && index < cardFaces.Length ? cardFaces[index] : null;
                cardFace.sprite = face;
                cardFace.enabled = face != null;
            }

            RefreshRowColors();

            // 两套布局二选一：位置都由场景里各自摆好的 RectTransform 决定，这里只开关。
            bool threeOptions = optionCount >= 3;
            ActivateGroup(twoOptionHotzones, threeOptions ? 0 : optionCount);
            ActivateGroup(threeOptionHotzones, threeOptions ? optionCount : 0);

            SetStatus("先点一下要选的选项行，再点同一行确认。");
        }

        public void Hide()
        {
            selectedIndex = -1;
            SetStatus(null);
            if (root != null) root.SetActive(false);
        }

        /// <summary>
        /// 选项行点击：第一次点 = 选中（高亮），再点同一行才 = 确认结算，防止手滑。
        /// 点别的行只会改选，不会结算。
        /// </summary>
        private void OnOptionClicked(int optionIndex)
        {
            if (optionIndex == selectedIndex)
            {
                selectedIndex = -1;
                RefreshRowColors();
                OptionSelected?.Invoke(optionIndex);
                return;
            }

            selectedIndex = optionIndex;
            RefreshRowColors();
            SetStatus($"已选中第 {optionIndex + 1} 项，再点一次同一行确认。");
        }

        /// <summary>当前是不是折叠状态。</summary>
        public bool IsCollapsed => cardRect != null && !cardRect.gameObject.activeSelf;

        /// <summary>
        /// 弹窗是不是还开着（展开或最小化都算）。
        /// GameScreenController 用它在地图点击时早退 —— 事件还没结算完就不该去点地图上的资源点。
        /// </summary>
        public bool IsVisible => root != null && root.activeSelf;

        /// <summary>最小化：收起卡面，只留下小横条，并放开遮罩让地图能缩放 / 拖动。</summary>
        public void Collapse()
        {
            if (cardRect != null) cardRect.gameObject.SetActive(false);
            if (collapsedBar != null) collapsedBar.gameObject.SetActive(true);
            SetDimBlocking(false);
        }

        /// <summary>展开卡面，并收掉小横条；遮罩重新恢复成「挡住地图」。</summary>
        public void Expand()
        {
            if (collapsedBar != null) collapsedBar.gameObject.SetActive(false);
            if (cardRect != null) cardRect.gameObject.SetActive(true);
            SetDimBlocking(true);
        }

        /// <summary>
        /// 遮罩的两种状态：
        /// 展开 = 保留 Raycast Target（挡住地图点击）+ 用场景里摆好的暗色；
        /// 最小化 = 放开拦截（这样滚轮缩放、右键平移地图才能生效）+ 调淡到 <see cref="collapsedDimAlpha"/>，
        /// 保证最小化后能一边看地图一边决定选项。
        /// 注意：最小化后遮罩不再拦截点击，所以此时只有抓住小横条本身才能拖动它，拖地图背景不会带走它。
        /// </summary>
        private void SetDimBlocking(bool blocking)
        {
            if (dim == null) return;

            Color color = dimIdleColor;
            color.a = blocking ? dimIdleColor.a : collapsedDimAlpha;
            dim.color = color;
            dim.raycastTarget = blocking;
        }

        /// <summary>拖动作用于当前看得见的那个：展开时是卡面，折叠时是小横条。</summary>
        private RectTransform DragTarget =>
            IsCollapsed && collapsedBar != null ? collapsedBar.transform as RectTransform : cardRect;

        /// <summary>
        /// 右键按住拖动改位置。左键留给「点选项」—— UGUI 的 Button 只响应左键，
        /// 所以这里判一下按键，两边不会打架。
        /// 因为事件卡卡面的图片是关闭 Raycast Target 的装饰元素，按在卡面上时事件实际落在遮罩上，
        /// 再往上冒泡到这里；也就是说整块遮罩（卡面周围那片暗区）右键拖动同样会移动卡面。
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;

            RectTransform target = DragTarget;
            if (target == null) return;

            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            target.anchoredPosition += eventData.delta / scale;
        }

        private void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text ?? string.Empty;
        }

        /// <summary>按事件牌 id 顺序排列的卡面（图鉴的事件卡页复用）。</summary>
        public Sprite[] CardFaces => cardFaces;

        /// <summary>
        /// 事件牌 id → <see cref="cardFaces"/> 下标。
        /// 基础牌 400–421 → 0–21（按素材拼版图从左到右、从上到下编号）；
        /// 企业扩展替换牌 450/451 → 22/23；其余 id 返回 -1。
        /// </summary>
        public static int SpriteIndexOf(int cardId)
        {
            if (cardId >= CardIds.EventBase && cardId < CardIds.EventBase + CardCounts.EventCards)
                return cardId - CardIds.EventBase;

            if (cardId >= CardIds.EventEnterpriseBase
                && cardId < CardIds.EventEnterpriseBase + CardIds.EventEnterpriseCount)
                return CardCounts.EventCards + (cardId - CardIds.EventEnterpriseBase);

            return -1;
        }
    }
}
