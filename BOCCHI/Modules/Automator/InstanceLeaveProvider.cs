using ECommons.DalamudServices;
using Ocelot.Config.Handlers;
using System;
using System.Linq;

namespace BOCCHI.Modules.Automator;

/// <summary>
/// The plugin whose instant-leave command ends the duty during instance
/// rotation. BOCCHI never turns the feature on; the settings page asks the
/// user to enable it in the chosen plugin.
/// </summary>
public enum InstanceLeaveProvider
{
    Omni,
    XszToolbox,
    DailyRoutines,
}

public static class InstanceLeaveProviderExtensions
{
    public static string GetLeaveCommand(this InstanceLeaveProvider provider)
    {
        return provider switch
        {
            InstanceLeaveProvider.XszToolbox => "/xsz leaveduty",
            InstanceLeaveProvider.DailyRoutines => "/pdr leaveduty",
            _ => "/omni 即刻退本",
        };
    }

    /// <summary>The chat command the plugin registers while it is loaded.</summary>
    public static string GetRootCommand(this InstanceLeaveProvider provider)
    {
        return provider switch
        {
            InstanceLeaveProvider.XszToolbox => "/xsz",
            InstanceLeaveProvider.DailyRoutines => "/pdr",
            _ => "/omni",
        };
    }

    public static string GetLabel(this InstanceLeaveProvider provider)
    {
        return provider switch
        {
            InstanceLeaveProvider.XszToolbox => "XSZToolbox",
            InstanceLeaveProvider.DailyRoutines => "DailyRoutines",
            _ => "Omni",
        };
    }

    // Plugin internal names differ between builds, so a loaded plugin is
    // detected by the command it registers rather than by its manifest.
    public static bool IsLoaded(this InstanceLeaveProvider provider)
    {
        var root = provider.GetRootCommand();
        try
        {
            return Svc.Commands.Commands.Keys.Any(command =>
                string.Equals(command, root, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, "Could not read the registered chat commands.");
            return false;
        }
    }
}

public sealed class InstanceLeaveProviderProvider : EnumProvider<InstanceLeaveProvider>
{
    public override string GetLabel(InstanceLeaveProvider item)
    {
        return item.GetLabel();
    }
}
