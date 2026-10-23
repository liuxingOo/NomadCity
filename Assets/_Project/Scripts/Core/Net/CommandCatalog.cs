using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// 命令类型 → <see cref="CommandKind"/> 的登记表。新增一条命令务必在此登记，
    /// 否则 <c>CommandCodecCoverageTests</c> 的覆盖测试会立刻失败（防「新增命令忘了编码」）。
    /// </summary>
    public static class CommandCatalog
    {
        private static readonly Dictionary<Type, CommandKind> Map = new Dictionary<Type, CommandKind>
        {
            { typeof(StartGameCommand), CommandKind.StartGame },
            { typeof(AdvancePhaseCommand), CommandKind.AdvancePhase },
            { typeof(ChooseStartNodeCommand), CommandKind.ChooseStartNode },
            { typeof(ResolveEventOptionCommand), CommandKind.ResolveEventOption },
            { typeof(DiscardSupplyFacilityCommand), CommandKind.DiscardSupplyFacility },
            { typeof(BuildFacilityCommand), CommandKind.BuildFacility },
            { typeof(PlaceCharacterCardCommand), CommandKind.PlaceCharacterCard },
            { typeof(UseCharacterCardCommand), CommandKind.UseCharacterCard },
            { typeof(EndCharacterCardUseCommand), CommandKind.EndCharacterCardUse },
            { typeof(DeclareCityPatternCommand), CommandKind.DeclareCityPattern },
            { typeof(ExploreCommand), CommandKind.Explore },
            { typeof(ExploreInfluenceCommand), CommandKind.ExploreInfluence },
            { typeof(MoveMobileCityCommand), CommandKind.MoveMobileCity },
            { typeof(MoveCityInfluenceCommand), CommandKind.MoveCityInfluence },
            { typeof(PlaceInfluenceCommand), CommandKind.PlaceInfluence },
            { typeof(RelocateInfluenceCommand), CommandKind.RelocateInfluence },
            { typeof(UsePatternSpecialActionCommand), CommandKind.UsePatternSpecialAction },
            { typeof(ResolveHarvestCommand), CommandKind.ResolveHarvest },
            { typeof(SubmitHarvestChoiceCommand), CommandKind.SubmitHarvestChoice },
        };

        /// <summary>查命令类型对应的 Kind；未登记时抛出异常（有意为之，逼新增命令登记）。</summary>
        public static CommandKind KindOf(Type commandType)
        {
            if (commandType != null && Map.TryGetValue(commandType, out CommandKind kind)) return kind;
            throw new ArgumentException(
                $"命令类型 {commandType?.Name ?? "null"} 还没有登记到 CommandCatalog（阶段 1 传输抽象的登记表）。",
                nameof(commandType));
        }
    }
}
