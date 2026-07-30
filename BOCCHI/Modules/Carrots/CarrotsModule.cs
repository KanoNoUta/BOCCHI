using Ocelot.Modules;
using Ocelot.Windows;
using System.Collections.Generic;

namespace BOCCHI.Modules.Carrots;

[OcelotModule(1004, 2)]
public class CarrotsModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override CarrotsConfig Config
    {
        get => PluginConfig.CarrotsConfig;
    }

    public override bool ShouldUpdate
    {
        get => true;
    }

    public override bool ShouldInitialize
    {
        get => true;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    private readonly CarrotsTracker tracker = new();

    private CarrotHunt hunter = null!;

    public List<Carrot> carrots
    {
        get => tracker.carrots;
    }

    private readonly Panel panel = new();

    private readonly Radar radar = new();

    public override void PostInitialize()
    {
        hunter = new CarrotHunt(this);
    }

    public override void Update(UpdateContext context)
    {
        tracker.Tick(context.Framework);
        if (BOCCHI.Data.ZoneData.IsInNorthHorn())
        {
            hunter.ObserveRuntimeNodes();
        }

        if (BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            hunter.Update();
        }
    }

    public override void Render(RenderContext context)
    {
        radar.Draw(context.ForModule(this));
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);

        if (Config.ShouldEnableCarrotHunt && BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            hunter.Draw(this);
        }

        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        StopHunt();
        tracker.Reset();
    }

    public void StopHunt()
    {
        hunter?.Stop();
    }

    public override void Dispose()
    {
        StopHunt();
        base.Dispose();
    }
}
