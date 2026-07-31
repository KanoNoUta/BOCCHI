using BOCCHI.Modules.AggroRange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Pathfinding;

/// <summary>
/// Pure XZ path post-processor. It inserts tangent/arc waypoints around the
/// live ordinary-mob circles while preserving the original path's topology.
/// </summary>
public static class AggroAvoidancePlanner
{
    private const float NumericalEpsilon = 0.05f;
    private const float ArcClearance = 0.75f;
    private const float ArcStepRadians = MathF.PI / 8f;
    private const int MaximumDetours = 24;
    private const int MaximumPathPoints = 512;

    public static bool TryAvoid(
        IReadOnlyList<Vector3> sourcePath,
        IReadOnlyList<AggroDangerZone> zones,
        float verticalTolerance,
        Func<Vector3, Vector3?> projector,
        out List<Vector3> safePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(projector);

        safePath = RemoveDuplicatePoints(sourcePath);
        if (safePath.Count < 2 || zones.Count == 0)
        {
            return safePath.Count >= 2;
        }

        // A destination located inside a circle is inherently unreachable
        // without entering it. Ignore only those terminal circles; all other
        // loaded ordinary mobs still participate in avoidance.
        var relevantZones = GetRelevantZones(zones, safePath[^1]);

        // vnavmesh may place intermediate corners inside a live danger circle.
        // Those corners are not constraints; remove them so the tangent can be
        // built between the last safe point and the next safe point.
        var originalPointCount = safePath.Count;
        safePath = safePath
            .Where((point, index) => index == 0
                                     || index == originalPointCount - 1
                                     || !relevantZones.Any(zone =>
                                         DistanceXZ(point, zone.Center) < zone.Radius + ArcClearance))
            .ToList();

        for (var detourCount = 0; detourCount < MaximumDetours; detourCount++)
        {
            if (!TryFindFirstBlockedSegment(safePath, relevantZones, verticalTolerance, out var segmentIndex, out var zone))
            {
                return safePath.Count <= MaximumPathPoints;
            }

            if (!TryCreateDetour(
                    safePath[segmentIndex],
                    safePath[segmentIndex + 1],
                    zone,
                    relevantZones,
                    verticalTolerance,
                    projector,
                    out var detour))
            {
                return false;
            }

            safePath.InsertRange(segmentIndex + 1, detour);
            safePath = RemoveDuplicatePoints(safePath);
            if (safePath.Count > MaximumPathPoints)
            {
                return false;
            }
        }

        return IsPathClear(safePath, relevantZones, verticalTolerance);
    }

    public static bool IsPathClear(
        IReadOnlyList<Vector3> path,
        IReadOnlyList<AggroDangerZone> zones,
        float verticalTolerance)
    {
        return !TryFindFirstBlockedSegment(path, zones, verticalTolerance, out _, out _);
    }

    /// <summary>
    /// Removes only circles that contain the terminal destination. Entering
    /// such a circle is unavoidable (for example while approaching the mob
    /// that is the requested target), so treating it as a dynamic obstruction
    /// would cause an endless replan loop.
    /// </summary>
    public static AggroDangerZone[] GetRelevantZones(
        IReadOnlyList<AggroDangerZone> zones,
        Vector3 destination)
    {
        ArgumentNullException.ThrowIfNull(zones);
        return zones
            .Where(zone => !ContainsXZ(zone, destination, NumericalEpsilon))
            .ToArray();
    }

    public static bool SegmentEntersZone(
        Vector3 start,
        Vector3 end,
        AggroDangerZone zone,
        float verticalTolerance)
    {
        if (!IsVerticallyRelevant(start, end, zone.Center.Y, verticalTolerance))
        {
            return false;
        }

        var radius = Math.Max(0.1f, zone.Radius);
        var startDistance = DistanceXZ(start, zone.Center);
        var closestDistance = DistanceToSegmentXZ(zone.Center, start, end);

        if (startDistance < radius - NumericalEpsilon)
        {
            // The player may already be inside a circle. Let an outward leg
            // escape, but reject a leg that first travels deeper into it.
            return closestDistance + NumericalEpsilon < startDistance;
        }

        return closestDistance < radius - NumericalEpsilon;
    }

    private static bool TryFindFirstBlockedSegment(
        IReadOnlyList<Vector3> path,
        IReadOnlyList<AggroDangerZone> zones,
        float verticalTolerance,
        out int segmentIndex,
        out AggroDangerZone blockedZone)
    {
        for (var i = 0; i + 1 < path.Count; i++)
        {
            foreach (var zone in zones)
            {
                if (SegmentEntersZone(path[i], path[i + 1], zone, verticalTolerance))
                {
                    segmentIndex = i;
                    blockedZone = zone;
                    return true;
                }
            }
        }

        segmentIndex = -1;
        blockedZone = default;
        return false;
    }

