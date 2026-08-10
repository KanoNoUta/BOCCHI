using BOCCHI.Data;
using System;

namespace BOCCHI.Modules.CeCrowdsource;

public static class CeCrowdsourceDisplayPolicy
{
    public static bool ShouldDisplayRecord(CeRecord record)
    {
        return ZoneData.IsOccultCrescentTerritory(record.TerritoryID);
    }

    /// <summary>
    /// Resolves an observation to the state that should be shown in the panel.
    /// A server record which is no longer active is history, even when its last
    /// observation was Battle; otherwise dead bosses would remain red forever.
    /// </summary>
    public static string? ResolveState(string? observedState, string? localState, bool isActive)
    {
        if (string.Equals(localState, "Inactive", StringComparison.Ordinal))
        {
            return "Inactive";
        }

        if (!string.IsNullOrWhiteSpace(localState))
        {
            return localState;
        }

        return isActive ? observedState : "Inactive";
    }
}
