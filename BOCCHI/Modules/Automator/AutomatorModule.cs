using ECommons.Automation;
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

        if (!EnsureCompatibleVnavmesh())
        {
            return;
        }

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
            navigation.Stop();
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
        var vnavmesh = GetVnavmeshVersionCheck();
        if (!vnavmesh.IsCompatible)
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
        PromeRotationController.Stop();
        if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null && navigation.IsReady())
        {
            navigation.Stop();
        }
        Plugin.Chain.Abort();
        ChainManager.AbortAll();

        if (wasEnabled)
        {
            Svc.Chat.Print(T("messages.off"));
        }
    }

    public VnavmeshVersionCheck GetVnavmeshVersionCheck()
    {
        var plugin = Svc.PluginInterface.InstalledPlugins.FirstOrDefault(p =>
            string.Equals(p.InternalName, VnavmeshVersionPolicy.PluginInternalName, StringComparison.OrdinalIgnoreCase));

        return VnavmeshVersionPolicy.Evaluate(
            plugin != null,
            plugin?.IsLoaded == true,
            plugin?.Version);
    }

    private bool EnsureCompatibleVnavmesh()
    {
        var check = GetVnavmeshVersionCheck();
        if (check.IsCompatible)
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
                navigation.Stop();
            }
            Plugin.Chain.Abort();
            ChainManager.AbortAll();
            PluginConfig.Save();
        }

        ReportVnavmeshFailure(check);
        return false;
    }

    private void ReportVnavmeshFailure(VnavmeshVersionCheck check)
    {
        if (vnavmeshFailureReported)
        {
            return;
        }

        var reason = check.Status switch
        {
            VnavmeshVersionStatus.Missing => "未安装 vnavmesh",
            VnavmeshVersionStatus.NotLoaded => "vnavmesh 未加载",
            VnavmeshVersionStatus.VersionMismatch => $"当前版本为 {check.ActualVersion?.ToString() ?? "未知"}",
            _ => "vnavmesh 状态未知",
        };
        var message = $"自动化已停用：{reason}，必须精确使用 vnavmesh {VnavmeshVersionPolicy.RequiredVersion}。";
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
