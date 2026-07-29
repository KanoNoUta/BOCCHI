using BOCCHI.Data.Traps;
using System.Collections.Generic;

namespace BOCCHI.Modules.ForkedTower;

public class TrackedGroup(TrapGroup group)
{
    private readonly TrapGroup Group = group.Clone();

    private readonly HashSet<string> TrapKeys = [];

    public void RecordTrap(string key)
    {
        TrapKeys.Add(key);
    }

    public bool HasDiscoveredAllTraps()
    {
        return TrapKeys.Count >= Group.MaxInGroup;
    }
}
