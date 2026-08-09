using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace BOCCHI.Modules.Treasure;

public enum SpiritPotDirection
{
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

public enum SpiritPotDistanceBand
{
    VeryNear,
    Near,
    Far,
    VeryFar,
}

public readonly record struct SpiritPotHint(
    Vector3 Origin,
    SpiritPotDirection Direction,
    SpiritPotDistanceBand DistanceBand,
    float MinimumDistance,
    float MaximumDistance)
{
    public const float SectorHalfAngleDegrees = 22.5f;

    public const float DistanceTolerance = 3f;

    public const float AngleToleranceDegrees = 2f;

    public Vector2 DirectionVector => Direction switch
    {
        SpiritPotDirection.North => new Vector2(0f, -1f),
        SpiritPotDirection.NorthEast => Vector2.Normalize(new Vector2(1f, -1f)),
        SpiritPotDirection.East => new Vector2(1f, 0f),
        SpiritPotDirection.SouthEast => Vector2.Normalize(new Vector2(1f, 1f)),
        SpiritPotDirection.South => new Vector2(0f, 1f),
        SpiritPotDirection.SouthWest => Vector2.Normalize(new Vector2(-1f, 1f)),
        SpiritPotDirection.West => new Vector2(-1f, 0f),
        SpiritPotDirection.NorthWest => Vector2.Normalize(new Vector2(-1f, -1f)),
        _ => Vector2.Zero,
    };

    public bool Contains(Vector3 candidate)
    {
        if (!IsFinite(candidate))
        {
            return false;
        }

        var offset = new Vector2(candidate.X - Origin.X, candidate.Z - Origin.Z);
        var distance = offset.Length();
        var minimum = Math.Max(0f, MinimumDistance - DistanceTolerance);
        var maximum = float.IsPositiveInfinity(MaximumDistance)
            ? float.PositiveInfinity
            : MaximumDistance + DistanceTolerance;
        if (distance < minimum || distance > maximum)
        {
            return false;
        }

        if (distance < 0.001f)
        {
            return minimum <= 0f;
        }

        var direction = offset / distance;
        var minimumDot = MathF.Cos(
            (SectorHalfAngleDegrees + AngleToleranceDegrees) * MathF.PI / 180f);
        return Vector2.Dot(direction, DirectionVector) >= minimumDot;
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }
}

public sealed class SpiritPotTreasurePredictor
{
    private static readonly Regex HintPattern = new(
        @"^财宝好像是在(?<direction>正北|东北|正东|东南|正南|西南|正西|西北)方向(?<distance>很近|不远|稍远|很远)的地方！$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<SpiritPotHint> hints = [];

    private List<Vector3> candidateUniverse = [];

    private List<Vector3> candidates = [];

    public IReadOnlyList<SpiritPotHint> Hints => hints;

    public IReadOnlyList<Vector3> Candidates => candidates;

    public bool HasPrediction => hints.Count > 0;

    public bool HasConflict { get; private set; }

    public bool TryApplyHint(
        string message,
        Vector3 origin,
        IEnumerable<Vector3> availableCandidates)
    {
        if (!TryParseHint(message, origin, out var hint))
        {
            return false;
        }

        if (hints.Count == 0)
        {
            candidateUniverse = availableCandidates
                .Where(IsFinite)
                .Distinct()
                .ToList();
        }

        var acceptedHints = hints.Append(hint).ToArray();
        var filtered = candidateUniverse
            .Where(candidate => acceptedHints.All(existing => existing.Contains(candidate)))
            .ToList();

        if (candidateUniverse.Count > 0 && filtered.Count == 0 && candidates.Count > 0)
        {
            HasConflict = true;
            return true;
        }

        hints.Add(hint);
        candidates = filtered;
        HasConflict = candidateUniverse.Count > 0 && candidates.Count == 0;
        return true;
    }

    public void Reset()
    {
        hints.Clear();
        candidateUniverse.Clear();
        candidates.Clear();
        HasConflict = false;
    }

    public static bool TryParseHint(string message, Vector3 origin, out SpiritPotHint hint)
    {
        hint = default;
        if (string.IsNullOrWhiteSpace(message) || !IsFinite(origin))
        {
            return false;
        }

        var match = HintPattern.Match(message.Trim());
        if (!match.Success
            || !TryParseDirection(match.Groups["direction"].Value, out var direction)
            || !TryParseDistance(match.Groups["distance"].Value, out var band, out var minimum, out var maximum))
        {
            return false;
        }

        hint = new SpiritPotHint(origin, direction, band, minimum, maximum);
        return true;
    }

    public static bool ShouldResetForMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var text = message.Trim();
        return text.Contains("发现了财宝", StringComparison.Ordinal)
               || text.Contains("已经发现的财宝消失了", StringComparison.Ordinal)
               || text.Contains("似乎能够告知第二处财宝所在地", StringComparison.Ordinal)
               || string.Equals(text, "不见了……", StringComparison.Ordinal)
               || string.Equals(text, "耗尽了力量……", StringComparison.Ordinal);
    }

    private static bool TryParseDirection(string value, out SpiritPotDirection direction)
    {
        direction = value switch
        {
            "正北" => SpiritPotDirection.North,
            "东北" => SpiritPotDirection.NorthEast,
            "正东" => SpiritPotDirection.East,
            "东南" => SpiritPotDirection.SouthEast,
            "正南" => SpiritPotDirection.South,
            "西南" => SpiritPotDirection.SouthWest,
            "正西" => SpiritPotDirection.West,
            "西北" => SpiritPotDirection.NorthWest,
            _ => default,
        };
        return value is "正北" or "东北" or "正东" or "东南"
            or "正南" or "西南" or "正西" or "西北";
    }

    private static bool TryParseDistance(
        string value,
        out SpiritPotDistanceBand band,
        out float minimum,
        out float maximum)
    {
        (band, minimum, maximum) = value switch
        {
            "很近" => (SpiritPotDistanceBand.VeryNear, 0f, 20f),
            "不远" => (SpiritPotDistanceBand.Near, 20f, 100f),
            "稍远" => (SpiritPotDistanceBand.Far, 100f, 200f),
            "很远" => (SpiritPotDistanceBand.VeryFar, 200f, float.PositiveInfinity),
            _ => default,
        };
        return value is "很近" or "不远" or "稍远" or "很远";
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }
}
