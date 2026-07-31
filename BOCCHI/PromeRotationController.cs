using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System;
using System.Linq;

namespace BOCCHI;

/// <summary>
/// Safe, optional integration with PromeRotation's public IPC API.
/// BOCCHI must continue running when PromeRotation is absent, unloaded, or
/// still registering its IPC endpoints during plugin startup.
/// </summary>
public static class PromeRotationController
{
    private const int FailureLogIntervalMs = 30_000;
    private static bool autoPullEnabledByBocchi;

    public const string PluginInternalName = "PromeRotation";
    public const string StartIpcName = "PromeRotation.IPC.Start";
    public const string StopIpcName = "PromeRotation.IPC.Stop";
    public const string IsRunningIpcName = "PromeRotation.IPC.IsRunning";
    public const string AutoPullOnCommand = "/pr autopull on";
    public const string AutoPullOffCommand = "/pr autopull off";

    public static bool IsLoaded
    {
        get => Svc.PluginInterface.InstalledPlugins.Any(plugin =>
            plugin.InternalName == PluginInternalName && plugin.IsLoaded);
    }

    public static void Start()
    {
        // PromeRotation's Start IPC only switches EnableAcr to On.  Outside
        // combat its decision loop still returns early unless AutoPull is on,
        // so selecting a FATE target alone is not enough to make it engage.
        SetAutoPull(true);
        Invoke(StartIpcName, "start");
    }

    public static void Stop()
    {
        Invoke(StopIpcName, "stop");
        SetAutoPull(false);
    }

    public static bool IsRunning()
    {
        if (!IsLoaded)
        {
            return false;
        }

        try
        {
            return Svc.PluginInterface
                .GetIpcSubscriber<bool>(IsRunningIpcName)
                .InvokeFunc();
        }
        catch (Exception exception)
        {
            if (ShouldLogFailure("query"))
            {
                Svc.Log.Warning(exception, "PromeRotation IPC failed to query automatic rotation state.");
            }
            return false;
        }
    }

    private static bool Invoke(string ipcName, string operation)
    {
        try
        {
            if (!IsLoaded)
            {
                return false;
            }

            var succeeded = Svc.PluginInterface
                .GetIpcSubscriber<bool>(ipcName)
                .InvokeFunc();

            if (!succeeded && ShouldLogFailure(operation))
            {
                Svc.Log.Warning($"PromeRotation IPC did not {operation} the automatic rotation.");
            }

            return succeeded;
        }
        catch (Exception exception)
        {
            // IPC endpoints can briefly be unavailable while Dalamud is
            // loading/unloading PromeRotation. Optional integration must never
            // abort a BOCCHI chain or bubble into Dalamud's draw/update loop.
            if (ShouldLogFailure(operation))
            {
                Svc.Log.Warning(exception, $"PromeRotation IPC failed to {operation} the automatic rotation.");
            }

            return false;
        }
    }

    private static void SetAutoPull(bool enabled)
    {
        if (!IsLoaded)
        {
            autoPullEnabledByBocchi = false;
            return;
        }

        if (autoPullEnabledByBocchi == enabled)
        {
            return;
        }

        try
        {
            Chat.ExecuteCommand(enabled ? AutoPullOnCommand : AutoPullOffCommand);
            autoPullEnabledByBocchi = enabled;
        }
        catch (Exception exception)
        {
            if (ShouldLogFailure(enabled ? "enable AutoPull" : "disable AutoPull"))
            {
                Svc.Log.Warning(exception,
                    $"PromeRotation command failed to {(enabled ? "enable" : "disable")} AutoPull.");
            }
        }
    }

    private static bool ShouldLogFailure(string operation)
    {
        return EzThrottler.Throttle($"PromeRotationController.{operation}.Failure", FailureLogIntervalMs);
    }
}
