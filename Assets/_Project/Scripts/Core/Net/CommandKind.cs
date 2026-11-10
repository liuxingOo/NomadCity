namespace NomadCity.Core.Net
{
    /// <summary>
    /// 命令类型标记（阶段 1：厂商中立的传输抽象）。联机协议里按它装箱 / 拆箱 <see cref="NomadCity.Core.Engine.ICommand"/>，
    /// 不做多态序列化。务必与 <see cref="CommandCatalog"/> 及 <see cref="CommandCodec"/> 三处保持一致。
    /// </summary>
    public enum CommandKind
    {
        /// <summary>未指定 / 占位。</summary>
        None = 0,

        StartGame = 1,
        AdvancePhase = 2,
        ChooseStartNode = 3,
        ResolveEventOption = 4,
        DiscardSupplyFacility = 5,
        BuildFacility = 6,
        PlaceCharacterCard = 7,
        UseCharacterCard = 8,
        EndCharacterCardUse = 9,
        DeclareCityPattern = 10,
        Explore = 11,
        ExploreInfluence = 12,
        MoveMobileCity = 13,
        MoveCityInfluence = 14,
        PlaceInfluence = 15,
        RelocateInfluence = 16,
        UsePatternSpecialAction = 17,
        ResolveHarvest = 18,
        SubmitHarvestChoice = 19,
        SkipPendingAction = 20,
        PassAction = 21,
        EndTurn = 22,

        // —— 企业扩展 ——
        ChooseInitialEnterprise = 23,
        AdvanceEnterpriseLevel = 24,
        TriggerEnterpriseEffect = 25,
    }
}
