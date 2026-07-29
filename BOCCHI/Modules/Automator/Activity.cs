using BOCCHI.ActionHelpers;
using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Pathfinding;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.Extensions;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

public abstract class Activity
{
    public readonly EventData data;

    private readonly Lifestream lifestream;

    protected readonly VNavmesh vnav;

    protected readonly AutomatorModule module;

    public ActivityState state = ActivityState.Idle;

    private bool hasEnteredActivity;

    protected readonly Dictionary<ActivityState, Func<StateManagerModule, Func<Chain>?>> handlers;

    private readonly static List<uint> DangerousEnemies = [
        18146,//指令罐小怪
        18123,//封印恶魔火球
    ];

    protected unsafe Activity(EventData data, Lifestream lifestream, VNavmesh vnav, AutomatorModule module)
    {
        this.data = data;
        this.lifestream = lifestream;
        this.vnav = vnav;
        this.module = module;

        handlers = new Dictionary<ActivityState, Func<StateManagerModule, Func<Chain>?>>
        {
            { ActivityState.Idle, GetIdleChain },
            { ActivityState.Pathfinding, GetPathfindingChain },
            { ActivityState.Participating, GetParticipatingChain },
            { ActivityState.Done, GetDoneChain },
        };

        var states = module.GetModule<StateManagerModule>();
        if (states.GetState() == State.InFate
            || states.GetState() == State.InCriticalEncounter
            || (FateManager.Instance() != null && FateManager.Instance()->GetCurrentFateId() != 0)
            || (DynamicEventContainer.GetInstance() != null && DynamicEventContainer.GetInstance()->CurrentEventId != 0))
        {
            state = ActivityState.Participating;
            hasEnteredActivity = true;
        }
    }


    public Func<Chain>? GetChain(StateManagerModule states)
    {
        return !IsValid() ? null : handlers[state](states);
    }

