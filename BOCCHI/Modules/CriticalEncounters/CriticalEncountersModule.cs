using BOCCHI.Data;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Modules;
using Ocelot.Windows;
using System.Collections.Generic;

namespace BOCCHI.Modules.CriticalEncounters;

[OcelotModule(1002, 6)]
public class CriticalEncountersModule : Module
{
    public override CriticalEncountersConfig Config
    {
        get => PluginConfig.CriticalEncountersConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    public readonly CriticalEncounterTracker Tracker;

    public Dictionary<uint, CriticalEncounterSnapshot> CriticalEncounters
    {
        get => Tracker.CriticalEncounters;
    }

    public Dictionary<uint, EventProgress> Progress
    {
        get => Tracker.Progress;
    }

    private readonly Panel panel = new();

    private readonly Alerter alerter;

    public CriticalEncountersModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        Tracker = new CriticalEncounterTracker(this);
        alerter = new Alerter(this);
    }

    public override void Update(UpdateContext context)
    {
        Tracker.Tick(context.Framework);
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        Tracker.Reset();
    }

    public override void Dispose()
    {
        Tracker.TowerTimer.Dispose();
        base.Dispose();
        alerter.Dispose();
    }
}
