using BOCCHI.Chains;
using BOCCHI.Enums;
using BOCCHI.Modules;
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

public abstract class Hunter
{
    protected Module m;
    protected const float DISTANCE_TO_NODE_TO_USE = 2f;

    private const int MAX_NODE_PATH_ATTEMPTS = 3;

    private static readonly TimeSpan NodePathCalculationTimeout = TimeSpan.FromSeconds(20);

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

    private Task<List<Vector3>>? nodePathTask;

    private CancellationTokenSource? nodePathCancellation;

    private DateTime nodePathStartedAt = DateTime.MinValue;

    private uint? navigationNodeId;

    private int nodePathAttempts;

    private float bestNodeDistance = float.MaxValue;

    private DateTime lastNodeProgress = DateTime.MinValue;

    private int transitStepIndex = -1;

    private int transitAttempts;

    private float bestTransitDistance = float.MaxValue;

    private DateTime transitStartedAt = DateTime.MinValue;

    private DateTime lastTransitProgress = DateTime.MinValue;

    private DateTime lastTransitAttempt = DateTime.MinValue;

    protected PathfinderStep CurrentStep
    {
        get => Steps[stepIndex];
    }

    protected string JSON = "";

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

    protected abstract bool TryGetDestinationForCurrentStep(out Vector3 destination);

    protected float GetDetectionRange()
    {
        return config.DetectionRange;
    }

    protected abstract IPathfinder CreatePathfinder();

    protected abstract Func<Chain> GetInteractionChain(IGameObject obj);

    protected abstract List<uint> GetValidNodes(int max);

