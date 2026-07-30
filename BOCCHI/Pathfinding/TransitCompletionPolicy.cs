namespace BOCCHI.Pathfinding;

public static class TransitCompletionPolicy
{
    public static bool HasVerifiedArrival(bool childSucceeded, bool destinationReached)
    {
        return childSucceeded && destinationReached;
    }

    public static bool CanContinueAfterReturn(bool returnSucceeded)
    {
        return returnSucceeded;
    }
}
