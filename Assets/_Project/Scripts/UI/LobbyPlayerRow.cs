using System;
using NomadCity.Core.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 准备房间里的固定玩家行。4 行各挂一个，由 <see cref="LobbyScreenPanel"/> 按名单填充；
    /// 空位显示「等待玩家…」，房主行显示房主徽标与（对其他玩家生效的）踢出按钮。
    /// </summary>
    public sealed class LobbyPlayerRow : MonoBehaviour
    {
        [Tooltip("有成员时显示的容器")]
        [SerializeField] private GameObject occupiedRoot;

        [Tooltip("空位时显示的容器（内含「等待玩家…」文本）")]
        [SerializeField] private GameObject emptyRoot;

        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text stateText;

        [Tooltip("房主徽标（Image 或 TMP_Text 都可以，这里只负责开关）")]
        [SerializeField] private GameObject hostBadge;

        [SerializeField] private Button kickButton;

        public event Action<string> KickRequested;

        private string playerId = string.Empty;

        /// <summary>填充一名成员；canKick 为 false 时隐藏踢出按钮（非房主 / 已开始）。</summary>
        public void SetMember(LobbyMemberInfo member, bool canKick)
        {
            playerId = member.PlayerId;

            if (occupiedRoot != null) occupiedRoot.SetActive(true);
            if (emptyRoot != null) emptyRoot.SetActive(false);

            if (nameText != null) nameText.text = member.Nickname;
            if (stateText != null) stateText.text = member.IsHost ? "房主" : (member.Ready ? "已准备" : "未准备");
            if (hostBadge != null) hostBadge.SetActive(member.IsHost);
            if (kickButton != null) kickButton.gameObject.SetActive(canKick && !member.IsHost);
        }

        /// <summary>空位。</summary>
        public void SetEmpty()
        {
            playerId = string.Empty;

            if (occupiedRoot != null) occupiedRoot.SetActive(false);
            if (emptyRoot != null) emptyRoot.SetActive(true);
            if (hostBadge != null) hostBadge.SetActive(false);
            if (kickButton != null) kickButton.gameObject.SetActive(false);
        }

        /// <summary>绑到踢出按钮的 OnClick。</summary>
        public void OnKickClicked()
        {
            if (string.IsNullOrEmpty(playerId)) return;
            KickRequested?.Invoke(playerId);
        }
    }
}
