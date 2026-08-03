using BOCCHI.Chains;
using BOCCHI.Enums;
using BOCCHI.Data;
using BOCCHI.Modules;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Pathfinder;
using BOCCHI.Modules.StateManager;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot;
using Ocelot.Chain;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TextCopy;

namespace BOCCHI.Pathfinding;

public static class HuntRoutePriorityPolicy
{
    public static bool PromoteOrInsert(
        List<PathfinderStep> steps,
        int stepIndex,
        uint priorityNodeId)
    {
        if (priorityNodeId == 0
            || stepIndex < 0
            || stepIndex >= steps.Count
            || steps[stepIndex].Type != PathfinderStepType.WalkToNode
            || steps[stepIndex].NodeId == priorityNodeId)
        {
            return false;
        }

        var futureIndex = steps.FindIndex(
            stepIndex + 1,
            step => step.Type == PathfinderStepType.WalkToNode
                    && step.NodeId == priorityNodeId);
        var priorityStep = futureIndex >= 0
            ? steps[futureIndex]
            : PathfinderStep.WalkToDestination(priorityNodeId);
        if (futureIndex >= 0)
        {
            steps.RemoveAt(futureIndex);
        }

        steps.Insert(stepIndex, priorityStep);
        return true;
    }

    public static bool TryRemoveCurrentDiversion(
        List<PathfinderStep> steps,
        int stepIndex,
        uint? priorityDiversionNodeId)
    {
        if (priorityDiversionNodeId == null
            || stepIndex < 0
            || stepIndex >= steps.Count
            || steps[stepIndex].Type != PathfinderStepType.WalkToNode
            || steps[stepIndex].NodeId != priorityDiversionNodeId.Value)
        {
            return false;
        }

        steps.RemoveAt(stepIndex);
        return true;
    }
}

public abstract class Hunter
{
    protected Module m;
    protected const float DISTANCE_TO_NODE_TO_USE = 2f;

    private const float NODE_ABSENCE_CONFIRMATION_DISTANCE = 4f;

    private const int MAX_NODE_PATH_ATTEMPTS = 3;

    private const int MAX_NODE_INTERACTION_ATTEMPTS = 1;

    private const int MAX_NODE_RECOVERY_ROUTES = 3;

    private static readonly TimeSpan NodeProgressTimeout = TimeSpan.FromSeconds(30);

    private const int MAX_TRANSIT_ATTEMPTS = 3;

    private static readonly TimeSpan TransitProgressTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan TransitRetryDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CombatReturnTimeout = TimeSpan.FromSeconds(120);

    protected StateManagerModule states;

    protected VNavmesh vnav => m.GetIPCSubscriber<VNavmesh>();

    protected PathfinderConfig config;

    protected bool running;

    protected IPathfinder? pathfinder;

    protected List<PathfinderStep> Steps = [];

    protected int stepIndex = 0;

    protected float distance = 0f;

    protected Stopwatch stopwatch = new();

    private uint? navigationNodeId;

    private int nodePathAttempts;

    private float bestNodeDistance = float.MaxValue;

    private DateTime lastNodeProgress = DateTime.MinValue;

    private DateTime nodeFollowSubmittedAt = DateTime.MinValue;

    private readonly HashSet<uint> dynamicallyPlannedNodes = [];

    private readonly Dictionary<uint, HashSet<Aethernet>> recoveredNodeAethernets = [];

    private bool interactionPending;

    private uint? interactionNodeId;

    private int interactionStepIndex = -1;

    private int interactionAttempts;

    private long routeGeneration;

    private int transitStepIndex = -1;

    private int transitAttempts;

    private float bestTransitDistance = float.MaxValue;

    private DateTime transitStartedAt = DateTime.MinValue;

    private DateTime lastTransitProgress = DateTime.MinValue;

    private DateTime lastTransitAttempt = DateTime.MinValue;

    private int transitChildAttempts;

    private DateTime lastTransitChildAttempt = DateTime.MinValue;

    private bool transitChildPending;

    private string? transitChildFailureReason;

    private Chain? activeTransitChild;

    private uint? priorityDiversionNodeId;

    protected PathfinderStep CurrentStep
    {
        get => Steps[stepIndex];
    }

    protected string JSON = "";

    public bool IsRunning => running;

    public uint? ActiveNodeId => running
                                 && stepIndex < Steps.Count
                                 && CurrentStep.Type == PathfinderStepType.WalkToNode
        ? CurrentStep.NodeId
        : null;

    protected ChainQueue StepProcessor
    {
        get => ChainManager.Get(GetType().FullName ?? "Hunter");
    }

    protected Dictionary<PathfinderStepType, Func<bool>> Handlers;

