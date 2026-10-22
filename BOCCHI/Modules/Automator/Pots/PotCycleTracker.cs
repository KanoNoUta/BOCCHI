using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Automator.Pots;

public sealed record PotCycleSnapshot
{
    public static readonly PotCycleSnapshot Empty = new();

    public uint TerritoryId { get; init; }

    public bool HasKnownAnchor { get; init; }

    public uint AnchorPotFateId { get; init; }

    public DateTimeOffset AnchorSpawnAt { get; init; }

    public bool IsRemoteAnchor { get; init; }

    public uint CurrentActivePotFateId { get; init; }

    public uint PredictedNextPotFateId { get; init; }

    public DateTimeOffset PredictedNextSpawnAt { get; init; }

    public bool HasPredictedNextPot =>
        HasKnownAnchor
        && PredictedNextPotFateId != 0
        && PredictedNextSpawnAt > DateTimeOffset.MinValue;

    public bool HasAnythingToShow => CurrentActivePotFateId != 0 || HasPredictedNextPot;
}

/// <summary>
/// The two magic pots of a zone alternate: the other pot spawns 30 minutes
/// after the current one. A pot seen live anchors the cycle; a pot reported by
/// the crowdsource server anchors it when none was seen here. Ported from
/// upstream BOCCHI 4.2's PotCycleTracker.
/// </summary>
public sealed class PotCycleTracker
{
    public static readonly TimeSpan PotCycleInterval = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan PredictionStaleGrace = TimeSpan.FromMinutes(5);

    private readonly Dictionary<uint, PotCycleSnapshot> cycles = new();

    public PotCycleSnapshot Get(uint territoryId)
    {
        return cycles.TryGetValue(territoryId, out var snapshot) ? snapshot : PotCycleSnapshot.Empty;
    }

    public IReadOnlyList<PotCycleSnapshot> KnownCycles =>
        cycles.Values.Where(cycle => cycle.HasAnythingToShow).OrderBy(cycle => cycle.TerritoryId).ToList();

    /// <summary>
    /// Updates the zone's cycle from the pot that is live right now, if any.
    /// <paramref name="liveSpawnAt"/> is when that pot spawned.
    /// </summary>
    public PotCycleSnapshot Update(
        uint territoryId,
        uint livePotFateId,
        DateTimeOffset liveSpawnAt,
        DateTimeOffset now)
    {
        var previous = Get(territoryId);
        var next = livePotFateId != 0
            ? BuildFromLivePot(territoryId, livePotFateId, liveSpawnAt, previous)
            : RollIdlePrediction(territoryId, now, previous);
        cycles[territoryId] = next;
        return next;
    }

    public bool TryApplyRemoteAnchor(uint territoryId, uint potFateId, DateTimeOffset spawnAt, DateTimeOffset now)
    {
        if (!PotZoneData.TryGetPotFate(potFateId, out var pot) || pot.TerritoryId != territoryId)
        {
            return false;
        }

        // A reported spawn in the future, or one so old that its whole cycle
        // has passed many times, is not worth trusting.
        if (spawnAt > now + TimeSpan.FromMinutes(1) || now - spawnAt > TimeSpan.FromHours(6))
        {
            return false;
        }

        var previous = Get(territoryId);
        if (previous.CurrentActivePotFateId != 0)
        {
            return false;
        }

        if (previous.HasKnownAnchor && previous.AnchorSpawnAt >= spawnAt)
        {
            return false;
        }

        var opposite = PotZoneData.GetOppositePot(potFateId);
        var applied = new PotCycleSnapshot
        {
            TerritoryId = territoryId,
            HasKnownAnchor = true,
            AnchorPotFateId = potFateId,
            AnchorSpawnAt = spawnAt,
            IsRemoteAnchor = true,
            PredictedNextPotFateId = opposite,
            PredictedNextSpawnAt = opposite == 0 ? DateTimeOffset.MinValue : spawnAt + PotCycleInterval,
        };

        // An old report still tells us the phase of the cycle; roll it forward.
        cycles[territoryId] = RollIdlePrediction(territoryId, now, applied);
        return true;
    }

    public void Invalidate(uint territoryId)
    {
        cycles.Remove(territoryId);
    }

    public void Clear()
    {
        cycles.Clear();
    }

