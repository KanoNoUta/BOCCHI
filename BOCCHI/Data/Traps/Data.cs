using BOCCHI.Modules.Data;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Collections.Generic;

namespace BOCCHI.Data.Traps;

public static partial class TrapData
{
    public readonly static List<TrapGroup> Groups;

    static TrapData()
    {
        Groups =
        [
            ..LeftHallway,
            ..RightHallway,
            ..HallwayJoin,
            ..LeftBridge,
            ..RightBridge,
            ..PuzzleRoom,
            ..FinalArea,
        ];
    }

    public static TrapGroup GetGroup(IEventObj obj)
    {
        if (TryGetGroup(obj, out var group))
        {
            return group;
        }

        throw new Exception("Trap group not found");
    }

    public static bool TryGetGroup(IEventObj obj, out TrapGroup group)
    {
        var key = obj.GetKey();
        foreach (var candidate in Groups)
        {
            foreach (var trap in candidate.Traps)
            {
                if (key == trap.GetKey())
                {
                    group = candidate;
                    return true;
                }
            }
        }

        group = null!;
        return false;
    }
}
