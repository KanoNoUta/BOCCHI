using BOCCHI.Data;
using BOCCHI.Modules.AggroRange;
using ECommons.DalamudServices;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BOCCHI.Pathfinding;

/// <summary>
/// Serializes live vnavmesh route requests and swaps in an avoided route only
/// after it has been fully calculated. Existing movement is never stopped just
/// to recalculate, which prevents the visible run/stop/run cycle.
/// </summary>
public static class AggroAvoidanceNavigation
{
    private sealed record PlannedRoute(List<Vector3> Path, int ZoneCount, bool Avoided, string? Failure);

    private sealed record PendingRoute(
        VNavmesh Vnav,
        Vector3 Destination,
        bool Fly,
        CancellationTokenSource Cancellation,
        Task<PlannedRoute> Task,
        bool Dynamic,
        bool PreserveTopology);

    private sealed record ActiveRoute(
        VNavmesh Vnav,
        Vector3 Destination,
        bool Fly,
        long SubmittedAt,
        bool PreserveTopology);

    private const float SameDestinationDistanceSquared = 1f;
    private const long NavigationStartGraceMs = 2500;
    private const long FailedDetourRetryBackoffMs = 5000;

    private static Func<AggroRangeConfig>? configProvider;
    private static PendingRoute? pending;
    private static ActiveRoute? active;
    private static List<Vector3> debugPath = [];
    private static long lastReplanAt;
    private static long retrySuppressedUntil;

    public static IReadOnlyList<Vector3> DebugPath => debugPath;

    public static bool IsPlanning => pending != null;

