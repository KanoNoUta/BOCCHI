using BOCCHI.Data;
using BOCCHI.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    /// <summary>CN 7.55 MKDSupportJob row 20.</summary>
    public static class Summoner
    {
        public static Action Inferno { get; } = A(49080);
        public static Action JudgmentBolt { get; } = A(49081);
        public static Action EarthenWall { get; } = A(49082);
        public static Action Thunderstorm { get; } = A(49083);
        public static Action Megaflare { get; } = A(49084);

        private static Action A(uint row) => new(ActionType.GeneralAction,
            NorthHornSupportActionData.GetGeneralActionId(JobId.Summoner, row));
    }
}