    protected Hunter(Module module)
    {
        m = module;
        states = module.GetModule<StateManagerModule>();
        config = module.PluginConfig.PathfinderConfig;

        Handlers = new Dictionary<PathfinderStepType, Func<bool>>
        {
            { PathfinderStepType.WalkToNode, WalkToNodeHandler },
            { PathfinderStepType.ReturnToBaseCamp, ReturnToBaseCampHandler },
            { PathfinderStepType.WalkToAethernet, WalkToAethernetHandler },
            { PathfinderStepType.TeleportToAethernet, TeleportToAethernetHandler },
        };
    }

    protected abstract IEnumerable<IGameObject> GetValidObjects();

    protected virtual IGameObject? ResolveLiveObjectForNode(uint nodeId, Vector3 expectedPosition)
    {
        return GetValidObjects()
            .Where(candidate => Vector3.DistanceSquared(expectedPosition, candidate.Position) <= 25f)
            .OrderBy(candidate => Vector3.DistanceSquared(expectedPosition, candidate.Position))
            .FirstOrDefault();
    }

    protected abstract bool TryGetDestinationForCurrentStep(out Vector3 destination);

    protected float GetDetectionRange()
    {
        return config.DetectionRange;
    }

    protected abstract IPathfinder CreatePathfinder();

    protected abstract Func<Chain> GetInteractionChain(
        uint nodeId,
        Vector3 expectedPosition,
        ulong gameObjectId,
        Action<HuntInteractionOutcome> complete);

    protected abstract List<uint> GetValidNodes(int max);

    protected virtual void OnStarted()
    {
    }

    protected virtual bool ShouldStopEarly()
    {
        return false;
    }

    protected virtual bool ShouldRepeatAfterCompletion()
    {
        return false;
    }

    protected virtual uint? GetPriorityNode(
        uint currentNodeId,
        IReadOnlyCollection<uint> remainingNodeIds)
    {
        return null;
    }

    protected virtual IReadOnlyList<uint>? ReorderRemainingNodes(
        Vector3 start,
        IReadOnlyCollection<uint> remainingNodeIds)
    {
        return null;
    }

    protected virtual bool ShouldUseInitialTransit(uint nodeId)
    {
        return true;
    }

    protected virtual bool ShouldUseForcedRecovery(uint nodeId)
    {
        return true;
    }

    public void Update()
    {
        if (!running || Plugin.Chain.IsRunning)
        {
            return;
        }

        if (ShouldStopEarly())
        {
            Svc.Log.Info("Hunt completed because no requested targets remain.");
            Teardown();
            return;
        }

        if (pathfinder == null && Steps.Count <= 0)
        {
            pathfinder = CreatePathfinder();
        }

        MaintainWatcherChain();
    }

    private void MaintainWatcherChain()
    {
        if (Plugin.Chain.IsRunning)
        {
            return;
        }

        if (pathfinder?.State is PathfinderState.FileUnavailable or PathfinderState.NoCompatibleNodes)
        {
            Stop();
            return;
        }

        if (pathfinder != null && pathfinder.State != PathfinderState.PathfindingDone)
        {
            Plugin.Chain.Submit(() =>
            {
                Task<List<PathfinderStep>> steps = null!;
                var valid = GetValidNodes(config.MaxLevel);

                // Prep pathfinding
                return Chain.Create("Hunter.Pathfinding")
                .Then(new TaskManagerTask(() => pathfinder?.State is PathfinderState.FileLoaded or PathfinderState.FallbackReady))
                    .Then(_ => steps = pathfinder.FindPath(Player.Position, valid))
                    .Then(new TaskManagerTask(() => steps!.IsCompleted))
                    .Then(_ => Steps = steps!.Result)
                    .Then(_ =>
                    {
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Converters =
                            {
                                new PathfinderStepConverter(),
                            },
                        };

                        JSON = JsonSerializer.Serialize(Steps, options);
                    })
                    // Keep the completed pathfinder so runtime/file-derived node
                    // positions remain available while the route is executed.
                    .Then(_ => { });
            });

            return;
        }

        if (StepProcessor.IsRunning || StepProcessor.QueueCount > 0)
        {
            return;
        }

        if (stepIndex >= Steps.Count)
        {
            CompleteRoute();
            return;
        }

        TryPromotePriorityNode();