    public static void Configure(Func<AggroRangeConfig> provider)
    {
        configProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static bool PathfindAndMoveTo(VNavmesh vnav, Vector3 destination, bool fly)
    {
        var config = configProvider?.Invoke();
        if (!ShouldAvoid(config))
        {
            CancelPending();
            active = null;
            debugPath = [];
            return vnav.PathfindAndMoveTo(destination, fly);
        }

        PumpCompletedPlan();

        if (pending is { } currentPending
            && IsSameDestination(currentPending.Destination, destination))
        {
            return true;
        }

        if (active is { } currentActive
            && IsSameDestination(currentActive.Destination, destination)
            && (IsNavigationActive(vnav)
                || Environment.TickCount64 - currentActive.SubmittedAt < NavigationStartGraceMs))
        {
            return true;
        }

        CancelPending();
        // The CN Dalamud IPC layer cannot marshal vnavmesh's Task-returning
        // path queries (Nav.Pathfind*), so a fresh route can never be planned
        // here. Submit SimpleMove immediately and let Update() bend the
        // materialized waypoints (Path.ListWaypoints is CN-safe) around any
        // danger zones once vnavmesh starts following them.
        var accepted = vnav.PathfindAndMoveTo(destination, fly);
        if (accepted)
        {
            active = new ActiveRoute(vnav, destination, fly, Environment.TickCount64, true);
            debugPath = [];
            retrySuppressedUntil = 0;
        }

        return accepted;
    }

    /// <summary>
    /// Applies avoidance to an already verified route, such as the fixed
    /// North Horn river crossing, without asking vnavmesh to rediscover it.
    /// </summary>
    public static void FollowPath(VNavmesh vnav, IReadOnlyList<Vector3> route, bool fly)
    {
        if (route.Count == 0)
        {
            return;
        }

        var config = configProvider?.Invoke();
        var finalPath = route.ToList();
        if (ShouldAvoid(config) && Svc.Objects.LocalPlayer is { } player)
        {
            var zones = AggroDangerZoneProvider.Capture(config!);
            var source = new List<Vector3>(route.Count + 1) { player.Position };
            source.AddRange(route);
            if (AggroAvoidancePlanner.TryAvoid(
                    source,
                    zones,
                    config!.VerticalTolerance,
                    point => Project(vnav, point),
                    out var avoided))
            {
                finalPath = RemoveCurrentPosition(avoided, player.Position);
                retrySuppressedUntil = 0;
            }
            else
            {
                retrySuppressedUntil = Environment.TickCount64 + FailedDetourRetryBackoffMs;
                Svc.Log.Warning("Aggro avoidance could not safely modify the verified route; keeping its original waypoints.");
            }
        }

        CancelPending();
        vnav.FollowPath(finalPath, fly);
        active = new ActiveRoute(vnav, route[^1], fly, Environment.TickCount64, true);
        debugPath = finalPath;
    }

    /// <summary>Called once per framework update by AggroRangeModule.</summary>
    public static void Update()
    {
        var config = configProvider?.Invoke();
        if (!ShouldAvoid(config))
        {
            CancelPending();
            active = null;
            debugPath = [];
            return;
        }

        PumpCompletedPlan();
        if (active is not { } current)
        {
            return;
        }

        if (!IsNavigationActive(current.Vnav))
        {
            if (pending == null && Environment.TickCount64 - current.SubmittedAt >= NavigationStartGraceMs)
            {
                active = null;
                debugPath = [];
            }

            return;
        }

        if (!config!.DynamicReplanning || pending != null)
        {
            return;
        }

        var now = Environment.TickCount64;
        var cooldownMs = (long)(Math.Max(0.5f, config.ReplanCooldownSeconds) * 1000f);
        if (now - lastReplanAt < cooldownMs
            || now < retrySuppressedUntil
            || Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        List<Vector3> remaining;
        try
        {
            remaining = current.Vnav.GetWaypoints();
        }
        catch (Exception exception)
        {
            Svc.Log.Verbose(exception, "Could not inspect vnavmesh waypoints for dynamic aggro avoidance.");
            return;
        }

        if (remaining.Count == 0)
        {
            return;
        }

        var path = new List<Vector3>(remaining.Count + 1) { player.Position };
        path.AddRange(remaining);
        IReadOnlyList<AggroDangerZone> zones = AggroAvoidancePlanner.GetRelevantZones(
            AggroDangerZoneProvider.Capture(config),
            current.Destination);
        if (zones.Count == 0 || AggroAvoidancePlanner.IsPathClear(path, zones, config.VerticalTolerance))
        {
            return;
        }

        // Keep following the current waypoints while the replacement is built.
        // FollowPath is called only once after the complete replacement exists.
        StartPlanFromExistingPath(current.Vnav, path, current.Destination, current.Fly, zones, config);
    }

    public static void Stop(VNavmesh? vnav = null)
    {
        CancelPending();
        active = null;
        debugPath = [];
        retrySuppressedUntil = 0;
        if (vnav == null)
        {
            return;
        }

        vnav.Stop();
    }

    private static void StartPlanFromExistingPath(
        VNavmesh vnav,
        List<Vector3> remainingPath,
        Vector3 destination,
        bool fly,
        IReadOnlyList<AggroDangerZone> zones,
        AggroRangeConfig config)
    {
        CancelPending();
        var cancellation = new CancellationTokenSource();
        var verticalTolerance = config.VerticalTolerance;
        var task = Task.Run(() =>
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!AggroAvoidancePlanner.TryAvoid(
                        remainingPath,
                        zones,
                        verticalTolerance,
                        point => Project(vnav, point),
                        out var avoided))
                {
                    return new PlannedRoute(
                        [],
                        zones.Count,
                        false,
                        "no fully projected detour was available for the preserved route");
                }

                cancellation.Token.ThrowIfCancellationRequested();
                var changed = !PathsEquivalent(remainingPath, avoided);
                return new PlannedRoute(avoided, zones.Count, changed, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new PlannedRoute([], zones.Count, false, exception.GetBaseException().Message);
            }
        }, cancellation.Token);

        pending = new PendingRoute(vnav, destination, fly, cancellation, task, true, true);
        lastReplanAt = Environment.TickCount64;
    }