    private static bool TryCreateDetour(
        Vector3 start,
        Vector3 end,
        AggroDangerZone blockedZone,
        IReadOnlyList<AggroDangerZone> allZones,
        float verticalTolerance,
        Func<Vector3, Vector3?> projector,
        out List<Vector3> detour)
    {
        detour = [];
        var startDistance = DistanceXZ(start, blockedZone.Center);

        if (startDistance < blockedZone.Radius - NumericalEpsilon)
        {
            var exitRadius = blockedZone.Radius + ArcClearance;
            var direction = NormalizeXZ(start - blockedZone.Center);
            if (direction == Vector3.Zero)
            {
                var towardEnd = NormalizeXZ(end - start);
                direction = towardEnd == Vector3.Zero
                    ? Vector3.UnitX
                    : new Vector3(-towardEnd.Z, 0f, towardEnd.X);
            }

            var exit = new Vector3(
                blockedZone.Center.X + direction.X * exitRadius,
                start.Y,
                blockedZone.Center.Z + direction.Z * exitRadius);
            var projectedExit = projector(exit);
            if (projectedExit == null
                || ContainsXZ(blockedZone, projectedExit.Value, NumericalEpsilon))
            {
                return false;
            }

            detour.Add(projectedExit.Value);
            return true;
        }

        var endDistance = DistanceXZ(end, blockedZone.Center);
        if (endDistance <= blockedZone.Radius + NumericalEpsilon)
        {
            return false;
        }

        // Preserve as much safety clearance as the local vnav corners allow.
        // A corner just outside the danger circle cannot have a tangent to a
        // larger radius, so shrink only the extra arc clearance, never the
        // actual protected radius.
        var routeRadius = Math.Max(
            blockedZone.Radius + 0.15f,
            Math.Min(
                blockedZone.Radius + ArcClearance,
                Math.Min(startDistance, endDistance) - 0.1f));

        var startAngles = GetTangentAngles(start, blockedZone.Center, routeRadius);
        var endAngles = GetTangentAngles(end, blockedZone.Center, routeRadius);
        List<Vector3>? best = null;
        var bestLength = float.MaxValue;

        foreach (var startAngle in startAngles)
        foreach (var endAngle in endAngles)
        foreach (var direction in new[] { -1f, 1f })
        {
            var delta = DirectedAngleDelta(startAngle, endAngle, direction);
            var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(delta) / ArcStepRadians));
            var candidate = new List<Vector3>(steps + 1);
            var valid = true;
            for (var step = 0; step <= steps; step++)
            {
                var progress = (float)step / steps;
                var angle = startAngle + delta * progress;
                var unprojected = new Vector3(
                    blockedZone.Center.X + MathF.Cos(angle) * routeRadius,
                    start.Y + (end.Y - start.Y) * progress,
                    blockedZone.Center.Z + MathF.Sin(angle) * routeRadius);
                var projected = projector(unprojected);
                if (projected == null
                    || ContainsXZ(blockedZone, projected.Value, NumericalEpsilon))
                {
                    valid = false;
                    break;
                }

                candidate.Add(projected.Value);
            }

            if (!valid)
            {
                continue;
            }

            var completeCandidate = new List<Vector3>(candidate.Count + 2) { start };
            completeCandidate.AddRange(candidate);
            completeCandidate.Add(end);
            if (!IsPathClear(completeCandidate, allZones, verticalTolerance))
            {
                continue;
            }

            var length = CalculateLength(completeCandidate);
            if (length < bestLength)
            {
                bestLength = length;
                best = candidate;
            }
        }

        if (best == null)
        {
            return false;
        }

        detour = best;
        return true;
    }

    private static float[] GetTangentAngles(Vector3 point, Vector3 center, float radius)
    {
        var direction = point - center;
        var distance = Math.Max(radius + NumericalEpsilon, DistanceXZ(point, center));
        var baseAngle = MathF.Atan2(direction.Z, direction.X);
        var offset = MathF.Acos(Math.Clamp(radius / distance, -1f, 1f));
        return [baseAngle - offset, baseAngle + offset];
    }

    private static float DirectedAngleDelta(float start, float end, float direction)
    {
        var delta = NormalizeAngle(end - start);
        if (direction > 0f && delta < 0f)
        {
            delta += MathF.Tau;
        }
        else if (direction < 0f && delta > 0f)
        {
            delta -= MathF.Tau;
        }

        return delta;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle <= -MathF.PI)
        {
            angle += MathF.Tau;
        }

        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        return angle;
    }

    private static bool IsVerticallyRelevant(Vector3 start, Vector3 end, float y, float tolerance)
    {
        var minY = Math.Min(start.Y, end.Y) - Math.Max(0f, tolerance);
        var maxY = Math.Max(start.Y, end.Y) + Math.Max(0f, tolerance);
        return y >= minY && y <= maxY;
    }

    private static bool ContainsXZ(AggroDangerZone zone, Vector3 point, float epsilon)
    {
        return DistanceXZ(point, zone.Center) < zone.Radius - epsilon;
    }

    private static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
    {
        var dx = end.X - start.X;
        var dz = end.Z - start.Z;
        var lengthSquared = dx * dx + dz * dz;
        if (lengthSquared <= float.Epsilon)
        {
            return DistanceXZ(point, start);
        }

        var t = ((point.X - start.X) * dx + (point.Z - start.Z) * dz) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        var closestX = start.X + dx * t;
        var closestZ = start.Z + dz * t;
        var x = point.X - closestX;
        var z = point.Z - closestZ;
        return MathF.Sqrt(x * x + z * z);
    }

    private static float DistanceXZ(Vector3 left, Vector3 right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return MathF.Sqrt(x * x + z * z);
    }

    private static Vector3 NormalizeXZ(Vector3 value)
    {
        var length = MathF.Sqrt(value.X * value.X + value.Z * value.Z);
        return length <= float.Epsilon
            ? Vector3.Zero
            : new Vector3(value.X / length, 0f, value.Z / length);
    }

    private static float CalculateLength(IReadOnlyList<Vector3> path)
    {
        var length = 0f;
        for (var i = 1; i < path.Count; i++)
        {
            length += Vector3.Distance(path[i - 1], path[i]);
        }

        return length;
    }

    private static List<Vector3> RemoveDuplicatePoints(IReadOnlyList<Vector3> path)
    {
        var result = new List<Vector3>(path.Count);
        foreach (var point in path)
        {
            if (result.Count == 0 || Vector3.DistanceSquared(result[^1], point) > 0.01f)
            {
                result.Add(point);
            }
        }

        return result;
    }
}
