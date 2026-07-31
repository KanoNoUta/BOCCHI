using ECommons.Automation;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.Treasure;
using ECommons.DalamudServices;
using BOCCHI.Pathfinding;
using Ocelot;
using Ocelot.Chain;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Automator;

[OcelotModule(int.MaxValue - 1)]
public class AutomatorModule : Module
{
    private bool vnavmeshFailureReported;

    public override AutomatorConfig Config
    {
        get => PluginConfig.AutomatorConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public readonly Automator automator = new();

    public readonly InstanceRotationController instanceRotation = new();

    public readonly Panel panel = new();

    public readonly Random random = new();

    public AutomatorModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        config.AutomatorConfig.Enabled = false;
        config.Save();
    }


    public override void PostUpdate(UpdateContext context)
    {
        if (!Config.Enabled)
        {
            return;
        }

        if (!EnsureVnavmeshAvailable())
        {
            return;
        }

        instanceRotation.PollDailyRoutinesCommandModules(this);
        if (instanceRotation.PostUpdate(this))
        {
            return;
        }

        automator.PostUpdate(this, context.Framework);
    }


    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        // Navigation and submitted activity chains are territory-bound. Always
        // terminate them before refreshing, including South Horn <-> North Horn.
        Plugin.Chain.Abort();
        if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null && navigation.IsReady())
        {
            AggroAvoidanceNavigation.Stop(navigation);
        }
        SetAiProviderEnabled(false);
        PromeRotationController.Stop();

        automator.Refresh();
        instanceRotation.OnTerritoryChanged(id);

        if (BOCCHI.Data.ZoneData.IsOccultCrescentTerritory(id))
        {
            return;
        }

        if (InstanceRotationController.IsTransitionActive)
        {
            return;
        }

        Config.Enabled = false;
        PluginConfig.Save();
    }

    public static void ToggleIllegalMode(OcelotPlugin plugin)
    {
        var module = plugin.Modules.GetModule<AutomatorModule>();
        if (!module.Config.Enabled)
        {
            module.EnableIllegalMode();
        }
        else
        {
            module.DisableIllegalMode();
        }

        if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
        {
            Chat.ExecuteCommand("/aeTargetSelector off");
        }
    }

    public void EnableIllegalMode()
    {
        var vnavmesh = GetVnavmeshAvailability();
        if (!vnavmesh.IsAvailable)
        {
            Config.Enabled = false;
            PluginConfig.Save();
            ReportVnavmeshFailure(vnavmesh);
            return;
        }

        vnavmeshFailureReported = false;
        var wasDisabled = !Config.Enabled;
        Config.Enabled = true;
        if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
        {
            SetAiProviderEnabled(false);
        }
        PromeRotationController.Stop();

        if (wasDisabled)
        {
            if (!BOCCHI.Data.ZoneData.IsInOccultCrescent())
            {
                instanceRotation.EnsureDailyRoutinesCommandModules(this);
                if (!instanceRotation.TryStartFromOutside(this))
                {
                    Config.Enabled = false;
                    instanceRotation.Reset();
                    PluginConfig.Save();
                    return;
                }
            }
            else if (Config.ShouldAutoRotateInstance)
            {
                instanceRotation.EnsureDailyRoutinesCommandModules(this);
            }

            Svc.Chat.Print(T("messages.on"));
        }
    }

    public void DisableIllegalMode()
    {
        var wasEnabled = Config.Enabled;
        // Do this before clearing Enabled: ShouldToggleAiProvider is a
        // configuration-dependent property and the plugin must release any
        // BossMod AI state it owns while the module is still active.
        SetAiProviderEnabled(false);
        Config.Enabled = false;
        instanceRotation.Reset();
        automator.Refresh();

        // Stop persistent feature state before aborting queues. Hunters and
        // the mob farmer otherwise remain marked as running and submit fresh
        // work on the frame after an emergency stop.
        if (TryGetModule<TreasureModule>(out var treasure) && treasure != null)
        {
            treasure.StopHunt();
        }
        if (TryGetModule<CarrotsModule>(out var carrots) && carrots != null)
        {
            carrots.StopHunt();
        }
        if (TryGetModule<MobFarmerModule>(out var mobFarmer) && mobFarmer != null)
        {
            mobFarmer.Farmer.DisableFarmerMode();
        }
        if (TryGetModule<BuffModule>(out var buffs) && buffs != null)
        {
            buffs.BuffManager.CancelPending();
        }

        PromeRotationController.Stop();
        Plugin.Chain.Abort();
        ChainManager.AbortAll();

        if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null && navigation.IsReady())
        {
            try
            {
                AggroAvoidanceNavigation.Stop(navigation);
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Emergency stop could not stop vnavmesh because its IPC became unavailable.");
            }
        }
        if (TryGetIPCSubscriber<Lifestream>(out var lifestream) && lifestream != null && lifestream.IsReady())
        {
            try
            {
                lifestream.Abort();
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Emergency stop could not abort Lifestream because its IPC became unavailable.");
            }
        }

        if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
        {
            Chat.ExecuteCommand("/aeTargetSelector off");
            Chat.ExecuteCommand("/aepull off");
        }

        Svc.Targets.Target = null;
        PluginConfig.Save();

        if (wasEnabled)
        {
            Svc.Chat.Print(T("messages.off"));
        }
    }

    public VnavmeshAvailabilityCheck GetVnavmeshAvailability()
    {
        var plugin = Svc.PluginInterface.InstalledPlugins.FirstOrDefault(p =>
            string.Equals(p.InternalName, VnavmeshAvailabilityPolicy.PluginInternalName, StringComparison.OrdinalIgnoreCase));

        return VnavmeshAvailabilityPolicy.Evaluate(
            plugin != null,
            plugin?.IsLoaded == true,
            plugin?.Version);
    }

    private bool EnsureVnavmeshAvailable()
    {
        var check = GetVnavmeshAvailability();
        if (check.IsAvailable)
        {
            vnavmeshFailureReported = false;
            return true;
        }

        if (Config.Enabled)
        {
            SetAiProviderEnabled(false);
            Config.Enabled = false;
            instanceRotation.Reset();
            automator.Refresh();
            PromeRotationController.Stop();
            if (TryGetIPCSubscriber<VNavmesh>(out var navigation)
                && navigation != null
                && navigation.IsReady())
            {
                AggroAvoidanceNavigation.Stop(navigation);
            }
            Plugin.Chain.Abort();
            ChainManager.AbortAll();
            PluginConfig.Save();
        }

        ReportVnavmeshFailure(check);
        return false;
    }

    private void ReportVnavmeshFailure(VnavmeshAvailabilityCheck check)
    {
        if (vnavmeshFailureReported)
        {
            return;
        }

        var reason = check.Status switch
        {
            VnavmeshAvailabilityStatus.Missing => "未安装 vnavmesh",
            VnavmeshAvailabilityStatus.NotLoaded => "vnavmesh 未加载",
            _ => "vnavmesh 状态未知",
        };
        var message = $"自动化已停用：{reason}。";
        Svc.Log.Warning(message);
        Svc.Chat.PrintError(message);
        vnavmeshFailureReported = true;
    }

    public void SetAiProviderEnabled(bool enabled)
    {
        if (!Config.ShouldToggleAiProvider)
        {
            return;
        }

        if (enabled)
        {
            Config.AiProvider.On();
        }
        else
        {
            Config.AiProvider.Off();
        }
    }
}
