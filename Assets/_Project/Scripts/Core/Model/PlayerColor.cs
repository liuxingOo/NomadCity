namespace NomadCity.Core.Model
{
    /// <summary>
    /// 玩家颜色。同时决定该玩家的起始角色牌、30 个玩家标记与移动城市。
    /// 顺序按规则规格 4.2「绿、黄、红、蓝」。
    /// </summary>
    public enum PlayerColor
    {
        Green = 0,
        Yellow = 1,
        Red = 2,
        Blue = 3,
    }
}
