using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Pathfinding;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BOCCHI.Modules.Automator;

public enum NavigationType
{
    Walk,
    ReturnWalk,
    ReturnTeleportWalk,
    WalkTeleportWalk,
}

public readonly record struct NavigationCandidate(
    NavigationType Type,
    Aethernet SourceAethernet,
    Aethernet DestinationAethernet,
    float Cost);

public sealed record NavigationPlan(
    NavigationType Type,
    Aethernet SourceAethernet,
    Aethernet DestinationAethernet,
    float Cost,
    Vector3 PlannedFrom,
    Vector3 Destination,
    bool UsedFallback,
    string? FallbackReason,
    IReadOnlyList<NavigationCandidate> Candidates)
{
    public bool IsStale(Vector3 currentPosition, Vector3 currentDestination, float movementThreshold = 100f)
    {
        return Vector3.DistanceSquared(PlannedFrom, currentPosition) > movementThreshold * movementThreshold
               || Vector3.DistanceSquared(Destination, currentDestination) > 1f;
    }
}

public static class SmartNavigation
{
    public const int DestinationCandidateCount = 3;
    public const int SourceCandidateCount = 2;

    private readonly record struct PathSegment(Vector3 Start, Vector3 Destination);

    public static Task<NavigationPlan> DecideAsync(
        VNavmesh vnav,
        Vector3 playerPosition,
        Vector3 destination,
        EventData eventData,
        float returnCost,
        float teleportCost,
        Action<string>? failure = null,
        CancellationToken cancellationToken = default)
    {
        var aethernets = AethernetData.All().ToArray();
        var baseCamp = ZoneData.GetBaseCampAethernet().GetData();

        return DecideAsync(
            playerPosition,
            destination,
            eventData,
            aethernets,
            baseCamp,
            (start, end, token) => Task.Run(
                () => vnav.PathfindCancelable(start, end, false, token),
                token),
            returnCost,
            teleportCost,
            failure: failure,
            cancellationToken: cancellationToken);
    }

    public static async Task<NavigationPlan> DecideAsync(
        Vector3 playerPosition,
        Vector3 destination,
        EventData eventData,
        IReadOnlyCollection<AethernetData> aethernets,
        AethernetData baseCamp,
        Func<Vector3, Vector3, CancellationToken, Task<List<Vector3>>> pathfind,
        float returnCost,
        float teleportCost,
        int destinationCandidateCount = DestinationCandidateCount,
        int sourceCandidateCount = SourceCandidateCount,
        TimeSpan? segmentTimeout = null,
        Action<string>? failure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aethernets);
        ArgumentNullException.ThrowIfNull(baseCamp);
        ArgumentNullException.ThrowIfNull(pathfind);

