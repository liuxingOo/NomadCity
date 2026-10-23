namespace NomadCity.Core.Net
{
    /// <summary>
    /// 准备房间里的一个成员。放在 Core 层是为了让 <c>NomadCity.Net</c>（LobbyClient）
    /// 与 <c>NomadCity.UI</c>（准备房间面板）都能引用，同时不引入对 App / Net 的依赖。
    /// </summary>
    public readonly struct LobbyMemberInfo
    {
        /// <summary>UGS 玩家 ID（房主踢人时用）。</summary>
        public readonly string PlayerId;

        /// <summary>界面显示的昵称；空值由 LobbyClient 统一填成「玩家」。</summary>
        public readonly string Nickname;

        /// <summary>是否为房主。</summary>
        public readonly bool IsHost;

        /// <summary>是否已准备（房主固定为 true，界面显示「房主」而不是「已准备」）。</summary>
        public readonly bool Ready;

        /// <summary>选定的阵营颜色（转成 <c>PlayerColor</c> 的整数值）；-1 表示还没选。</summary>
        public readonly int Color;

        public LobbyMemberInfo(string playerId, string nickname, bool isHost, bool ready, int color = -1)
        {
            PlayerId = playerId ?? string.Empty;
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "玩家" : nickname;
            IsHost = isHost;
            Ready = ready;
            Color = color;
        }
    }

    /// <summary>准备房间里的游戏设置（房主可改，所有人读取）。</summary>
    public readonly struct LobbySettings
    {
        public readonly int PlayerCount;
        public readonly bool Enterprise;

        public LobbySettings(int playerCount, bool enterprise)
        {
            PlayerCount = playerCount;
            Enterprise = enterprise;
        }
    }
}
