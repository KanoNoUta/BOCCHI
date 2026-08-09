using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Treasure;

public enum TreasureRouteStartMode
{
    Nearest = 0,
    Manual = 1,
}

public static class NorthHornTreasureRoute
{
    public const int RouteCount = 68;

    public const uint WaypointIdBase = 0xF0000000u;

    public const float MapImageSize = 1078f;

    private const float MapCenter = MapImageSize / 2f;

    private const float MapScale = MapImageSize / 2048f;

    private static readonly uint[] RouteNodeIds = Enumerable.Range(1, RouteCount)
        .Select(routeNumber => WaypointIdBase + (uint)routeNumber)
        .ToArray();

    private static readonly Vector2[] RouteMapPoints =
    [
        new(855, 1021), new(886, 910), new(970, 879), new(949, 834),
        new(928, 669), new(883, 447), new(888, 333), new(1036, 335),
        new(888, 237), new(970, 184), new(1046, 92), new(882, 134),
        new(868, 98), new(864, 49), new(748, 111), new(621, 73),
        new(539, 102), new(435, 145), new(401, 297), new(307, 236),
        new(273, 123), new(331, 43), new(190, 51), new(58, 133),
        new(94, 155), new(95, 209), new(164, 234), new(165, 318),
        new(104, 394), new(52, 640), new(127, 735), new(124, 864),
        new(93, 916), new(168, 1001), new(197, 1025), new(231, 934),
        new(405, 939), new(276, 934), new(218, 835), new(207, 651),
        new(230, 559), new(206, 455), new(226, 392), new(408, 389),
        new(456, 629), new(389, 831), new(579, 818), new(531, 859),
        new(529, 899), new(664, 1008), new(675, 892), new(776, 748),
        new(776, 584), new(795, 445), new(741, 443), new(693, 339),
        new(679, 242), new(525, 299), new(575, 377), new(551, 480),
        new(454, 453), new(451, 585), new(377, 575), new(452, 675),
        new(554, 617), new(609, 541), new(675, 511), new(704, 632),
    ];

    public static IReadOnlyList<uint> NodeIds => RouteNodeIds;

    public static IReadOnlyList<Vector2> MapPoints => RouteMapPoints;

    public static bool IsWaypointId(uint nodeId)
    {
        return nodeId > WaypointIdBase
               && nodeId <= WaypointIdBase + RouteCount;
    }

    public static Dictionary<uint, Vector3> BuildWaypointPositions(
        IEnumerable<Vector3> packagedTreasurePositions)
    {
        var unassignedPackaged = packagedTreasurePositions
            .Where(IsFinite)
            .Distinct()
            .ToList();
        var positions = new Dictionary<uint, Vector3>(RouteCount);
        for (var index = 0; index < RouteCount; index++)
        {
            // A numbered checkpoint must own one real layout position. Reusing
            // a position after the packaged candidates are exhausted makes
            // several route numbers complete at the same place and appears as
            // skipped points. Leave incomplete data incomplete so the caller
            // can report the missing checkpoints instead of fabricating them.
            if (unassignedPackaged.Count == 0)
            {
                break;
            }

            var mapPoint = RouteMapPoints[index];
            var x = (mapPoint.X - MapCenter) / MapScale;
            var z = (mapPoint.Y - MapCenter) / MapScale;
            // The numbered image points describe route order, but their
            // inverse-transformed X/Z values are not guaranteed to lie on a
            // vnavmesh polygon. Snap each number to the closest real layout
            // spawn so the route remains faithful and pathable. Prefer an
            // unused spawn to prevent adjacent numbers collapsing together.
            var nearestPackagedPosition = unassignedPackaged
                .OrderBy(position => HorizontalDistanceSquared(position, x, z))
                .ThenBy(position => position.X)
                .ThenBy(position => position.Z)
                .FirstOrDefault();
            positions[RouteNodeIds[index]] = nearestPackagedPosition;
            unassignedPackaged.Remove(nearestPackagedPosition);
        }

        return positions;
    }