    public void Update()
    {
        if (!running || Plugin.Chain.IsRunning)
        {
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

        if (StepProcessor.IsRunning)
        {
            return;
        }

        if (stepIndex >= Steps.Count)
        {
            Teardown();
            return;
        }

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

        var obj = GetValidObjects().FirstOrDefault(o => Vector3.Distance(Player.Position, o.Position) <= 5f);
        if (obj != null)
        {
            StepProcessor.Submit(GetInteractionChain(obj));
        }
    }

    public void Draw(Module<Plugin, Config> module)
    {
        OcelotUi.Title($"{module.T("panel.hunt.title")}:");
        OcelotUi.Indent(() =>
        {
            if (ImGui.Button(running ? I18N.T("generic.label.stop") : I18N.T("generic.label.start")))
            {
                running = !running;
                if (running == false)
                {
                    stopwatch.Stop();
                    running = false;
                    stepIndex = 0;
                    Steps.Clear();
                    vnav.Stop();
                    Plugin.Chain.Abort();
                    StepProcessor.Abort();
                    pathfinder = null;
                    ResetNodeNavigation();
                    ResetTransitNavigation();
                }
                else
                {
                    stopwatch.Restart();
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

    protected virtual void Teardown()
    {
        stopwatch.Stop();
        running = false;
        stepIndex = 0;
        Steps.Clear();
        if (m.TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null && navigation.IsReady())
        {
            navigation.Stop();
        }
        Plugin.Chain.Abort();
        StepProcessor.Abort();
        pathfinder = null;
        ResetNodeNavigation();
        ResetTransitNavigation();
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
        if (navigationNodeId != CurrentStep.NodeId)
        {
            ResetNodeNavigation();
            navigationNodeId = CurrentStep.NodeId;
            lastNodeProgress = DateTime.UtcNow;
        }

        if (!TryGetDestinationForCurrentStep(out var destination))
        {
            return SkipCurrentNode("no position is known for this node");
        }

        distance = Player.DistanceTo(destination);
        if (distance + 0.5f < bestNodeDistance)
        {
            bestNodeDistance = distance;
            lastNodeProgress = DateTime.UtcNow;
        }

        if (distance <= GetDetectionRange())
        {
            var obj = GetValidObjects().FirstOrDefault(o => Vector3.Distance(destination, o.Position) <= 5f);

            if (obj == null)
            {
                vnav.Stop();
                ResetNodeNavigation();
                return true;
            }

            if (distance <= DISTANCE_TO_NODE_TO_USE)
            {
                StepProcessor.SubmitFront(GetInteractionChain(obj));
                ResetNodeNavigation();
                return true;
            }
        }

        if (vnav.IsRunning())
        {
            if (DateTime.UtcNow - lastNodeProgress >= NodeProgressTimeout)
            {
                vnav.Stop();
                Svc.Log.Warning($"Hunt node {CurrentStep.NodeId} made no progress for {NodeProgressTimeout.TotalSeconds:f0}s; recalculating the segment.");
                lastNodeProgress = DateTime.UtcNow;
            }

            if (!Player.Mounted)
            {
                StepProcessor.SubmitFront(ChainHelper.MountChain());
            }

            return false;
        }

        if (nodePathTask == null)
        {
            if (nodePathAttempts >= MAX_NODE_PATH_ATTEMPTS)
            {
                return SkipCurrentNode($"vnavmesh could not reach it after {nodePathAttempts} attempts");
            }

            nodePathAttempts++;
            var navigation = vnav;
            var start = Player.Position;
            nodePathCancellation = new CancellationTokenSource();
            var cancellationToken = nodePathCancellation.Token;
            nodePathTask = Task.Run(
                () => navigation.PathfindCancelable(start, destination, false, cancellationToken),
                cancellationToken);
            nodePathStartedAt = DateTime.UtcNow;
            return false;
        }

        if (!nodePathTask.IsCompleted)
        {
            if (DateTime.UtcNow - nodePathStartedAt >= NodePathCalculationTimeout)
            {
                Svc.Log.Warning(
                    $"vnavmesh path calculation timed out for hunt node {CurrentStep.NodeId} " +
                    $"after {NodePathCalculationTimeout.TotalSeconds:F0}s " +
                    $"(attempt {nodePathAttempts}/{MAX_NODE_PATH_ATTEMPTS}).");
                ReleaseNodePathTask(cancel: true);
            }

            return false;
        }

        if (nodePathTask.IsCanceled)
        {
            ReleaseNodePathTask(cancel: false);
            Svc.Log.Warning($"vnavmesh cancelled path calculation for hunt node {CurrentStep.NodeId} (attempt {nodePathAttempts}/{MAX_NODE_PATH_ATTEMPTS}).");
            return false;
        }

        if (nodePathTask.IsFaulted)
        {
            var exception = nodePathTask.Exception?.GetBaseException();
            Svc.Log.Warning(exception, $"vnavmesh path calculation failed for hunt node {CurrentStep.NodeId} (attempt {nodePathAttempts}/{MAX_NODE_PATH_ATTEMPTS}).");
            ReleaseNodePathTask(cancel: false);
            return false;
        }

        var path = nodePathTask.Result;
        ReleaseNodePathTask(cancel: false);
        if (path.Count <= 1)
        {
            Svc.Log.Warning($"vnavmesh returned no route to hunt node {CurrentStep.NodeId} (attempt {nodePathAttempts}/{MAX_NODE_PATH_ATTEMPTS}).");
            return false;
        }

        var route = path.SkipWhile(point => Vector3.DistanceSquared(point, Player.Position) < 0.25f).ToList();
        if (route.Count == 0)
        {
            return SkipCurrentNode("vnavmesh returned an empty walking segment");
        }

        vnav.FollowPath(route, false);
        lastNodeProgress = DateTime.UtcNow;
        return false;
    }

    private bool SkipCurrentNode(string reason)
    {
        Svc.Log.Warning($"Skipping hunt node {CurrentStep.NodeId}: {reason}.");
        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        ResetNodeNavigation();
        return true;
    }

    protected void ResetNodeNavigation()
    {
        ReleaseNodePathTask(cancel: true);
        navigationNodeId = null;
        nodePathAttempts = 0;
        bestNodeDistance = float.MaxValue;
        lastNodeProgress = DateTime.MinValue;
    }

    private void ReleaseNodePathTask(bool cancel)
    {
        var task = nodePathTask;
        var cancellation = nodePathCancellation;
        nodePathTask = null;
        nodePathCancellation = null;
        nodePathStartedAt = DateTime.MinValue;

        if (cancel)
        {
            cancellation?.Cancel();
        }

        if (task == null)
        {
            cancellation?.Dispose();
            return;
        }

        ObserveDetachedTask(task, cancellation);
    }

    private static void ObserveDetachedTask(Task task, CancellationTokenSource? cancellation = null)
    {
        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }

                cancellation?.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool ReturnToBaseCampHandler()
    {
        BeginTransitStep();
        var destination = BOCCHI.Data.ZoneData.GetBaseCampAethernet().GetData().Position;
        distance = Player.DistanceTo(destination);
        ObserveTransitProgress(distance);
        var inCombat = states.GetState() == State.InCombat;

        if (!inCombat)
        {
            if (vnav.IsRunning())
            {
                vnav.Stop();
            }

            ResetTransitNavigation();

            StepProcessor.SubmitFront(ChainHelper.ReturnChain(new ReturnChainConfig
            {
                ApproachAetheryte = true,
            }));

            return true;
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
        var destination = CurrentStep.Aethernet.GetData().Position;

        distance = Player.DistanceTo(destination);
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

        if (!Player.Mounted)
        {
            StepProcessor.SubmitFront(ChainHelper.MountChain());
        }

        return MaintainTransit(destination, $"aethernet {CurrentStep.Aethernet}");
    }

    private bool MaintainTransit(Vector3 destination, string label)
    {
        var now = DateTime.UtcNow;
        if (vnav.IsRunning())
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
            return FailTransit($"vnavmesh could not reach {label} after {transitAttempts} attempts");
        }

        transitAttempts++;
        lastTransitAttempt = now;
        lastTransitProgress = now;
        vnav.PathfindAndMoveTo(destination, false);
        return false;
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
        transitStepIndex = -1;
        transitAttempts = 0;
        bestTransitDistance = float.MaxValue;
        transitStartedAt = DateTime.MinValue;
        lastTransitProgress = DateTime.MinValue;
        lastTransitAttempt = DateTime.MinValue;
    }

    private bool TeleportToAethernetHandler()
    {
        distance = 0;
        StepProcessor.SubmitFront(ChainHelper.TeleportChain(CurrentStep.Aethernet));
        return true;
    }
}
