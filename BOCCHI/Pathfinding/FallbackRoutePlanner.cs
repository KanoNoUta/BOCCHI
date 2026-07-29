using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Pathfinding;

public static class FallbackRoutePlanner
{
    public static List<uint> OrderByEuclideanCost(
        Vector3 start,
        IEnumerable<uint> nodeIds,
        IReadOnlyDictionary<uint, Vector3> nodePositions)
    {
        var remaining = nodeIds
            .Distinct()
            .Where(nodePositions.ContainsKey)
            .ToHashSet();
        var ordered = new List<uint>(remaining.Count);
        var current = start;

        while (remaining.Count > 0)
        {
            var next = remaining
                .OrderBy(nodeId => Vector3.DistanceSquared(current, nodePositions[nodeId]))
                .ThenBy(nodeId => nodeId)
                .First();

            ordered.Add(next);
            current = nodePositions[next];
            remaining.Remove(next);
        }

        return ordered;
    }
}

public static class HybridRoutePlanner
{
    // Exact longest-simple-path search is exponential. Active hunt sets up to
    // this size use memoized DFS; larger sets use deterministic reachability
    // look-ahead so a cheap terminal edge is not selected first.
    private const int ExactSearchNodeLimit = 15;

    public static bool HasCompleteFiniteGraph(
        IReadOnlyCollection<uint> nodes,
        IReadOnlyDictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        return nodes.All(from => nodes.All(to =>
            from == to
            || graph.TryGetValue(from, out var edges)
            && edges.TryGetValue(to, out var edge)
            && IsUsable(edge)));
    }

    public static List<uint> BuildReachableGreedyRoute(
        uint start,
        IReadOnlyCollection<uint> nodes,
        IReadOnlyDictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        var candidateNodes = nodes.Distinct().ToHashSet();
        if (!candidateNodes.Contains(start))
        {
            return [];
        }

        var reachableNodes = GetReachableNodes(start, candidateNodes, graph);
        if (reachableNodes.Count <= ExactSearchNodeLimit)
        {
            return BuildMaximumCoverageRoute(start, reachableNodes, graph);
        }

        return BuildReachabilityAwareGreedyRoute(start, candidateNodes, graph);
    }

    private static List<uint> BuildMaximumCoverageRoute(
        uint start,
        IReadOnlyCollection<uint> reachableNodes,
        IReadOnlyDictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        var orderedNodes = reachableNodes.OrderBy(node => node).ToArray();
        var nodeIndexes = orderedNodes
            .Select((node, index) => (node, index))
            .ToDictionary(pair => pair.node, pair => pair.index);
        var memo = new Dictionary<(int CurrentIndex, ulong Visited), RouteCandidate>();

        return Search(nodeIndexes[start], 1UL << nodeIndexes[start]).Nodes;

        RouteCandidate Search(int currentIndex, ulong visited)
        {
            var key = (currentIndex, visited);
            if (memo.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var currentNode = orderedNodes[currentIndex];
            var best = new RouteCandidate([currentNode], 0d);
            if (graph.TryGetValue(currentNode, out var edges))
            {
                for (var nextIndex = 0; nextIndex < orderedNodes.Length; nextIndex++)
                {
                    var bit = 1UL << nextIndex;
                    if ((visited & bit) != 0)
                    {
                        continue;
                    }

                    var nextNode = orderedNodes[nextIndex];
                    if (!edges.TryGetValue(nextNode, out var edge) || !IsUsable(edge))
                    {
                        continue;
                    }

                    var suffix = Search(nextIndex, visited | bit);
                    var route = new List<uint>(suffix.Nodes.Count + 1) { currentNode };
                    route.AddRange(suffix.Nodes);
                    var candidate = new RouteCandidate(route, edge.Cost + suffix.TotalCost);
                    if (IsBetter(candidate, best))
                    {
                        best = candidate;
                    }
                }
            }

            memo[key] = best;
            return best;
        }
    }

    private static List<uint> BuildReachabilityAwareGreedyRoute(
        uint start,
        IReadOnlyCollection<uint> nodes,
        IReadOnlyDictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        var route = new List<uint> { start };
        var remaining = nodes.Where(node => node != start).ToHashSet();
        var current = start;

        while (remaining.Count > 0 && graph.TryGetValue(current, out var edges))
        {
            var next = remaining
                .Where(node => edges.TryGetValue(node, out var edge) && IsUsable(edge))
                .OrderByDescending(node => CountReachableNodes(node, remaining, graph))
                .ThenBy(node => edges[node].Cost)
                .ThenBy(node => node)
                .Cast<uint?>()
                .FirstOrDefault();
            if (!next.HasValue)
            {
                break;
            }

            current = next.Value;
            route.Add(current);
            remaining.Remove(current);
        }

        return route;
    }

    private static HashSet<uint> GetReachableNodes(
        uint start,
        IReadOnlySet<uint> candidates,
        IReadOnlyDictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        var reachable = new HashSet<uint> { start };
        var pending = new Queue<uint>();
        pending.Enqueue(start);

        while (pending.TryDequeue(out var current))
        {
            if (!graph.TryGetValue(current, out var edges))
            {
                continue;
            }

            foreach (var next in candidates.OrderBy(node => node))
            {
                if (!reachable.Contains(next)
                    && edges.TryGetValue(next, out var edge)
                    && IsUsable(edge))
                {
                    reachable.Add(next);
                    pending.Enqueue(next);
                }
            }
        }

        return reachable;
    }

    private static int CountReachableNodes(
        uint start,
        IReadOnlySet<uint> candidates,
        IReadOnlyDictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        return GetReachableNodes(start, candidates, graph).Count;
    }

    private static bool IsBetter(RouteCandidate candidate, RouteCandidate currentBest)
    {
        if (candidate.Nodes.Count != currentBest.Nodes.Count)
        {
            return candidate.Nodes.Count > currentBest.Nodes.Count;
        }

        var costComparison = candidate.TotalCost.CompareTo(currentBest.TotalCost);
        if (costComparison != 0)
        {
            return costComparison < 0;
        }

        for (var i = 0; i < candidate.Nodes.Count; i++)
        {
            if (candidate.Nodes[i] != currentBest.Nodes[i])
            {
                return candidate.Nodes[i] < currentBest.Nodes[i];
            }
        }

        return false;
    }

    private static bool IsUsable((float Cost, List<PathfinderStep> Steps) edge)
    {
        return float.IsFinite(edge.Cost)
               && edge.Cost >= 0f
               && edge.Cost < float.MaxValue
               && edge.Steps.Count > 0;
    }

    private sealed record RouteCandidate(List<uint> Nodes, double TotalCost);
}

public static class RuntimeNodeId
{
    public static uint FromPosition(Vector3 position)
    {
        if (!IsFinite(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Runtime node positions must be finite.");
        }

        // Carrot EventObj IDs are session-bound. A fixed FNV-1a hash of the
        // actual, decimetre-quantized world position stays stable across
        // sessions without inventing or persisting guessed North Horn data.
        var x = checked((int)MathF.Round(position.X * 10f, MidpointRounding.AwayFromZero));
        var y = checked((int)MathF.Round(position.Y * 10f, MidpointRounding.AwayFromZero));
        var z = checked((int)MathF.Round(position.Z * 10f, MidpointRounding.AwayFromZero));

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        Add(x);
        Add(y);
        Add(z);

        // Keep runtime IDs disjoint from the hand-authored South Horn IDs.
        return hash | 0x80000000u;

        void Add(int value)
        {
            unchecked
            {
                for (var i = 0; i < sizeof(int); i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= prime;
                }
            }
        }
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }
}
