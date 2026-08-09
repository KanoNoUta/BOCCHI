using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.AggroRange;
using BOCCHI.Data;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.CeCrowdsource;
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
using System.Linq;

namespace BOCCHI;

[Serializable]
public class Config : IOcelotConfig
{
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    public bool CompactMainWindow { get; set; } = false;

    public bool ShowAdvancedUi { get; set; } = false;

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

    public CeCrowdsourceConfig CeCrowdsourceConfig { get; set; } = new();

    public BuffConfig BuffConfig { get; set; } = new();

    public AggroRangeConfig AggroRangeConfig { get; set; } = new();

    // Trackers
    public CurrencyConfig CurrencyConfig { get; set; } = new();

    public ExpConfig ExpConfig { get; set; } = new();

    // Other
    public MobFarmerConfig MobFarmerConfig { get; set; } = new();

    public AutomatorConfig AutomatorConfig { get; set; } = new();

    public DataConfig DataConfig { get; set; } = new();

    /// <summary>
    /// Applies one-way configuration migrations immediately after deserialization.
    /// The v1 pot FATE switches (PersistentPots / PleadingPots) are South Horn
    /// events, unrelated to North Horn 2072/2073, so migration deliberately does
    /// NOT copy them: a v1 config predates the North Horn switches entirely and
    /// 2072/2073 fall back to their enabled-by-default initializer values, just
    /// like a fresh install. Any config that already stored values for these
    /// switches (v2+) keeps its stored value, so an explicit user choice is
    /// never overridden.
    /// </summary>
    public bool Migrate()
    {
        var changed = false;

        if (Version < 2)
        {
            // Version 1 never serialized DoNorthHornFate2072/2073. Leave them
            // alone so they keep the new defaults; only fix up null subtrees
            // that pre-date nested config objects.
            AutomatorConfig ??= new AutomatorConfig();
            Version = 2;
            changed = true;
        }

        MobFarmerConfig ??= new MobFarmerConfig();
        AggroRangeConfig ??= new AggroRangeConfig();
        AggroRangeConfig.Calibrations ??= [];
        MobFarmerConfig.Mobs ??= [];
        MobFarmerConfig.SouthHornMobs ??= [];
        MobFarmerConfig.NorthHornMobs ??= [];

        if (Version < 3)
        {
            foreach (var mob in MobFarmerConfig.Mobs.Distinct())
            {
                var destination = MobData.IsNorthHornMob(mob)
                    ? MobFarmerConfig.NorthHornMobs
                    : MobFarmerConfig.SouthHornMobs;
                if (!destination.Contains(mob))
                {
                    destination.Add(mob);
                }
            }

            MobFarmerConfig.Mobs.Clear();
            Version = 3;
            changed = true;
        }

        if (Version < 4)
        {
            // CE 众包面板现在默认保留历史记录。活动项仍可由用户在设置中单独筛选。
            CeCrowdsourceConfig ??= new CeCrowdsourceConfig();
            CeCrowdsourceConfig.ShowOnlyActive = false;
            Version = 4;
            changed = true;
        }

        return changed;
    }

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}

