using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 19.</summary>
    public static class Dragoon
    {
        public static Action Jump { get; } = A(49077);
        public static Action ForwardStep { get; } = A(49078);
        public static Action DragonSword { get; } = A(49079);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.Dragoon, row));
    }
}
