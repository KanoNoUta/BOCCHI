using BOCCHI.Data;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.Treasure;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.IPC;
using System;

namespace BOCCHI.Services;

/// <summary>
/// Stops BOCCHI's walking when the player (or their rotation plugin) uses a
/// combat action while nothing automated owns the movement, so a manual fight
/// is not dragged along by a leftover route. Ported from upstream BOCCHI 4.2's
/// CombatPathfindCancelService.
/// </summary>
public sealed unsafe class ManualCastPathCancel : IDisposable
{
    private delegate bool UseActionDelegate(
        ActionManager* manager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted);

    private readonly Plugin plugin;

    private Hook<UseActionDelegate>? hook;

    public ManualCastPathCancel(Plugin plugin)
    {
        this.plugin = plugin;
        try
        {
            hook = Svc.Hook.HookFromAddress<UseActionDelegate>(
                (nint)ActionManager.MemberFunctionPointers.UseAction,
                Detour);
            hook.Enable();
        }
        catch (Exception exception)
        {
            Svc.Log.Error($"[ManualCastPathCancel] Could not hook UseAction: {exception.Message}");
        }
    }

    private bool Detour(
        ActionManager* manager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        try
        {
            if (ManualCastPathCancelPolicy.ShouldConsider(
                    plugin.Config.PathfinderConfig.CancelPathingOnManualCast,
                    ZoneData.IsInOccultCrescent(),
                    actionType == ActionType.Action,
                    actionId)
                && Plugin.Chain.IsRunning
                && !AutomationOwnsMovement())
            {
                var vnav = plugin.IPC.GetSubscriber<VNavmesh>();
                var lifestream = plugin.IPC.GetSubscriber<Lifestream>();
                // Never cut an aethernet hop in half.
                if (vnav.IsReady()
                    && (vnav.IsRunning() || vnav.IsPathfinding())
                    && !(lifestream.IsReady() && lifestream.IsBusy()))
                {
                    Svc.Log.Info($"Action {actionId} used by hand; stopping BOCCHI's walking.");
                    Plugin.Chain.Abort();
                    vnav.Stop();
                }
            }
        }
        catch (Exception exception)
        {
            Svc.Log.Debug($"[ManualCastPathCancel] {exception.Message}");
        }

        return hook!.Original(manager, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    private bool AutomationOwnsMovement()
    {
        var modules = plugin.Modules;
        return (modules.TryGetModule<AutomatorModule>(out var automator) && automator?.IsEnabled == true)
               || (modules.TryGetModule<TreasureModule>(out var treasure) && treasure?.IsHuntRunning == true)
               || (modules.TryGetModule<CarrotsModule>(out var carrots) && carrots?.IsHuntRunning == true)
               || (modules.TryGetModule<MobFarmerModule>(out var farmer) && farmer?.Farmer.Running == true);
    }

    public void Dispose()
    {
        hook?.Disable();
        hook?.Dispose();
        hook = null;
    }
}

public static class ManualCastPathCancelPolicy
{
    /// <summary>Only real combat actions in Occult Crescent; mounts, sprint and phantom general actions never cancel.</summary>
    public static bool ShouldConsider(bool enabled, bool inOccultCrescent, bool isCombatAction, uint actionId)
    {
        return enabled && inOccultCrescent && isCombatAction && actionId != 0;
    }
}