    private Func<Chain> GetIdleChain(StateManagerModule states)
    {
        return () =>
        {
            bool ShouldToggleAi(ChainContext _)
            {
                return module.Config.ShouldToggleAiProvider && !Svc.Condition[ConditionFlag.InCombat];
            }

            return Chain.Create("Illegal:Idle")
                .Then(_ => PromeRotationController.Stop())
                .ConditionalThen(ShouldToggleAi, _ => module.Config.AiProvider.Off())
                .ConditionalThen(_ => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded), _ =>
                {
                    Chat.ExecuteCommand("/aeTargetSelector off");
                    Chat.ExecuteCommand("/aepull off");
                })
                .Then(_ => vnav.Stop())
                .Then(_ => state = ActivityState.Pathfinding);
        };
    }

    private Func<Chain> GetPathfindingChain(StateManagerModule states)
    {
        return () =>
        {
            var playerShard = AethernetData.AllByDistance().First();
            var activityShard = GetAethernetData();

            var isFate = data.Type == EventType.Fate;
            var destination = GetPosition();
            var navType = SmartNavigation.Decide(Player.Position, destination, activityShard);
            var pathfinding = new PathfindingChain(vnav, destination, data);

            module.Debug("Selected navigation type: " + navType);

            var chain = Chain.Create("Illegal:Pathfinding")
                .ConditionalThen(_ => isFate && module.Config.ShouldStanceOnBeforeDoFates && Player.Job.IsTank(), new StanceChain(isFate))
                .ConditionalThen(_ => !isFate && module.Config.ShouldStanceOffBeforeCriticalEncounters && Player.Job.IsTank(), new StanceChain(isFate))
                .ConditionalWait(_ => !isFate && module.Config.ShouldDelayCriticalEncounters && lifestream.GetActiveCustomAetheryte() != 0, Random.Shared.Next((int)module.Config.MinDelay * 1000, (int)module.Config.MaxDelay * 1000));

            switch (navType)
            {
                case NavigationType.Walk:
                    chain = AppendPathfinding(chain, destination, pathfinding);
                    break;

                case NavigationType.ReturnWalk:
                    chain.Then(ChainHelper.ReturnChain());
                    chain = AppendPathfinding(chain, destination, pathfinding);
                    break;

                case NavigationType.ReturnTeleportWalk:
                    chain
                        .Then(ChainHelper.ReturnChain(new ReturnChainConfig { ApproachAetheryte = true }))
                        .Then(ChainHelper.TeleportChain(activityShard.Aethernet))
                        .Debug("Waiting for lifestream to not be 'busy'")
                        .Then(new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));
                    chain = AppendPathfinding(chain, destination, pathfinding);
                    break;

                case NavigationType.WalkTeleportWalk:
                    chain
                        .ConditionalThen(_ => lifestream.GetActiveCustomAetheryte() == 0, new PathfindAndMoveToChain(vnav, playerShard.Position))
                        .BreakIf(() => lifestream.GetActiveCustomAetheryte() == 0)
                        .Then(_ => vnav.Stop())
                        .Then(ChainHelper.TeleportChain(activityShard.Aethernet))
                        .Debug("Waiting for lifestream to not be 'busy'")
                        .Then(new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));
                    chain = AppendPathfinding(chain, destination, pathfinding);
                    break;
            }

            chain
                .ConditionalThen(_ => !pathfinding.TransitReachedEnd, _ => GetPathfindingWatcher(states))
                .ConditionalThen(_ => !vnav.IsRunning(), _ =>
                {
                    if (module.GetModule<AutomatorModule>().random.NextDouble() < 0.5)
                    {
                        Actions.TryUnmount();
                    }
                })
                .Then(_ => state = GetPostPathfindingState());

            return chain;
        };
    }

    private Chain AppendPathfinding(Chain chain, Vector3 destination, PathfindingChain pathfinding)
    {
        var useSouthCrossing = false;

        return chain
            .ConditionalThen(_ =>
            {
                useSouthCrossing = NorthHornSouthCrossingRoute.ShouldUse(data, Player.Position);
                return useSouthCrossing && ShouldMountToPathfindTo(destination);
            }, ChainHelper.MountChain())
            .Then(pathfinding)
            .BreakIf(() => pathfinding.TransitAttempted && !pathfinding.TransitReachedEnd)
            .ConditionalThen(_ => !useSouthCrossing && ShouldMountToPathfindTo(destination), ChainHelper.MountChain());
    }


    private Func<Chain> GetParticipatingChain(StateManagerModule states)
    {
        return () =>
        {
            return Chain.Create("Illegal:Participating")
                .Then(_ => PromeRotationController.Start())
                .ConditionalThen(_ => module.Config.ShouldToggleAiProvider, _ => module.Config.AiProvider.On())
                .ConditionalThen(_ => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded), _ =>
                {
                    Chat.ExecuteCommand("/aeTargetSelector off");
                    Chat.ExecuteCommand("/aepull on");
                })
                .Then(_ => vnav.Stop())
                .Then(new TaskManagerTask(() =>
                {
                    if (!module.Config.ShouldForceTarget || !EzThrottler.Throttle("Participating.ForceTarget", 500))
                    {
                        return HasParticipationEnded(states);
                    }

                    var enemies = GetEnemies();

                    if (enemies.Any(e => DangerousEnemies.Contains(e.BaseId) && e.CurrentHp > 0))
                    {
                        Svc.Targets.Target = enemies.FirstOrDefault(e => DangerousEnemies.Contains(e.BaseId) && e.CurrentHp > 0);
                        return HasParticipationEnded(states);
                    }

                    Svc.Targets.Target = module.Config.ShouldForceTargetCentralEnemy ? enemies.Centroid() : enemies.Closest();

                    return HasParticipationEnded(states);
                }, new TaskManagerConfiguration { TimeLimitMS = int.MaxValue }))
                .Then(_ => state = ActivityState.Done);
        };
    }

    private Func<Chain>? GetDoneChain(StateManagerModule states)
    {
        return null;
    }

    protected List<IBattleNpc> GetEnemies()
    {
        return TargetHelper.Enemies.Where(IsActivityTarget).ToList();
    }

    protected abstract bool IsActivityTarget(IBattleNpc obj);

    private AethernetData GetAethernetData()
    {
        return data.Aethernet?.GetData() ?? AethernetData.AllByDistance(GetPosition()).First();
    }

    protected bool IsInZone()
    {
        var radius = data.Radius ?? GetRadius();

        return Player.DistanceTo(GetPosition()) <= radius;
    }

    private bool HasParticipationEnded(StateManagerModule states)
    {
        var currentState = states.GetState();
        hasEnteredActivity |= ActivityParticipationState.IsInsideActivity(currentState);
        return ActivityParticipationState.HasEnded(hasEnteredActivity, currentState);
    }

    /// <summary>
    /// vnavmesh's simple-move request calculates the route before Path.IsRunning
    /// becomes true. Treat that calculation window as active navigation so the
    /// automator does not tear down and resubmit the same activity every frame.
    /// </summary>
    protected bool IsPathfindingInProgress()
    {
        return NavigationActivityState.IsCalculating(vnav.IsSimpleMoveInProgress(), vnav.IsPathfinding());
    }

    protected bool IsNavigationActive()
    {
        return NavigationActivityState.IsActive(
            vnav.IsRunning(),
            vnav.IsSimpleMoveInProgress(),
            vnav.IsPathfinding());
    }

    private bool ShouldMountToPathfindTo(Vector3 destination)
    {
        if (!module.PluginConfig.TeleporterConfig.ShouldMount)
        {
            return false;
        }

        return Vector3.Distance(Player.Position, destination) > 20f;
    }

    protected abstract float GetRadius();

    protected abstract TaskManagerTask GetPathfindingWatcher(StateManagerModule states);

    public abstract bool IsValid();

    protected abstract Vector3 GetPosition();

    public abstract string GetName();

    protected abstract ActivityState GetPostPathfindingState();
}

public static class NavigationActivityState
{
    public static bool IsCalculating(bool simpleMoveInProgress, bool pathfindInProgress)
    {
        return simpleMoveInProgress || pathfindInProgress;
    }

    public static bool IsActive(bool movementRunning, bool simpleMoveInProgress, bool pathfindInProgress)
    {
        return movementRunning || IsCalculating(simpleMoveInProgress, pathfindInProgress);
    }
}

public static class ActivityParticipationState
{
    public static bool IsInsideActivity(State state)
    {
        return state is State.InFate or State.InCriticalEncounter;
    }

    public static bool HasEnded(bool hasEnteredActivity, State state)
    {
        // Idle before the client awards FATE/CE participation means "waiting
        // for the event to engage", not "event finished". Only accept Idle as
        // completion after the state machine has observed the event once.
        return hasEnteredActivity && state == State.Idle;
    }
}
