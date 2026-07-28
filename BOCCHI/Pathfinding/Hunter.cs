using BOCCHI.Chains;
using BOCCHI.Enums;
using BOCCHI.Modules;
using BOCCHI.Modules.Pathfinder;
using BOCCHI.Modules.StateManager;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
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
using System.Threading.Tasks;
using TextCopy;

namespace BOCCHI.Pathfinding;

public abstract class Hunter
{
    protected Module m;
    protected const float DISTANCE_TO_NODE_TO_USE = 2f;

    protected StateManagerModule states;

    protected VNavmesh vnav => m.GetIPCSubscriber<VNavmesh>();

    protected PathfinderConfig config;

    protected bool running;

    protected IPathfinder? pathfinder;

    protected List<PathfinderStep> Steps = [];

    protected int stepIndex = 0;

    protected float distance = 0f;

    protected Stopwatch stopwatch = new();

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

    protected abstract Vector3 GetDestinationForCurrentStep();

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
                    .Then(new TaskManagerTask(() => pathfinder?.State == PathfinderState.FileLoaded))
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
                    // Keep failed pathfinders around for one update so the watcher can
                    // force-stop repeat hunts instead of immediately recreating them.
                    .Then(_ =>
                    {
                        if (pathfinder?.State == PathfinderState.PathfindingDone)
                        {
                            pathfinder = null;
                        }
                    });
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
        var destination = GetDestinationForCurrentStep();

        if (!vnav.IsRunning())
        {
            vnav.PathfindAndMoveTo(destination, false);
        }

        if (!Player.Mounted)
        {
            StepProcessor.SubmitFront(ChainHelper.MountChain());
        }

        distance = Player.DistanceTo(destination);
        if (distance <= GetDetectionRange())
        {
            var obj = GetValidObjects().FirstOrDefault(o => Vector3.Distance(destination, o.Position) <= 5f);

            if (obj == null)
            {
                vnav.Stop();
                return true;
            }

            if (distance <= DISTANCE_TO_NODE_TO_USE)
            {
                StepProcessor.SubmitFront(GetInteractionChain(obj));
                return true;
            }
        }

        return distance <= DISTANCE_TO_NODE_TO_USE;
    }

    private bool ReturnToBaseCampHandler()
    {
        distance = 0;
        var inCombat = states.GetState() == State.InCombat;

        // If we are in combat, start running back to the base camp so we can escape combat
        if (inCombat && !vnav.IsRunning())
        {
            vnav.PathfindAndMoveTo(BOCCHI.Data.ZoneData.GetBaseCampAethernet().GetData().Position, false);
            return false;
        }

        if (!inCombat && vnav.IsRunning())
        {
            vnav.Stop();
        }

        if (inCombat)
        {
            return false;
        }

        StepProcessor.SubmitFront(ChainHelper.ReturnChain(new ReturnChainConfig
        {
            ApproachAetheryte = true,
        }));

        return true;
    }

    private bool WalkToAethernetHandler()
    {
        var destination = CurrentStep.Aethernet.GetData().Position;

        if (!vnav.IsRunning())
        {
            vnav.PathfindAndMoveTo(destination, false);
        }

        if (!Player.Mounted)
        {
            StepProcessor.SubmitFront(ChainHelper.MountChain());
        }

        distance = Player.DistanceTo(destination);
        return distance <= 4f;
    }

    private bool TeleportToAethernetHandler()
    {
        distance = 0;
        StepProcessor.SubmitFront(ChainHelper.TeleportChain(CurrentStep.Aethernet));
        return true;
    }
}
