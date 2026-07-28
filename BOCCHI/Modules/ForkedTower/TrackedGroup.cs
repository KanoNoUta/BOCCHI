using BOCCHI.Data.Traps;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System.Collections.Generic;

namespace BOCCHI.Modules.ForkedTower;

public class TrackedGroup(TrapGroup group)
{
    private readonly TrapGroup Group = group.Clone();

    public readonly List<IEventObj> Traps = [];

    public bool HasDiscoveredAllTraps()
    {
        return Traps.Count >= Group.MaxInGroup;
    }
}
