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

        /// <summary>把当前选择打包成 Core 的模式配置。</summary>
        public GameSetup ToGameSetup()
        {
            var flags = enterpriseExpansion ? ExpansionFlags.Enterprise : ExpansionFlags.None;
            return new GameSetup(playerCount, flags);
        }
    }
}
