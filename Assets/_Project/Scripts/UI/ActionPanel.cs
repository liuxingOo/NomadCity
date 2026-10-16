using System;
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
    }

    /// <summary>
    /// 操作面板：只管「发出意图」，不含任何规则判断。
    ///
    /// 按规则规格 §6 分三块：
    /// - 快速行动（使用角色牌 / 宣告城市样式）：主要行动前后可做任意次；
    /// - 主要行动（部署 / 调度 / 探索 / 城市移动 / 建设 / 特殊行动）：每轮 6 选 1；
    /// - 流程与子步骤：结束回合，以及行动进行中的「确认 / 取消」。
    ///
    /// 引擎尚未实现的行动（特殊行动 / 使用角色牌）
    /// 会自动置灰并标注「待实现」，等对应规则与卡面文案就绪后再接。
    /// </summary>
    public sealed class ActionPanel : MonoBehaviour
    {
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

        [Header("快速行动（主要行动前后可做任意次，§6.3）")]
        [Tooltip("宣告城市样式（§6.3b）：点按钮弹出场上的 6 张城市样式牌 → 选中一张 → 引擎自动匹配组合并结算")]
        [SerializeField] private Button declarePatternButton;

        [Header("流程")]
        [Tooltip("结束回合 / 推进阶段。注意：它不是规则里的行动，而是阶段流转（§5）。")]
        [SerializeField] private Button advanceButton;

        [Header("行动中的子步骤")]
        [Tooltip("确认：把已排定的调度步骤落地。只有「调度」是分步行动，所以只在调度模式下可点。")]
        [SerializeField] private Button commitButton;
        [Tooltip("取消：退出当前行动，并丢弃已做的选点。选中任一行动后都可点。")]
        [SerializeField] private Button cancelButton;

        [Header("待实现的行动（自动置灰并标注「待实现」）")]
        [Tooltip("特殊行动 / 使用角色牌")]
        [SerializeField] private Button[] notImplementedButtons = Array.Empty<Button>();

        [Tooltip("引导文本，如「已选 1/2 步」")]
        [SerializeField] private TMP_Text hintText;

        public event Action<ActionMode> ModeRequested;
        public event Action CommitRequested;
        public event Action CancelRequested;
        public event Action AdvancePhaseRequested;

        private void Awake()
        {
            On(placeButton, () => ModeRequested?.Invoke(ActionMode.Place));
            On(relocateButton, () => ModeRequested?.Invoke(ActionMode.Relocate));
            On(exploreButton, () => ModeRequested?.Invoke(ActionMode.Explore));
            On(moveCityButton, () => ModeRequested?.Invoke(ActionMode.MoveCity));
            On(buildButton, () => ModeRequested?.Invoke(ActionMode.Build));
            On(declarePatternButton, () => ModeRequested?.Invoke(ActionMode.Declare));
            On(advanceButton, () => AdvancePhaseRequested?.Invoke());
            On(commitButton, () => CommitRequested?.Invoke());
            On(cancelButton, () => CancelRequested?.Invoke());

            if (notImplementedButtons != null)
            {
                foreach (Button button in notImplementedButtons)
                {
                    if (button == null) continue;
                    button.interactable = false;

                    TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                    if (label != null && !label.text.Contains("待实现")) label.text += "（待实现）";
                }
            }

            SetMode(ActionMode.None);
        }

        /// <summary>显示引导文本。</summary>
        public void SetHint(string text)
        {
            if (hintText != null) hintText.text = text ?? string.Empty;
        }

        /// <summary>
        /// 切换当前行动模式：
        /// 「取消」只要选中了任一行动就可用（用来退出当前行动）；
        /// 「确认」用于分步执行、需要先排定再落地的行动 —— 调度的 1–2 步，以及探索先选目标点与路线。
        /// </summary>
        public void SetMode(ActionMode mode)
        {
            bool inAction = mode != ActionMode.None;

            if (cancelButton != null) cancelButton.interactable = inAction;
            if (commitButton != null) commitButton.interactable = mode == ActionMode.Relocate || mode == ActionMode.Explore;
        }

        private static void On(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
