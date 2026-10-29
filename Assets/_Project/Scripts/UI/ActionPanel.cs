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
        Harvest = 8,   // 采集阶段选点（§7）：不挂按钮，由 GameScreenController 进入采集阶段时主动切入；此时只留「确认 / 取消」
    }

    /// <summary>
    /// 操作面板：只管「发出意图」，不含任何规则判断。
    ///
    /// 按规则规格 §6 分三块：
    /// - 快速行动（使用角色牌 / 宣告城市样式）：主要行动前后可做任意次；
    /// - 主要行动（部署 / 调度 / 探索 / 城市移动 / 建设 / 特殊行动）：每轮 6 选 1；
    /// - 流程与子步骤：行动进行中的「确认 / 取消」。阶段推进由引擎自动判断，面板上不再有「结束回合」。
    ///
    /// 基础版 6 个主要行动 + 2 个快速行动已全部实现，没有「待实现」按钮。
    ///
    /// 现在这块面板整体是一个**弹窗**：常驻操作区里那颗「行动」按钮（<see cref="openButton"/>）负责开合。
    /// 入口按钮**常驻不动** —— 展开、收起都不会把它藏起来，它就一直摆在操作区里，玩家随时能再点。
    /// 弹窗里另有「关闭」键（<see cref="closeButton"/>）也能收起。
    /// 点弹窗里的行动按钮**不会**自动收起 —— 面板停在屏幕边上不挡地图，提示文本与「确认 / 取消」全程可用。
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

        [Tooltip("弹窗里的「关闭」按钮：收起弹窗。入口按钮不会消失，所以这颗只当顺手关一下的快捷键")]
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

        [Header("行动中的子步骤")]
        [Tooltip("确认：把已排定的调度步骤落地。只有「调度」是分步行动，所以只在调度模式下可点。")]
        [SerializeField] private Button commitButton;
        [Tooltip("取消：退出当前行动，并丢弃已做的选点。选中任一行动后都可点。")]
        [SerializeField] private Button cancelButton;

        [Tooltip("引导文本，如「已选 1/2 步」")]
        [SerializeField] private TMP_Text hintText;

        public event Action<ActionMode> ModeRequested;
        public event Action CommitRequested;
        public event Action CancelRequested;

        /// <summary>点了「使用角色牌」：轮到 GameScreenController 弹手牌 / 选策略计谋（§6.3a）。</summary>
        public event Action CharacterCardRequested;

        /// <summary>除「取消」外的按钮：建设选位时把它们全部锁死，只留取消当返回键。</summary>
        private readonly List<Button> gatedButtons = new List<Button>();

        private ActionMode currentMode = ActionMode.None;
        private bool locked;

        /// <summary>不可点时按钮文字用的灰褐色，跟 Button 的 Disabled Color 配套。</summary>
        private static readonly Color DisabledLabelColor = new Color(0.42f, 0.40f, 0.36f, 1f);

        private TMP_Text commitLabel;
        private TMP_Text cancelLabel;
        private Color commitLabelColor;
        private Color cancelLabelColor;

        private void Awake()
        {
            // 入口按钮：只在这个字段接了、并且按钮的 On Click 里没手工接过 ToggleVisible 的时候才自动绑。
            // 不然场景里接一次、代码里再绑一次，一次点击会开关两次 —— 表现就是「点了没反应」。
            if (openButton != null && openButton.onClick.GetPersistentEventCount() == 0)
                openButton.onClick.AddListener(ToggleVisible);
            On(closeButton, Hide);

            On(placeButton, () => ModeRequested?.Invoke(ActionMode.Place));
            On(relocateButton, () => ModeRequested?.Invoke(ActionMode.Relocate));
            On(exploreButton, () => ModeRequested?.Invoke(ActionMode.Explore));
            On(moveCityButton, () => ModeRequested?.Invoke(ActionMode.MoveCity));
            On(buildButton, () => ModeRequested?.Invoke(ActionMode.Build));
            On(specialActionButton, () => ModeRequested?.Invoke(ActionMode.SpecialAction));
            On(declarePatternButton, () => ModeRequested?.Invoke(ActionMode.Declare));
            On(characterCardButton, () => CharacterCardRequested?.Invoke());
            On(commitButton, () => CommitRequested?.Invoke());
            On(cancelButton, () => CancelRequested?.Invoke());

            gatedButtons.Add(placeButton);
            gatedButtons.Add(relocateButton);
            gatedButtons.Add(exploreButton);
            gatedButtons.Add(moveCityButton);
            gatedButtons.Add(buildButton);
            gatedButtons.Add(specialActionButton);
            gatedButtons.Add(characterCardButton);
            gatedButtons.Add(declarePatternButton);

            // 确认 / 取消常驻显示，只靠 interactable 控制能否点击，所以先记住各自的正常文字色。
            commitLabel = commitButton != null ? commitButton.GetComponentInChildren<TMP_Text>(true) : null;
            cancelLabel = cancelButton != null ? cancelButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (commitLabel != null) commitLabelColor = commitLabel.color;
            if (cancelLabel != null) cancelLabelColor = cancelLabel.color;

            SetMode(ActionMode.None);
            Hide(); // 弹窗默认收起，主界面上只留那个「行动」按钮
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
        }

        /// <summary>收起弹窗。入口按钮从头到尾没被藏起来，所以这里也不用把它放回来。</summary>
        public void Hide()
        {
            if (popupRoot != null) popupRoot.SetActive(false);
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

        /// <summary>
        /// 切换当前行动模式：
        /// 「取消」只要选中了任一行动就可用（用来退出当前行动）；
        /// 「确认」用于分步执行、需要先排定再落地的行动 —— 调度的 1–2 步，以及探索先选目标点与路线。
        /// </summary>
        public void SetMode(ActionMode mode)
        {
            currentMode = mode;
            UpdateInteractable();
        }

        /// <summary>
        /// 建设走到「选位 / 确认 / 入场效果」这些层层递进的子步骤时，把别的行动按钮全部锁死，
        /// 只留「取消」当返回键 —— 免得玩家中途改点别的行动，把这次建设做到一半的进度作废。
        /// </summary>
        public void SetLocked(bool value)
        {
            locked = value;
            UpdateInteractable();
        }

        /// <summary>
        /// 刷新按钮的可用状态（两个键常驻显示，不能点时变灰且点不动）：
        /// 「确认」只在调度 / 探索 / 采集这三个「先排定再落地」的行动里可用；
        /// 「取消」是返回 / 退出键，只在行动进行中、或多步流程把面板锁住时可用。
        /// </summary>
        private void UpdateInteractable()
        {
            bool inAction = currentMode != ActionMode.None;

            foreach (Button button in gatedButtons)
            {
                if (button != null) button.interactable = !locked;
            }

            // 确认键用于「先排定再落地」的分步行动：调度、探索、采集。
            // 采集阶段虽会把其它按钮锁死，但仍要留「确认」推进到下一位玩家，所以采集模式下无视锁定态。
            bool needsCommit = currentMode == ActionMode.Relocate
                || currentMode == ActionMode.Explore
                || currentMode == ActionMode.Harvest;

            if (commitButton != null)
            {
                commitButton.gameObject.SetActive(true);
                commitButton.interactable = needsCommit && (!locked || currentMode == ActionMode.Harvest);
                SetLabelEnabled(commitLabel, commitLabelColor, commitButton.interactable);
            }

            // 取消键在锁定期间仍然可点：它是这一层子步骤的返回键。
            // 事件选项点地图作答时行动模式可能本来就是 None（如开局入场那一次），所以锁定态也要放行。
            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(true);
                cancelButton.interactable = inAction || locked;
                SetLabelEnabled(cancelLabel, cancelLabelColor, cancelButton.interactable);
            }
        }

        /// <summary>按钮不可点时把文字一起压灰 —— Color Tint 只染按钮自己的 Image，管不到子物体上的文字。</summary>
        private static void SetLabelEnabled(TMP_Text label, Color idleColor, bool enabled)
        {
            if (label == null) return;
            label.color = enabled ? idleColor : DisabledLabelColor;
        }

        private static void On(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
