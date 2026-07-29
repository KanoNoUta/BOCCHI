using BOCCHI.Enums;
using BOCCHI.Modules.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BOCCHI.Pathfinding;

public readonly record struct HuntNode(uint Id, Vector3 Position);

public readonly record struct HuntAethernet(
    Aethernet Id,
    Vector3 ArrivalPosition,
    Vector3 ShardPosition);

public static class NodeDataPrecomputer
{
    public static int GetTaskCount(int nodeCount, int aethernetCount)
    {
        return checked(nodeCount * (nodeCount - 1 + 2 * aethernetCount));
    }

    public static Task<NodeDataSchema> ComputeAsync(
        IReadOnlyCollection<HuntNode> sourceNodes,
        IReadOnlyCollection<HuntAethernet> aethernets,
        Func<Vector3, Vector3, Task<List<Vector3>>> pathfind,
        Action<int>? progress = null,
        Action<string>? failure = null,
        TimeSpan? segmentTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathfind);
        return ComputeCoreAsync(
            sourceNodes,
            aethernets,
            (start, destination, _) => pathfind(start, destination),
            progress,
            failure,
            segmentTimeout,
            cancellationToken);
    }

    public static Task<NodeDataSchema> ComputeAsync(
        IReadOnlyCollection<HuntNode> sourceNodes,
        IReadOnlyCollection<HuntAethernet> aethernets,
        Func<Vector3, Vector3, CancellationToken, List<Vector3>> pathfind,
        Action<int>? progress = null,
        Action<string>? failure = null,
        TimeSpan? segmentTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathfind);
        return ComputeCoreAsync(
            sourceNodes,
            aethernets,
            (start, destination, segmentToken) => Task.Run(
                () => pathfind(start, destination, segmentToken),
                segmentToken),
            progress,
            failure,
            segmentTimeout,
            cancellationToken);
    }

    private static async Task<NodeDataSchema> ComputeCoreAsync(
        IReadOnlyCollection<HuntNode> sourceNodes,
        IReadOnlyCollection<HuntAethernet> aethernets,
        Func<Vector3, Vector3, CancellationToken, Task<List<Vector3>>> pathfind,
        Action<int>? progress,
        Action<string>? failure,
        TimeSpan? segmentTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceNodes);
        ArgumentNullException.ThrowIfNull(aethernets);
        ArgumentNullException.ThrowIfNull(pathfind);

        var duplicateNode = sourceNodes
            .GroupBy(node => node.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateNode != null)
        {
            throw new ArgumentException($"Duplicate hunt node ID {duplicateNode.Key}.", nameof(sourceNodes));
        }

        var nodes = sourceNodes.OrderBy(node => node.Id).ToList();
        var shards = aethernets.OrderBy(aethernet => aethernet.Id).ToList();
        var data = new NodeDataSchema();
        var completed = 0;
        var effectiveSegmentTimeout = segmentTimeout ?? TimeSpan.FromSeconds(30);
        if (effectiveSegmentTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentTimeout));
        }

        foreach (var node in nodes)
        {
            data.NodePositions[node.Id] = Position.Create(node.Position);
            data.NodeToNodeDistances[node.Id] = [];
            data.NodeToAethernetDistances[node.Id] = [];
        }

        foreach (var aethernet in shards)
        {
            data.AethernetToNodeDistances[aethernet.Id] = [];
        }

        foreach (var node in nodes)
        {
            foreach (var other in nodes.Where(other => other.Id != node.Id))
            {
                var route = await CalculateRouteAsync(
                    node.Position,
                    other.Position,
                    $"node {node.Id} -> node {other.Id}").ConfigureAwait(false);
                if (route != null)
                {
                    data.NodeToNodeDistances[node.Id].Add(new ToNode(other.Id, route.Value.Distance, route.Value.Path));
                }
            }

            foreach (var aethernet in shards)
            {
                var fromAethernet = await CalculateRouteAsync(
                    aethernet.ArrivalPosition,
                    node.Position,
                    $"aethernet {aethernet.Id} -> node {node.Id}").ConfigureAwait(false);
                if (fromAethernet != null)
                {
                    data.AethernetToNodeDistances[aethernet.Id].Add(
                        new ToNode(node.Id, fromAethernet.Value.Distance, fromAethernet.Value.Path));
                }

                var toAethernet = await CalculateRouteAsync(
                    node.Position,
                    aethernet.ShardPosition,
                    $"node {node.Id} -> aethernet {aethernet.Id}").ConfigureAwait(false);
                if (toAethernet != null)
                {
                    data.NodeToAethernetDistances[node.Id].Add(
                        new ToAethernet(aethernet.Id, toAethernet.Value.Distance, toAethernet.Value.Path));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return data;

        async Task<(float Distance, List<Position> Path)?> CalculateRouteAsync(
            Vector3 start,
            Vector3 destination,
            string label)
        {
            CancellationTokenSource? segmentCancellation = null;
            Task<List<Vector3>>? pathTask = null;
            var taskOwnershipTransferred = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                segmentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                pathTask = pathfind(start, destination, segmentCancellation.Token);
                List<Vector3> path;
                try
                {
                    path = await pathTask.WaitAsync(effectiveSegmentTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    segmentCancellation.Cancel();
                    ObserveDetachedTask(pathTask, segmentCancellation);
                    taskOwnershipTransferred = true;
                    cancellationToken.ThrowIfCancellationRequested();
                    failure?.Invoke(
                        $"vnavmesh timed out after {effectiveSegmentTimeout.TotalSeconds:F0}s for {label}; " +
                        "the segment was omitted.");
                    return null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    segmentCancellation.Cancel();
                    ObserveDetachedTask(pathTask, segmentCancellation);
                    taskOwnershipTransferred = true;
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (path == null || path.Count <= 1)
                {
                    failure?.Invoke($"No vnavmesh path for {label}; the segment was omitted.");
                    return null;
                }

                return (CalculatePathLength(path), path.Select(Position.Create).ToList());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failure?.Invoke($"vnavmesh failed for {label}: {ex.GetBaseException().Message}");
                return null;
            }
            finally
            {
                if (!taskOwnershipTransferred)
                {
                    segmentCancellation?.Dispose();
                }

                progress?.Invoke(++completed);
            }
        }
    }

    private static void ObserveDetachedTask(Task task, CancellationTokenSource cancellation)
    {
        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }

                cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static async Task WriteAtomicAsync(
        string outputFile,
        NodeDataSchema data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);

        var directory = Path.GetDirectoryName(outputFile)
                        ?? throw new ArgumentException("The output path must include a directory.", nameof(outputFile));
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            IncludeFields = false,
        };
        var json = JsonSerializer.Serialize(data, options);
        var temporaryFile = Path.Join(directory, $".{Path.GetFileName(outputFile)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryFile, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryFile, outputFile, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    private static float CalculatePathLength(IReadOnlyList<Vector3> path)
    {
        var length = 0f;
        for (var i = 1; i < path.Count; i++)
        {
            length += Vector3.Distance(path[i - 1], path[i]);
        }

        return length;
    }
}
