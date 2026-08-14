namespace BOCCHI.Modules.CeCrowdsource;

public readonly record struct CeCrowdsourcePresenceScope(
    bool IsIsland,
    uint TerritoryId,
    uint ZoneServerId,
    uint InstanceId);

public static class CeCrowdsourcePresencePolicy
{
    public static bool CanPublishIslandPresence(bool isIsland, uint zoneServerId)
    {
        return isIsland && zoneServerId != 0;
    }

    /// <summary>
    /// The territory callback can arrive before the island's zone-server and
    /// public-instance identifiers are populated. Rebind as soon as either
    /// identifier settles instead of requiring a plugin reload to create a
    /// fresh heartbeat/fetch cycle.
    /// </summary>
    public static bool ShouldRestartConnection(
        CeCrowdsourcePresenceScope previous,
        CeCrowdsourcePresenceScope current)
    {
        return previous != current;
    }
}
