using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.IPC;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

public static class FateTravelTargetPolicy
{
    public static bool ShouldPursue(uint activityFateId, uint targetFateId)
    {
        return activityFateId != 0 && targetFateId == activityFateId;
    }
}

public static class FateNavigationPolicy
{
    public const float RepathDistance = 5f;

    public static bool ShouldRepath(bool navigationActive, float targetMovement)
    {
        return !navigationActive && targetMovement > RepathDistance;
    }
}

public class FateActivity(EventData data, Lifestream lifestream, VNavmesh vnav, AutomatorModule module, Fate fate)
    : Activity(data, lifestream, vnav, module)
{
    protected override TaskManagerTask GetPathfindingWatcher(StateManagerModule states)
    {
        var lastTargetPos = Vector3.Zero;
        var followingActivityTarget = false;

        return new TaskManagerTask(() =>
        {
            var target = Svc.Targets.Target is IBattleNpc
                {
                    IsDead: false,
                    IsTargetable: true,
                    CurrentHp: > 0,
                } currentTarget && IsActivityTarget(currentTarget)
                ? currentTarget
                : null;

            if (target == null && EzThrottler.Throttle("FatePathfindingWatcher.EnemyScan", 100))
            {
                target = GetEnemies().Centroid();
                if (target != null)
                {
                    Svc.Targets.Target = target;
                }
            }

            if (target != null)
            {
                // Target selectors can switch targets several times per second.
                // Let the active route finish before repathing; replacing a
                // running SimpleMove every second causes visible stop/start.
                if (FateNavigationPolicy.ShouldRepath(
                        IsNavigationActive(),
                        Vector3.Distance(target.Position, lastTargetPos))
                    && EzThrottler.Throttle("FatePathfindingWatcher.Repath", 1000)
                    && vnav.PathfindAndMoveTo(target.Position, false))
                {
                    lastTargetPos = target.Position;
                }

                followingActivityTarget = true;

                if (states.GetState() == State.InFate)
                {
                    var distance = Vector3.Distance(Player.Position, target.Position) - target.HitboxRadius;
                    if (distance <= module.Config.EngagementRange)
                    {
                        Actions.TryUnmount();

                        vnav.Stop();

                        return true;
                    }
                }
            }
            else if (followingActivityTarget)
            {
                // The FATE target died or left the object table. Resume the
                // activity route; never substitute an unrelated aggro target.
                if (!IsNavigationActive()
                    && EzThrottler.Throttle("FatePathfindingWatcher.ResumeActivityRoute", 1000)
                    && vnav.PathfindAndMoveTo(GetPosition(), false))
                {
                    lastTargetPos = Vector3.Zero;
                    followingActivityTarget = false;
                }

                // Keep the watcher alive while a previous calculation finishes
                // or the resume request is throttled. Throwing here would tear
                // down and recreate the whole activity, causing stop/start
                // movement every second.
                if (followingActivityTarget)
                {
                    return false;
                }
            }

            // Some FATEs have no targetable actor until after the player has
            // entered their area. Arrival is still successful; let the
            // participating chain take over instead of treating stopped
            // navigation as a failure and recreating the activity forever.
            if (target == null && IsInZone())
            {
                Actions.TryUnmount();
                if (vnav.IsRunning())
                {
                    vnav.Stop();
                }

                return true;
            }

            if (!IsNavigationActive())
            {
                throw new VnavmeshStoppedException();
            }

            return false;
        }, new TaskManagerConfiguration { TimeLimitMS = 180000, ShowError = false });
    }

    protected override float GetRadius()
    {
        return fate.Radius;
    }

    public override bool IsValid()
    {
        return Svc.Fates.Any(f => f.FateId == fate.Id);
    }

    protected override Vector3 GetPosition()
    {
        return fate.StartPosition;
    }

    public override string GetName()
    {
        return fate.Name;
    }

    protected override unsafe bool IsActivityTarget(IBattleNpc obj)
    {
        try
        {
            var battleChara = (BattleChara*)obj.Address;

            return FateTravelTargetPolicy.ShouldPursue(data.Id, battleChara->FateId);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex.Message);
            return false;
        }
    }

    protected override ActivityState GetPostPathfindingState()
    {
        return ActivityState.Participating;
    }
}
