using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 21.</summary>
    public static class BlueMage
    {
        public static Action Aero { get; } = A(49085);
        public static Action Missile { get; } = A(49086);
        public static Action AquaBreath { get; } = A(49087);
        public static Action MightyGuard { get; } = A(49088);
        public static Action WhiteWind { get; } = A(49090);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.BlueMage, row));
    }
}
