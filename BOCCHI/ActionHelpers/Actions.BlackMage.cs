using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 18.</summary>
    public static class BlackMage
    {
        public static Action Fire { get; } = A(49072);
        public static Action Blizzard { get; } = A(49073);
        public static Action Thunder { get; } = A(49074);
        public static Action Toad { get; } = A(49075);
        public static Action Flare { get; } = A(49076);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.BlackMage, row));
    }
}
