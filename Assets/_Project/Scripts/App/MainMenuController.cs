using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NomadCity.App
{
    /// <summary>
    /// 主菜单。所有交互方法都是 public，直接在 Inspector 里绑到按钮 / 滑条 / 勾选框的 UnityEvent 上，
    /// 这样以后调整 UI 布局不用改代码。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("跨场景的模式配置资产，放在 Assets/_Project/Data/GameSession.asset")]
        [SerializeField] private GameSession session;

        [Tooltip("点「创建房间」后弹出的开局设置弹窗（人数滑条 + 企业扩展包勾选）。平时隐藏；本脚本只负责开/关与回填，不改它的布局")]
        [SerializeField] private GameObject startPanel;

        [Tooltip("所有主页按钮（如「创建房间」）。打开任何主页弹窗时统一隐藏，关掉弹窗后统一恢复；以后再加主页按钮，直接拖进这个列表即可")]
        [SerializeField] private GameObject[] menuButtons;

        [Tooltip("开局设置弹窗里的「进入游戏」按钮（单机）。从「单机游戏」进来时显示，从「创建游戏」进来时隐藏")]
        [SerializeField] private GameObject localConfirmButton;

        [Tooltip("开局设置弹窗里的「创建联机房间」按钮。从「创建游戏」进来时显示，从「单机游戏」进来时隐藏")]
        [SerializeField] private GameObject hostConfirmButton;

        [Tooltip("「加入游戏」弹窗：6 位房间码输入框 + 加入 / 返回按钮。平时隐藏")]
        [SerializeField] private GameObject joinPanel;

        [Tooltip("开局设置弹窗里的人数滑条。请在 Inspector 里把 Min Value 设成 2、Max Value 设成 4，并勾上 Whole Numbers（整数值）")]
        [SerializeField] private Slider playerCountSlider;

        [Tooltip("可选：显示当前人数的文本，拖滑条时会同步成「N 人」。留空则跳过，不影响功能")]
        [SerializeField] private TMP_Text playerCountLabel;

        [Tooltip("开局设置弹窗里的「企业扩展包」勾选框。打开弹窗时按上次记下的选择回填，避免弹窗显示与实际开局不一致")]
        [SerializeField] private Toggle enterpriseToggle;

        [Tooltip("点「进入游戏」后加载的场景名")]
        [SerializeField] private string gameSceneName = "Game";

        [Tooltip("点「创建房间 / 加入房间」后进入的准备房间场景名")]
        [SerializeField] private string lobbySceneName = "Lobby";

        [Tooltip("「加入房间」用的 6 位房间码输入框（TMP_InputField）。不填则无法以客户端身份加入")]
        [SerializeField] private TMP_InputField joinCodeInput;

        [Tooltip("开局设置弹窗里的房主昵称输入框（TMP_InputField）。必填：留空时不允许创建房间")]
        [SerializeField] private TMP_InputField hostNicknameInput;

        [Tooltip("加入游戏弹窗里的玩家昵称输入框（TMP_InputField）。必填：留空时不允许加入房间")]
        [SerializeField] private TMP_InputField clientNicknameInput;

        /// <summary>每次启动主菜单时，人数都从这个值开始。</summary>
        private const int DefaultPlayerCount = 4;

        /// <summary>
        /// 开场准备。用 Start 而不是 Awake：滑条自己的 OnEnable 会把数值刷回场景里存的那个数（场景里是 2），
        /// 在 Awake 里设会被它覆盖，Start 在所有 Awake / OnEnable 之后跑，设了才作数。
        ///
        /// 这里做三件事：
        /// 1) 把人数复位成默认的 <see cref="DefaultPlayerCount"/>（4 人）—— GameSession 是 ScriptableObject 资产，
        ///    跨场景、跨运行都保留上次的值，不复位就会出现「这次启动滑条还停在 2，实际却按上一局留下的 4 人开局」；
        /// 2) 把滑条同步到 4（停在最右端），并刷新人数文本，让它显示的与实际开局一致；
        /// 3) 用代码绑定滑条与勾选框的事件，不依赖 Inspector 里那两条「值改变时 (On Value Changed)」——
        ///    那里的参数有「静态参数 / 动态」两种模式，配成静态时每次都会传同一个固定值，界面上完全看不出异常。
        /// </summary>
        private void Start()
        {
            if (session != null)
            {
                session.PlayerCount = DefaultPlayerCount;
                // 回主菜单后不沿用上一局的联机身份 / 房间码 / 昵称；否则再点「进入游戏」会莫名其妙地又开一次房。
                session.Role = OnlineRole.None;
                session.JoinCode = string.Empty;
                session.Nickname = string.Empty;
            }

            if (hostNicknameInput == null)
                Debug.LogWarning("[主菜单] 未指定房主昵称输入框（Host Nickname Input），将无法创建房间。");
            if (clientNicknameInput == null)
                Debug.LogWarning("[主菜单] 未指定玩家昵称输入框（Client Nickname Input），将无法加入房间。");

            // 字段为空时绑定不上，滑条/勾选框会完全没反应，而界面上看不出是哪一步漏了 —— 直接点名。
            if (playerCountSlider == null)
            {
                Debug.LogWarning("[主菜单] 未指定人数滑条（Player Count Slider），滑条不会生效。");
            }
            else
            {
                playerCountSlider.onValueChanged.AddListener(SetPlayerCountFromSlider);
                SyncPlayerCountSlider(); // 停在最右端（4 人）
            }

            if (enterpriseToggle == null)
                Debug.LogWarning("[主菜单] 未指定企业扩展勾选框（Enterprise Toggle），勾选框不会生效。");
            else
                enterpriseToggle.onValueChanged.AddListener(SetEnterprise);

            if (menuButtons == null || menuButtons.Length == 0)
                Debug.LogWarning("[主菜单] 未指定主页按钮（Menu Buttons），打开弹窗时按钮不会隐藏。");
        }

        /// <summary>绑到「单机游戏」按钮的 OnClick：弹出开局设置弹窗，确认按钮是「进入游戏」。</summary>
        public void OpenStartPanel()
        {
            OpenStartPanel(false);
        }

        /// <summary>绑到「创建游戏」按钮的 OnClick：同一个弹窗，确认按钮换成「创建联机房间」。</summary>
        public void OpenStartPanelForHost()
        {
            OpenStartPanel(true);
        }

        /// <summary>打开开局设置弹窗。hostMode 决定弹窗里显示哪一个确认按钮。</summary>
        private void OpenStartPanel(bool hostMode)
        {
            if (startPanel == null)
            {
                Debug.LogError("[主菜单] 未指定开局设置弹窗（Start Panel），请在 Inspector 里拖入。");
                return;
            }

            // 两个控件都回填成上次记下的选择，免得弹窗显示跟实际开局对不上。
            SyncPlayerCountSlider();
            SyncEnterpriseToggle();
            RefreshConfirmButtons(hostMode);
            SetMenuButtonsActive(false); // 弹窗盖住了主菜单，先把主页按钮收起来
            startPanel.SetActive(true);
        }

        /// <summary>弹窗里只留一个确认按钮：单机显示「进入游戏」，创建游戏显示「创建联机房间」。</summary>
        private void RefreshConfirmButtons(bool hostMode)
        {
            if (localConfirmButton == null && hostConfirmButton == null)
            {
                Debug.LogWarning("[主菜单] 开局设置弹窗的两个确认按钮都没接线（Enter Game / Create Room），弹窗里将没有确认入口。");
                return;
            }

            if (localConfirmButton != null) localConfirmButton.SetActive(!hostMode);
            if (hostConfirmButton != null) hostConfirmButton.SetActive(hostMode);
        }

        /// <summary>绑到弹窗里「返回」按钮的 OnClick：收起弹窗回到主菜单。</summary>
        public void CloseStartPanel()
        {
            if (startPanel != null) startPanel.SetActive(false);
            SetMenuButtonsActive(true); // 回到主菜单，把主页按钮放回来
        }

        /// <summary>绑到「加入游戏」按钮的 OnClick：弹出 6 位房间码输入弹窗。</summary>
        public void OpenJoinPanel()
        {
            if (joinPanel == null)
            {
                Debug.LogError("[主菜单] 未指定加入游戏弹窗（Join Panel），请在 Inspector 里拖入。");
                return;
            }

            SetMenuButtonsActive(false);
            joinPanel.SetActive(true);
        }

        /// <summary>绑到加入弹窗里「返回」按钮的 OnClick：收起弹窗回到主菜单。</summary>
        public void CloseJoinPanel()
        {
            if (joinPanel != null) joinPanel.SetActive(false);
            SetMenuButtonsActive(true);
        }

        /// <summary>
        /// 统一开/关主页按钮：弹窗盖住主菜单时收起来，关掉弹窗再放回来。
        /// 以后新增主页按钮，只要加进 menuButtons 列表，就自动按这套规则走，不用再改代码。
        /// </summary>
        private void SetMenuButtonsActive(bool active)
        {
            if (menuButtons == null) return;
            foreach (var button in menuButtons)
            {
                if (button != null) button.SetActive(active);
            }
        }

        /// <summary>绑到人数滑条的 OnValueChanged：滑条给的是 float，这里取整成 2–4 人。</summary>
        public void SetPlayerCountFromSlider(float value)
        {
            SetPlayerCount(Mathf.RoundToInt(value));
        }

        /// <summary>绑到扩展包勾选框的 OnValueChanged。</summary>
        public void SetEnterprise(bool enabled)
        {
            if (session == null)
            {
                Debug.LogError("[主菜单] 未指定 GameSession 资产。");
                return;
            }

            session.EnterpriseExpansion = enabled;
        }

        private void SetPlayerCount(int count)
        {
            if (session == null)
            {
                Debug.LogError("[主菜单] 未指定 GameSession 资产。");
                return;
            }

            session.PlayerCount = count; // GameSession 内部会把人数 Clamp 到 2–4
            RefreshPlayerCountLabel();
        }

        /// <summary>把滑条回填成上次记下的人数（不触发 OnValueChanged，免得反过来又写一遍）。</summary>
        private void SyncPlayerCountSlider()
        {
            if (playerCountSlider == null || session == null) return;
            playerCountSlider.SetValueWithoutNotify(session.PlayerCount);
            RefreshPlayerCountLabel();
        }

        /// <summary>把勾选框回填成上次记下的选择（不触发 OnValueChanged，免得反过来又写一遍）。</summary>
        private void SyncEnterpriseToggle()
        {
            if (enterpriseToggle == null || session == null) return;
            enterpriseToggle.SetIsOnWithoutNotify(session.EnterpriseExpansion);
        }

        private void RefreshPlayerCountLabel()
        {
            if (playerCountLabel == null || session == null) return;
            playerCountLabel.text = $"{session.PlayerCount} 人";
        }

        /// <summary>绑到「进入游戏」按钮的 OnClick：按弹窗里的选择加载对应地图的 Game 场景（单机热座）。</summary>
        public void StartGame()
        {
            if (!ValidateSession()) return;

            session.Role = OnlineRole.None;
            session.JoinCode = string.Empty;
            session.Nickname = string.Empty;
            LoadScene(gameSceneName);
        }

        /// <summary>
        /// 绑到「创建联机房间」按钮的 OnClick：仍按弹窗里的人数 / 扩展包开局，
        /// 但先进入准备房间——房间码、玩家名单、准备与开始都在那边完成。
        /// </summary>
        public void StartHostGame()
        {
            if (!ValidateSession()) return;
            if (!TryReadNickname(hostNicknameInput, "房主", out string hostName)) return;

            session.Role = OnlineRole.Host;
            session.JoinCode = string.Empty;
            session.Nickname = hostName;
            Debug.Log($"[主菜单] 创建联机房间 —— {session.ToGameSetup()}，昵称：{session.Nickname}");
            LoadScene(lobbySceneName);
        }

        /// <summary>
        /// 绑到「加入房间」按钮的 OnClick：读取输入框里的 6 位房间码，以客户端身份进入准备房间。
        /// </summary>
        public void StartClientGame()
        {
            if (joinCodeInput == null)
            {
                Debug.LogError("[主菜单] 未指定房间码输入框（Join Code Input），无法加入房间。");
                return;
            }

            string code = (joinCodeInput.text ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length != 6)
            {
                Debug.LogError($"[主菜单] 房间码应为 6 位，当前是「{joinCodeInput.text}」。");
                return;
            }

            if (!ValidateSession()) return;
            if (!TryReadNickname(clientNicknameInput, "玩家", out string playerName)) return;

            session.Role = OnlineRole.Client;
            session.JoinCode = code;
            session.Nickname = playerName;
            Debug.Log($"[主菜单] 加入联机房间 {code}，昵称：{session.Nickname}");
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

        /// <summary>
        /// 昵称必填：空 / 纯空格时拒绝创建或加入，并把光标停在输入框上，
        /// 避免带着「房主」「玩家」这类默认名进房间后无法区分谁是谁。
        /// </summary>
        private static bool TryReadNickname(TMP_InputField input, string roleLabel, out string nickname)
        {
            nickname = input != null ? (input.text ?? string.Empty).Trim() : string.Empty;
            if (nickname.Length > 0) return true;

            if (input == null)
            {
                Debug.LogError($"[主菜单] 没有指定{roleLabel}昵称输入框（Nickname Input），无法继续。" +
                               "请把弹窗里的 TMP 输入框拖到 MainMenuController 对应字段。");
                return false;
            }

            Debug.LogWarning("[主菜单] 请输入昵称。");
            input.Select();
            input.ActivateInputField();
            return false;
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
