using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Pathfinding;

public static class HuntNavigationPlanner
{
    public const float PathEndpointTolerance = 5f;

    public static bool ReachesDestination(IReadOnlyList<Vector3> path, Vector3 destination)
    {
        return path.Count > 1
               && Vector3.DistanceSquared(path[^1], destination)
               <= PathEndpointTolerance * PathEndpointTolerance;
    }

    public static List<PathfinderStep> BuildTransitSteps(NavigationPlan plan)
    {
        return plan.Type switch
        {
            NavigationType.Walk => [],
            NavigationType.ReturnWalk =>
            [
                PathfinderStep.ReturnToBaseCamp(),
            ],
            NavigationType.ReturnTeleportWalk =>
            [
                PathfinderStep.ReturnToBaseCamp(),
                PathfinderStep.TeleportToAethernet(plan.DestinationAethernet),
            ],
            NavigationType.WalkTeleportWalk =>
            [
                PathfinderStep.WalkToAethernet(plan.SourceAethernet),
                PathfinderStep.TeleportToAethernet(plan.DestinationAethernet),
            ],
            _ => [],
        };
    }

    public static void InsertBeforeCurrentNode(
        List<PathfinderStep> route,
        int currentIndex,
        IReadOnlyList<PathfinderStep> transitSteps)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(transitSteps);
        if (currentIndex < 0 || currentIndex >= route.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(currentIndex));
        }

        if (transitSteps.Count > 0)
        {
            route.InsertRange(currentIndex, transitSteps);
        }
    }

    public static List<PathfinderStep> BuildForcedRecoverySteps(
        Aethernet destinationAethernet,
        Aethernet baseCampAethernet)
    {
        var steps = new List<PathfinderStep>
        {
            PathfinderStep.ReturnToBaseCamp(),
        };

        if (destinationAethernet != baseCampAethernet)
        {
            steps.Add(PathfinderStep.TeleportToAethernet(destinationAethernet));
        }

        return steps;
    }
}
