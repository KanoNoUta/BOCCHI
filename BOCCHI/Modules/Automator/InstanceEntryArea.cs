using BOCCHI.Data;
using Ocelot.Config.Handlers;
using System;

namespace BOCCHI.Modules.Automator;

public enum InstanceEntryArea
{
    SouthHorn,
    NorthHorn,
}

public static class InstanceEntryAreaExtensions
{
    public static uint ToTerritoryId(this InstanceEntryArea area)
    {
        return area switch
        {
            InstanceEntryArea.SouthHorn => ZoneData.SOUTHHORN,
            InstanceEntryArea.NorthHorn => ZoneData.NORTHHORN,
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, null),
        };
    }
}

public sealed class InstanceEntryAreaProvider : EnumProvider<InstanceEntryArea>
{
    public override string GetLabel(InstanceEntryArea item)
    {
        return item switch
        {
            InstanceEntryArea.SouthHorn => "South Horn",
            InstanceEntryArea.NorthHorn => "North Horn",
            _ => item.ToString(),
        };
    }
}
