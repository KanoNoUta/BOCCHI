namespace BOCCHI.Pathfinding;

public static class NavigationStopPolicy
{
    public const long StartGraceMs = 2000;

    public const long StableInactiveMs = 1500;

    public static long? UpdateInactiveSince(
        bool navigationActive,
        long now,
        long? inactiveSince)
    {
        return navigationActive ? null : inactiveSince ?? now;
    }

    public static bool HasStopped(
        long startedAt,
        long? inactiveSince,
        long now)
    {
        return now - startedAt >= StartGraceMs
               && inactiveSince.HasValue
               && now - inactiveSince.Value >= StableInactiveMs;
    }
}
