using BOCCHI.Data;
using BOCCHI.Enums;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace BOCCHI.Pathfinding;

public abstract class BasePathfinder : IPathfinder
{
    public PathfinderState State { get; private set; } = PathfinderState.None;

    private NodeDataSchema data = new();

    private readonly Dictionary<uint, Vector3> knownNodePositions;

    private readonly float returnCost;

    private readonly float teleportCost;

    public IReadOnlyCollection<uint> KnownNodeIds
    {
        get => knownNodePositions.Keys;
    }

    protected BasePathfinder(float returnCost = 300f, float teleportCost = 50f)
        : this(new Dictionary<uint, Vector3>(), returnCost, teleportCost)
    {
    }

    protected BasePathfinder(
        IReadOnlyDictionary<uint, Vector3> knownNodePositions,
        float returnCost = 300f,
        float teleportCost = 50f)
    {
        this.knownNodePositions = knownNodePositions
            .Where(pair => IsFinite(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        this.returnCost = returnCost;
        this.teleportCost = teleportCost;
    }

    protected abstract uint GetStartingNode(Vector3 start, List<uint> nodes);

    public bool TryGetNodePosition(uint nodeId, out Vector3 position)
    {
        return knownNodePositions.TryGetValue(nodeId, out position);
    }

    public Task<List<PathfinderStep>> FindPath(Vector3 start, List<uint> nodes)
    {
        if (State is not (PathfinderState.FileLoaded or PathfinderState.FallbackReady))
        {
            throw new InvalidOperationException("Pathfinder data is not ready.");
        }

        nodes = nodes.Distinct().ToList();
        State = PathfinderState.Pathfinding;

        var steps = new List<PathfinderStep>();
        var routedNodeIds = new HashSet<uint>();
        var fallbackStart = start;

        if (StateBeforePathfindingWasFileLoaded())
        {
            var precomputedNodes = nodes
                .Where(node => knownNodePositions.ContainsKey(node)
                               && data.NodeToNodeDistances.ContainsKey(node)
                               && data.NodeToAethernetDistances.ContainsKey(node))
                .ToList();

            if (precomputedNodes.Count > 0)
            {
                var startNode = GetStartingNode(start, precomputedNodes);
                var graph = BuildCostGraph(precomputedNodes);
                var graphIsComplete = HybridRoutePlanner.HasCompleteFiniteGraph(precomputedNodes, graph);
                var ordered = graphIsComplete
                    ? SolveTSPNearestInsertion(startNode, precomputedNodes, graph)
                    : HybridRoutePlanner.BuildReachableGreedyRoute(startNode, precomputedNodes, graph);

                if (!graphIsComplete)
                {
                    Svc.Log.Warning(
                        "Precomputed hunt graph contains missing/unreachable segments; " +
                        "using verified graph edges first and vnavmesh fallback for the remainder.");
                }

                if (ordered.Count > 0)
                {
                    steps.Add(PathfinderStep.WalkToDestination(ordered[0]));
                    steps.AddRange(BuildStepPath(ordered, graph));
                    routedNodeIds.UnionWith(ordered);
                    if (knownNodePositions.TryGetValue(ordered[^1], out var lastPosition))
                    {
                        fallbackStart = lastPosition;
                    }
                }
            }
        }

        var fallbackCandidates = nodes.Where(node => !routedNodeIds.Contains(node)).ToList();
        var fallbackOrdered = FallbackRoutePlanner.OrderByEuclideanCost(
            fallbackStart,
            fallbackCandidates,
            knownNodePositions);
        steps.AddRange(fallbackOrdered.Select(PathfinderStep.WalkToDestination));
        routedNodeIds.UnionWith(fallbackOrdered);

        var skippedNodes = nodes.Where(node => !routedNodeIds.Contains(node)).ToList();
        if (skippedNodes.Count > 0)
        {
            Svc.Log.Warning(
                $"Skipping {skippedNodes.Count} hunt nodes without usable paths or known positions: " +
                FormatNodeIds(skippedNodes));
        }

        if (steps.Count == 0)
        {
            Svc.Log.Error("No compatible hunt nodes with verified positions were found.");
            State = PathfinderState.NoCompatibleNodes;
            return Task.FromResult(new List<PathfinderStep>());
        }

        PrintPath(steps);
        State = PathfinderState.PathfindingDone;
        return Task.FromResult(steps);

        bool StateBeforePathfindingWasFileLoaded()
        {
            // State has already changed to Pathfinding above; usable graph data
            // is the reliable discriminator for hybrid routing.
            return HasUsablePrecomputedData(data);
        }
    }

    protected (float Cost, List<PathfinderStep> Steps) GetBestSteps(uint fromId, uint toId)
    {
        var bestCost = float.MaxValue;
        List<PathfinderStep> bestSteps = [];

        if (data.NodeToNodeDistances.TryGetValue(fromId, out var directList))
        {
            var direct = directList.FirstOrDefault(x => x.Id == toId);
            if (direct.Id == toId)
            {
                bestCost = direct.Distance;
                bestSteps = [PathfinderStep.WalkToDestination(toId)];
            }
        }

        if (data.NodeToAethernetDistances.TryGetValue(fromId, out var fromAethernetDistances)
            && fromAethernetDistances.Count > 0)
        {
            var fromShard = fromAethernetDistances.OrderBy(x => x.Distance).First();
            foreach (var (aethernet, list) in data.AethernetToNodeDistances)
            {
                var to = list.FirstOrDefault(x => x.Id == toId);
                if (to.Id != toId)
                {
                    continue;
                }

                var cost = fromShard.Distance + teleportCost + to.Distance;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestSteps =
                    [
                        PathfinderStep.WalkToAethernet(fromShard.Aethernet),
                        PathfinderStep.TeleportToAethernet(aethernet),
                        PathfinderStep.WalkToDestination(toId),
                    ];
                }
            }
        }

        foreach (var (aethernet, list) in data.AethernetToNodeDistances)
        {
            var to = list.FirstOrDefault(x => x.Id == toId);
            if (to.Id != toId)
            {
                continue;
            }

            var cost = returnCost + teleportCost + to.Distance;
            if (cost < bestCost)
            {
                bestCost = cost;
                if (aethernet == ZoneData.GetBaseCampAethernet())
                {
                    bestSteps =
                    [
                        PathfinderStep.ReturnToBaseCamp(),
                        PathfinderStep.WalkToDestination(toId),
                    ];
                }
                else
                {
                    bestSteps =
                    [
                        PathfinderStep.ReturnToBaseCamp(),
                        PathfinderStep.TeleportToAethernet(aethernet),
                        PathfinderStep.WalkToDestination(toId),
                    ];
                }
            }
        }

        return (bestCost, bestSteps);
    }

    protected void LoadFile(string filename)
    {
        State = PathfinderState.LoadingFile;
        var options = new JsonSerializerOptions
        {
            IncludeFields = false,
        };

        var file = Path.Join(ZoneData.GetCurrentZoneDataDirectory(), filename);
        if (!File.Exists(file))
        {
            UseFallbackOrUnavailable($"Precomputed hunt data file was not found: {file}");
            return;
        }

        try
        {
            var json = File.ReadAllText(file);
            var loaded = JsonSerializer.Deserialize<NodeDataSchema>(json, options);
            loaded.NodePositions ??= [];
            loaded.NodeToNodeDistances ??= [];
            loaded.NodeToAethernetDistances ??= [];
            loaded.AethernetToNodeDistances ??= [];
            var currentAethernets = ZoneData.GetCurrentAethernets().ToHashSet();
            if (loaded.AethernetToNodeDistances.Keys.Any(aethernet => !currentAethernets.Contains(aethernet)))
            {
                UseFallbackOrUnavailable($"Precomputed hunt data belongs to a different territory: {file}");
                return;
            }

            RecoverNodePositions(ref loaded);
            foreach (var (nodeId, position) in knownNodePositions)
            {
                loaded.NodePositions[nodeId] = BOCCHI.Modules.Data.Position.Create(position);
            }

            foreach (var (nodeId, position) in loaded.NodePositions)
            {
                var vector = new Vector3(position.X, position.Y, position.Z);
                if (IsFinite(vector))
                {
                    knownNodePositions.TryAdd(nodeId, vector);
                }
            }

            data = loaded;
            if (HasUsablePrecomputedData(loaded))
            {
                State = PathfinderState.FileLoaded;
            }
            else
            {
                UseFallbackOrUnavailable($"Precomputed hunt data file has no usable route segments: {file}");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, $"Failed to load precomputed hunt data: {file}");
            UseFallbackOrUnavailable($"Precomputed hunt data could not be loaded: {file}");
        }
    }

    private void UseFallbackOrUnavailable(string message)
    {
        if (knownNodePositions.Count > 0)
        {
            Svc.Log.Warning($"{message} Using Euclidean fallback ordering for {knownNodePositions.Count} known nodes; vnavmesh will calculate each walking segment.");
            State = PathfinderState.FallbackReady;
            return;
        }

        Svc.Log.Error($"{message} No runtime nodes are currently known.");
        State = PathfinderState.FileUnavailable;
    }

    private static void RecoverNodePositions(ref NodeDataSchema loaded)
    {
        foreach (var (fromId, destinations) in loaded.NodeToNodeDistances)
        {
            foreach (var destination in destinations.Where(destination => destination.Path is { Count: > 0 }))
            {
                var first = destination.Path[0];
                var last = destination.Path[^1];
                loaded.NodePositions.TryAdd(fromId, first);
                loaded.NodePositions.TryAdd(destination.Id, last);
            }
        }

        foreach (var destinations in loaded.AethernetToNodeDistances.Values)
        {
            foreach (var destination in destinations.Where(destination => destination.Path is { Count: > 0 }))
            {
                loaded.NodePositions.TryAdd(destination.Id, destination.Path[^1]);
            }
        }
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }

    private static string FormatNodeIds(IEnumerable<uint> nodeIds)
    {
        const int limit = 10;
        var ids = nodeIds.Take(limit).ToList();
        var suffix = nodeIds.Skip(limit).Any() ? ", ..." : string.Empty;
        return string.Join(", ", ids) + suffix;
    }

    private static bool HasUsablePrecomputedData(NodeDataSchema schema)
    {
        var hasDirectRoute = schema.NodeToNodeDistances.Values
            .SelectMany(routes => routes)
            .Any(route => IsUsableDistance(route.Distance));
        var hasRouteToShard = schema.NodeToAethernetDistances.Values
            .SelectMany(routes => routes)
            .Any(route => IsUsableDistance(route.Distance));
        var hasRouteFromShard = schema.AethernetToNodeDistances.Values
            .SelectMany(routes => routes)
            .Any(route => IsUsableDistance(route.Distance));

        return hasDirectRoute || (hasRouteToShard && hasRouteFromShard);
    }

    private static bool IsUsableDistance(float distance)
    {
        return float.IsFinite(distance) && distance >= 0f && distance < float.MaxValue;
    }

    protected Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> BuildCostGraph(List<uint> nodes)
    {
        var graph = new Dictionary<uint, Dictionary<uint, (float, List<PathfinderStep>)>>();

        foreach (var from in nodes)
        {
            graph[from] = new Dictionary<uint, (float, List<PathfinderStep>)>();
            foreach (var to in nodes)
            {
                if (from == to)
                {
                    continue;
                }

                var result = GetBestSteps(from, to);
                graph[from][to] = result;
            }
        }

        return graph;
    }

    protected List<uint> SolveTSPNearestInsertion(
        uint start,
        List<uint> nodes,
        Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        if (nodes.Count == 1)
        {
            return [start];
        }


        var route = new List<uint> { start };
        var unvisited = new HashSet<uint>(nodes.Where(n => n != start));
        while (unvisited.Count > 0)
        {
            var bestInsertionCost = float.MaxValue;
            var bestIndex = -1;
            uint bestNode = 0;

            foreach (var nodeToInsert in unvisited)
            {
                for (var i = 0; i < route.Count; i++)
                {
                    var from = route[i];
                    uint to;

                    if (i == route.Count - 1)
                    {
                        var costAtEnd = graph[from][nodeToInsert].Cost;
                        if (costAtEnd < bestInsertionCost)
                        {
                            bestInsertionCost = costAtEnd;
                            bestIndex = route.Count;
                            bestNode = nodeToInsert;
                        }
                    }
                    else
                    {
                        to = route[i + 1];

                        var addedCost = graph[from][nodeToInsert].Cost + graph[nodeToInsert][to].Cost
                                        - graph[from][to].Cost;

                        if (addedCost < bestInsertionCost)
                        {
                            bestInsertionCost = addedCost;
                            bestIndex = i + 1;
                            bestNode = nodeToInsert;
                        }
                    }
                }
            }

            if (bestIndex != -1)
            {
                route.Insert(bestIndex, bestNode);
                unvisited.Remove(bestNode);
            }
            else
            {
                Svc.Log.Error($"Could not find best insertion point for {unvisited.First()}");
                break;
            }
        }

        return route;
    }

    protected List<PathfinderStep> BuildStepPath(List<uint> orderedNodes, Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        List<PathfinderStep> steps = [];

        for (var i = 0; i < orderedNodes.Count - 1; i++)
        {
            var from = orderedNodes[i];
            var to = orderedNodes[i + 1];
            if (graph.ContainsKey(from) && graph[from].ContainsKey(to))
            {
                steps.AddRange(graph[from][to].Steps);
            }
            else
            {
                Svc.Log.Error($"Graph missing path from {from} to {to}. This should not happen if TSP is correct.");
            }
        }

        return steps;
    }

    protected void PrintPath(List<PathfinderStep> steps)
    {
        Svc.Log.Info("== Pathfinder Steps ==");

        var index = 1;
        var treasureCount = 0;

        foreach (var step in steps)
        {
            var message = step.Type switch
            {
                PathfinderStepType.WalkToNode => $"[{index}] Walk to Destination: {step.NodeId}",
                PathfinderStepType.WalkToAethernet => $"[{index}] Walk to Aethernet: {step.Aethernet}",
                PathfinderStepType.TeleportToAethernet => $"[{index}] Teleport to Aethernet: {step.Aethernet}",
                PathfinderStepType.ReturnToBaseCamp => $"[{index}] Return to Base Camp",
                _ => $"[{index}] Unknown Step Type",
            };

            if (step.Type == PathfinderStepType.WalkToNode)
            {
                treasureCount++;
            }

            Svc.Log.Info(message);
            index++;
        }

        Svc.Log.Info($"== Total treasures visited: {treasureCount} ==");
        Svc.Log.Info("=======================");
    }
}
