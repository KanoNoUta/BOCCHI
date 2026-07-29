using ECommons.DalamudServices;
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
    public const string PluginInternalName = "PromeRotation";
    public const string StartIpcName = "PromeRotation.IPC.Start";
    public const string StopIpcName = "PromeRotation.IPC.Stop";
    public const string IsRunningIpcName = "PromeRotation.IPC.IsRunning";

    public static bool Start()
    {
        return Invoke(StartIpcName, "start");
    }

    public static bool Stop()
    {
        return Invoke(StopIpcName, "stop");
    }

    private static bool Invoke(string ipcName, string operation)
    {
        try
        {
            if (!Svc.PluginInterface.InstalledPlugins.Any(plugin =>
                    plugin.InternalName == PluginInternalName && plugin.IsLoaded))
            {
                return false;
            }

            var succeeded = Svc.PluginInterface
                .GetIpcSubscriber<bool>(ipcName)
                .InvokeFunc();

            if (!succeeded)
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
            Svc.Log.Warning(exception, $"PromeRotation IPC failed to {operation} the automatic rotation.");
            return false;
        }
    }
}
