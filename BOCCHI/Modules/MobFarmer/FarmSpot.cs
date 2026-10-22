using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer;

/// <summary>A named place to farm. Ported from upstream BOCCHI 4.2.</summary>
public class FarmSpot
{
    public string Name { get; set; } = "Spot";

    public bool Enabled { get; set; } = true;

    /// <summary>Higher first; the nearest spot breaks a tie.</summary>
    public int Priority { get; set; }

    /// <summary>The zone the spot was recorded in; 0 matches any zone.</summary>
    public uint TerritoryId { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public bool UseStackPoint { get; set; }

    public float StackX { get; set; }

    public float StackY { get; set; }

    public float StackZ { get; set; }

    /// <summary>0 uses the farmer's own minimum.</summary>
    public int MinimumMobsToStartFight { get; set; }

    // Methods rather than properties, so the config file only stores the floats.
    public Vector3 GetOrigin()
    {
        return new Vector3(X, Y, Z);
    }

    public Vector3? GetStackPoint()
    {
        return UseStackPoint ? new Vector3(StackX, StackY, StackZ) : null;
    }

    public void SetOrigin(Vector3 position)
    {
        X = position.X;
        Y = position.Y;
        Z = position.Z;
    }

    public void SetStackPoint(Vector3 position)
    {
        StackX = position.X;
        StackY = position.Y;
        StackZ = position.Z;
        UseStackPoint = true;
    }
}

public static class FarmSpotPolicy
{
    public const int MaximumMobsToStartFight = 20;

    public static float Distance2D(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }

    public static bool IsInZone(FarmSpot spot, uint territoryId)
    {
        return spot.TerritoryId == 0 || spot.TerritoryId == territoryId;
    }

    /// <summary>The enabled, unclaimed spot of this zone with the highest priority, nearest first on a tie.</summary>
    public static FarmSpot? SelectBest(
        IEnumerable<FarmSpot> spots,
        uint territoryId,
        IReadOnlyCollection<FarmSpot> claimed,
        Vector3 from)
    {
        return spots
            .Where(spot => spot.Enabled && IsInZone(spot, territoryId) && !claimed.Contains(spot))
            .OrderByDescending(spot => spot.Priority)
            .ThenBy(spot => Distance2D(from, spot.GetOrigin()))
            .FirstOrDefault();
    }

    public static int EffectiveMinimumMobsToStartFight(FarmSpot? spot, int fallback)
    {
        return spot is { MinimumMobsToStartFight: > 0 }
            ? Math.Min(spot.MinimumMobsToStartFight, MaximumMobsToStartFight)
            : fallback;
    }

    /// <summary>
    /// Someone else has the spot: their fight holds every enemy around it, or
    /// another player stands on the farm or stack point.
    /// </summary>
    public static bool IsClaimedNow(bool anyContested, bool anyFree, bool otherPlayerNearby)
    {
        return (anyContested && !anyFree) || otherPlayerNearby;
    }

    /// <summary>Moves a spot one place up or down the priority order.</summary>
    public static void Move(IReadOnlyList<FarmSpot> spots, FarmSpot spot, bool up)
    {
        var ordered = Ordered(spots).ToList();
        var index = ordered.IndexOf(spot);
        var otherIndex = up ? index - 1 : index + 1;
        if (index < 0 || otherIndex < 0 || otherIndex >= ordered.Count)
        {
            return;
        }

        var other = ordered[otherIndex];
        if (other.Priority == spot.Priority)
        {
            spot.Priority += up ? 1 : -1;
            return;
        }

        (spot.Priority, other.Priority) = (other.Priority, spot.Priority);
    }

    /// <summary>Display and choice order: priority, highest first; list order breaks ties.</summary>
    public static IEnumerable<FarmSpot> Ordered(IEnumerable<FarmSpot> spots)
    {
        return spots.OrderByDescending(spot => spot.Priority);
    }

    public static int NextPriority(IReadOnlyCollection<FarmSpot> spots)
    {
        // Like upstream, a new spot goes to the top of the list.
        return spots.Count == 0 ? 0 : spots.Max(spot => spot.Priority) + 1;
    }
}

/// <summary>A spot counts as claimed only after it stayed claimed for the configured time.</summary>
public sealed class FarmSpotClaimTimer
{
    private DateTimeOffset? claimedSince;

    public void Reset()
    {
        claimedSince = null;
    }

    public bool Tick(bool claimedNow, DateTimeOffset now, float seconds)
    {
        if (!claimedNow)
        {
            claimedSince = null;
            return false;
        }

        claimedSince ??= now;
        if (now - claimedSince.Value < TimeSpan.FromSeconds(seconds))
        {
            return false;
        }

        claimedSince = null;
        return true;
    }
}
