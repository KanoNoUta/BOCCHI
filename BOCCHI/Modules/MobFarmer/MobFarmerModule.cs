using Ocelot;
using Ocelot.Chain;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.Windows;

namespace BOCCHI.Modules.MobFarmer;

[OcelotModule(int.MaxValue - 2)]
public class MobFarmerModule : Module
{
    public override MobFarmerConfig Config
    {
        get => PluginConfig.MobFarmerConfig;
    }

    public override bool IsEnabled
    {
        get => Config.Enabled;
    }

    private readonly Panel panel = new();

    public readonly Scanner Scanner;

    public readonly Farmer Farmer;

    public MobFarmerModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        Scanner = new Scanner(this);
        Farmer = new Farmer(this);
    }

    public override void Update(UpdateContext context)
    {
        Scanner.Tick(context.Framework);
        Farmer.Update(context.ForModule(this));
    }

    public override void Render(RenderContext context)
    {
        Farmer.Draw(context.ForModule(this));
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        if (!Farmer.Running)
        {
            return;
        }

        if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null && navigation.IsReady())
        {
            BOCCHI.Pathfinding.AggroAvoidanceNavigation.Stop(navigation);
        }

        Plugin.Chain.Abort();
        ChainManager.Get("MobFarmer+Farmer").Abort();
        Farmer.DisableFarmerMode();
    }

    public override void Dispose()
    {
        Farmer.Dispose();
    }
}
