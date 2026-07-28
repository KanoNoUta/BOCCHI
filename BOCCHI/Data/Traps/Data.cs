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
        foreach (var group in Groups)
        {
            foreach (var trap in group.Traps)
            {
                if (obj.GetKey() == trap.GetKey())
                {
                    return group;
                }
            }
        }

        throw new Exception("Trap group not found");
    }
}
