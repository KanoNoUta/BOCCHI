using BOCCHI.Modules.Automator.Pots;
using System;

namespace BOCCHI.Modules.MobFarmer;

public enum FarmerYieldReason
{
    None,
    Pots,
    TreasureHunt,
    CrystalBuffs,
}

/// <summary>
/// When the mob farmer hands the player to another mode. Kept free of game
/// types so the smoke tests can check it. Ported from upstream BOCCHI 4.2's
/// MobFarmerYieldService.
/// </summary>
public static class MobFarmerYieldPolicy
{
    /// <summary>The automator gets this long to find the pot before an idle automator counts as done.</summary>
    public static readonly TimeSpan PotStartGrace = TimeSpan.FromSeconds(60);

    /// <summary>The automator must stay idle this long after the pot before the farmer takes over again.</summary>
    public static readonly TimeSpan PotIdleSettle = TimeSpan.FromSeconds(5);

    /// <summary>After a buff trip the farmer does not go back for buffs again for this long.</summary>
    public static readonly TimeSpan CrystalBuffCooldown = TimeSpan.FromMinutes(5);

    public static bool IsLivePotStartable(long timeRemainingSeconds, int minimumMinutesRemaining)
    {
        // Some North Horn FATEs report ~1s for their whole life, so a value
        // that low means "unknown", not "about to end".
        return timeRemainingSeconds < 10
               || !PotTimingPolicy.ShouldSkipLivePot(minimumMinutesRemaining, timeRemainingSeconds);
    }

    public static bool ShouldYieldToPots(
        bool enabled,
        bool hasStartableLivePot,
        PotCycleSnapshot cycle,
        DateTimeOffset now,
        int leadMinutes)
    {
        return enabled
               && (hasStartableLivePot || PotTimingPolicy.ShouldPreposition(cycle, now, leadMinutes, true));
    }

    public static bool ShouldReleasePotYield(
        bool sawPotWork,
        TimeSpan runningFor,
        TimeSpan idleFor,
        bool managedHuntStarted)
    {
        // Pots & Treasure starts a treasure hunt once the pot is done; that
        // hunt is not the farmer's to wait for.
        if (managedHuntStarted)
        {
            return true;
        }

        return (sawPotWork || runningFor >= PotStartGrace) && idleFor >= PotIdleSettle;
    }

    public static bool IsTreasureHuntDue(
        bool enabled,
        DateTimeOffset now,
        DateTimeOffset nextHuntAt,
        bool countInitialised,
        bool meetsMinimumFill,
        int chestsRemaining)
    {
        return enabled
               && now >= nextHuntAt
               && countInitialised
               && meetsMinimumFill
               && chestsRemaining > 0;
    }

    public static TimeSpan GetInterval(int minutes)
    {
        return TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    /// <summary>
    /// Treasuresight after a fight. An interval of 0 casts after every fight,
    /// which is how the farmer always behaved.
    /// </summary>
    public static bool IsTreasureSightDue(bool enabled, int intervalMinutes, DateTimeOffset? lastCastAt, DateTimeOffset now)
    {
        if (!enabled)
        {
            return false;
        }

        return intervalMinutes <= 0
               || lastCastAt == null
               || now - lastCastAt.Value >= TimeSpan.FromMinutes(intervalMinutes);
    }

    /// <summary>With an interval set, the farmer also casts while it waits for enemies.</summary>
    public static bool ShouldCastTreasureSightWhileWaiting(bool enabled, int intervalMinutes, DateTimeOffset? lastCastAt, DateTimeOffset now)
    {
        return intervalMinutes > 0 && IsTreasureSightDue(enabled, intervalMinutes, lastCastAt, now);
    }

    public static bool ShouldYieldToCrystalBuffs(
        bool enabled,
        bool buffModuleEnabled,
        bool needsRefresh,
        DateTimeOffset now,
        DateTimeOffset nextAllowedAt)
    {
        return enabled && buffModuleEnabled && needsRefresh && now >= nextAllowedAt;
    }
}

/// <summary>
/// Notices a walk back to the farm spot that makes no progress, so the farmer
/// can give up and farm from where it stands instead of retrying forever.
/// </summary>
public sealed class FarmerApproachTracker
{
    public const int MaxFailures = 3;

    public const float ProgressThreshold = 2f;

    private float bestDistance = float.MaxValue;

    public int Failures { get; private set; }

    public void Reset()
    {
        bestDistance = float.MaxValue;
        Failures = 0;
    }

    /// <summary>Records one finished walk attempt; true once the walk should be abandoned.</summary>
    public bool RecordAttempt(float distance)
    {
        if (distance < bestDistance - ProgressThreshold)
        {
            bestDistance = distance;
            Failures = 0;
            return false;
        }

        Failures++;
        return Failures >= MaxFailures;
    }
}