    private static void PumpCompletedPlan()
    {
        if (pending is not { Task.IsCompleted: true } completed)
        {
            return;
        }

        pending = null;
        try
        {
            if (completed.Task.IsCanceled)
            {
                return;
            }

            var result = completed.Task.GetAwaiter().GetResult();
            if (result.Path.Count < 2)
            {
                retrySuppressedUntil = Environment.TickCount64 + FailedDetourRetryBackoffMs;
                Svc.Log.Warning(
                    $"Dynamic aggro avoidance could not modify the preserved route " +
                    $"({result.Failure ?? "unknown"}); keeping the route already in progress.");
                return;
            }

            if (!result.Avoided)
            {
                retrySuppressedUntil = Environment.TickCount64 + FailedDetourRetryBackoffMs;
                Svc.Log.Verbose(
                    "Dynamic aggro avoidance produced no route change; keeping the route already in progress.");
                return;
            }

            var playerPosition = Svc.Objects.LocalPlayer?.Position ?? result.Path[0];
            if (!IsNavigationActive(completed.Vnav))
            {
                // The caller reached its stopping condition or issued an
                // emergency stop while this replacement was being calculated.
                // A late FollowPath here would restart movement after arrival.
                retrySuppressedUntil = 0;
                Svc.Log.Verbose(
                    "Discarding completed aggro detour because navigation is no longer active.");
                return;
            }

            var route = RemoveCurrentPosition(result.Path, playerPosition);
            if (route.Count == 0)
            {
                return;
            }

            completed.Vnav.FollowPath(route, completed.Fly);
            active = new ActiveRoute(
                completed.Vnav,
                completed.Destination,
                completed.Fly,
                Environment.TickCount64,
                completed.PreserveTopology);
            debugPath = route;
            retrySuppressedUntil = 0;
            if (result.Avoided)
            {
                Svc.Log.Info(
                    $"Aggro avoidance submitted {(completed.Dynamic ? "dynamic " : string.Empty)}route " +
                    $"with {route.Count} waypoints around {result.ZoneCount} live danger zones.");
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded destination or emergency stop.
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, "Aggro avoidance could not submit the calculated vnavmesh route.");
        }
        finally
        {
            completed.Cancellation.Dispose();
        }
    }

    private static Vector3? Project(VNavmesh vnav, Vector3 point)
    {
        try
        {
            return vnav.FindNearestPointOnMesh(point, 4f, 8f);
        }
        catch (Exception exception)
        {
            Svc.Log.Verbose(exception, "Could not project an aggro avoidance waypoint onto vnavmesh.");
            return null;
        }
    }

    private static void CancelPending()
    {
        var old = pending;
        pending = null;
        if (old == null)
        {
            return;
        }

        old.Cancellation.Cancel();
        _ = old.Task.ContinueWith(
            _ => old.Cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static bool ShouldAvoid(AggroRangeConfig? config)
    {
        return config is { Enabled: true, AutoAvoidance: true } && ZoneData.IsInNorthHorn();
    }

    private static bool IsSameDestination(Vector3 left, Vector3 right)
    {
        return Vector3.DistanceSquared(left, right) <= SameDestinationDistanceSquared;
    }

    private static bool IsNavigationActive(VNavmesh vnav)
    {
        try
        {
            return vnav.IsRunning() || vnav.IsSimpleMoveInProgress() || vnav.IsPathfinding();
        }
        catch
        {
            return false;
        }
    }

    private static List<Vector3> RemoveCurrentPosition(IReadOnlyList<Vector3> path, Vector3 playerPosition)
    {
        var result = path.ToList();
        while (result.Count > 1 && Vector3.DistanceSquared(result[0], playerPosition) <= 1f)
        {
            result.RemoveAt(0);
        }

        return result;
    }

    private static bool PathsEquivalent(IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (Vector3.DistanceSquared(left[i], right[i]) > 0.01f)
            {
                return false;
            }
        }

        return true;
    }
}
