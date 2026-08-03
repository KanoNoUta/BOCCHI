using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace BOCCHI.Modules.Treasure;

public class TreasureConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool DrawLineToBronzeChests { get; set; } = true;

    public bool ShouldDrawLineToBronzeChests
    {
        get => IsPropertyEnabled(nameof(DrawLineToBronzeChests));
    }

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool DrawLineToSilverChests { get; set; } = true;

    public bool ShouldDrawLineToSilverChests
    {
        get => IsPropertyEnabled(nameof(DrawLineToSilverChests));
    }

    [Checkbox]
    [Experimental]
    [Illegal]
    [RequiredPlugin("vnavmesh", "Lifestream")]
    [DependsOn(nameof(Enabled))]
    [Label("modules.treasure.config.show_hunt_button.label")]
    public bool EnableTreasureHunt { get; set; } = false;

    public bool ShouldEnableTreasureHunt
    {
        get => IsPropertyEnabled(nameof(EnableTreasureHunt));
    }

    [Checkbox]
    [Experimental]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public bool CastTreasureSightBeforeHunt { get; set; } = true;

    [Checkbox] public bool CastTreasureSightUponReturn { get; set; } = false;

    [Checkbox] public bool ShowPercentageActiveTreasureCount { get; set; } = false;

    public TreasureRouteStartMode NorthHornRouteStartMode { get; set; } = TreasureRouteStartMode.Nearest;

    public int NorthHornManualRouteStart { get; set; } = 1;

    public bool ShowNorthHornRouteMap { get; set; } = true;
}
