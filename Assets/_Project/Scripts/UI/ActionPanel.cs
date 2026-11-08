using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>玩家当前选中的行动模式。</summary>
    public enum ActionMode
    {
        None = 0,
        Place = 1,     // 部署
        Relocate = 2,  // 调度
        MoveCity = 3,  // 城市移动
        Explore = 4,   // 探索
        Build = 5,     // 建设
        Declare = 6,   // 宣告城市样式（快速行动，§6.3b）：弹出场上的 6 张样式牌供挑选
        SpecialAction = 7, // 使用城市样式特殊行动（主要行动，§6.3f）：点「特殊行动」→ 点场上一张样式牌 → 结算
        Harvest = 8,   // 采集阶段选点（§7）：不挂按钮，由 GameScreenController 进入采集阶段时主动切入；提交由采集确认窗收尾
    }

    /// <summary>
    /// 操作面板：只管「发出意图」，不含任何规则判断。
    ///
    /// 按规则规格 §6 分三块：
    /// - 快速行动（使用角色牌 / 宣告城市样式）：主要行动前后可做任意次；
    /// - 主要行动（部署 / 调度 / 探索 / 城市移动 / 建设 / 特殊行动）：每轮 6 选 1；
    /// - 执行前的确认：每个行动真正落地前都会弹一个「确定 / 取消」确认窗（由 GameScreenController 的
    ///   <see cref="ChoicePopup"/> 负责），所以这块面板上不再放「确认 / 取消」两颗子步骤按钮。
    ///   主要行动做完后回合不会自动结束：玩家还能任意次执行快速行动，做完点「结束回合」交给下一位（§6.1）。
    ///
    /// 基础版 6 个主要行动 + 2 个快速行动已全部实现，没有「待实现」按钮。
    ///
    /// 现在这块面板整体是一个**弹窗**：常驻操作区里那颗「行动」按钮（<see cref="openButton"/>）负责开合。
    /// 入口按钮**常驻不动** —— 展开、收起都不会把它藏起来，它就一直摆在操作区里，玩家随时能再点。
    /// 弹窗里另有「关闭」键（<see cref="closeButton"/>）也能收起。
    /// 点弹窗里的行动按钮**不会**自动收起 —— 面板停在屏幕边上不挡地图，提示文本全程可读。
    /// 右上角的「关闭」键与其它弹窗同一个样式：平时收起弹窗；正在展开某个行动的子步骤时，它充当「返回上一步」。
    /// </summary>
    public sealed class ActionPanel : MonoBehaviour
    {
        [Header("弹窗")]
        [Tooltip("弹窗的根节点：展开 / 收起时开关它。把下面全部行动按钮与提示文本都放进这个节点下面")]
        [SerializeField] private GameObject popupRoot;

        [Tooltip("常驻的「行动」按钮：点它开 / 收弹窗。这颗按钮永远摆在操作区里，不会被藏起来。" +
                 "两种接法二选一：① 只把按钮拖进这个字段；② 这个字段留空，改用按钮 On Click 接 ToggleVisible。" +
                 "两种都接会一次点击开关两次，看着像没反应。")]
        [SerializeField] private Button openButton;

        [Tooltip("弹窗右上角的「关闭」按钮：平时收起弹窗；正在选行动 / 多步流程锁住面板时当「返回上一步」")]
        [SerializeField] private Button closeButton;

        [Header("主要行动：引擎已实现")]
        [Tooltip("部署（§6.4a）")]
        [SerializeField] private Button placeButton;
        [Tooltip("调度（§6.4b）")]
        [SerializeField] private Button relocateButton;
        [Tooltip("探索（§6.4c）：点一个没有指示物的资源点 → 付路费翻事件牌 → 在目标点放影响力")]
        [SerializeField] private Button exploreButton;
        [Tooltip("城市移动（§6.4d）")]
        [SerializeField] private Button moveCityButton;
        [Tooltip("建设（§6.4e）：在设施供应区点一张牌 → 点自己城市面板的空位 → 按需要的费用支付")]
        [SerializeField] private Button buildButton;
        [Tooltip("特殊行动（§6.3f）：点按钮 → 点场上一张已宣告过的城市样式牌 → 付费用并结算其解锁的特殊行动")]
        [SerializeField] private Button specialActionButton;

        [Header("快速行动（主要行动前后可做任意次，§6.3）")]
        [Tooltip("使用角色牌（§6.3a）：点按钮弹出本机手牌 → 选一张 → 再选发动「策略」或「计谋」")]
        [SerializeField] private Button characterCardButton;
        [Tooltip("宣告城市样式（§6.3b）：点按钮弹出场上的 6 张城市样式牌 → 选中一张 → 引擎自动匹配组合并结算")]
        [SerializeField] private Button declarePatternButton;

        [Tooltip("引导文本，如「已选 1/2 步」")]
        [SerializeField] private TMP_Text hintText;

        [Tooltip("结束回合（§6.1）：做完 1 次主要行动后，随时点它把行动权交给下一位。留空时运行时会自动建一颗。")]
        [SerializeField] private Button endTurnButton;

        public event Action<ActionMode> ModeRequested;

        /// <summary>
        /// 右上角「关闭」键在子步骤里被按下：交给 GameScreenController 当「返回上一步 / 退出当前行动」。
        /// 没有子步骤在跑时不会触发，关闭键只是收掉弹窗。
        /// </summary>
        public event Action CancelRequested;

        /// <summary>点了「使用角色牌」：轮到 GameScreenController 弹手牌 / 选策略计谋（§6.3a）。</summary>
        public event Action CharacterCardRequested;

        /// <summary>点了「结束回合」：轮到 GameScreenController 弹确认窗并派发 <c>EndTurnCommand</c>（§6.1）。</summary>
        public event Action EndTurnRequested;

        /// <summary>八个行动按钮：建设选位这类层层递进的子步骤里把它们全部锁死，免得中途改点别的行动。</summary>
        private readonly List<Button> gatedButtons = new List<Button>();

        private ActionMode currentMode = ActionMode.None;
        private bool locked;

        /// <summary>「结束回合」是否可点：没做过主要行动 / 不是自己的回合时置灰。</summary>
        private bool endTurnAvailable;

        // 与图鉴 / 聊天 / 行动记录同一套配色。
        private static readonly Color PopupColor = new Color(0.247f, 0.208f, 0.153f, 0.78f);
        private static readonly Color ButtonColor = new Color(0.45f, 0.36f, 0.28f, 1f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);

        private void Awake()
        {
            // 入口按钮：只在这个字段接了、并且按钮的 On Click 里没手工接过 ToggleVisible 的时候才自动绑。
            // 不然场景里接一次、代码里再绑一次，一次点击会开关两次 —— 表现就是「点了没反应」。
            if (openButton != null && openButton.onClick.GetPersistentEventCount() == 0)
                openButton.onClick.AddListener(ToggleVisible);
            On(closeButton, OnCloseClicked);

            On(placeButton, () => ModeRequested?.Invoke(ActionMode.Place));
            On(relocateButton, () => ModeRequested?.Invoke(ActionMode.Relocate));
            On(exploreButton, () => ModeRequested?.Invoke(ActionMode.Explore));
            On(moveCityButton, () => ModeRequested?.Invoke(ActionMode.MoveCity));
            On(buildButton, () => ModeRequested?.Invoke(ActionMode.Build));
            On(specialActionButton, () => ModeRequested?.Invoke(ActionMode.SpecialAction));
            On(declarePatternButton, () => ModeRequested?.Invoke(ActionMode.Declare));
            On(characterCardButton, () => CharacterCardRequested?.Invoke());

            // 「结束回合」是后加的一颗按钮：场景里没接时在运行时补建，保证老场景也能用。
            EnsureEndTurnButton();
            On(endTurnButton, () => EndTurnRequested?.Invoke());

            gatedButtons.Add(placeButton);
            gatedButtons.Add(relocateButton);
            gatedButtons.Add(exploreButton);
            gatedButtons.Add(moveCityButton);
            gatedButtons.Add(buildButton);
            gatedButtons.Add(specialActionButton);
            gatedButtons.Add(characterCardButton);
            gatedButtons.Add(declarePatternButton);

            // 配色统一（弹窗底色 / 按钮底色 / 文字色），再按新尺寸重排一次布局、把关闭键换成其它弹窗的样式。
            ApplyPalette();
            ApplyLayout();
            StyleCloseButton();

            // 面板里的按钮一律不再吃「选中态」的亮色：亮 / 暗只由弹窗开没开、当前行动模式决定。
            UiButtonTint.Ensure(openButton);
            UiButtonTint.Ensure(closeButton);
            for (int i = 0; i < gatedButtons.Count; i++) UiButtonTint.Ensure(gatedButtons[i]);
            UiButtonTint.Ensure(endTurnButton);

            SetMode(ActionMode.None);
            Hide(); // 弹窗默认收起，主界面上只留那个「行动」按钮
        }

        /// <summary>
        /// 把行动弹窗的配色统一成图鉴 / 聊天 / 行动记录那一套：
        /// 弹窗底色换成同款深棕半透明，按钮底图换成沙褐、文字换成米白。
        /// </summary>
        private void ApplyPalette()
        {
            if (popupRoot == null) return;

            if (popupRoot.TryGetComponent(out Image background)) background.color = PopupColor;

            foreach (Button button in popupRoot.GetComponentsInChildren<Button>(true))
            {
                if (button.targetGraphic is Image image) image.color = ButtonColor;
            }

            foreach (TMP_Text text in popupRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = TextColor;
            }
        }

        /// <summary>显示引导文本。</summary>
        public void SetHint(string text)
        {
            if (hintText != null) hintText.text = text ?? string.Empty;
        }

        // —— 弹窗开关 ——

        /// <summary>弹窗是不是开着。</summary>
        public bool IsVisible => popupRoot != null && popupRoot.activeSelf;

        /// <summary>展开弹窗。入口按钮常驻不动 —— 这里只开弹窗，不去碰它。</summary>
        public void Show()
        {
            // 没接「弹窗根节点」时什么都不做，免得误开一坨没归位的按钮。
            if (popupRoot == null) return;

            popupRoot.SetActive(true);
            UiButtonTint.SetLit(openButton, IsVisible);
        }

        /// <summary>收起弹窗。入口按钮从头到尾没被藏起来，所以这里也不用把它放回来。</summary>
        public void Hide()
        {
            if (popupRoot != null) popupRoot.SetActive(false);
            UiButtonTint.SetLit(openButton, IsVisible);
        }

        /// <summary>
        /// 展开 / 收起切换。给常驻操作区里那颗「行动」按钮用：点一次展开、再点一次收起。
        /// 按钮本身两种情况下都留在操作区里，不会消失。
        /// </summary>
        public void ToggleVisible()
        {
            if (IsVisible) Hide();
            else Show();
        }

        /// <summary>切换当前行动模式，只用来点亮对应的那颗按钮（执行前的确认窗由控制器负责）。</summary>
        public void SetMode(ActionMode mode)
        {
            currentMode = mode;
            UpdateInteractable();
        }

        /// <summary>
        /// 建设走到「选位 / 确认 / 入场效果」这些层层递进的子步骤时，把别的行动按钮全部锁死，
        /// 免得玩家中途改点别的行动，把这次建设做到一半的进度作废。
        /// </summary>
        public void SetLocked(bool value)
        {
            locked = value;
            UpdateInteractable();
        }

        /// <summary>
        /// 「结束回合」是否可点（§6.1）：不是自己的回合时置灰。轮到自己的时候一律可点 ——
        /// 还没做主要行动时由控制器直接给出「必须先做 1 次主要行动」的原因，比默默变灰更好懂。
        /// 真正的规则判断在引擎（<c>EndTurnCommand.Validate</c>）。
        /// </summary>
        public void SetEndTurnAvailable(bool value)
        {
            endTurnAvailable = value;
            UpdateInteractable();
        }

        /// <summary>
        /// 刷新按钮的可用状态：多步流程（建设选位等）把面板锁住时，八个行动按钮一起变灰点不动，
        /// 免得玩家中途改点别的行动，把这次的进度作废。
        /// </summary>
        private void UpdateInteractable()
        {
            foreach (Button button in gatedButtons)
            {
                if (button != null) button.interactable = !locked;
            }

            // 结束回合不是「行动」，但也得跟着多步流程一起锁死：建设中点它会把这次建设作废。
            if (endTurnButton != null) endTurnButton.interactable = endTurnAvailable && !locked;

            RefreshModeVisuals();
        }

        /// <summary>
        /// 当前行动模式对应的那颗按钮点亮，其余熄灭。
        /// 亮 / 暗只跟模式走 —— 不然点过之后 EventSystem 一直选中它，模式早就退了按钮还亮着。
        /// </summary>
        private void RefreshModeVisuals()
        {
            UiButtonTint.SetLit(placeButton, currentMode == ActionMode.Place);
            UiButtonTint.SetLit(relocateButton, currentMode == ActionMode.Relocate);
            UiButtonTint.SetLit(exploreButton, currentMode == ActionMode.Explore);
            UiButtonTint.SetLit(moveCityButton, currentMode == ActionMode.MoveCity);
            UiButtonTint.SetLit(buildButton, currentMode == ActionMode.Build);
            UiButtonTint.SetLit(specialActionButton, currentMode == ActionMode.SpecialAction);
            UiButtonTint.SetLit(declarePatternButton, currentMode == ActionMode.Declare);
        }

        /// <summary>
        /// 右上角「关闭」：没有子步骤在跑时只是收起弹窗（与其它弹窗一致）；
        /// 正在选行动、或某个多步流程把面板锁住时，先当成「返回上一步 / 退出当前行动」交给控制器。
        /// </summary>
        private void OnCloseClicked()
        {
            if (locked || currentMode != ActionMode.None)
            {
                CancelRequested?.Invoke();
                return;
            }

            Hide();
        }

        // —— 布局与样式 ——

        /// <summary>
        /// 弹窗收窄到 400×570（去掉顶部那排「确认 / 取消」后空出来的高度），
        /// 八个行动按钮整体上移：两列四行，横排「快速行动」与「主要行动」两个小标题；
        /// 最下面单独一颗「结束回合」（§6.1），不参与八颗行动按钮的互斥点亮。
        /// 只在运行时排一次，场景里就算还留着旧位置也不影响。
        /// </summary>
        private void ApplyLayout()
        {
            if (popupRoot == null) return;

            var rect = (RectTransform)popupRoot.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400f, 570f);

            // 顶边固定在原来的位置（不再是垂直居中朝两边长），所以中心点随高度缩小上移。
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 185f);

            PlaceLabel("Text1", 45f);    // 快速行动（使用角色牌 / 宣告城市样式）
            PlaceButton(characterCardButton, 100f, -110f);
            PlaceButton(declarePatternButton, 250f, -110f);

            PlaceLabel("Texr2", 180f);   // 主要行动
            PlaceButton(placeButton, 100f, -250f);
            PlaceButton(relocateButton, 250f, -250f);
            PlaceButton(exploreButton, 100f, -335f);
            PlaceButton(moveCityButton, 250f, -335f);
            PlaceButton(buildButton, 100f, -420f);
            PlaceButton(specialActionButton, 250f, -420f);
            PlaceButton(endTurnButton, 175f, -510f, 250f, 56f);
        }

        /// <summary>把一颗行动按钮摆到「距弹窗左上角 (x, y)」的位置（y 传负数）。</summary>
        private static void PlaceButton(Button button, float x, float y, float width = 100f, float height = 70f)
        {
            if (button == null) return;

            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>把分组小标题摆到距顶 <paramref name="y"/> 的位置；X 与按钮组中线对齐（-25）。</summary>
        private void PlaceLabel(string childName, float y)
        {
            Transform child = popupRoot.transform.Find(childName);
            if (child == null) return;

            var rect = (RectTransform)child;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 50f);
            rect.anchoredPosition = new Vector2(-25f, 285f - y);
        }

        /// <summary>
        /// 场景里没接「结束回合」按钮时在运行时补建一颗：老场景不用重做 UI 也能用上新流程。
        /// 样式与其它行动按钮一致（沙褐底 + 米白字），位置由 <see cref="ApplyLayout"/> 统一摆放。
        /// </summary>
        private void EnsureEndTurnButton()
        {
            if (endTurnButton != null || popupRoot == null) return;

            var go = new GameObject("EndTurnButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(popupRoot.transform, false);

            var image = go.GetComponent<Image>();
            image.color = ButtonColor;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "结束回合";
            label.fontSize = 20f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = TextColor;
            label.raycastTarget = false;
            if (hintText != null && hintText.font != null) label.font = hintText.font;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            endTurnButton = button;
        }

        /// <summary>
        /// 把右上角的「关闭」键改成与图鉴 / 设施 / 城市样式弹窗同款：贴右上角 72×32 的小按钮，底沙褐、字米白。
        /// 场景里那颗只有一块底图，文字是这里补上的。
        /// </summary>
        private void StyleCloseButton()
        {
            if (closeButton == null) return;

            var rect = (RectTransform)closeButton.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-14f, -14f);
            rect.sizeDelta = new Vector2(72f, 32f);

            if (closeButton.targetGraphic is Image image)
            {
                // 场景里原来贴的是 X 图标；与其它弹窗一致，换成纯沙褐底 + 「关闭」两个字。
                image.sprite = null;
                image.color = ButtonColor;
            }
            UiButtonTint.Ensure(closeButton);

            if (closeButton.GetComponentInChildren<TMP_Text>(true) != null) return;

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
            if (hintText != null && hintText.font != null) label.font = hintText.font;
        }

        private static void On(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
