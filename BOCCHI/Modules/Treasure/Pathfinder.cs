using BOCCHI.Pathfinding;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Data_TreasureData = BOCCHI.Data.TreasureData;

namespace BOCCHI.Modules.Treasure;

public class Pathfinder : BasePathfinder
{
    private readonly List<Data_TreasureData.TreasureDatum> treasure;

    public Pathfinder(List<Data_TreasureData.TreasureDatum> treasure, float returnCost = 300f, float teleportCost = 50f)
        : base(CreateNodePositions(treasure), returnCost, teleportCost)
    {
        this.treasure = treasure;

        LoadFile("precomputed_treasure_hunt_data.json");
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
        IEnumerable<Data_TreasureData.TreasureDatum> treasure)
    {
        return treasure
            .GroupBy(node => node.Id)
            .ToDictionary(group => group.Key, group => group.First().Position);
    }
}
