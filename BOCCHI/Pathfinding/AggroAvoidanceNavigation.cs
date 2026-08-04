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
        bool PreserveTopology,
        List<Vector3> FallbackPath);

    private sealed record ActiveRoute(
        VNavmesh Vnav,
        Vector3 Destination,
        bool Fly,
        long SubmittedAt,
        bool PreserveTopology,
        bool Avoided,
        List<Vector3>? FallbackPath,
        Vector3 LastProgressPosition,
        long LastProgressAt);

    private const float SameDestinationDistanceSquared = 1f;
    private const long NavigationStartGraceMs = 2500;
    private const long FailedDetourRetryBackoffMs = 5000;
    private const long BlockedCorridorSuppressionMs = 30000;
    private const long AvoidedRouteStallTimeoutMs = 6000;
    private const float AvoidedRouteProgressDistanceSquared = 1f;
    private const float MaximumProjectionDisplacementSquared = 6.25f;

    private static Func<AggroRangeConfig>? configProvider;
    private static VNavmesh? lastVnav;
    private static PendingRoute? pending;
    private static ActiveRoute? active;
    private static List<Vector3> debugPath = [];
    private static long lastReplanAt;
    private static long retrySuppressedUntil;
    private static Vector3? avoidanceSuppressedDestination;
    private static long avoidanceSuppressedUntil;

    public static IReadOnlyList<Vector3> DebugPath => debugPath;

    public static bool IsPlanning => pending != null;

    public static void Configure(Func<AggroRangeConfig> provider)
    {
        configProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static bool PathfindAndMoveTo(VNavmesh vnav, Vector3 destination, bool fly)
    {
        lastVnav = vnav;
        var config = configProvider?.Invoke();
        if (!ShouldAvoid(config))
        {
            CancelPending();
            active = null;
            debugPath = [];
            return vnav.PathfindAndMoveTo(destination, fly);
        }

        PumpCompletedPlan();
        var now = Environment.TickCount64;
        ClearSuppressionForDifferentDestination(destination, now);
        if (pending is { } currentPending
            && IsSameDestination(currentPending.Destination, destination))
        {
            return true;
        }

        if (active is { } currentActive
            && IsSameDestination(currentActive.Destination, destination)
            && (IsNavigationActive(vnav)
                || now - currentActive.SubmittedAt < NavigationStartGraceMs))
        {
            return true;
        }

        if (IsAvoidanceSuppressed(destination, now))
        {
            CancelPending();
            var acceptedWithoutAvoidance = vnav.PathfindAndMoveTo(destination, fly);
            if (acceptedWithoutAvoidance)
            {
                var position = Svc.Objects.LocalPlayer?.Position ?? destination;
                active = new ActiveRoute(
                    vnav,
                    destination,
                    fly,
                    now,
                    true,
                    false,
                    null,
                    position,
                    now);
                debugPath = [];
            }

            return acceptedWithoutAvoidance;
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
            var position = Svc.Objects.LocalPlayer?.Position ?? destination;
            active = new ActiveRoute(
                vnav,
                destination,
                fly,
                now,
                true,
                false,
                null,
                position,
                now);
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
        lastVnav = vnav;
        if (route.Count == 0)
        {
            return;
        }

        var config = configProvider?.Invoke();
        var now = Environment.TickCount64;
        ClearSuppressionForDifferentDestination(route[^1], now);
        var finalPath = route.ToList();
        var avoidedApplied = false;
        if (ShouldAvoid(config)
            && !IsAvoidanceSuppressed(route[^1], now)
            && Svc.Objects.LocalPlayer is { } player)
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
                avoidedApplied = !PathsEquivalent(source, avoided);
                retrySuppressedUntil = 0;
            }
            else
            {
                retrySuppressedUntil = Environment.TickCount64 + FailedDetourRetryBackoffMs;
                SuppressAvoidance(route[^1], BlockedCorridorSuppressionMs);
                Svc.Log.Warning("Aggro avoidance could not safely modify the verified route; keeping its original waypoints.");
            }
        }

        CancelPending();
        vnav.FollowPath(finalPath, fly);
        var playerPosition = Svc.Objects.LocalPlayer?.Position ?? route[0];
        active = new ActiveRoute(
            vnav,
            route[^1],
            fly,
            now,
            true,
            avoidedApplied,
            avoidedApplied ? route.ToList() : null,
            playerPosition,
            now);
        debugPath = finalPath;
    }

    /// <summary>Called once per framework update by AggroRangeModule.</summary>
    public static void Update()
    {
        StuckJumpRecovery.Update(lastVnav, active != null);

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

        var now = Environment.TickCount64;
        var navigationActive = IsNavigationActive(current.Vnav);
        if (navigationActive
            && Svc.Objects.LocalPlayer is { } currentPlayer
            && RecoverStalledAvoidance(current, currentPlayer.Position, now))
        {
            return;
        }

        if (!navigationActive)
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

        var cooldownMs = (long)(Math.Max(0.5f, config.ReplanCooldownSeconds) * 1000f);
        if (now - lastReplanAt < cooldownMs
            || now < retrySuppressedUntil
            || IsAvoidanceSuppressed(current.Destination, now)
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
        avoidanceSuppressedDestination = null;
        avoidanceSuppressedUntil = 0;
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

        pending = new PendingRoute(
            vnav,
            destination,
            fly,
            cancellation,
            task,
            true,
            true,
            remainingPath.ToList());
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
                SuppressAvoidance(completed.Destination, BlockedCorridorSuppressionMs);
                Svc.Log.Warning(
                    $"Dynamic aggro avoidance could not modify the preserved route " +
                    $"({result.Failure ?? "unknown"}); the corridor is treated as blocked and " +
                    $"the original vnavmesh route is kept temporarily.");
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
            var now = Environment.TickCount64;
            active = new ActiveRoute(
                completed.Vnav,
                completed.Destination,
                completed.Fly,
                now,
                completed.PreserveTopology,
                true,
                completed.FallbackPath,
                playerPosition,
                now);
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
            var projected = vnav.FindNearestPointOnMesh(point, 4f, 8f);
            if (projected == null
                || DistanceSquaredXZ(projected.Value, point) > MaximumProjectionDisplacementSquared)
            {
                // A large sideways projection normally means the requested arc
                // is on the other side of a wall. Accepting it creates a direct
                // FollowPath segment through that wall and leaves the player
                // permanently pushing against collision.
                return null;
            }

            return projected;
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

    private static bool RecoverStalledAvoidance(
        ActiveRoute current,
        Vector3 playerPosition,
        long now)
    {
        if (!current.Avoided)
        {
            return false;
        }

        if (DistanceSquaredXZ(playerPosition, current.LastProgressPosition)
            >= AvoidedRouteProgressDistanceSquared)
        {
            active = current with
            {
                LastProgressPosition = playerPosition,
                LastProgressAt = now,
            };
            return false;
        }

        if (now - current.LastProgressAt < AvoidedRouteStallTimeoutMs)
        {
            return false;
        }

        CancelPending();
        SuppressAvoidance(current.Destination, BlockedCorridorSuppressionMs);
        var fallback = PrepareFallbackPath(current.FallbackPath, playerPosition);

        try
        {
            if (fallback.Count > 0)
            {
                current.Vnav.FollowPath(fallback, current.Fly);
            }
            else
            {
                current.Vnav.Stop();
                current.Vnav.PathfindAndMoveTo(current.Destination, current.Fly);
            }

            active = new ActiveRoute(
                current.Vnav,
                current.Destination,
                current.Fly,
                now,
                current.PreserveTopology,
                false,
                null,
                playerPosition,
                now);
            debugPath = fallback;
            // IsAvoidanceSuppressed already protects this destination. Keep
            // the generic retry timer clear so a new destination can plan
            // immediately instead of inheriting this corridor's 30s block.
            retrySuppressedUntil = 0;
            Svc.Log.Warning(
                "Avoided route made no movement progress and was probably pushing into a wall; " +
                "restored the original vnavmesh route and temporarily ignored monster avoidance " +
                "for this destination.");
        }
        catch (Exception exception)
        {
            active = current with { LastProgressAt = now };
            Svc.Log.Warning(exception, "Could not restore the original route after an avoided-route stall.");
        }

        return true;
    }

    private static List<Vector3> PrepareFallbackPath(
        IReadOnlyList<Vector3>? fallbackPath,
        Vector3 playerPosition)
    {
        if (fallbackPath == null || fallbackPath.Count == 0)
        {
            return [];
        }

        var result = fallbackPath.ToList();
        while (result.Count > 1
               && DistanceSquaredXZ(playerPosition, result[1]) + 0.25f
               < DistanceSquaredXZ(playerPosition, result[0]))
        {
            result.RemoveAt(0);
        }

        while (result.Count > 1
               && DistanceSquaredXZ(playerPosition, result[0]) <= 1f)
        {
            result.RemoveAt(0);
        }

        return result;
    }

    private static void SuppressAvoidance(Vector3 destination, long durationMs)
    {
        avoidanceSuppressedDestination = destination;
        avoidanceSuppressedUntil = Math.Max(
            avoidanceSuppressedUntil,
            Environment.TickCount64 + Math.Max(0, durationMs));
    }

    private static bool IsAvoidanceSuppressed(Vector3 destination, long now)
    {
        if (avoidanceSuppressedDestination is not { } suppressed
            || now >= avoidanceSuppressedUntil)
        {
            if (now >= avoidanceSuppressedUntil)
            {
                avoidanceSuppressedDestination = null;
                avoidanceSuppressedUntil = 0;
            }

            return false;
        }

        return IsSameDestination(suppressed, destination);
    }

    private static void ClearSuppressionForDifferentDestination(Vector3 destination, long now)
    {
        if (avoidanceSuppressedDestination is not { } suppressed
            || now >= avoidanceSuppressedUntil
            || !IsSameDestination(suppressed, destination))
        {
            avoidanceSuppressedDestination = null;
            avoidanceSuppressedUntil = 0;
        }
    }

    private static float DistanceSquaredXZ(Vector3 left, Vector3 right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return x * x + z * z;
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
