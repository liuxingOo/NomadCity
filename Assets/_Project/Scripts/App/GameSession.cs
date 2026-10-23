using NomadCity.Core.Rules;
using UnityEngine;

namespace NomadCity.App
{
    /// <summary>
    /// 跨场景传递一局游戏的配置：主菜单写入，游戏场景读出。
    /// 用 ScriptableObject 而不是静态字段，是为了让它独立于场景存活，
    /// 并且能直接从 Editor 打开游戏场景调试（不经过主菜单也知道用什么模式开局）。
    /// </summary>
    [CreateAssetMenu(menuName = "NomadCity/Game Session", fileName = "GameSession")]
    public sealed class GameSession : ScriptableObject
    {
        [SerializeField, Range(2, 4)] private int playerCount = 4;
        [SerializeField] private bool enterpriseExpansion;

        [Tooltip("联机身份：主菜单写入；None = 单机热座")]
        [SerializeField] private OnlineRole onlineRole = OnlineRole.None;

        [Tooltip("客户端要加入的 6 位房间码（Lobby Code）；主机或单机为空")]
        [SerializeField] private string joinCode = string.Empty;

        [Tooltip("准备房间里显示的昵称；主菜单在进入准备房间前写入")]
        [SerializeField] private string nickname = string.Empty;

        [Tooltip("本局各座位（按座位号 0..N-1）的昵称；准备房间的名单同步写入，联机对局顶栏用它显示玩家名")]
        [SerializeField] private string[] playerNicknames = new string[0];

        public int PlayerCount
        {
            get => playerCount;
            set => playerCount = Mathf.Clamp(value, 2, 4);
        }

        public bool EnterpriseExpansion
        {
            get => enterpriseExpansion;
            set => enterpriseExpansion = value;
        }

        /// <summary>本局的联机身份。主菜单在加载 Game 场景前写入，GameBootstrap 读取。</summary>
        public OnlineRole Role
        {
            get => onlineRole;
            set => onlineRole = value;
        }

        /// <summary>客户端要加入的房间码；主机与单机为空。</summary>
        public string JoinCode
        {
            get => joinCode;
            set => joinCode = value ?? string.Empty;
        }

        /// <summary>准备房间里显示的昵称（最多 12 字符）。</summary>
        public string Nickname
        {
            get => nickname;
            set
            {
                string trimmed = (value ?? string.Empty).Trim();
                nickname = trimmed.Length > 12 ? trimmed.Substring(0, 12) : trimmed;
            }
        }

        /// <summary>本局各座位（按座位号 0..N-1）的昵称；没有时返回空数组，界面回退成「玩家 N」。</summary>
        public string[] PlayerNicknames
        {
            get => playerNicknames ?? new string[0];
            set => playerNicknames = value ?? new string[0];
        }

        /// <summary>把当前选择打包成 Core 的模式配置。</summary>
        public GameSetup ToGameSetup()
        {
            var flags = enterpriseExpansion ? ExpansionFlags.Enterprise : ExpansionFlags.None;
            return new GameSetup(playerCount, flags);
        }
    }
}