    private static PotCycleSnapshot BuildFromLivePot(
        uint territoryId,
        uint livePotFateId,
        DateTimeOffset spawnAt,
        PotCycleSnapshot previous)
    {
        if (previous.CurrentActivePotFateId == livePotFateId && !previous.IsRemoteAnchor)
        {
            return previous;
        }

        var opposite = PotZoneData.GetOppositePot(livePotFateId);
        return new PotCycleSnapshot
        {
            TerritoryId = territoryId,
            HasKnownAnchor = true,
            AnchorPotFateId = livePotFateId,
            AnchorSpawnAt = spawnAt,
            IsRemoteAnchor = false,
            CurrentActivePotFateId = livePotFateId,
            PredictedNextPotFateId = opposite,
            PredictedNextSpawnAt = opposite == 0 ? DateTimeOffset.MinValue : spawnAt + PotCycleInterval,
        };
    }

    private static PotCycleSnapshot RollIdlePrediction(uint territoryId, DateTimeOffset now, PotCycleSnapshot previous)
    {
        if (!previous.HasPredictedNextPot || now <= previous.PredictedNextSpawnAt + PredictionStaleGrace)
        {
            return previous with { TerritoryId = territoryId, CurrentActivePotFateId = 0 };
        }

        // The predicted pot was never seen. Assume it spawned on schedule and
        // move the prediction to the pot after it.
        var anchorId = previous.AnchorPotFateId;
        var anchorSpawn = previous.AnchorSpawnAt;
        var nextId = previous.PredictedNextPotFateId;
        var nextSpawn = previous.PredictedNextSpawnAt;
        while (nextId != 0 && now > nextSpawn + PredictionStaleGrace)
        {
            anchorId = nextId;
            anchorSpawn = nextSpawn;
            nextId = PotZoneData.GetOppositePot(nextId);
            nextSpawn += PotCycleInterval;
        }

        return new PotCycleSnapshot
        {
            TerritoryId = territoryId,
            HasKnownAnchor = nextId != 0,
            AnchorPotFateId = anchorId,
            AnchorSpawnAt = anchorSpawn,
            IsRemoteAnchor = previous.IsRemoteAnchor,
            PredictedNextPotFateId = nextId,
            PredictedNextSpawnAt = nextId == 0 ? DateTimeOffset.MinValue : nextSpawn,
        };
    }
}

public static class PotTimingPolicy
{
    /// <summary>
    /// Whether an activity may start now. The cutoff counts down to the pot's
    /// spawn, not to the time BOCCHI leaves for it.
    /// </summary>
    public static bool AllowsStart(PotCycleSnapshot cycle, DateTimeOffset now, TimeSpan cutoff, bool potWorkEnabled)
    {
        if (!potWorkEnabled || !cycle.HasPredictedNextPot)
        {
            return true;
        }

        if (now > cycle.PredictedNextSpawnAt + PotCycleTracker.PredictionStaleGrace)
        {
            return true;
        }

        return cutoff <= TimeSpan.Zero || cycle.PredictedNextSpawnAt - now > cutoff;
    }

    public static bool ShouldPreposition(PotCycleSnapshot cycle, DateTimeOffset now, int leadMinutes, bool potWorkEnabled)
    {
        if (!potWorkEnabled || !cycle.HasPredictedNextPot)
        {
            return false;
        }

        if (now > cycle.PredictedNextSpawnAt + PotCycleTracker.PredictionStaleGrace)
        {
            return false;
        }

        return cycle.PredictedNextSpawnAt - now <= TimeSpan.FromMinutes(Math.Max(0, leadMinutes));
    }

    /// <summary>Skips a live pot that has fewer than the configured minutes left.</summary>
    public static bool ShouldSkipLivePot(int minimumMinutesRemaining, long timeRemainingSeconds)
    {
        return minimumMinutesRemaining > 0
               && timeRemainingSeconds > 0
               && timeRemainingSeconds < minimumMinutesRemaining * 60L;
    }

    /// <summary>
    /// The live pot's spawn time. The game's start time is used when it is
    /// plausible; some North Horn FATEs report no timers, so the time BOCCHI
    /// first saw the pot is the fallback.
    /// </summary>
    public static DateTimeOffset ResolveSpawnTime(long startTimeEpoch, long firstSeenEpoch, DateTimeOffset now)
    {
        if (startTimeEpoch > 0)
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(startTimeEpoch);
            if (start <= now + TimeSpan.FromMinutes(1) && now - start <= PotCycleTracker.PotCycleInterval)
            {
                return start;
            }
        }

        return firstSeenEpoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(firstSeenEpoch) : now;
    }
}
