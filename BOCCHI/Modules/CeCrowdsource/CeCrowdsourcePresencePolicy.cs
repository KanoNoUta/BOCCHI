namespace BOCCHI.Modules.CeCrowdsource;

public static class CeCrowdsourcePresencePolicy
{
    public static bool CanPublishIslandPresence(bool isIsland, uint zoneServerId)
    {
        return isIsland && zoneServerId != 0;
    }
}
