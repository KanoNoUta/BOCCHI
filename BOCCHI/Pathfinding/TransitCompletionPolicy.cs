using BOCCHI.Enums;
using System.Numerics;

namespace BOCCHI.Pathfinding;

public static class TransitCompletionPolicy
{
    // North Horn's Return landing varies across the base-camp PopRange. The
    // observed landing points are roughly 31 yalms from its maintained center,
    // so keep enough margin for that variation while still rejecting every
    // non-base-camp aethernet.
    public const float MaximumReturnLandingDistance = 45f;

    public static bool HasVerifiedArrival(bool childSucceeded, bool destinationReached)
    {
        return childSucceeded && destinationReached;
    }

    public static bool CanContinueAfterReturn(bool returnSucceeded)
    {
        return returnSucceeded;
    }

    public static bool IsVerifiedReturnLanding(
        Vector3 playerPosition,
        Vector3 startingLocation,
        Aethernet closestAethernet,
        Aethernet baseCampAethernet)
    {
        return closestAethernet == baseCampAethernet
               && Vector3.Distance(playerPosition, startingLocation)
               <= MaximumReturnLandingDistance;
    }
}
