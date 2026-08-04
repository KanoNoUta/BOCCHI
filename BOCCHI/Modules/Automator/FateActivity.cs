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

    public const float ArrivalRadiusWithoutTarget = 5f;

    public const float MaximumReliableHitboxRadius = 20f;

    public const int NavigationStartGraceMs = 2000;

    public static bool ShouldRepath(bool navigationActive, float targetMovement)
    {
        return !navigationActive && targetMovement > RepathDistance;
    }

    public static bool IsTargetInEngagementRange(float centerDistance, float hitboxRadius, float engagementRange)
    {
        if (!float.IsFinite(centerDistance) || !float.IsFinite(hitboxRadius) || !float.IsFinite(engagementRange))
        {
            return false;
        }

        var reliableHitboxRadius = Math.Clamp(hitboxRadius, 0f, MaximumReliableHitboxRadius);
        return centerDistance - reliableHitboxRadius <= Math.Max(0f, engagementRange);
    }

    public static bool IsTargetlessArrival(float distanceToCenter, float fateRadius)
    {
        if (!float.IsFinite(distanceToCenter) || !float.IsFinite(fateRadius))
        {
            return false;
        }

        return distanceToCenter <= Math.Min(Math.Max(0f, fateRadius), ArrivalRadiusWithoutTarget);
    }

    public static bool IsInsideNavigationStartGrace(bool hasObservedNavigation, long elapsedMs)
    {
        return !hasObservedNavigation && elapsedMs < NavigationStartGraceMs;
    }

    public static long StartAtFirstTick(long? startedAt, long now)
    {
        return startedAt ?? now;
    }
}

public class FateActivity(EventData data, Lifestream lifestream, VNavmesh vnav, AutomatorModule module, Fate fate)
    : Activity(data, lifestream, vnav, module)
{
    protected override TaskManagerTask GetPathfindingWatcher(StateManagerModule states)
    {
        var lastTargetPos = Vector3.Zero;
        var followingActivityTarget = false;
        long? watcherStartedAt = null;
        var hasObservedNavigation = false;

        return new TaskManagerTask(() =>
        {
            // The user pressed stop: end the watcher immediately so the
            // per-second repath cannot keep resubmitting movement while the
            // activity chain is still alive.
            if (!module.IsEnabled)
            {
                vnav.Stop();
                StopAtArrivalAndTryDismount("Fate.StopDismount");
                return true;
            }

            var now = Environment.TickCount64;
            watcherStartedAt = FateNavigationPolicy.StartAtFirstTick(watcherStartedAt, now);
            hasObservedNavigation |= IsNavigationActive();

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
                var centerDistance = Vector3.Distance(Player.Position, target.Position);
                var inEngagementRange = states.GetState() == State.InFate
                    && FateNavigationPolicy.IsTargetInEngagementRange(
                        centerDistance,
                        target.HitboxRadius,
                        module.Config.EngagementRange);

                // Check arrival before submitting a new route.  A target can
                // enter the engagement range on the same tick that the last
                // route finishes; queueing another SimpleMove first can
                // restart movement after the dismount request.
                if (inEngagementRange)
                {
                    StopAtArrivalAndTryDismount("Fate.ArrivalDismount");

                    Svc.Log.Info(
                        $"FATE arrival confirmed at target: {GetName()}, " +
                        $"distance={centerDistance:F1}, hitbox={target.HitboxRadius:F1}, " +
                        $"engagement={module.Config.EngagementRange:F1}");

                    return true;
                }

                // Target selectors can switch targets several times per second.
                // Let the active route finish before repathing; replacing a
                // running SimpleMove every second causes visible stop/start.
                if (FateNavigationPolicy.ShouldRepath(
                        IsNavigationActive(),
                        Vector3.Distance(target.Position, lastTargetPos))
                    && EzThrottler.Throttle("FatePathfindingWatcher.Repath", 1000)
                    && BOCCHI.Pathfinding.AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, target.Position, false))
                {
                    lastTargetPos = target.Position;
                }

                followingActivityTarget = true;
            }
            else if (followingActivityTarget)
            {
                // The FATE target died or left the object table. Resume the
                // activity route; never substitute an unrelated aggro target.
                if (!IsNavigationActive()
                    && EzThrottler.Throttle("FatePathfindingWatcher.ResumeActivityRoute", 1000)
                    && BOCCHI.Pathfinding.AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, GetPosition(), false))
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

            // A FATE radius can cover most of the route from an aethernet.
            // Only the small center approach counts as a targetless arrival;
            // otherwise teleporting into the outer FATE circle dismounts the
            // player after only a couple of steps.
            var distanceToCenter = Player.DistanceTo(GetPosition());
            if (target == null && FateNavigationPolicy.IsTargetlessArrival(distanceToCenter, GetRadius()))
            {
                StopAtArrivalAndTryDismount("Fate.ArrivalDismount");

                Svc.Log.Info(
                    $"FATE arrival confirmed at center: {GetName()}, " +
                    $"distance={distanceToCenter:F1}, radius={GetRadius():F1}");

                return true;
            }

            if (!IsNavigationActive())
            {
                // vnavmesh briefly reports neither calculating nor running
                // after accepting a SimpleMove and before movement begins.
                // Do not tear down the activity during that startup window.
                var elapsedMs = now - watcherStartedAt.Value;
                if (FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation, elapsedMs))
                {
                    return false;
                }

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
        // Presence in the FATE table is the authoritative liveness signal.
        // The classic timer fields are not populated for every North Horn
        // FATE, so requiring TimeRemaining > 0 despawned them prematurely.
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
