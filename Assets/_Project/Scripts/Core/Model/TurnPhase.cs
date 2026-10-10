namespace NomadCity.Core.Model
{
    /// <summary>一个回合内的 4 个阶段（规则规格 5 节）。</summary>
    public enum TurnPhase
    {
        CharacterPlacement = 0, // 角色牌盖放
        PlayerActions = 1,      // 玩家行动（2 轮）
        Harvest = 2,            // 采集资源
        Cleanup = 3,            // 收尾
    }
}
