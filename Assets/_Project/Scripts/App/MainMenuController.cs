using NomadCity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NomadCity.App
{
    /// <summary>
    /// 主菜单。所有交互方法都是 public，方便在 Inspector 里绑到按钮上。
    ///
    /// 三个设置类入口（单机游戏 / 创建游戏 / 开发者模式）共用同一个「开局设置」弹窗，
    /// 「加入游戏」用同一规格的「加入游戏」弹窗；两者都由 <see cref="MainMenuDialog"/> 在运行时生成，
    /// 尺寸、位置、字号与按钮大小完全一致。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("跨场景的模式配置资产，放在 Assets/_Project/Data/GameSession.asset")]
        [SerializeField] private GameSession session;

        [Tooltip("所有主页按钮（单机 / 创建 / 加入 / 开发者）。打开任何主页弹窗时统一隐藏，关掉弹窗后统一恢复")]
        [SerializeField] private GameObject[] menuButtons;

        [Tooltip("点「进入游戏」后加载的场景名")]
        [SerializeField] private string gameSceneName = "Game";

        [Tooltip("点「创建房间 / 加入房间」后进入的准备房间场景名")]
        [SerializeField] private string lobbySceneName = "Lobby";

        /// <summary>每次启动主菜单时，人数都从这个值开始。</summary>
        private const int DefaultPlayerCount = 4;

        /// <summary>运行时创建的「以开发者模式启动」按钮（复制主页按钮样式，不要求改场景）。</summary>
        private GameObject developerButton;

        /// <summary>开局设置弹窗（单机 / 创建 / 开发者三种模式共用）。</summary>
        private MainMenuDialog startDialog;

        /// <summary>加入游戏弹窗（与开局设置同一套规格）。</summary>
        private MainMenuDialog joinDialog;

        /// <summary>这次开局设置是准备以开发者模式进入。</summary>
        private bool pendingDeveloperStart;

        /// <summary>这次开局设置是从「创建游戏」打开的（确认后进准备房间）。</summary>
        private bool startHostMode;

        /// <summary>上次填过的昵称，重新打开弹窗时回填，减少重复输入。</summary>
        private string hostNickname = string.Empty;
        private string clientNickname = string.Empty;

        /// <summary>
        /// 开场准备。用 Start 而不是 Awake：滑条等场景控件的 OnEnable 会先把值刷回场景里存的旧值。
        /// 这里复位人数、清掉上一局留下的联机身份，再补齐开发者按钮与两个运行时弹窗。
        /// </summary>
        private void Start()
        {
            if (session != null)
            {
                session.PlayerCount = DefaultPlayerCount;
                // 感染者扩展还没做：入口灰掉，并且强制关掉，避免上一局留下的勾选把它带进对局。
                session.InfectedExpansion = false;
                // 回主菜单后不沿用上一局的联机身份 / 房间码 / 昵称；否则再点「进入游戏」会莫名其妙地又开一次房。
                session.Role = OnlineRole.None;
                session.JoinCode = string.Empty;
                session.Nickname = string.Empty;
                session.DeveloperMode = false;
            }

            if (menuButtons == null || menuButtons.Length == 0)
                Debug.LogWarning("[主菜单] 未指定主页按钮（Menu Buttons），打开弹窗时按钮不会隐藏。");

            EnsureDeveloperButton();
            EnsureDialogs();
        }

        /// <summary>
        /// 运行时补一个「以开发者模式启动」按钮：主菜单场景里只有原来的按钮，
        /// 这里复制第一个主页按钮的样式，摆在所有按钮最下方，并纳入 <see cref="menuButtons"/> 统一显隐。
        /// 场景里已经有同名按钮时不会重复创建。
        /// </summary>
        private void EnsureDeveloperButton()
        {
            if (developerButton != null) return;

            GameObject template = null;
            if (menuButtons != null)
            {
                foreach (GameObject button in menuButtons)
                {
                    if (button != null) { template = button; break; }
                }
            }

            if (template == null)
            {
                Debug.LogWarning("[主菜单] 没有可复制的按钮模板，无法创建「以开发者模式启动」按钮。");
                return;
            }

            developerButton = Instantiate(template, template.transform.parent);
            developerButton.name = "Btn_DeveloperMode";

            // 与模板同锚点 / 同尺寸，并在现有按钮的最下方再往下排一行。
            var templateRect = template.GetComponent<RectTransform>();
            var rect = developerButton.GetComponent<RectTransform>();
            if (templateRect != null && rect != null)
            {
                rect.anchorMin = templateRect.anchorMin;
                rect.anchorMax = templateRect.anchorMax;
                rect.pivot = templateRect.pivot;
                rect.sizeDelta = templateRect.sizeDelta;

                float lowest = templateRect.anchoredPosition.y;
                float step = templateRect.sizeDelta.y + 24f;
                if (menuButtons != null)
                {
                    foreach (GameObject button in menuButtons)
                    {
                        if (button == null) continue;
                        var other = button.GetComponent<RectTransform>();
                        if (other != null) lowest = Mathf.Min(lowest, other.anchoredPosition.y);
                    }
                }

                rect.anchoredPosition = new Vector2(templateRect.anchoredPosition.x, lowest - step);
            }

            var click = developerButton.GetComponent<UnityEngine.UI.Button>();
            if (click != null)
            {
                click.onClick.RemoveAllListeners();
                click.onClick.AddListener(OpenStartPanelForDeveloper);
                click.interactable = true;
            }

            TMP_Text label = developerButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "以开发者模式启动";

            // 纳入主页按钮列表，打开任何弹窗时跟着一起隐藏。
            var buttons = new System.Collections.Generic.List<GameObject>();
            if (menuButtons != null) buttons.AddRange(menuButtons);
            buttons.Add(developerButton);
            menuButtons = buttons.ToArray();

            developerButton.SetActive(true);
        }

        /// <summary>创建两个运行时弹窗，并把它们接回主菜单流程。</summary>
        private void EnsureDialogs()
        {
            if (startDialog != null && joinDialog != null) return;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[主菜单] 场景里没有 Canvas，无法创建开局弹窗。");
                return;
            }

            TMP_FontAsset font = ResolveFont(canvas.transform);

            startDialog = MainMenuDialog.Create(canvas.transform, font, MainMenuDialog.Kind.Start);
            startDialog.PlayerCountChanged = count =>
            {
                if (session != null) session.PlayerCount = count;
            };
            startDialog.EnterpriseChanged = enabled =>
            {
                if (session != null) session.EnterpriseExpansion = enabled;
            };
            startDialog.InfectedChanged = enabled =>
            {
                if (session != null) session.InfectedExpansion = enabled;
            };
            startDialog.BackRequested = CloseStartPanel;
            startDialog.ConfirmRequested = ConfirmStart;

            joinDialog = MainMenuDialog.Create(canvas.transform, font, MainMenuDialog.Kind.Join);
            joinDialog.BackRequested = CloseJoinPanel;
            joinDialog.ConfirmRequested = StartClientGame;
        }

        /// <summary>
        /// 取场景里正在用的中文字体：主菜单按钮上的 TMP 文本已经配好了能显示中文的字体资产，
        /// 直接借用比在代码里硬填资源路径稳。
        /// </summary>
        private static TMP_FontAsset ResolveFont(Transform canvas)
        {
            TMP_Text sample = canvas.GetComponentInChildren<TMP_Text>(true);
            if (sample != null && sample.font != null) return sample.font;

            TMP_Text[] all = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].font != null) return all[i].font;
            }

            return TMP_Settings.defaultFontAsset;
        }

        // —— 开局设置 ——

        /// <summary>绑到「单机游戏」按钮的 OnClick：弹出开局设置弹窗，确认按钮是「进入游戏」。</summary>
        public void OpenStartPanel()
        {
            OpenStartPanel(false, false);
        }

        /// <summary>绑到「创建游戏」按钮的 OnClick：同一个弹窗，确认按钮换成「创建联机房间」。</summary>
        public void OpenStartPanelForHost()
        {
            OpenStartPanel(true, false);
        }

        /// <summary>绑到「以开发者模式启动」按钮的 OnClick：同单机弹窗，确认后以开发者模式进入。</summary>
        public void OpenStartPanelForDeveloper()
        {
            OpenStartPanel(false, true);
        }

        /// <summary>打开开局设置弹窗。hostMode 决定确认按钮与昵称行，developerStart 决定进入后是否带开发者面板。</summary>
        private void OpenStartPanel(bool hostMode, bool developerStart)
        {
            EnsureDialogs();
            if (startDialog == null) return;

            startHostMode = hostMode;
            pendingDeveloperStart = developerStart;

            string subtitle;
            string hint;
            string confirmText;
            if (developerStart)
            {
                subtitle = "开发者模式 · 先选择人数与扩展包";
                hint = "进入后额外显示开发者面板，其余流程与普通对局一致。";
                confirmText = "以开发者模式进入";
            }
            else if (hostMode)
            {
                subtitle = "联机房间 · 设置人数与扩展包后创建房间";
                hint = "创建后进入准备房间，由房主开始对局。";
                confirmText = "创建联机房间";
            }
            else
            {
                subtitle = "单机热座 · 一台电脑轮流操作全部玩家";
                hint = "企业扩展：开局每人 7 张角色牌，选 5 张使用。";
                confirmText = "进入游戏";
            }

            startDialog.ShowStart(subtitle, hint, confirmText, hostMode,
                session != null ? session.PlayerCount : DefaultPlayerCount,
                session != null && session.EnterpriseExpansion,
                false, // 感染者扩展暂未开放
                hostNickname);

            SetMenuButtonsActive(false);
        }

        /// <summary>绑到弹窗里「返回」按钮的 OnClick：收起弹窗回到主菜单。</summary>
        public void CloseStartPanel()
        {
            if (startDialog != null) startDialog.Hide();
            pendingDeveloperStart = false;
            startHostMode = false;
            SetMenuButtonsActive(true);
        }

        /// <summary>弹窗里的确认按钮：按这次打开的来源分流到单机 / 创建房间 / 开发者模式。</summary>
        private void ConfirmStart()
        {
            if (!ValidateSession()) return;

            if (pendingDeveloperStart)
            {
                pendingDeveloperStart = false;
                StartDeveloperGame();
                return;
            }

            if (startHostMode) StartHostGame();
            else StartGame();
        }

        // —— 加入游戏 ——

        /// <summary>绑到「加入游戏」按钮的 OnClick：弹出 6 位房间码输入弹窗（与开局设置同规格）。</summary>
        public void OpenJoinPanel()
        {
            EnsureDialogs();
            if (joinDialog == null) return;

            // 上次进过的房间码存在本地（关掉游戏重开也还在）：直接填好，重连回原座位不用再手输。
            joinDialog.ShowJoin("联机房间 · 输入房间码后加入",
                "房间码由房主在准备房间提供；昵称会显示给同房间的其他玩家。",
                LobbyClient.LastRoomCode, clientNickname);
            SetMenuButtonsActive(false);
        }

        /// <summary>绑到加入弹窗里「返回」按钮的 OnClick：收起弹窗回到主菜单。</summary>
        public void CloseJoinPanel()
        {
            if (joinDialog != null) joinDialog.Hide();
            SetMenuButtonsActive(true);
        }

        /// <summary>
        /// 统一开/关主页按钮：弹窗盖住主菜单时收起来，关掉弹窗再放回来。
        /// 以后新增主页按钮，只要加进 menuButtons 列表，就自动按这套规则走，不用再改代码。
        /// </summary>
        private void SetMenuButtonsActive(bool active)
        {
            if (menuButtons == null) return;
            foreach (GameObject button in menuButtons)
            {
                if (button != null) button.SetActive(active);
            }
        }

        /// <summary>把两个弹窗都收起来（进场景前调用，避免残留）。</summary>
        private void HideDialogs()
        {
            if (startDialog != null) startDialog.Hide();
            if (joinDialog != null) joinDialog.Hide();
            SetMenuButtonsActive(true);
        }

        // —— 老的场景控件回调（保留给可能还绑着的 UnityEvent，行为与以前一致） ——

        /// <summary>人数滑条 OnValueChanged。</summary>
        public void SetPlayerCountFromSlider(float value)
        {
            if (session != null) session.PlayerCount = Mathf.RoundToInt(value);
        }

        /// <summary>企业扩展勾选框 OnValueChanged。</summary>
        public void SetEnterprise(bool enabled)
        {
            if (session != null) session.EnterpriseExpansion = enabled;
        }

        /// <summary>感染者扩展勾选框 OnValueChanged。该扩展暂未开放，无论传什么都保持关闭。</summary>
        public void SetInfected(bool enabled)
        {
            if (session != null) session.InfectedExpansion = false;
        }

        // —— 进入游戏 ——

        /// <summary>单机热座：按弹窗里的选择加载 Game 场景。</summary>
        public void StartGame()
        {
            if (!ValidateSession()) return;

            session.Role = OnlineRole.None;
            session.DeveloperMode = false;
            session.JoinCode = string.Empty;
            session.Nickname = string.Empty;
            HideDialogs();
            LoadScene(gameSceneName);
        }

        /// <summary>开发者模式：仍按单人热座正常开局，只在 Game 场景里额外显示开发者面板。</summary>
        public void StartDeveloperGame()
        {
            if (!ValidateSession()) return;

            session.Role = OnlineRole.None;
            session.DeveloperMode = true;
            session.JoinCode = string.Empty;
            session.Nickname = string.Empty;
            Debug.Log($"[主菜单] 以开发者模式启动 —— {session.ToGameSetup()}");
            HideDialogs();
            LoadScene(gameSceneName);
        }

        /// <summary>创建联机房间：仍按弹窗里的人数 / 扩展包开局，先进入准备房间。</summary>
        public void StartHostGame()
        {
            if (!ValidateSession()) return;

            string name = (startDialog != null ? startDialog.HostNickname : hostNickname).Trim();
            if (name.Length == 0)
            {
                if (startDialog != null)
                {
                    startDialog.SetError("请先填写房主昵称。");
                    startDialog.FocusHostNickname();
                }
                return;
            }

            hostNickname = name;
            session.Role = OnlineRole.Host;
            session.DeveloperMode = false;
            session.JoinCode = string.Empty;
            session.Nickname = name;
            Debug.Log($"[主菜单] 创建联机房间 —— {session.ToGameSetup()}，昵称：{session.Nickname}");
            HideDialogs();
            LoadScene(lobbySceneName);
        }

        /// <summary>加入房间：读取弹窗里的 6 位房间码，以客户端身份进入准备房间。</summary>
        public void StartClientGame()
        {
            string code = (joinDialog != null ? joinDialog.JoinCode : string.Empty)
                .Trim().ToUpperInvariant();
            if (code.Length != 6)
            {
                if (joinDialog != null)
                {
                    joinDialog.SetError("房间码应为 6 位，请向房主确认。");
                    joinDialog.FocusJoinCode();
                }
                return;
            }

            string name = (joinDialog != null ? joinDialog.ClientNickname : clientNickname).Trim();
            if (name.Length == 0)
            {
                if (joinDialog != null) joinDialog.SetError("请先填写昵称。");
                return;
            }

            if (!ValidateSession()) return;

            clientNickname = name;
            session.Role = OnlineRole.Client;
            session.DeveloperMode = false;
            session.JoinCode = code;
            session.Nickname = name;
            Debug.Log($"[主菜单] 加入联机房间 {code}，昵称：{session.Nickname}");
            HideDialogs();
            LoadScene(lobbySceneName);
        }

        /// <summary>校验配置资产是否可用。三条入口共用。</summary>
        private bool ValidateSession()
        {
            if (session == null)
            {
                Debug.LogError("[主菜单] 未指定 GameSession 资产，无法开始游戏。");
                return false;
            }

            var setup = session.ToGameSetup();
            if (!setup.IsValid)
            {
                Debug.LogError($"[主菜单] 无效的模式配置：{setup}");
                return false;
            }

            return true;
        }

        /// <summary>清掉 UGUI 的选中引用后切场景（单场景加载会销毁当前整个 Canvas）。</summary>
        private void LoadScene(string sceneName)
        {
            // 否则帧末 UGUI 会去访问已销毁的 Image，抛出 MissingReferenceException。
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            SceneManager.LoadScene(sceneName);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
