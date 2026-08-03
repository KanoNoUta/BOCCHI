using BOCCHI.Pathfinding;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Data_TreasureData = BOCCHI.Data.TreasureData;

namespace BOCCHI.Modules.Treasure;

public class Pathfinder : BasePathfinder
{
    private readonly List<Data_TreasureData.TreasureDatum> treasure;

    private readonly IReadOnlyDictionary<uint, Vector3> explicitRoutePositions;

    private readonly TreasureRouteStartMode? routeStartMode;

    private readonly int manualRouteStart;

    public Pathfinder(
        List<Data_TreasureData.TreasureDatum> treasure,
        float returnCost = 300f,
        float teleportCost = 50f,
        TreasureRouteStartMode? routeStartMode = null,
        int manualRouteStart = 1)
        : base(CreateNodePositions(treasure, routeStartMode != null), returnCost, teleportCost)
    {
        this.treasure = treasure;
        this.routeStartMode = routeStartMode;
        this.manualRouteStart = manualRouteStart;
        explicitRoutePositions = routeStartMode == null
            ? new Dictionary<uint, Vector3>()
            : NorthHornTreasureRoute.BuildWaypointPositions(
                treasure.Select(node => node.Position));

        LoadFile("precomputed_treasure_hunt_data.json");
    }

    protected override IReadOnlyList<uint>? GetExplicitNodeOrder(Vector3 start, List<uint> nodes)
    {
        if (routeStartMode == null)
        {
            return null;
        }

        return NorthHornTreasureRoute.OrderNodes(
            start,
            nodes,
            explicitRoutePositions,
            routeStartMode.Value,
            manualRouteStart);
    }

    protected override uint GetStartingNode(Vector3 start, List<uint> nodes)
    {
        var closestDistance = float.MaxValue;
        var startTreasure = treasure.First();
        foreach (var treasureData in treasure)
        {
            if (!nodes.Contains(treasureData.Id))
            {
                continue;
            }

            var distance = Vector3.Distance(start, treasureData.Position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                startTreasure = treasureData;
            }
        }

        return startTreasure.Id;
    }

    private static IReadOnlyDictionary<uint, Vector3> CreateNodePositions(
        IEnumerable<Data_TreasureData.TreasureDatum> treasure,
        bool includeNorthHornWaypoints)
    {
        var treasureList = treasure.ToList();
        var positions = treasureList
            .GroupBy(node => node.Id)
            .ToDictionary(group => group.Key, group => group.First().Position);
        if (!includeNorthHornWaypoints)
        {
            return positions;
        }

        foreach (var (nodeId, position) in NorthHornTreasureRoute.BuildWaypointPositions(
                     treasureList.Select(node => node.Position)))
        {
            positions[nodeId] = position;
        }

        return positions;
    }
}
