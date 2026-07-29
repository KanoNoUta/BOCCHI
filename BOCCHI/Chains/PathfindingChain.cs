using BOCCHI.Data;
using BOCCHI.Pathfinding;
using ECommons.Automation.NeoTaskManager;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Chains;

public class PathfindingChain : ChainFactory
{
    // The route is about 1,234 yalms long. Keep enough headroom for an
    // unmounted character while still stopping navigation on a true timeout.
    private const int NorthHornTransitTimeoutMs = 300_000;

    private const int NorthHornTransitStartGraceMs = 5_000;

    private readonly EventData data;

    private readonly Vector3 destination;

    private readonly float? maxRadius;

    private readonly float? minRadius;

    private readonly VNavmesh vnav;

    private long northHornTransitStartedAt;

    public bool TransitAttempted { get; private set; }

    public bool TransitReachedEnd { get; private set; }

    public PathfindingChain(
        VNavmesh vnav,
        Vector3 destination,
        EventData data,
        float? maxRadius = null,
        float? minRadius = null)
    {
        this.vnav = vnav;
        this.destination = destination;
        this.data = data;
        this.maxRadius = maxRadius;
        this.minRadius = minRadius;
    }

    protected override Chain Create(Chain chain)
    {
        if (!NorthHornSouthCrossingRoute.TryCreate(data, Player.Position, out var transitRoute))
        {
            return chain.Then(PathfindAndMoveToChain.RandomNearby(vnav, destination, maxRadius ?? 1f, minRadius ?? 0f));
        }

        var lastWaypoint = transitRoute[^1];

        // This is an explicitly verified land route. Re-pathfinding each
        // waypoint lets vnavmesh rediscover the water shortcut that this
        // profile intentionally avoids, so feed the full route to FollowPath.
        return chain
            .Debug($"Using North Horn south-crossing transit route for FATE {data.Id}")
            .Then(_ => StartNorthHornTransit(transitRoute))
            .Then(WaitForNorthHornTransit(vnav, lastWaypoint))
            .ConditionalThen(_ => TransitReachedEnd, _ => vnav.Stop());
    }

    private void StartNorthHornTransit(List<Vector3> transitRoute)
    {
        TransitAttempted = true;
        TransitReachedEnd = false;
        northHornTransitStartedAt = Environment.TickCount64;
        vnav.FollowPath(transitRoute, false);
    }

    private TaskManagerTask WaitForNorthHornTransit(VNavmesh vnav, Vector3 lastWaypoint)
    {
        var arrivalDistanceSquared = NorthHornSouthCrossingRoute.ArrivalDistance * NorthHornSouthCrossingRoute.ArrivalDistance;

        return new TaskManagerTask((Func<bool?>)(() =>
        {
            if (Vector3.DistanceSquared(Player.Position, lastWaypoint) <= arrivalDistanceSquared)
            {
                TransitReachedEnd = true;
                return true;
            }

            // FollowPath starts asynchronously. Allow it to enter the running
            // state, but abort this child chain if it later stops short of the
            // east-bank endpoint. This prevents any fallback from re-routing
            // across the river after a failed transit.
            if (vnav.IsRunning() || Environment.TickCount64 - northHornTransitStartedAt < NorthHornTransitStartGraceMs)
            {
                return false;
            }

            return null;
        }), new TaskManagerConfiguration
        {
            TimeLimitMS = NorthHornTransitTimeoutMs,
            AbortOnTimeout = true,
            OnTaskTimeout = (TaskManagerTask task, ref long remainingTimeMs) => vnav.Stop(),
        });
    }
}
