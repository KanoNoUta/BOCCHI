using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Currency;
using BOCCHI.Modules.Data;
using BOCCHI.Modules.EventDrop;
using BOCCHI.Modules.Exp;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.ForkedTower;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.Mount;
using BOCCHI.Modules.Pathfinder;
using BOCCHI.Modules.StateManager;
using BOCCHI.Modules.Teleporter;
using BOCCHI.Modules.Treasure;
using BOCCHI.Modules.WindowManager;
using ECommons.DalamudServices;
using Ocelot;
using System;

namespace BOCCHI;

[Serializable]
public class Config : IOcelotConfig
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    // Core
    public MountConfig MountConfig { get; set; } = new();

    public TeleporterConfig TeleporterConfig { get; set; } = new();

    public PathfinderConfig PathfinderConfig { get; set; } = new();

    public EventDropConfig EventDropConfig { get; set; } = new();

    public WindowManagerConfig WindowManagerConfig { get; set; } = new();

    public StateManagerConfig StateManagerConfig { get; set; } = new();

    // Functional

    public FatesConfig FatesConfig { get; set; } = new();

    public CriticalEncountersConfig CriticalEncountersConfig { get; set; } = new();

    public ForkedTowerConfig ForkedTowerConfig { get; set; } = new();

    public TreasureConfig TreasureConfig { get; set; } = new();

    public CarrotsConfig CarrotsConfig { get; set; } = new();

    public BuffConfig BuffConfig { get; set; } = new();

    // Trackers
    public CurrencyConfig CurrencyConfig { get; set; } = new();

    public ExpConfig ExpConfig { get; set; } = new();

    // Other
    public MobFarmerConfig MobFarmerConfig { get; set; } = new();

    public AutomatorConfig AutomatorConfig { get; set; } = new();

    public DataConfig DataConfig { get; set; } = new();

    /// <summary>
    /// Applies one-way configuration migrations immediately after deserialization.
    /// The old pot FATE switches were opt-in; their North Horn replacements must
    /// keep that explicit user choice instead of silently becoming enabled.
    /// </summary>
    public bool Migrate()
    {
        var changed = false;

        if (Version < 2)
        {
            AutomatorConfig ??= new AutomatorConfig();
            AutomatorConfig.DoNorthHornFate2072 = AutomatorConfig.DoPersistentPots;
            AutomatorConfig.DoNorthHornFate2073 = AutomatorConfig.DoPleadingPots;
            Version = 2;
            changed = true;
        }

        return changed;
    }

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}
