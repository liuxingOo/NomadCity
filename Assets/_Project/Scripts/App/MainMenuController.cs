using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NomadCity.App
{
    /// <summary>
    /// 主菜单。所有交互方法都是 public，直接在 Inspector 里绑到按钮 / 开关的 UnityEvent 上，
    /// 这样以后调整 UI 布局不用改代码。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("跨场景的模式配置资产，放在 Assets/_Project/Data/GameSession.asset")]
        [SerializeField] private GameSession session;

        [Tooltip("点「开始游戏」后加载的场景名")]
        [SerializeField] private string gameSceneName = "Game";

        /// <summary>绑到人数按钮的 OnClick，参数填 2 / 3 / 4。</summary>
        public void SetPlayerCount(int count)
        {
            if (session == null)
            {
                Debug.LogError("[主菜单] 未指定 GameSession 资产。");
                return;
            }

            session.PlayerCount = count;
        }

        /// <summary>绑到扩展包 Toggle 的 OnValueChanged。</summary>
        public void SetEnterprise(bool enabled)
        {
            if (session == null)
            {
                Debug.LogError("[主菜单] 未指定 GameSession 资产。");
                return;
            }

            session.EnterpriseExpansion = enabled;
        }

        /// <summary>绑到「N 人游戏」按钮的 OnClick：设人数并直接开局。</summary>
        public void StartGameWith(int count)
        {
            SetPlayerCount(count);
            StartGame();
        }

        public void StartGame()
        {
            if (session == null)
            {
                Debug.LogError("[主菜单] 未指定 GameSession 资产，无法开始游戏。");
                return;
            }

            var setup = session.ToGameSetup();
            if (!setup.IsValid)
            {
                Debug.LogError($"[主菜单] 无效的模式配置：{setup}");
                return;
            }

            // 单场景加载会连根销毁主菜单的整个 Canvas。先清掉 UGUI 里对旧场景按钮的残留选中引用，
            // 否则帧末 UGUI 会去访问已销毁的 Image，抛出 MissingReferenceException。
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            Debug.Log($"[主菜单] 开始游戏 —— {setup}");
            SceneManager.LoadScene(gameSceneName);
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
