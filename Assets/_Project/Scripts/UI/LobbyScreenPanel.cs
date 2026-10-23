using System;
using System.Collections.Generic;
using NomadCity.Core.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 准备房间面板（Lobby 场景）。纯显示层：不引用 App / Net，
    /// 由 <c>LobbySceneController</c> 把 <c>OnlineSessionHost</c> 的事件翻译成这里的 Set 方法。
    ///
    /// 场景结构（建议）：
    /// Canvas
    ///   LobbyUI（挂本脚本，宿主始终激活）
    ///     Root                       ← root：整体开关
    ///       Background
    ///       TitleText
    ///       RoomCodeText
    ///       PlayerList
    ///         PlayerRow0 … PlayerRow3（各挂 LobbyPlayerRow）
    ///       HostSettings             ← hostSettingsRoot（房主可改；客户端只读）
    ///       ClientReady              ← clientReadyRoot（仅客户端显示）
    ///       StatusText
    ///       StartButton
    ///       LeaveButton
    ///
    /// 按钮的 OnClick 请在 Inspector 里手动指向本组件的 OnApplySettingsClicked / OnReadyClicked /
    /// OnStartClicked / OnLeaveClicked，不要在运行时自动绑定。
    /// </summary>
    public sealed class LobbyScreenPanel : MonoBehaviour
    {
        [Tooltip("整体开关节点；宿主（挂本脚本的物体）保持激活，只开关这个子节点")]
        [SerializeField] private GameObject root;

        [Tooltip("6 位房间码文本（TMP_Text）")]
        [SerializeField] private TMP_Text roomCodeText;

        [Tooltip("状态文本：正在登记 / 等待玩家 / 出错原因等")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("设置摘要，如「3 人 · 企业扩展已启用」")]
        [SerializeField] private TMP_Text settingsSummaryText;

        [Tooltip("固定的 4 行玩家；每行挂 LobbyPlayerRow")]
        [SerializeField] private LobbyPlayerRow[] playerRows;

        [Tooltip("设置区容器（HostSettings）。房主可改，客户端只读")]
        [SerializeField] private GameObject hostSettingsRoot;

        [Tooltip("人数滑条：Min 2 / Max 4 / Whole Numbers")]
        [SerializeField] private Slider playerCountSlider;

        [SerializeField] private Toggle enterpriseToggle;
        [SerializeField] private Button applySettingsButton;

        [Tooltip("客户端「准备 / 取消准备」区域（ClientReady）")]
        [SerializeField] private GameObject clientReadyRoot;

        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;

        [Tooltip("「开始游戏」按钮（仅房主可见）")]
        [SerializeField] private Button startButton;

        [SerializeField] private Button leaveButton;

        [Header("二次确认弹窗（防止误点直接开房 / 退房）")]
        [Tooltip("确认弹窗的开关节点；建议放在 LobbyUI 下、主面板 Root 之后，保证盖在最上层")]
        [SerializeField] private GameObject confirmRoot;

        [Tooltip("确认弹窗里的提示文本")]
        [SerializeField] private TMP_Text confirmMessageText;

        /// <summary>确认弹窗上「确定」之后要执行的动作。</summary>
        private Action pendingConfirm;

        private bool confirmMissingWarned;

        public event Action<int, bool> ApplySettingsRequested;
        public event Action ReadyToggleRequested;
        public event Action StartRequested;
        public event Action LeaveRequested;
        public event Action<string> KickRequested;

        public bool IsReady { get; private set; }

        /// <summary>名单行接线的警告只打一次。</summary>
        private bool rowsWarned;

        private void Awake()
        {
            if (confirmRoot != null) confirmRoot.SetActive(false);

            if (playerRows == null) return;

            foreach (LobbyPlayerRow row in playerRows)
            {
                if (row != null) row.KickRequested += OnRowKickRequested;
            }
        }

        private void OnDestroy()
        {
            if (playerRows == null) return;

            foreach (LobbyPlayerRow row in playerRows)
            {
                if (row != null) row.KickRequested -= OnRowKickRequested;
            }
        }

        /// <summary>房主视图：能改设置、能踢人、能点开始。</summary>
        public void ShowHostView()
        {
            Show();

            if (hostSettingsRoot != null) hostSettingsRoot.SetActive(true);
            if (clientReadyRoot != null) clientReadyRoot.SetActive(false);
            if (startButton != null) startButton.gameObject.SetActive(true);
            if (applySettingsButton != null) applySettingsButton.gameObject.SetActive(true);
            SetSettingsInteractable(true);
        }

        /// <summary>客户端视图：设置只读、显示准备按钮、没有开始按钮。</summary>
        public void ShowClientView()
        {
            Show();

            if (hostSettingsRoot != null) hostSettingsRoot.SetActive(true);
            if (clientReadyRoot != null) clientReadyRoot.SetActive(true);
            if (startButton != null) startButton.gameObject.SetActive(false);
            if (applySettingsButton != null) applySettingsButton.gameObject.SetActive(false);
            SetSettingsInteractable(false);
            SetReady(false);
        }

        public void Show()
        {
            if (root != null) root.SetActive(true);
        }

        public void SetRoomCode(string code)
        {
            if (roomCodeText != null) roomCodeText.text = string.IsNullOrEmpty(code) ? "房间码 ------ " : $"房间码 {code}";
        }

        public void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        public void SetSettings(int playerCount, bool enterprise)
        {
            if (playerCountSlider != null) playerCountSlider.SetValueWithoutNotify(Mathf.Clamp(playerCount, 2, 4));
            if (enterpriseToggle != null) enterpriseToggle.SetIsOnWithoutNotify(enterprise);

            if (settingsSummaryText != null)
                settingsSummaryText.text = $"{Mathf.Clamp(playerCount, 2, 4)} 人 · 企业扩展{(enterprise ? "已启用" : "未启用")}";
        }

        /// <summary>
        /// 刷新名单。playerCount 决定显示几行：2 人局只露前 2 行，多出来的行整行隐藏，
        /// 不要留成「等待玩家…」，否则看起来永远是 4 人房。
        /// </summary>
        public void SetMembers(IReadOnlyList<LobbyMemberInfo> members, bool canKick, int playerCount)
        {
            if (playerRows == null || playerRows.Length == 0)
            {
                WarnRowsUnwiredOnce();
                return;
            }

            int visible = Mathf.Clamp(playerCount, 2, playerRows.Length);

            for (int i = 0; i < playerRows.Length; i++)
            {
                LobbyPlayerRow row = playerRows[i];
                if (row == null)
                {
                    WarnRowsUnwiredOnce();
                    continue;
                }

                bool used = i < visible;
                row.gameObject.SetActive(used);
                if (!used) continue;

                if (members != null && i < members.Count) row.SetMember(members[i], canKick);
                else row.SetEmpty();
            }
        }

        /// <summary>名单行漏接线时只提示一次，避免每 2 秒刷屏。</summary>
        private void WarnRowsUnwiredOnce()
        {
            if (rowsWarned) return;

            rowsWarned = true;
            Debug.LogWarning("[准备房间] Player Rows 数组没有接线（或里面有空的元素）：名单不会显示。" +
                             "请把 PlayerRow0…3 依次拖进 LobbyScreenPanel 的 Player Rows，并确认每行挂了 LobbyPlayerRow。", this);
        }

        /// <summary>开始按钮常驻，只切换可点状态，避免布局跳动。</summary>
        public void SetCanStart(bool canStart)
        {
            if (startButton != null) startButton.interactable = canStart;
        }

        public void SetReady(bool ready)
        {
            IsReady = ready;
            if (readyButtonLabel != null) readyButtonLabel.text = ready ? "取消准备" : "准备";
        }

        /// <summary>开始流程锁定后，房主也不能再改设置。</summary>
        public void SetSettingsInteractable(bool interactable)
        {
            if (playerCountSlider != null) playerCountSlider.interactable = interactable;
            if (enterpriseToggle != null) enterpriseToggle.interactable = interactable;
            if (applySettingsButton != null) applySettingsButton.interactable = interactable;
        }

        // —— Inspector 手动绑定 ——

        public void OnApplySettingsClicked()
        {
            int playerCount = playerCountSlider != null
                ? Mathf.RoundToInt(playerCountSlider.value)
                : 4;
            bool enterprise = enterpriseToggle != null && enterpriseToggle.isOn;
            ApplySettingsRequested?.Invoke(playerCount, enterprise);
        }

        public void OnReadyClicked() => ReadyToggleRequested?.Invoke();

        /// <summary>「开始游戏」按钮：先弹二次确认，避免误点直接把整房人拉进对局。</summary>
        public void OnStartClicked()
        {
            AskConfirm("是否开始游戏？", () => StartRequested?.Invoke());
        }

        /// <summary>「返回主菜单」按钮：先弹二次确认，避免误点直接退房。</summary>
        public void OnLeaveClicked()
        {
            AskConfirm("是否返回主菜单？", () => LeaveRequested?.Invoke());
        }

        /// <summary>弹出确认窗；确认窗没接线时退回「点了就执行」，并在 Console 点名。</summary>
        private void AskConfirm(string message, Action action)
        {
            if (confirmRoot == null)
            {
                if (!confirmMissingWarned)
                {
                    confirmMissingWarned = true;
                    Debug.LogWarning("[准备房间] 确认弹窗（Confirm Root）没有接线：开始 / 返回会直接执行，没有二次确认。", this);
                }

                action?.Invoke();
                return;
            }

            pendingConfirm = action;
            if (confirmMessageText != null) confirmMessageText.text = message;
            confirmRoot.SetActive(true);
        }

        /// <summary>确认弹窗「确定」按钮的 OnClick。</summary>
        public void OnConfirmYesClicked()
        {
            Action action = pendingConfirm;
            pendingConfirm = null;

            if (confirmRoot != null) confirmRoot.SetActive(false);

            action?.Invoke();
        }

        /// <summary>确认弹窗「取消」按钮的 OnClick。</summary>
        public void OnConfirmNoClicked()
        {
            pendingConfirm = null;

            if (confirmRoot != null) confirmRoot.SetActive(false);
        }

        private void OnRowKickRequested(string playerId) => KickRequested?.Invoke(playerId);
    }
}
