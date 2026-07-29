using BOCCHI.Data;
using BOCCHI.Pathfinding;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Carrots;

public class Pathfinder : BasePathfinder
{
    public Pathfinder(
        IReadOnlyDictionary<uint, Vector3> nodePositions,
        float returnCost = 300f,
        float teleportCost = 50f)
        : base(nodePositions, returnCost, teleportCost)
    {
        LoadFile("precomputed_carrot_hunt_data.json");
    }

    protected override uint GetStartingNode(Vector3 start, List<uint> nodes)
    {
        return nodes
            .Select(nodeId =>
            {
                var found = TryGetNodePosition(nodeId, out var position);
                return (NodeId: nodeId, Found: found, Position: position);
            })
            .Where(candidate => candidate.Found)
            .OrderBy(candidate => Vector3.DistanceSquared(start, candidate.Position))
            .ThenBy(candidate => candidate.NodeId)
            .Select(candidate => candidate.NodeId)
            .First();
    }
}
