using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator.Pots;

public enum PotHintDirection
{
    Unknown,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

public enum PotHintDistance
{
    Unknown,
    Immediate,
    Close,
    Far,
    BeyondFar,
}

/// <summary>
/// Narrows the coffer pads with the pot's compass hint. Bearings are measured
/// clockwise from north (-Z), so east (+X) is 90 degrees.
/// </summary>
public static class PotTreasureFilter
{
    public const float OctantTolerance = 22.5f;

    public const float WideTolerance = 67.5f;

    public const float RevealSpotTolerance = 22f;

    public static PotHintDirection DirectionFromParameter(int value)
    {
        return value is >= 1 and <= 8 ? (PotHintDirection)value : PotHintDirection.Unknown;
    }

    public static float? HintBearing(PotHintDirection direction)
    {
        return direction == PotHintDirection.Unknown ? null : ((int)direction - 1) * 45f;
    }

    /// <summary>The distance the hint suggests; it orders pads, it never filters them.</summary>
    public static float ExpectedDistance(PotHintDistance distance)
    {
        return distance switch
        {
            PotHintDistance.Immediate => 8f,
            PotHintDistance.Close => 20f,
            PotHintDistance.Far => 40f,
            PotHintDistance.BeyondFar => 100f,
            _ => 20f,
        };
    }

    public static float Bearing(Vector3 from, Vector3 to)
    {
        var degrees = MathF.Atan2(to.X - from.X, -(to.Z - from.Z)) * (180f / MathF.PI);
        return degrees < 0f ? degrees + 360f : degrees;
    }

    public static float AngleDelta(float a, float b)
    {
        var delta = MathF.Abs(a - b) % 360f;
        return delta > 180f ? 360f - delta : delta;
    }

    public static float Distance2D(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }

    public static List<Vector3> Narrow(
        IEnumerable<Vector3> pool,
        Vector3 from,
        PotHintDirection direction,
        PotHintDistance distance,
        float toleranceDegrees)
    {
        if (HintBearing(direction) is not { } hinted)
        {
            return pool.ToList();
        }

        var expected = ExpectedDistance(distance);
        return pool
            .Where(pad => Distance2D(pad, from) > 1f)
            .Where(pad => AngleDelta(Bearing(from, pad), hinted) <= toleranceDegrees)
            .OrderBy(pad => MathF.Abs(Distance2D(pad, from) - expected))
            .ThenBy(pad => Distance2D(pad, from))
            .ToList();
    }

    /// <summary>
    /// A revealed coffer belongs to the pot only when it sits on a pot pad and
    /// no ordinary treasure coffer spot is closer.
    /// </summary>
    public static bool IsOnPotPad(Vector3 position, IReadOnlyList<Vector3> potPads, IEnumerable<Vector3> otherCofferSpots)
    {
        if (potPads.Count == 0)
        {
            return false;
        }

        var nearestPot = potPads.Min(pad => Distance2D(pad, position));
        if (nearestPot > RevealSpotTolerance)
        {
            return false;
        }

        return !otherCofferSpots.Any(spot => Distance2D(spot, position) < nearestPot);
    }
}
