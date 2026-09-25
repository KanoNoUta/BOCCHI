using BOCCHI.Chains;
using BOCCHI.Modules.StateManager;
using Ocelot.Modules;

namespace BOCCHI.Modules.Teleporter;

[OcelotModule(2)]
public class TeleporterModule : Module
{
    public override TeleporterConfig Config
    {
        get => PluginConfig.TeleporterConfig;
    }

    public override bool ShouldInitialize
    {
        get => true;
    }

    public readonly Teleporter teleporter;

    internal ReturnChain? ActiveReturn { get; set; }

    public bool ShouldPreserveReturn(uint territory) => ActiveReturn?.ShouldPreserveTransition(territory) == true;

    public TeleporterModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        teleporter = new Teleporter(this);
    }

    public override void Initialize()
    {
        var states = GetModule<StateManagerModule>();
        states.OnExitInFate += teleporter.OnFateEnd;
        states.OnExitInCriticalEncounter += teleporter.OnCriticalEncounterEnd;
    }

    public override void Dispose()
    {
        var states = GetModule<StateManagerModule>();
        states.OnExitInFate -= teleporter.OnFateEnd;
        states.OnExitInCriticalEncounter -= teleporter.OnCriticalEncounterEnd;

        ActiveReturn = null;
    }

    public bool IsReady()
    {
        return teleporter.IsReady();
    }
}
