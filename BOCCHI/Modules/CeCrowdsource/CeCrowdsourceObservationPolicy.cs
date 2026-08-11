namespace BOCCHI.Modules.CeCrowdsource;

public static class CeCrowdsourceObservationPolicy
{
    public static bool ShouldUploadCriticalEncounter(uint territoryId, byte eventType, uint eventId)
    {
        if (eventType < 4)
        {
            return true;
        }

        return TowerHelper.TryGetDefinitionByEventId(eventId, out var tower)
               && tower.TerritoryId == territoryId;
    }
}
