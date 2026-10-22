using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Action = BOCCHI.ActionHelpers.Action;

namespace BOCCHI.Modules.MobFarmer;

/// <summary>
/// Lets a tank open on the enemy it is walking to with a ranged attack,
/// Provoke or a gap closer. Ported from upstream BOCCHI 4.2.
/// </summary>
public static class FarmerPullAssist
{
    private const ulong InvalidTargetId = 0xE0000000;

    private static readonly Action Provoke = new(ActionType.Action, MobFarmerPullPolicy.ProvokeActionId);

    public static bool IsEnabled(MobFarmerConfig config)
    {
        return config.UseRangedPull || config.UseProvoke || config.UseGapCloser;
    }

    public static bool TryPull(IBattleNpc target, MobFarmerConfig config)
    {
        var classJob = Player.Object?.ClassJob.RowId ?? 0;
        if (!IsEnabled(config)
            || !MobFarmerPullPolicy.IsTank(classJob)
            || target.IsTargetingPlayer()
            || !EzThrottler.Throttle("MobFarmer.Pull", 400))
        {
            return false;
        }

        var targetId = target.GameObjectId;
        if (targetId == 0 || targetId == InvalidTargetId)
        {
            return false;
        }

        if (config.UseRangedPull
            && MobFarmerPullPolicy.GetRangedPullActionId(classJob) is { } ranged
            && TryCast(ranged, targetId))
        {
            return true;
        }

        if (config.UseProvoke && Provoke.CanCast() && Provoke.TryCastOn(targetId))
        {
            return true;
        }

        return config.UseGapCloser
               && MobFarmerPullPolicy.GetGapCloserActionId(classJob) is { } gapCloser
               && TryCast(gapCloser, targetId);
    }

    private static bool TryCast(uint actionId, ulong targetId)
    {
        var action = new Action(ActionType.Action, actionId);
        return action.CanCast() && action.TryCastOn(targetId);
    }
}
