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
    }

    /// <summary>
    /// 操作面板：只管「发出意图」，不含任何规则判断。
    ///
    /// 按规则规格 §6 分三块：
    /// - 快速行动（使用角色牌 / 宣告城市样式）：主要行动前后可做任意次；
    /// - 主要行动（部署 / 调度 / 探索 / 城市移动 / 建设 / 特殊行动）：每轮 6 选 1；
    /// - 流程与子步骤：结束回合，以及行动进行中的「确认 / 取消」。
    ///
    /// 其余尚未接通的行动若挂在 <see cref="notImplementedButtons"/> 上，会自动置灰并标注「待实现」。
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
        [Tooltip("特殊行动（§6.3f）：点按钮 → 点场上一张已宣告过的城市样式牌 → 付费用并结算其解锁的特殊行动")]
        [SerializeField] private Button specialActionButton;

        [Header("快速行动（主要行动前后可做任意次，§6.3）")]
        [Tooltip("使用角色牌（§6.3a）：点按钮弹出本机手牌 → 选一张 → 再选发动「策略」或「计谋」")]
        [SerializeField] private Button characterCardButton;
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
        [Tooltip("特殊行动")]
        [SerializeField] private Button[] notImplementedButtons = Array.Empty<Button>();

        [Tooltip("引导文本，如「已选 1/2 步」")]
        [SerializeField] private TMP_Text hintText;

        public event Action<ActionMode> ModeRequested;
        public event Action CommitRequested;
        public event Action CancelRequested;
        public event Action AdvancePhaseRequested;

        /// <summary>点了「使用角色牌」：轮到 GameScreenController 弹手牌 / 选策略计谋（§6.3a）。</summary>
        public event Action CharacterCardRequested;

        /// <summary>除「取消」外的按钮：建设选位时把它们全部锁死，只留取消当返回键。</summary>
        private readonly List<Button> gatedButtons = new List<Button>();

        private ActionMode currentMode = ActionMode.None;
        private bool locked;

        private void Awake()
        {
            On(placeButton, () => ModeRequested?.Invoke(ActionMode.Place));
            On(relocateButton, () => ModeRequested?.Invoke(ActionMode.Relocate));
            On(exploreButton, () => ModeRequested?.Invoke(ActionMode.Explore));
            On(moveCityButton, () => ModeRequested?.Invoke(ActionMode.MoveCity));
            On(buildButton, () => ModeRequested?.Invoke(ActionMode.Build));
            On(specialActionButton, () => ModeRequested?.Invoke(ActionMode.SpecialAction));
            On(characterCardButton, () => CharacterCardRequested?.Invoke());
            On(declarePatternButton, () => ModeRequested?.Invoke(ActionMode.Declare));
            On(advanceButton, () => AdvancePhaseRequested?.Invoke());
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
            gatedButtons.Add(advanceButton);

            if (notImplementedButtons != null)
            {
                foreach (Button button in notImplementedButtons)
                {
                    if (button == null) continue;

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

        private void UpdateInteractable()
        {
            bool inAction = currentMode != ActionMode.None;

            foreach (Button button in gatedButtons)
            {
                if (button != null) button.interactable = !locked;
            }

            if (notImplementedButtons != null)
            {
                foreach (Button button in notImplementedButtons)
                {
                    if (button != null) button.interactable = false;
                }
            }

            // 取消键在锁定期间仍然可点：它是这一层子步骤的返回键。
            // 事件选项点地图作答时行动模式可能本来就是 None（如开局入场那一次），所以锁定态也要放行。
            if (cancelButton != null) cancelButton.interactable = inAction || locked;

            if (commitButton != null)
                commitButton.interactable = !locked
                    && (currentMode == ActionMode.Relocate || currentMode == ActionMode.Explore);
        }

        private static void On(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
