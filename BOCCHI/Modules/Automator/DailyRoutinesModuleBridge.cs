using ECommons.DalamudServices;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons.Automation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Automator;

public enum DailyRoutinesModuleStatus
{
    Unavailable,
    Enabling,
    Ready,
}

/// <summary>
/// Enables the DailyRoutines modules that own the instance entry and exit
/// commands. The module names and IPC signatures are from DailyRoutines
/// 2.1.7.0's public PluginIPC contract.
/// </summary>
public static class DailyRoutinesModuleBridge
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly Dictionary<string, DateTimeOffset> loadRequestedAt = new();

    public const string PluginInternalName = "DailyRoutines";
    public const string IsModuleEnabledIpcName = "DailyRoutines.IsModuleEnabled";
    public const string LoadModuleIpcName = "DailyRoutines.LoadModule";
    public const string AutoTalkSkipModuleName = "AutoTalkSkip";
    public const string FieldEntryCommandModuleName = "FieldEntryCommand";
    public const string InstantLeaveDutyModuleName = "InstantLeaveDuty";

    // Load prerequisites first. FieldEntryCommand declares AutoTalkSkip as its
    // prerequisite, so it is not requested until AutoTalkSkip reports ready.
    public static IReadOnlyList<string> RequiredModuleNames { get; } =
        new[]
        {
            AutoTalkSkipModuleName,
            FieldEntryCommandModuleName,
            InstantLeaveDutyModuleName,
        };

    public static bool IsLoaded => Svc.PluginInterface.InstalledPlugins.Any(plugin =>
        string.Equals(plugin.InternalName, PluginInternalName, StringComparison.OrdinalIgnoreCase)
        && plugin.IsLoaded);

    public static string GetLoadCommand(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ArgumentException("DailyRoutines module name cannot be empty.", nameof(moduleName));
        }

        return $"/pdr load {moduleName}";
    }

    public static bool ShouldUseCommandFallback(bool ipcAccepted) => !ipcAccepted;

    public static DailyRoutinesModuleStatus EnsureRequiredModules()
    {
        if (!IsLoaded)
        {
            loadRequestedAt.Clear();
            return DailyRoutinesModuleStatus.Unavailable;
        }

        try
        {
            var isEnabled = Svc.PluginInterface
                .GetIpcSubscriber<string, bool?>(IsModuleEnabledIpcName);
            var loadModule = Svc.PluginInterface
                .GetIpcSubscriber<string, bool, bool>(LoadModuleIpcName);
            var now = DateTimeOffset.UtcNow;

            foreach (var moduleName in RequiredModuleNames)
            {
                var enabled = isEnabled.InvokeFunc(moduleName);
                if (enabled == true)
                {
                    loadRequestedAt.Remove(moduleName);
                    continue;
                }

                if (enabled == null)
                {
                    Svc.Log.Warning($"DailyRoutines module was not found: {moduleName}");
                    return DailyRoutinesModuleStatus.Unavailable;
                }

                if (!loadRequestedAt.TryGetValue(moduleName, out var requestedAt)
                    || now - requestedAt >= RetryInterval)
                {
                    loadRequestedAt[moduleName] = now;
                    var ipcAccepted = false;
                    try
                    {
                        ipcAccepted = loadModule.InvokeFunc(moduleName, true);
                    }
                    catch (IpcNotReadyError)
                    {
                        // /pdr can be ready one frame before every public IPC
                        // gate; the rate-limited command fallback covers that gap.
                    }

                    var commandFallback = false;
                    if (ShouldUseCommandFallback(ipcAccepted))
                    {
                        Chat.ExecuteCommand(GetLoadCommand(moduleName));
                        commandFallback = true;
                    }

                    Svc.Log.Info(
                        $"Requested DailyRoutines module enable: {moduleName} "
                        + $"(ipcAccepted={ipcAccepted}, commandFallback={commandFallback})");
                }

                return DailyRoutinesModuleStatus.Enabling;
            }

            return DailyRoutinesModuleStatus.Ready;
        }
        catch (IpcNotReadyError)
        {
            // InstalledPlugins can report the plugin as loaded one framework
            // tick before its call gates are registered. Keep the queued entry
            // request alive and retry instead of turning that startup race into
            // a permanent failed rotation.
            return DailyRoutinesModuleStatus.Enabling;
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, "DailyRoutines module IPC was unavailable while preparing pdr commands.");
            return DailyRoutinesModuleStatus.Unavailable;
        }
    }
}
