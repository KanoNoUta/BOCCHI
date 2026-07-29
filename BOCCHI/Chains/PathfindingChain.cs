using BOCCHI.Data;
using BOCCHI.Pathfinding;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System.Numerics;

namespace BOCCHI.Chains;

public class PathfindingChain : ChainFactory
{
    private readonly EventData data;

    private readonly Vector3 destination;

    private readonly float? maxRadius;

    private readonly float? minRadius;

    private readonly VNavmesh vnav;

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
            .Then(_ => vnav.FollowPath(transitRoute, false))
            .WaitUntilNear(vnav, lastWaypoint, NorthHornSouthCrossingRoute.ArrivalDistance)
            .Then(_ => vnav.Stop())
            .Then(PathfindAndMoveToChain.RandomNearby(vnav, destination, maxRadius ?? 1f, minRadius ?? 0f));
    }
}