        StepProcessor.Submit(() =>
            Chain.Create("Hunter.Run")
                .Then(_ =>
                {
                    var handler = Handlers[CurrentStep.Type];
                    if (handler())
                    {
                        stepIndex++;
                    }
                })
                .Wait(1000 / 60)
        );

    }

    private void CompleteRoute()
    {
        // Capture this before Teardown clears the running flag. Repeating a
        // route still has to pass through the base cleanup so stale async
        // callbacks, interaction state, and runtime recovery decisions cannot
        // leak into the next cycle.
        var repeat = running && ShouldRepeatAfterCompletion();
        Teardown();
        if (!repeat)
        {
            return;
        }

        running = true;
        stopwatch.Start();
        OnStarted();
    }

    public virtual void Draw(Module<Plugin, Config> module)
    {
        OcelotUi.Title($"{module.T("panel.hunt.title")}:");
        OcelotUi.Indent(() =>
        {
            if (ImGui.Button(running ? I18N.T("generic.label.stop") : I18N.T("generic.label.start")))
            {
                if (running)
                {
                    Stop();
                }
                else
                {
                    Start();
                }
            }

            if (stopwatch.Elapsed > TimeSpan.Zero)
            {
                ImGui.SameLine();
                if (ImGui.Button(I18N.T("hunter.export.label")))
                {
                    ClipboardService.SetText(JSON);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(I18N.T("hunter.export.tooltip"));
                }

                OcelotUi.LabelledValue(I18N.T("hunter.elapsed"), $"{stopwatch.Elapsed:mm\\:ss}");
            }


            if (running && stepIndex < Steps.Count)
            {
                OcelotUi.LabelledValue(I18N.T("hunter.progress"), $"{stepIndex}/{Steps.Count}");

                if (CurrentStep.Type == PathfinderStepType.WalkToNode)
                {
                    OcelotUi.LabelledValue(module.T("panel.hunt.distance_node"), $"{distance:f2}/{GetDetectionRange():f2}");
                }

                if (CurrentStep.Type == PathfinderStepType.WalkToAethernet)
                {
                    OcelotUi.LabelledValue(I18N.T("hunter.distance_shard"), $"{distance:f2}");
                }
            }
        });
    }

    protected void ResetHunter(bool keepRunning)
    {
        routeGeneration++;
        if (!keepRunning)
        {
            stopwatch.Stop();
            running = false;
        }

        stepIndex = 0;
        Steps.Clear();
        AbortActiveTransitChild();
        AggroAvoidanceNavigation.Stop();
        if (m.TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null)
        {
            try
            {
                navigation.Stop();
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Hunter could not stop vnavmesh because its IPC became unavailable.");
            }
        }
        Plugin.Chain.Abort();
        StepProcessor.Abort();
        pathfinder = null;
        ResetNodeNavigation();
        ResetTransitNavigation();
        ResetHuntRouteState();
    }

    protected virtual void Teardown()
    {
        ResetHunter(keepRunning: false);
    }

    public void Start()
    {
        if (running)
        {
            return;
        }

        routeGeneration++;
        ResetHuntRouteState();
        running = true;
        stopwatch.Restart();
        OnStarted();
    }

    public void Stop()
    {
        if (!running && pathfinder == null && Steps.Count == 0)
        {
            return;
        }

        stopwatch.Stop();
        running = false;
        Teardown();
    }


    protected bool WalkToNodeHandler()
    {
        var nodeId = CurrentStep.NodeId;
        if (navigationNodeId != nodeId)
        {
            ResetNodeNavigation();
            navigationNodeId = nodeId;
            lastNodeProgress = DateTime.UtcNow;
        }

        if (interactionPending)
        {
            return false;
        }

        if (!TryGetDestinationForCurrentStep(out var destination))
        {
            return SkipCurrentNode("no position is known for this node");
        }

        var expectedPosition = destination;
        if (TryInsertDynamicTransit(nodeId, destination))
        {
            return false;
        }

        // Layout positions can differ from the live object by several yalms.
        // Once the object is loaded, navigate and range-check against its real
        // position instead of consuming the node at the layout coordinate.
        var obj = ResolveLiveObjectForNode(nodeId, expectedPosition);
        if (obj != null)
        {
            destination = obj.Position;
        }

        distance = Player.DistanceTo(destination);
        if (distance + 0.5f < bestNodeDistance)
        {
            bestNodeDistance = distance;
            lastNodeProgress = DateTime.UtcNow;
        }

        if (distance <= GetDetectionRange())
        {
            if (obj == null && distance <= NODE_ABSENCE_CONFIRMATION_DISTANCE)
            {
                vnav.Stop();
                ResetNodeNavigation();
                if (HuntRoutePriorityPolicy.TryRemoveCurrentDiversion(
                        Steps,
                        stepIndex,
                        priorityDiversionNodeId))
                {
                    priorityDiversionNodeId = null;
                    RejoinRemainingRoute();
                    return false;
                }

                return true;
            }

            if (obj != null && distance <= DISTANCE_TO_NODE_TO_USE)
            {
                if (vnav.IsRunning())
                {
                    vnav.Stop();
                }

                BeginNodeInteraction(nodeId, expectedPosition, obj);
                return false;
            }
        }

        // vnavmesh's cancelable Pathfind query returns a Task<List<Vector3>>.
        // Under the CN Dalamud the IPC layer serializes the return value, and a
        // Task result trips a Newtonsoft self-referencing-loop exception, which
        // faulted every node and made the route teleport-loop without ever
        // digging. SimpleMove (PathfindAndMoveTo) returns a bool and walks
        // internally, matching the CN-safe pattern already used for transits.
        if (NavigationActivityState.IsActive(
                vnav.IsRunning(),
                vnav.IsSimpleMoveInProgress(),
                vnav.IsPathfinding()))
        {
            if (DateTime.UtcNow - lastNodeProgress >= NodeProgressTimeout)
            {
                vnav.Stop();
                Svc.Log.Warning(
                    $"Hunt node {CurrentStep.NodeId} made no progress for " +
                    $"{NodeProgressTimeout.TotalSeconds:f0}s; recalculating the segment.");
                lastNodeProgress = DateTime.UtcNow;
            }

            return false;
        }

        // Give a freshly issued SimpleMove a moment to spin up before the idle
        // navigation state is treated as "stopped short of the node".
        if (nodeFollowSubmittedAt != DateTime.MinValue)
        {
            if (DateTime.UtcNow - nodeFollowSubmittedAt < TimeSpan.FromSeconds(2))
            {
                return false;
            }

            nodeFollowSubmittedAt = DateTime.MinValue;
        }

        // Mount before issuing the walking segment. A mount action issued after
        // movement starts interrupts vnavmesh and is the main source of the
        // visible run-stop-run cycle.
        if (ShouldMountForTravel())
        {
            StepProcessor.SubmitFront(ChainHelper.MountChain());
            return false;
        }

        if (nodePathAttempts >= MAX_NODE_PATH_ATTEMPTS)
        {
            if (TryInsertForcedRecovery(nodeId, expectedPosition))
            {
                return false;
            }

            return SkipCurrentNode($"vnavmesh could not reach it after {nodePathAttempts} attempts");
        }

        nodePathAttempts++;
        lastNodeProgress = DateTime.UtcNow;
        AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, destination, false);
        nodeFollowSubmittedAt = DateTime.UtcNow;
        return false;
    }

    private void BeginNodeInteraction(uint nodeId, Vector3 expectedPosition, IGameObject obj)
    {
        var scheduledGeneration = routeGeneration;
        var scheduledStepIndex = stepIndex;
        interactionPending = true;
        interactionNodeId = nodeId;
        interactionStepIndex = scheduledStepIndex;
        Svc.Log.Info(
            $"Starting hunt node {nodeId} interaction with object {obj.GameObjectId} " +
            $"at distance {Player.DistanceTo(obj.Position):F2}.");

        StepProcessor.SubmitFront(GetInteractionChain(
            nodeId,
            expectedPosition,
            obj.GameObjectId,
            outcome => CompleteNodeInteraction(
                scheduledGeneration,
                scheduledStepIndex,
                nodeId,
                outcome)));
    }

    private void CompleteNodeInteraction(
        long scheduledGeneration,
        int scheduledStepIndex,
        uint nodeId,
        HuntInteractionOutcome outcome)
    {
        if (!running
            || routeGeneration != scheduledGeneration
            || !interactionPending
            || interactionNodeId != nodeId
            || interactionStepIndex != scheduledStepIndex
            || stepIndex != scheduledStepIndex
            || stepIndex >= Steps.Count
            || CurrentStep.Type != PathfinderStepType.WalkToNode
            || CurrentStep.NodeId != nodeId)
        {
            return;
        }

        interactionPending = false;
        interactionNodeId = null;
        interactionStepIndex = -1;

        if (outcome is HuntInteractionOutcome.Succeeded or HuntInteractionOutcome.TargetGone)
        {
            Svc.Log.Info($"Hunt node {nodeId} interaction completed with outcome {outcome}.");
            var wasPriorityDiversion = priorityDiversionNodeId == nodeId;
            if (wasPriorityDiversion)
            {
                priorityDiversionNodeId = null;
            }

            ResetNodeNavigation();
            stepIndex++;
            if (wasPriorityDiversion)
            {
                RejoinRemainingRoute();
            }

            return;
        }

        if (outcome == HuntInteractionOutcome.OutOfRange)
        {
            Svc.Log.Warning($"Hunt node {nodeId} moved out of interaction range; approaching it again.");
            ResetNodePathState(keepCurrentNode: true);
            return;
        }

        interactionAttempts++;
        if (interactionAttempts >= MAX_NODE_INTERACTION_ATTEMPTS)
        {
            Svc.Log.Error(
                $"Skipping hunt node {nodeId}: interaction failed " +
                $"{interactionAttempts}/{MAX_NODE_INTERACTION_ATTEMPTS} times.");
            var wasPriorityDiversion = priorityDiversionNodeId == nodeId;
            if (wasPriorityDiversion)
            {
                priorityDiversionNodeId = null;
            }

            ResetNodeNavigation();
            stepIndex++;
            if (wasPriorityDiversion)
            {
                RejoinRemainingRoute();
            }

            return;
        }

        Svc.Log.Warning(
            $"Retrying hunt node {nodeId} interaction " +
            $"({interactionAttempts}/{MAX_NODE_INTERACTION_ATTEMPTS}).");
        ResetNodePathState(keepCurrentNode: true);
    }

    private bool TryInsertDynamicTransit(uint nodeId, Vector3 destination)
    {
        if (!ZoneData.IsInNorthHorn()
            || !ShouldUseInitialTransit(nodeId)
            || !dynamicallyPlannedNodes.Add(nodeId))
        {
            return false;
        }

        var aethernets = AethernetData.All().ToArray();
        if (aethernets.Length == 0)
        {
            return false;
        }

        var plan = SmartNavigation.DecideFallback(
            Player.Position,
            destination,
            (Aethernet?)null,
            aethernets,
            ZoneData.GetBaseCampAethernet().GetData(),
            config.ReturnCost,
            config.TeleportCost,
            "North Horn hunt runtime routing",
            includeWalkTeleportCandidate: true);
        var transitSteps = HuntNavigationPlanner.BuildTransitSteps(plan);
        if (transitSteps.Count == 0)
        {
            return false;
        }

        Svc.Log.Info(
            $"Hunt node {nodeId} selected navigation plan {plan.Type}: " +
            $"{plan.SourceAethernet} -> {plan.DestinationAethernet} (cost={plan.Cost:F2}).");
        HuntNavigationPlanner.InsertBeforeCurrentNode(Steps, stepIndex, transitSteps);
        ResetNodePathState();
        return true;
    }

    private bool TryInsertForcedRecovery(uint nodeId, Vector3 destination)
    {
        if (!ZoneData.IsInNorthHorn() || !ShouldUseForcedRecovery(nodeId))
        {
            return false;
        }

        if (!recoveredNodeAethernets.TryGetValue(nodeId, out var attemptedAethernets))
        {
            attemptedAethernets = [];
            recoveredNodeAethernets[nodeId] = attemptedAethernets;
        }

        if (attemptedAethernets.Count >= MAX_NODE_RECOVERY_ROUTES)
        {
            return false;
        }

        var target = AethernetData.All()
            .Where(shard => !attemptedAethernets.Contains(shard.Aethernet))
            .OrderBy(shard => Vector3.DistanceSquared(shard.Destination, destination))
            .FirstOrDefault();
        if (target == null)
        {
            return false;
        }

        attemptedAethernets.Add(target.Aethernet);

        var recoverySteps = HuntNavigationPlanner.BuildForcedRecoverySteps(
            target.Aethernet,
            ZoneData.GetBaseCampAethernet());
        Svc.Log.Warning(
            $"Hunt node {nodeId} was not reachable directly; inserting verified " +
            $"return/aethernet recovery through {target.Aethernet}.");
        HuntNavigationPlanner.InsertBeforeCurrentNode(Steps, stepIndex, recoverySteps);
        ResetNodePathState();
        return true;
    }

    private bool SkipCurrentNode(string reason)
    {
        var nodeId = CurrentStep.NodeId;
        Svc.Log.Warning($"Skipping hunt node {nodeId}: {reason}.");
        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        ResetNodeNavigation();
        if (HuntRoutePriorityPolicy.TryRemoveCurrentDiversion(
                Steps,
                stepIndex,
                priorityDiversionNodeId))
        {
            priorityDiversionNodeId = null;
            RejoinRemainingRoute();
            return false;
        }

        return true;
    }

    private bool TryPromotePriorityNode()
    {
        if (CurrentStep.Type != PathfinderStepType.WalkToNode)
        {
            return false;
        }

        if (priorityDiversionNodeId == CurrentStep.NodeId)
        {
            return false;
        }

        var remaining = Steps
            .Skip(stepIndex)
            .Where(step => step.Type == PathfinderStepType.WalkToNode)
            .Select(step => step.NodeId)
            .Distinct()
            .ToArray();
        var priorityNodeId = GetPriorityNode(CurrentStep.NodeId, remaining);
        if (priorityNodeId == null || priorityNodeId == CurrentStep.NodeId)
        {
            return false;
        }

        if (!HuntRoutePriorityPolicy.PromoteOrInsert(
                Steps,
                stepIndex,
                priorityNodeId.Value))
        {
            return false;
        }

        priorityDiversionNodeId = priorityNodeId;
        // A live coffer can be promoted while SimpleMove is still following
        // the interrupted patrol waypoint. Cancel both the avoidance plan and
        // vnavmesh movement before clearing node state; otherwise the next
        // handler sees the stale movement as active and never paths to or
        // interacts with the promoted coffer.
        AggroAvoidanceNavigation.Stop(vnav);
        ResetNodeNavigation();
        Svc.Log.Info(
            $"Promoted live hunt node {priorityNodeId} ahead of planned node " +
            $"{Steps[stepIndex + 1].NodeId}; cancelled previous navigation.");
        return true;
    }

    private void RejoinRemainingRoute()
    {
        var remaining = Steps
            .Skip(stepIndex)
            .Where(step => step.Type == PathfinderStepType.WalkToNode)
            .Select(step => step.NodeId)
            .Distinct()
            .ToArray();
        if (remaining.Length == 0)
        {
            return;
        }

        var reordered = ReorderRemainingNodes(Player.Position, remaining);
        if (reordered == null)
        {
            return;
        }

        Steps.RemoveRange(stepIndex, Steps.Count - stepIndex);
        Steps.AddRange(reordered.Select(PathfinderStep.WalkToDestination));
        ResetNodeNavigation();
        Svc.Log.Info(
            $"Rejoined the hunt route at next planned node {reordered.FirstOrDefault()}.");
    }

    protected void ResetNodeNavigation()
    {
        ResetNodePathState();
        ResetInteractionState();
    }

    private void ResetNodePathState(bool keepCurrentNode = false)
    {
        if (!keepCurrentNode)
        {
            navigationNodeId = null;
        }
        nodePathAttempts = 0;
        bestNodeDistance = float.MaxValue;
        lastNodeProgress = DateTime.MinValue;
        nodeFollowSubmittedAt = DateTime.MinValue;
    }

    private void ResetInteractionState()
    {
        interactionPending = false;
        interactionNodeId = null;
        interactionStepIndex = -1;
        interactionAttempts = 0;
    }

    private void ResetHuntRouteState()
    {
        dynamicallyPlannedNodes.Clear();
        recoveredNodeAethernets.Clear();
        priorityDiversionNodeId = null;
        ResetInteractionState();
    }

    private bool ReturnToBaseCampHandler()
    {
        BeginTransitStep();
        var baseCamp = ZoneData.GetBaseCampAethernet();
        var baseCampData = baseCamp.GetData();
        var destination = baseCampData.NavigationPosition;
        distance = baseCampData.DistanceToPlayer();
        ObserveTransitProgress(distance);
        var inCombat = states.GetState() == State.InCombat;

        if (!inCombat)
        {
            if (vnav.IsRunning())
            {
                vnav.Stop();
            }

            if (ZoneData.IsNearAethernetShard(baseCamp, AethernetData.DISTANCE + 1f))
            {
                ResetTransitNavigation();
                return true;
            }

            if (transitChildPending)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (transitChildAttempts > 0 && now - lastTransitChildAttempt < TransitRetryDelay)
            {
                return false;
            }

            if (transitChildAttempts >= MAX_TRANSIT_ATTEMPTS)
            {
                return FailTransit(
                    transitChildFailureReason
                    ?? $"return chain did not reach base-camp aethernet {baseCamp} after {transitChildAttempts} attempts");
            }

            var scheduledGeneration = routeGeneration;
            var scheduledStepIndex = stepIndex;
            var returnChain = ChainHelper.ReturnChain(new ReturnChainConfig
            {
                ApproachAetheryte = true,
                ForceReturn = true,
            });

            transitChildAttempts++;
            lastTransitChildAttempt = now;
            ScheduleTransitChild(
                "Hunter.ReturnToBaseCamp",
                returnChain,
                TimeSpan.FromSeconds(310),
                _ =>
                {
                    if (!IsCurrentTransitStep(
                            scheduledGeneration,
                            scheduledStepIndex,
                            PathfinderStepType.ReturnToBaseCamp))
                    {
                        return;
                    }

                    if (returnChain.Succeeded
                        && ZoneData.IsNearAethernetShard(baseCamp, AethernetData.DISTANCE + 1f))
                    {
                        ResetTransitNavigation();
                        stepIndex++;
                        return;
                    }

                    transitChildFailureReason =
                        $"return chain did not reach base-camp aethernet {baseCamp}; " +
                        $"reason={returnChain.FailureReason ?? "chain timed out or was aborted"}";
                    lastTransitChildAttempt = DateTime.UtcNow;
                    Svc.Log.Warning(
                        $"{transitChildFailureReason} " +
                        $"({transitChildAttempts}/{MAX_TRANSIT_ATTEMPTS}).");
                });

            // The managed child advances this step only after verified arrival.
            return false;
        }

        if (DateTime.UtcNow - transitStartedAt >= CombatReturnTimeout)
        {
            return FailTransit($"combat did not end within {CombatReturnTimeout.TotalSeconds:F0}s while returning to base camp");
        }

        // At the camp there is nowhere closer to path to; wait for combat to
        // drop under the bounded overall timeout instead of endlessly repathing.
        if (distance <= 4f)
        {
            if (vnav.IsRunning())
            {
                vnav.Stop();
            }

            return false;
        }

        return MaintainTransit(destination, "base camp while in combat");
    }

    private bool WalkToAethernetHandler()
    {
        BeginTransitStep();
        var aethernet = CurrentStep.Aethernet.GetData();
        var destination = aethernet.NavigationPosition;

        distance = aethernet.DistanceToPlayer();
        ObserveTransitProgress(distance);

        if (distance <= 4f)
        {
            if (vnav.IsRunning())
            {
                vnav.Stop();
            }

            ResetTransitNavigation();
            return true;
        }

        if (ShouldMountForTravel())
        {
            StepProcessor.SubmitFront(ChainHelper.MountChain());
            return false;
        }

        return MaintainTransit(
            destination,
            $"aethernet {CurrentStep.Aethernet}",
            TryReplaceUnreachableWalkTransit);
    }

    private bool MaintainTransit(
        Vector3 destination,
        string label,
        Func<bool>? recover = null)
    {
        var now = DateTime.UtcNow;
        if (NavigationActivityState.IsActive(
                vnav.IsRunning(),
                vnav.IsSimpleMoveInProgress(),
                vnav.IsPathfinding()))
        {
            if (now - lastTransitProgress < TransitProgressTimeout)
            {
                return false;
            }

            vnav.Stop();
            Svc.Log.Warning(
                $"Hunt travel to {label} made no progress for {TransitProgressTimeout.TotalSeconds:F0}s; " +
                $"recalculating ({transitAttempts}/{MAX_TRANSIT_ATTEMPTS}).");
        }

        if (transitAttempts > 0 && now - lastTransitAttempt < TransitRetryDelay)
        {
            return false;
        }

        if (transitAttempts >= MAX_TRANSIT_ATTEMPTS)
        {
            if (recover?.Invoke() == true)
            {
                return false;
            }

            return FailTransit($"vnavmesh could not reach {label} after {transitAttempts} attempts");
        }

        transitAttempts++;
        lastTransitAttempt = now;
        lastTransitProgress = now;
        AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, destination, false);
        return false;
    }

    private bool TryReplaceUnreachableWalkTransit()
    {
        if (!ZoneData.IsInNorthHorn()
            || stepIndex + 1 >= Steps.Count
            || CurrentStep.Type != PathfinderStepType.WalkToAethernet
            || Steps[stepIndex + 1].Type != PathfinderStepType.TeleportToAethernet)
        {
            return false;
        }

        var source = CurrentStep.Aethernet;
        var destination = Steps[stepIndex + 1].Aethernet;
        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        Steps[stepIndex] = PathfinderStep.ReturnToBaseCamp();
        ResetTransitNavigation();
        Svc.Log.Warning(
            $"Aethernet {source} was not reachable from the current side of North Horn; " +
            $"replacing the walk with a forced return before teleporting to {destination}.");
        return true;
    }

    private void BeginTransitStep()
    {
        if (transitStepIndex == stepIndex)
        {
            return;
        }

        ResetTransitNavigation();
        transitStepIndex = stepIndex;
        transitStartedAt = DateTime.UtcNow;
        lastTransitProgress = transitStartedAt;
    }

    private void ObserveTransitProgress(float currentDistance)
    {
        if (currentDistance + 0.5f < bestTransitDistance)
        {
            bestTransitDistance = currentDistance;
            lastTransitProgress = DateTime.UtcNow;
        }
    }

    private bool FailTransit(string reason)
    {
        Svc.Log.Error($"Stopping hunt route: {reason}.");
        Stop();
        return false;
    }

    protected void ResetTransitNavigation()
    {
        AbortActiveTransitChild();
        transitStepIndex = -1;
        transitAttempts = 0;
        bestTransitDistance = float.MaxValue;
        transitStartedAt = DateTime.MinValue;
        lastTransitProgress = DateTime.MinValue;
        lastTransitAttempt = DateTime.MinValue;
        transitChildAttempts = 0;
        lastTransitChildAttempt = DateTime.MinValue;
        transitChildPending = false;
        transitChildFailureReason = null;
    }

    private bool TeleportToAethernetHandler()
    {
        BeginTransitStep();
        var destinationAethernet = CurrentStep.Aethernet;
        var destinationReached = ZoneData.IsNearAethernetShard(
            destinationAethernet,
            AethernetData.DISTANCE + 1f);
        distance = destinationAethernet.GetData().DistanceToPlayer();

        // A successful teleport can finish before an optional trailing child
        // task reports completion. Trust only the verified landing position,
        // abort any stale child, and never walk back toward the old source.
        if (destinationReached)
        {
            ResetTransitNavigation();
            stepIndex++;
            return false;
        }

        if (transitChildPending)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (transitChildAttempts > 0 && now - lastTransitChildAttempt < TransitRetryDelay)
        {
            return false;
        }

        if (transitChildAttempts >= MAX_TRANSIT_ATTEMPTS)
        {
            return FailTransit(
                transitChildFailureReason
                ?? $"aethernet teleport to {destinationAethernet} failed after {transitChildAttempts} attempts");
        }

        var scheduledGeneration = routeGeneration;
        var scheduledStepIndex = stepIndex;
        var teleport = ChainHelper.TeleportChain(
            destinationAethernet,
            ResolveTeleportSourceAethernet(),
            mountAfterTeleport: false);

        transitChildAttempts++;
        lastTransitChildAttempt = now;
        ScheduleTransitChild(
            "Hunter.TeleportToAethernet",
            teleport,
            TimeSpan.FromSeconds(250),
            _ =>
            {
                if (!IsCurrentTransitStep(
                        scheduledGeneration,
                        scheduledStepIndex,
                        PathfinderStepType.TeleportToAethernet,
                        destinationAethernet))
                {
                    return;
                }

                if (TransitCompletionPolicy.HasVerifiedArrival(
                        teleport.Succeeded,
                        ZoneData.IsNearAethernetShard(
                            destinationAethernet,
                            AethernetData.DISTANCE + 1f)))
                {
                    ResetTransitNavigation();
                    stepIndex++;
                    return;
                }

                transitChildFailureReason =
                    $"aethernet teleport to {destinationAethernet} did not complete; " +
                    $"reason={teleport.FailureReason ?? "teleport chain timed out or was aborted"}";
                lastTransitChildAttempt = DateTime.UtcNow;
                Svc.Log.Warning(
                    $"{transitChildFailureReason} " +
                    $"({transitChildAttempts}/{MAX_TRANSIT_ATTEMPTS}).");
            });

        // This step is advanced only by the managed, verified teleport child.
        return false;
    }

    private bool ShouldMountForTravel()
    {
        return m.PluginConfig.TeleporterConfig.ShouldMount
               && !Player.Mounted
               && states.GetState() != State.InCombat
               && !Player.IsCasting
               && !Player.IsDead;
    }

    private Aethernet? ResolveTeleportSourceAethernet()
    {
        if (stepIndex <= 0)
        {
            return null;
        }

        var previous = Steps[stepIndex - 1];
        return previous.Type switch
        {
            PathfinderStepType.WalkToAethernet => previous.Aethernet,
            PathfinderStepType.ReturnToBaseCamp => ZoneData.GetBaseCampAethernet(),
            _ => null,
        };
    }

    private void ScheduleTransitChild(
        string name,
        ChainFactory childFactory,
        TimeSpan timeout,
        Action<bool> complete)
    {
        if (transitChildPending)
        {
            throw new InvalidOperationException("A hunt transit child is already pending.");
        }

        var factory = childFactory.Factory();
        transitChildPending = true;
        StepProcessor.SubmitFront(() =>
        {
            Chain? child = null;
            var childCompleted = false;
            var timedOut = false;

            void CloseChild(bool abort)
            {
                var current = child;
                child = null;
                if (ReferenceEquals(activeTransitChild, current))
                {
                    activeTransitChild = null;
                }

                if (current == null)
                {
                    return;
                }

                if (abort)
                {
                    current.Abort();
                }
                else
                {
                    current.Dispose();
                }
            }

            var configuration = new TaskManagerConfiguration
            {
                TimeLimitMS = checked((int)timeout.TotalMilliseconds),
                AbortOnTimeout = true,
                AbortOnError = true,
                OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                {
                    timedOut = true;
                    CloseChild(abort: true);
                },
            };

            return Chain.Create(name)
                .Then(new TaskManagerTask(() =>
                {
                    if (child == null)
                    {
                        child = factory();
                        activeTransitChild = child;
                    }

                    if (!child.IsComplete())
                    {
                        return false;
                    }

                    childCompleted = true;
                    return true;
                }, configuration))
                .OnFinally(() =>
                {
                    var completedNormally = childCompleted && !timedOut;
                    CloseChild(abort: !childCompleted);
                    transitChildPending = false;
                    complete(completedNormally);
                });
        });
    }

    private void AbortActiveTransitChild()
    {
        var child = activeTransitChild;
        activeTransitChild = null;
        transitChildPending = false;
        if (child != null)
        {
            child.Abort();
        }
    }

    private bool IsCurrentTransitStep(
        long scheduledGeneration,
        int scheduledStepIndex,
        PathfinderStepType expectedType,
        Aethernet expectedAethernet = Aethernet.Unknown)
    {
        if (!running
            || routeGeneration != scheduledGeneration
            || stepIndex != scheduledStepIndex
            || stepIndex >= Steps.Count
            || CurrentStep.Type != expectedType)
        {
            return false;
        }

        return expectedAethernet == Aethernet.Unknown
               || CurrentStep.Aethernet == expectedAethernet;
    }
}