    public static List<uint> OrderNodes(
        Vector3 start,
        IEnumerable<uint> validNodeIds,
        IReadOnlyDictionary<uint, Vector3> positions,
        TreasureRouteStartMode startMode,
        int manualRouteNumber)
    {
        var valid = validNodeIds.ToHashSet();
        if (valid.Count == 0)
        {
            return [];
        }

        var startIndex = startMode == TreasureRouteStartMode.Manual
            ? FindNextValidIndex(Math.Clamp(manualRouteNumber, 1, RouteCount) - 1, valid)
            : FindNearestValidIndex(start, valid, positions);
        if (startIndex < 0)
        {
            return [];
        }

        var ordered = new List<uint>(valid.Count);
        for (var offset = 0; offset < RouteCount; offset++)
        {
            var nodeId = RouteNodeIds[(startIndex + offset) % RouteCount];
            if (valid.Contains(nodeId))
            {
                ordered.Add(nodeId);
            }
        }

        return ordered;
    }

    public static bool TryGetRouteNumber(uint nodeId, out int routeNumber)
    {
        if (!IsWaypointId(nodeId))
        {
            routeNumber = 0;
            return false;
        }

        routeNumber = checked((int)(nodeId - WaypointIdBase));
        return true;
    }

    public static Vector2 GetMapPoint(int routeNumber)
    {
        return RouteMapPoints[Math.Clamp(routeNumber, 1, RouteCount) - 1];
    }

    public static Vector2 WorldToMapPoint(Vector3 position)
    {
        return new Vector2(
            MapCenter + position.X * MapScale,
            MapCenter + position.Z * MapScale);
    }

    private static int FindNextValidIndex(int startIndex, IReadOnlySet<uint> valid)
    {
        for (var offset = 0; offset < RouteCount; offset++)
        {
            var index = (startIndex + offset) % RouteCount;
            if (valid.Contains(RouteNodeIds[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNearestValidIndex(
        Vector3 start,
        IReadOnlySet<uint> valid,
        IReadOnlyDictionary<uint, Vector3> positions)
    {
        var bestIndex = -1;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < RouteCount; index++)
        {
            var nodeId = RouteNodeIds[index];
            if (!valid.Contains(nodeId) || !positions.TryGetValue(nodeId, out var position))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(start, position);
            if (distance < bestDistance)
            {
                bestIndex = index;
                bestDistance = distance;
            }
        }

        return bestIndex >= 0 ? bestIndex : FindNextValidIndex(0, valid);
    }

    private static float HorizontalDistanceSquared(Vector3 position, float x, float z)
    {
        var deltaX = position.X - x;
        var deltaZ = position.Z - z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }
}

public static class TreasureLevelPolicy
{
    public static bool IsEligible(bool isNorthHorn, uint? verifiedLevel, int maximumLevel)
    {
        return verifiedLevel.HasValue
            ? verifiedLevel.Value <= maximumLevel
            : isNorthHorn;
    }
}

public static class LiveTreasureObjectPolicy
{
    public static bool ShouldTrack(bool isValid, bool isTargetable, bool isOpened)
    {
        return isValid && isTargetable && !isOpened;
    }
}

public static class NorthHornRouteTransitPolicy
{
    public static bool AllowsInitialTransit(bool isNorthHorn, int stepIndex)
    {
        return isNorthHorn && stepIndex == 0;
    }

    public static bool AllowsForcedRecovery(bool isNorthHorn)
    {
        return !isNorthHorn;
    }
}

public static class NorthHornRouteRejoinPolicy
{
    public static IReadOnlyList<uint> PreservePlannedOrder(IEnumerable<uint> remainingNodeIds)
    {
        return remainingNodeIds
            .Distinct()
            .ToArray();
    }
}

public static class NorthHornCurrentTreasurePolicy
{
    public static bool IsMatch(
        Vector3 expectedPosition,
        Vector3 candidatePosition,
        float matchRadius)
    {
        if (!IsFinite(expectedPosition)
            || !IsFinite(candidatePosition)
            || !float.IsFinite(matchRadius)
            || matchRadius < 0f)
        {
            return false;
        }

        return Vector3.DistanceSquared(expectedPosition, candidatePosition)
               <= matchRadius * matchRadius;
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }
}

public static class TreasureObjectMatchPolicy
{
    public static bool IsMatch(
        bool isNorthHorn,
        uint nodeId,
        uint candidateBaseId,
        float distanceSquared,
        float matchRadius,
        bool allowPositionFallback = true)
    {
        if (NorthHornTreasureRoute.IsWaypointId(nodeId)
            || candidateBaseId == 0
            || !float.IsFinite(distanceSquared)
            || distanceSquared < 0f
            || !float.IsFinite(matchRadius)
            || matchRadius < 0f
            || distanceSquared > matchRadius * matchRadius)
        {
            return false;
        }

        return candidateBaseId == nodeId
               || (isNorthHorn && allowPositionFallback);
    }
}
