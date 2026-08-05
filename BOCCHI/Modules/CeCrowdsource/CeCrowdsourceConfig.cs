using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace BOCCHI.Modules.CeCrowdsource;

public class CeCrowdsourceConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    public string ServerUrl { get; set; } = "http://8.138.201.201:8080";

    [IntRange(5, 120)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled))]
    public int PollIntervalSeconds { get; set; } = 15;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool UploadObservations { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool SendHeartbeat { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool ShowOnlyActive { get; set; } = true;
}

