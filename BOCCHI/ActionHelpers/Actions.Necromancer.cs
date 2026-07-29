using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 23.</summary>
    public static class Necromancer
    {
        public static Action VampiricTouch { get; } = A(49097);
        public static Action DeepFreeze { get; } = A(49098);
        public static Action HellWind { get; } = A(49099);
        public static Action ChaosThunder { get; } = A(49100);
        public static Action PunishingLight { get; } = A(49101);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.Necromancer, row));
    }
}
