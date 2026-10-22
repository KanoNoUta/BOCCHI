using BOCCHI.Data;
using System;

namespace BOCCHI.Modules.Treasure;

public static class TreasureHuntOptionsPolicy
{
    public const uint SilverType = 1597;

    // South Horn only, from upstream BOCCHI 4.2's zone data.
    private static readonly byte[] SouthHornUnsafeWeather = [7, 62, 64, 192];

    private const int AshkinStartEorzeaMinute = 1350;

    private const int AshkinEndEorzeaMinute = 240;

    public static bool ShouldRecastSight(int everyNLocations, int visitedSinceSight)
    {
        return everyNLocations > 0 && visitedSinceSight >= everyNLocations;
    }

    public static int GetEorzeaMinuteOfDay(DateTimeOffset utc)
    {
        var eorzeaSeconds = utc.ToUnixTimeSeconds() * 3600L / 175L;
        return (int)(eorzeaSeconds % 86400L / 60L);
    }

    public static bool IsAshkinPeriod(int eorzeaMinuteOfDay)
    {
        return eorzeaMinuteOfDay >= AshkinStartEorzeaMinute || eorzeaMinuteOfDay < AshkinEndEorzeaMinute;
    }

    /// <summary>South Horn's night or dangerous weather, right now.</summary>
    public static bool IsUnsafeNow()
    {
        return ZoneData.IsInSouthHorn() && IsSouthHornUnsafe(DateTimeOffset.UtcNow, GetCurrentWeatherId());
    }

    private static unsafe byte GetCurrentWeatherId()
    {
        var env = FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager.Instance();
        return env == null ? (byte)0 : env->ActiveWeather;
    }

    public static bool IsSouthHornUnsafe(DateTimeOffset utc, byte weatherId)
    {
        return IsAshkinPeriod(GetEorzeaMinuteOfDay(utc))
               || (weatherId != 0 && Array.IndexOf(SouthHornUnsafeWeather, weatherId) >= 0);
    }
}
