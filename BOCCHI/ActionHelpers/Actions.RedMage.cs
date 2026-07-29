using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 22.</summary>
    public static class RedMage
    {
        public static Action Fire { get; } = A(49092);
        public static Action Cure { get; } = A(49093);
        public static Action Detect { get; } = A(49094);
        public static Action Freeze { get; } = A(49095);
        public static Action Shock { get; } = A(49096);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.RedMage, row));
    }
}
