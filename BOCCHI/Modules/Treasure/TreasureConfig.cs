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

    [IntRange(0, 50)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public int TreasureSightEveryNLocations { get; set; } = 10;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public bool HuntSilverChestsOnly { get; set; } = false;

    public bool ShouldHuntSilverChestsOnly
    {
        get => IsPropertyEnabled(nameof(HuntSilverChestsOnly));
    }

    [IntRange(0, 100)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public int HuntMinBronzePercent { get; set; } = 50;

    [IntRange(0, 100)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public int HuntMinSilverPercent { get; set; } = 50;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public bool SkipUnsafeTreasureWindows { get; set; } = true;

    public bool ShouldSkipUnsafeTreasureWindows
    {
        get => IsPropertyEnabled(nameof(SkipUnsafeTreasureWindows));
    }

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public bool ReturnToBaseCampAfterHunt { get; set; } = true;

    public bool ShouldReturnToBaseCampAfterHunt
    {
        get => IsPropertyEnabled(nameof(ReturnToBaseCampAfterHunt));
    }

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public bool PlaySoundOnHuntComplete { get; set; } = true;

    public bool ShouldPlaySoundOnHuntComplete
    {
        get => IsPropertyEnabled(nameof(PlaySoundOnHuntComplete));
    }

    [Checkbox] public bool CastTreasureSightUponReturn { get; set; } = false;

    [Checkbox] public bool ShowPercentageActiveTreasureCount { get; set; } = false;

    public TreasureRouteStartMode NorthHornRouteStartMode { get; set; } = TreasureRouteStartMode.Nearest;

    public int NorthHornManualRouteStart { get; set; } = 1;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    [Label("modules.treasure.config.use_south_horn_authored_route.label")]
    [Tooltip("modules.treasure.config.use_south_horn_authored_route.tooltip")]
    public bool UseSouthHornAuthoredRoute { get; set; } = true;

    /// <summary>A <see cref="SouthHornTreasureRoute"/> segment id; null starts at the nearest coffer.</summary>
    public string? SouthHornStartSegment { get; set; }

    public bool ShowNorthHornRouteMap { get; set; } = true;

    [Checkbox]
    [Experimental]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    public bool UseNinjaHideOnDangerousRoutes { get; set; } = false;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt), nameof(UseNinjaHideOnDangerousRoutes))]
    public bool UseOccultSprintWhileHidden { get; set; } = false;

    [IntRange(0, 100)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt), nameof(UseNinjaHideOnDangerousRoutes))]
    public int NinjaGearsetNumber { get; set; } = 0;

    [IntRange(-5, 10)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt), nameof(UseNinjaHideOnDangerousRoutes))]
    public int KnowledgeHideOffset { get; set; } = 0;

    [FloatRange(5f, 40f)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt), nameof(UseNinjaHideOnDangerousRoutes))]
    public float KnowledgeThreatEnterDistance { get; set; } = 10f;

    [FloatRange(10f, 60f)]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt), nameof(UseNinjaHideOnDangerousRoutes))]
    public float KnowledgeThreatExitDistance { get; set; } = 20f;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(EnableTreasureHunt))]
    [Label("modules.treasure.config.show_spirit_pot_prediction.label")]
    public bool ShowSpiritPotPrediction { get; set; } = true;

    public bool ShouldShowSpiritPotPrediction
    {
        get => IsPropertyEnabled(nameof(ShowSpiritPotPrediction));
    }
}
