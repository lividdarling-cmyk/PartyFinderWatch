using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace PartyFinderWatch;

internal sealed class WorldDataCenters
{
    private readonly Dictionary<uint, WorldDCGroupType> byWorld = [];

    internal WorldDataCenters(IDataManager dataManager)
        : this(dataManager.GetExcelSheet<World>(), dataManager.GetExcelSheet<WorldDCGroupType>()) { }

    internal WorldDataCenters(IEnumerable<World> worlds, ExcelSheet<WorldDCGroupType> dataCenters)
    {
        // The older API 15 sheet exposes the same logical data center column as Unknown0.
        var logicalColumn = typeof(World).GetProperty("LogicalDataCenter") ?? typeof(World).GetProperty("Unknown0");
        foreach (var world in worlds)
        {
            uint logicalId = logicalColumn?.GetValue(world) switch
            {
                RowRef<WorldDCGroupType> linked => linked.RowId,
                byte id => id,
                _ => 0,
            };

            uint dataCenterId = logicalId != 0 ? logicalId : world.DataCenter.RowId;
            if (dataCenterId != 0 && dataCenters.TryGetRow(dataCenterId, out var dataCenter))
                byWorld[world.RowId] = dataCenter;
        }
    }

    internal bool TryGet(uint worldId, out WorldDCGroupType dataCenter) => byWorld.TryGetValue(worldId, out dataCenter);
}
