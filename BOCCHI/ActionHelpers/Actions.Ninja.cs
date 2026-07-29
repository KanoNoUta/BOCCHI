using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 16.</summary>
    public static class Ninja
    {
        public static Action FumaShuriken { get; } = A(49062);
        public static Action SmokeBomb { get; } = A(49063);
        public static Action Raiton { get; } = A(49064);
        public static Action Katon { get; } = A(49065);
        public static Action Bunshin { get; } = A(49066);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.Ninja, row));
    }
}
