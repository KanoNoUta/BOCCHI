using ECommons.Automation.NeoTaskManager;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Numerics;

namespace BOCCHI.Chains;

public class PathfindAndMoveToChain : OwnedChainFactory
{
    private readonly Vector3 destination;

    private readonly VNavmesh vnav;

    private readonly Func<bool>? stopCheck;

    public PathfindAndMoveToChain(
        VNavmesh vnav,
        Vector3 destination,
        float maxRadius = 1f,
        float minRadius = 0f,
        Func<bool>? stopCheck = null)
    {
        this.vnav = vnav;
        this.destination = destination;
        this.stopCheck = stopCheck;
    }

    public static PathfindAndMoveToChain RandomNearby(
        VNavmesh vnav,
        Vector3 destination,
        float maxRadius = 1f,
        float minRadius = 0f,
        Func<bool>? stopCheck = null)
    {
        var angle = (float)(Random.Shared.NextDouble() * MathF.Tau);
        var distance = minRadius + (float)(Random.Shared.NextDouble() * (maxRadius - minRadius));

        var offsetX = MathF.Cos(angle) * distance;
        var offsetZ = MathF.Sin(angle) * distance;

        destination = new Vector3(destination.X + offsetX, destination.Y, destination.Z + offsetZ);
        destination = vnav.FindPointOnFloor(destination, false, 0.5f) ?? destination;

        return new PathfindAndMoveToChain(vnav, destination, stopCheck: stopCheck);
    }


    protected override Chain Create(Chain chain)
    {
        // A stopped owner must not start a route after it already stopped
        // navigation; that walks the player toward an event that has ended.
        return chain
            .BreakIf(() => stopCheck?.Invoke() == true)
            .Then(_ => BOCCHI.Pathfinding.AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, destination, false));
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration
        {
            TimeLimitMS = 180000,
        };
    }
}
