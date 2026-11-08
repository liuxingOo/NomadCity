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

        /// <summary>
        /// 玩家身份令牌（与 Relay 重连用的是同一个本机令牌）。用来把名单里的选择对到 Relay 座位上：
        /// 两个客户端几乎同时连入时，连接顺序不一定等于名单顺序。
        /// </summary>
        public readonly string Identity;

        /// <summary>
        /// 这名玩家的 Relay 座位号（由玩家自己写进 Lobby，-1 = 还不知道）。
        /// 名单是按加入时间排的、座位是按连入顺序分的，两者可能不一致，靠它精确对齐。
        /// </summary>
        public readonly int Seat;

        public LobbyMemberInfo(string playerId, string nickname, bool isHost, bool ready, int color = -1,
            string identity = null, int seat = -1)
        {
            PlayerId = playerId ?? string.Empty;
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "玩家" : nickname;
            IsHost = isHost;
            Ready = ready;
            Color = color;
            Identity = identity ?? string.Empty;
            Seat = seat;
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