        if (destinationCandidateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationCandidateCount));
        }

        if (sourceCandidateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceCandidateCount));
        }

        var timeout = segmentTimeout ?? TimeSpan.FromSeconds(8);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentTimeout));
        }

        var shards = aethernets
            .Where(shard => shard.Aethernet != Aethernet.Unknown)
            .GroupBy(shard => shard.Aethernet)
            .Select(group => group.First())
            .ToArray();

        if (shards.Length == 0)
        {
            return CreateDirectFallback(playerPosition, destination, "No aethernet data is available for the current territory.");
        }

        var preferred = eventData.Aethernet is { } preferredAethernet
            ? shards.FirstOrDefault(shard => shard.Aethernet == preferredAethernet)
            : null;

        var destinationShards = Enumerable.Empty<AethernetData>()
            .Concat(preferred == null ? [] : [preferred])
            .Concat(shards.OrderBy(shard => Vector3.DistanceSquared(shard.Destination, destination)))
            .GroupBy(shard => shard.Aethernet)
            .Select(group => group.First())
            .Take(destinationCandidateCount)
            .ToArray();

        var sourceShards = shards
            .OrderBy(shard => Vector3.DistanceSquared(playerPosition, shard.Position))
            .Take(sourceCandidateCount)
            .ToArray();

        var segmentDistances = new Dictionary<PathSegment, float?>();

        async Task<float?> GetDistanceAsync(Vector3 start, Vector3 end, bool allowKnownTransit)
        {
            if (allowKnownTransit && TryCalculateKnownTransitDistance(eventData, start, end, out var knownDistance))
            {
                return knownDistance;
            }

            var segment = new PathSegment(start, end);
            if (segmentDistances.TryGetValue(segment, out var cachedDistance))
            {
                return cachedDistance;
            }

            // vnavmesh owns one pathfinding worker. Running candidate probes in
            // parallel causes "Pathfinding task is in progress" and can tear
            // down the live movement request. Measure every candidate strictly
            // in sequence and cache duplicate segments.
            var distance = await MeasurePathAsync(
                start,
                end,
                pathfind,
                timeout,
                failure,
                cancellationToken).ConfigureAwait(false);
            segmentDistances[segment] = distance;
            return distance;
        }

        var directDistance = await GetDistanceAsync(playerPosition, destination, true).ConfigureAwait(false);
        var baseDistance = await GetDistanceAsync(baseCamp.Destination, destination, true).ConfigureAwait(false);
        var destinationDistances = new Dictionary<Aethernet, float?>();
        foreach (var shard in destinationShards)
        {
            destinationDistances[shard.Aethernet] = await GetDistanceAsync(
                shard.Destination,
                destination,
                true).ConfigureAwait(false);
        }

        var sourceDistances = new Dictionary<Aethernet, float?>();
        foreach (var shard in sourceShards)
        {
            sourceDistances[shard.Aethernet] = await GetDistanceAsync(
                playerPosition,
                shard.Position,
                false).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<NavigationCandidate>();
        AddCandidateIfReachable(
            candidates,
            NavigationType.Walk,
            Aethernet.Unknown,
            Aethernet.Unknown,
            directDistance);
        AddCandidateIfReachable(
            candidates,
            NavigationType.ReturnWalk,
            Aethernet.Unknown,
            baseCamp.Aethernet,
            AddCost(baseDistance, returnCost));

        foreach (var destinationShard in destinationShards)
        {
            var destinationDistance = destinationDistances[destinationShard.Aethernet];
            if (destinationShard.Aethernet != baseCamp.Aethernet)
            {
                AddCandidateIfReachable(
                    candidates,
                    NavigationType.ReturnTeleportWalk,
                    baseCamp.Aethernet,
                    destinationShard.Aethernet,
                    AddCost(destinationDistance, returnCost + teleportCost));
            }

            foreach (var sourceShard in sourceShards.Where(source => source.Aethernet != destinationShard.Aethernet))
            {
                var sourceDistance = sourceDistances[sourceShard.Aethernet];
                AddCandidateIfReachable(
                    candidates,
                    NavigationType.WalkTeleportWalk,
                    sourceShard.Aethernet,
                    destinationShard.Aethernet,
                    AddCosts(sourceDistance, destinationDistance, teleportCost));
            }
        }

        if (candidates.Count == 0)
        {
            return DecideFallback(
                playerPosition,
                destination,
                eventData,
                shards,
                baseCamp,
                returnCost,
                teleportCost,
                "vnavmesh did not return a usable path for any candidate route.");
        }

        var best = candidates
            .OrderBy(candidate => candidate.Cost)
            .ThenBy(candidate => candidate.Type)
            .First();

        return new NavigationPlan(
            best.Type,
            best.SourceAethernet,
            best.DestinationAethernet,
            best.Cost,
            playerPosition,
            destination,
            false,
            null,
            candidates.OrderBy(candidate => candidate.Cost).ToArray());
    }

    public static NavigationPlan DecideFallback(
        Vector3 playerPosition,
        Vector3 destination,
        EventData eventData,
        IReadOnlyCollection<AethernetData> aethernets,
        AethernetData baseCamp,
        float returnCost,
        float teleportCost,
        string reason,
        bool includeWalkTeleportCandidate = true)
    {
        return DecideFallback(
            playerPosition,
            destination,
            eventData.Aethernet,
            aethernets,
            baseCamp,
            returnCost,
            teleportCost,
            reason,
            includeWalkTeleportCandidate);
    }

    public static NavigationPlan DecideFallback(
        Vector3 playerPosition,
        Vector3 destination,
        Aethernet? preferredAethernet,
        IReadOnlyCollection<AethernetData> aethernets,
        AethernetData baseCamp,
        float returnCost,
        float teleportCost,
        string reason,
        bool includeWalkTeleportCandidate = true)
    {
        var shards = aethernets
            .Where(shard => shard.Aethernet != Aethernet.Unknown)
            .GroupBy(shard => shard.Aethernet)
            .Select(group => group.First())
            .ToArray();

        if (shards.Length == 0)
        {
            return CreateDirectFallback(playerPosition, destination, reason);
        }

        var source = shards.OrderBy(shard => Vector3.DistanceSquared(playerPosition, shard.Position)).First();
        var target = preferredAethernet is { } preferred
            ? shards.FirstOrDefault(shard => shard.Aethernet == preferred)
              ?? shards.OrderBy(shard => Vector3.DistanceSquared(shard.Destination, destination)).First()
            : shards.OrderBy(shard => Vector3.DistanceSquared(shard.Destination, destination)).First();

        var directDistance = Vector3.Distance(playerPosition, destination);
        var baseDistance = Vector3.Distance(baseCamp.Destination, destination);
        var targetDistance = Vector3.Distance(target.Destination, destination);
        var sourceDistance = Vector3.Distance(playerPosition, source.Position);
        var candidates = new List<NavigationCandidate>
        {
            new(NavigationType.Walk, Aethernet.Unknown, Aethernet.Unknown, directDistance),
            new(NavigationType.ReturnWalk, Aethernet.Unknown, baseCamp.Aethernet, returnCost + baseDistance),
        };

        if (target.Aethernet != baseCamp.Aethernet)
        {
            candidates.Add(new NavigationCandidate(
                NavigationType.ReturnTeleportWalk,
                baseCamp.Aethernet,
                target.Aethernet,
                returnCost + teleportCost + targetDistance));
        }

        if (includeWalkTeleportCandidate && source.Aethernet != target.Aethernet)
        {
            candidates.Add(new NavigationCandidate(
                NavigationType.WalkTeleportWalk,
                source.Aethernet,
                target.Aethernet,
                sourceDistance + teleportCost + targetDistance));
        }

        var best = candidates
            .OrderBy(candidate => candidate.Cost)
            .ThenBy(candidate => candidate.Type)
            .First();

        return new NavigationPlan(
            best.Type,
            best.SourceAethernet,
            best.DestinationAethernet,
            best.Cost,
            playerPosition,
            destination,
            true,
            reason,
            candidates.OrderBy(candidate => candidate.Cost).ToArray());
    }

    private static NavigationPlan CreateDirectFallback(Vector3 playerPosition, Vector3 destination, string reason)
    {
        var direct = new NavigationCandidate(
            NavigationType.Walk,
            Aethernet.Unknown,
            Aethernet.Unknown,
            Vector3.Distance(playerPosition, destination));

        return new NavigationPlan(
            direct.Type,
            direct.SourceAethernet,
            direct.DestinationAethernet,
            direct.Cost,
            playerPosition,
            destination,
            true,
            reason,
            [direct]);
    }

    private static async Task<float?> MeasurePathAsync(
        Vector3 start,
        Vector3 destination,
        Func<Vector3, Vector3, CancellationToken, Task<List<Vector3>>> pathfind,
        TimeSpan timeout,
        Action<string>? failure,
        CancellationToken cancellationToken)
    {
        if (Vector3.DistanceSquared(start, destination) <= 1f)
        {
            return Vector3.Distance(start, destination);
        }

        using var segmentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<List<Vector3>>? pathTask = null;
        try
        {
            pathTask = pathfind(start, destination, segmentCancellation.Token);
            var path = await pathTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (path == null || !HuntNavigationPlanner.ReachesDestination(path, destination))
            {
                failure?.Invoke($"No complete vnavmesh path from {start} to {destination}.");
                return null;
            }

            var length = CalculatePathLength(path);
            return float.IsFinite(length) ? length : null;
        }
        catch (TimeoutException)
        {
            segmentCancellation.Cancel();
            if (pathTask != null
                && !await WaitForCancellationAsync(pathTask, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                ObserveDetachedTask(pathTask);
                throw new InvalidOperationException(
                    $"vnavmesh did not cancel the timed-out path from {start} to {destination}; " +
                    "candidate probing was stopped to avoid overlapping requests.");
            }

            failure?.Invoke($"vnavmesh timed out after {timeout.TotalSeconds:F0}s from {start} to {destination}.");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            segmentCancellation.Cancel();
            ObserveDetachedTask(pathTask);
            throw;
        }
        catch (Exception ex)
        {
            failure?.Invoke($"vnavmesh failed from {start} to {destination}: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static bool TryCalculateKnownTransitDistance(
        EventData eventData,
        Vector3 start,
        Vector3 destination,
        out float distance)
    {
        distance = 0f;
        if (!NorthHornSouthCrossingRoute.TryCreate(eventData, start, out var route) || route.Count == 0)
        {
            return false;
        }

        var previous = start;
        foreach (var point in route)
        {
            distance += Vector3.Distance(previous, point);
            previous = point;
        }

        distance += Vector3.Distance(previous, destination);
        return float.IsFinite(distance);
    }

    private static async Task<bool> WaitForCancellationAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch
        {
            // Cancellation and faults both mean vnavmesh released this request,
            // so it is safe for the sequential planner to submit the next one.
            return true;
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

    private static float? AddCost(float? distance, float fixedCost)
    {
        return distance.HasValue ? distance.Value + fixedCost : null;
    }

    private static float? AddCosts(float? first, float? second, float fixedCost)
    {
        return first.HasValue && second.HasValue
            ? first.Value + second.Value + fixedCost
            : null;
    }

    private static void AddCandidateIfReachable(
        ICollection<NavigationCandidate> candidates,
        NavigationType type,
        Aethernet source,
        Aethernet destination,
        float? cost)
    {
        if (cost is { } finiteCost && float.IsFinite(finiteCost))
        {
            candidates.Add(new NavigationCandidate(type, source, destination, finiteCost));
        }
    }

    private static void ObserveDetachedTask(Task? task)
    {
        if (task == null)
        {
            return;
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
