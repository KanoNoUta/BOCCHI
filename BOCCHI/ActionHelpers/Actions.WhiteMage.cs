using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 17.</summary>
    public static class WhiteMage
    {
        public static Action Cure { get; } = A(49067);
        public static Action Cura { get; } = A(49068);
        public static Action MagicEvasion { get; } = A(49069);
        public static Action Raise { get; } = A(49070);
        public static Action Holy { get; } = A(49071);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.WhiteMage, row));
    }
}
