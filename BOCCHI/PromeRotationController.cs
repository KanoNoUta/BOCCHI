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

    public const string PluginInternalName = "PromeRotation";
    public const string StartIpcName = "PromeRotation.IPC.Start";
    public const string StopIpcName = "PromeRotation.IPC.Stop";
    public const string IsRunningIpcName = "PromeRotation.IPC.IsRunning";

    public static void Start()
    {
        Invoke(StartIpcName, "start");
    }

    public static void Stop()
    {
        Invoke(StopIpcName, "stop");
    }

    private static void Invoke(string ipcName, string operation)
    {
        try
        {
            if (!Svc.PluginInterface.InstalledPlugins.Any(plugin =>
                    plugin.InternalName == PluginInternalName && plugin.IsLoaded))
            {
                return;
            }

            var succeeded = Svc.PluginInterface
                .GetIpcSubscriber<bool>(ipcName)
                .InvokeFunc();

            if (!succeeded && ShouldLogFailure(operation))
            {
                Svc.Log.Warning($"PromeRotation IPC did not {operation} the automatic rotation.");
            }
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
        }
    }

    private static bool ShouldLogFailure(string operation)
    {
        return EzThrottler.Throttle($"PromeRotationController.{operation}.Failure", FailureLogIntervalMs);
    }
}
