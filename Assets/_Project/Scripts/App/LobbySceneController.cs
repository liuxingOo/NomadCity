using System.Collections;
using System.Collections.Generic;
using NomadCity.Core.Net;
using NomadCity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NomadCity.App
{
    /// <summary>
    /// Lobby（准备房间）场景入口：把面板上的点击翻译成 <see cref="OnlineSessionHost"/> 的调用，
    /// 再把网络层的事件翻译成面板显示。面板本身不引用 App / Net。
    ///
    /// 它只负责「本场景」；连接由常驻的 <see cref="OnlineSessionHost"/> 持有，
    /// 切到 Game 时不能在这里 Shutdown，否则会断线。
    /// </summary>
    public sealed class LobbySceneController : MonoBehaviour
    {
        [Tooltip("跨场景配置资产：Assets/_Project/Data/GameSession.asset")]
        [SerializeField] private GameSession session;

        [Tooltip("准备房间面板（挂在 Canvas/LobbyUI 上）")]
        [SerializeField] private LobbyScreenPanel panel;

        [Tooltip("开始后加载的对局场景名")]
        [SerializeField] private string gameSceneName = "Game";

        [Tooltip("离开 / 出错后回到的场景名")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private OnlineSessionHost host;
        private bool sceneLoadRequested;
        private bool returnScheduled;
        private bool acceptingChanges = true;

        /// <summary>房主设置的人数；名单行数与「N/总人数」提示都按它来，改人数时立即重排。</summary>
        private int currentPlayerCount = 4;

        /// <summary>最近一次名单，改人数后要用它重新铺一次行。</summary>
        private IReadOnlyList<LobbyMemberInfo> lastMembers = System.Array.Empty<LobbyMemberInfo>();

        private void Start()
        {
            host = OnlineSessionHost.Instance;

            if (session == null || panel == null || host == null)
            {
                string message = session == null ? "LobbySceneController 没有指定 GameSession。"
                    : panel == null ? "LobbySceneController 没有指定 LobbyScreenPanel。"
                    : "场景里找不到 OnlineSessionHost（应放在 MainMenu 场景并常驻）。";
                if (panel != null) panel.SetStatus(message);
                Debug.LogError("[准备房间] " + message, this);
                ScheduleReturnToMenu(2.5f);
                return;
            }

            SubscribePanel();
            SubscribeHost();

            currentPlayerCount = Mathf.Clamp(session.PlayerCount, 2, 4);

            if (session.Role == OnlineRole.Host)
            {
                panel.ShowHostView();
                panel.SetRoomCode(null);
                panel.SetSettings(session.PlayerCount, session.EnterpriseExpansion);
                panel.SetCanStart(false);
                panel.SetStatus("正在登记房间…");
                host.BeginHost(session.PlayerCount, session.EnterpriseExpansion, session.Nickname);
            }
            else if (session.Role == OnlineRole.Client)
            {
                panel.ShowClientView();
                panel.SetRoomCode(session.JoinCode);
                panel.SetCanStart(false);
                panel.SetStatus("正在加入房间…");
                host.BeginClient(session.JoinCode, session.Nickname);
            }
            else
            {
                panel.SetStatus("当前不是联机身份，正在返回主菜单…");
                ScheduleReturnToMenu(2f);
            }
        }

        private void SubscribePanel()
        {
            panel.ApplySettingsRequested += OnApplySettings;
            panel.ReadyToggleRequested += OnReadyToggle;
            panel.StartRequested += OnStartClicked;
            panel.LeaveRequested += OnLeave;
            panel.KickRequested += OnKick;
        }

        private void SubscribeHost()
        {
            host.GameReady += OnGameReady;
            host.ErrorArrived += OnError;
            host.RoomCodeReady += panel.SetRoomCode;
            host.RosterChanged += OnRosterChanged;
            host.SettingsChanged += OnSettingsChanged;
            host.StartEligibilityChanged += panel.SetCanStart;
            host.StatusChanged += panel.SetStatus;
        }

        private void OnDestroy()
        {
            if (panel != null)
            {
                panel.ApplySettingsRequested -= OnApplySettings;
                panel.ReadyToggleRequested -= OnReadyToggle;
                panel.StartRequested -= OnStartClicked;
                panel.LeaveRequested -= OnLeave;
                panel.KickRequested -= OnKick;
            }

            if (host != null)
            {
                host.GameReady -= OnGameReady;
                host.ErrorArrived -= OnError;
                host.RosterChanged -= OnRosterChanged;
                host.SettingsChanged -= OnSettingsChanged;

                if (panel != null)
                {
                    host.RoomCodeReady -= panel.SetRoomCode;
                    host.StartEligibilityChanged -= panel.SetCanStart;
                    host.StatusChanged -= panel.SetStatus;
                }
            }
        }

        private void OnApplySettings(int playerCount, bool enterprise)
        {
            if (!acceptingChanges) return;
            host.SetSettings(playerCount, enterprise);
        }

        private void OnReadyToggle()
        {
            panel.SetReady(!panel.IsReady); // 先给本地反馈；真实状态由下一次轮询校正
            host.SetReady(panel.IsReady);
        }

        private void OnStartClicked()
        {
            if (sceneLoadRequested) return;

            acceptingChanges = false;
            panel.SetSettingsInteractable(false);
            panel.SetCanStart(false);
            host.RequestStart();
        }

        private void OnKick(string playerId)
        {
            if (!acceptingChanges) return;
            host.Kick(playerId);
        }

        private void OnLeave()
        {
            if (sceneLoadRequested) return;

            sceneLoadRequested = true;
            host.Leave();
            LoadScene(mainMenuSceneName);
        }

        private void OnGameReady()
        {
            if (sceneLoadRequested) return;

            sceneLoadRequested = true;
            LoadScene(gameSceneName);
        }

        private void OnRosterChanged(IReadOnlyList<LobbyMemberInfo> members)
        {
            lastMembers = members ?? System.Array.Empty<LobbyMemberInfo>();
            panel.SetMembers(lastMembers, CanKick(), currentPlayerCount);

            // 有人进来 / 离开时给一行明确提示，否则界面上完全看不出名单变了。
            if (!sceneLoadRequested)
                panel.SetStatus($"房间内 {lastMembers.Count}/{currentPlayerCount} 人。");
        }

        private void OnSettingsChanged(int playerCount, bool enterprise)
        {
            currentPlayerCount = Mathf.Clamp(playerCount, 2, 4);
            panel.SetSettings(currentPlayerCount, enterprise);
            panel.SetMembers(lastMembers, CanKick(), currentPlayerCount);
        }

        private void OnError(string message)
        {
            panel.SetStatus(message);

            // 被踢 / 房主关房时 OnlineSessionHost 已经把 Role 复位成 None：
            // 这两类错误需要离开准备房间，其余错误（刷新失败等）留在原地重试。
            if (host.Role == OnlineRole.None && !sceneLoadRequested && !host.GameStarting)
                ScheduleReturnToMenu(2.5f);
        }

        private bool CanKick() =>
            session.Role == OnlineRole.Host && acceptingChanges && !sceneLoadRequested;

        private void ScheduleReturnToMenu(float seconds)
        {
            if (sceneLoadRequested || returnScheduled) return;

            returnScheduled = true;
            StartCoroutine(ReturnToMenuAfterDelay(seconds));
        }

        private IEnumerator ReturnToMenuAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (sceneLoadRequested) yield break;

            sceneLoadRequested = true;
            LoadScene(mainMenuSceneName);
        }

        private void LoadScene(string sceneName)
        {
            // 单场景加载会销毁当前 UI。先清掉 UGUI 的选中引用，避免帧末访问已销毁的 Image。
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            SceneManager.LoadScene(sceneName);
        }
    }
}
