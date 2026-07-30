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

public abstract class Activity : IDisposable
{
    public readonly EventData data;

    private readonly Lifestream lifestream;

    protected readonly VNavmesh vnav;

    protected readonly AutomatorModule module;

    private bool disposed;

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
        if (disposed || !IsValid())
        {
            return null;
        }

        return handlers[state](states);
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
                .ConditionalThen(ShouldToggleAi, _ => module.SetAiProviderEnabled(false))
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
            var isFate = data.Type == EventType.Fate;
            var destination = GetPosition();
            var pathfinderConfig = module.PluginConfig.PathfinderConfig;
            var baseCamp = ZoneData.GetBaseCampAethernet().GetData();
            var selectedPlan = SmartNavigation.DecideFallback(
                Player.Position,
                destination,
                data,
                AethernetData.All().ToArray(),
                baseCamp,
                pathfinderConfig.ReturnCost,
                pathfinderConfig.TeleportCost,
                "Fast runtime selection; candidate precomputation is not allowed to block movement.");
            var delayCriticalEncounter = !isFate && module.Config.ShouldDelayCriticalEncounters;
            var plan = delayCriticalEncounter
                ? SmartNavigation.PreferBaseCampReturn(selectedPlan, baseCamp)
                : selectedPlan;
            var pathfinding = new PathfindingChain(vnav, destination, data);

            if (plan.Type != selectedPlan.Type)
            {
                Svc.Log.Info(
                    $"Delayed CE navigation prefers base-camp Return: " +
                    $"{selectedPlan.Type} -> {plan.Type}, destination={plan.DestinationAethernet}");
            }

            Svc.Log.Info(
                $"Using navigation plan: {plan.Type} ({plan.Cost:F2}), " +
                $"source={plan.SourceAethernet}, destination={plan.DestinationAethernet}, " +
                $"fallback={plan.UsedFallback}");

            var chain = Chain.Create("Illegal:Pathfinding")
                .ConditionalThen(_ => isFate && module.Config.ShouldStanceOnBeforeDoFates && Player.Job.IsTank(), new StanceChain(isFate))
                .ConditionalThen(_ => !isFate && module.Config.ShouldStanceOffBeforeCriticalEncounters && Player.Job.IsTank(), new StanceChain(isFate))
                .ConditionalWait(_ => delayCriticalEncounter, Random.Shared.Next((int)module.Config.MinDelay * 1000, (int)module.Config.MaxDelay * 1000));

            switch (plan.Type)
            {
                case NavigationType.Walk:
                    chain = AppendPathfinding(chain, destination, pathfinding, states);
                    break;

                case NavigationType.ReturnWalk:
                {
                    var returnChain = ChainHelper.ReturnChain(new ReturnChainConfig
                    {
                        ApproachAetheryte = true,
                        ForceReturn = true,
                    });
                    chain
                        .ConditionalThen(_ => ShouldContinueNavigation(states), returnChain)
                        .ConditionalThen(_ => ShouldContinueNavigation(states), _ => LogReturnFailure(returnChain, plan))
                        .BreakIf(() => ShouldContinueNavigation(states)
                            && !TransitCompletionPolicy.CanContinueAfterReturn(returnChain.Succeeded));
                    chain = AppendPathfinding(chain, destination, pathfinding, states);
                    break;
                }

                case NavigationType.ReturnTeleportWalk:
                {
                    var returnChain = ChainHelper.ReturnChain(new ReturnChainConfig
                    {
                        ApproachAetheryte = true,
                        ForceReturn = true,
                    });
                    var teleport = ChainHelper.TeleportChain(
                        plan.DestinationAethernet,
                        plan.SourceAethernet,
                        mountAfterTeleport: false);
                    chain
                        .ConditionalThen(_ => ShouldContinueNavigation(states), returnChain)
                        .ConditionalThen(_ => ShouldContinueNavigation(states), _ => LogReturnFailure(returnChain, plan))
                        .BreakIf(() => ShouldContinueNavigation(states)
                            && !TransitCompletionPolicy.CanContinueAfterReturn(returnChain.Succeeded))
                        .ConditionalThen(_ => ShouldContinueNavigation(states), teleport)
                        .ConditionalThen(_ => ShouldContinueNavigation(states), _ => LogTeleportFailure(teleport, plan))
                        .BreakIf(() => ShouldContinueNavigation(states) && !teleport.Succeeded)
                        .Debug("Waiting for lifestream to not be 'busy'")
                        .ConditionalThen(
                            _ => ShouldContinueNavigation(states),
                            new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));
                    chain = AppendPathfinding(chain, destination, pathfinding, states);
                    break;
                }

                case NavigationType.WalkTeleportWalk:
                {
                    var sourcePosition = plan.SourceAethernet.GetData().NavigationPosition;
                    var teleport = ChainHelper.TeleportChain(
                        plan.DestinationAethernet,
                        plan.SourceAethernet,
                        mountAfterTeleport: false);
                    chain
                        .ConditionalThen(
                            _ => ShouldContinueNavigation(states) && ShouldMountToPathfindTo(sourcePosition),
                            ChainHelper.MountChain())
                        .ConditionalThen(_ => ShouldContinueNavigation(states), teleport)
                        .ConditionalThen(_ => ShouldContinueNavigation(states), _ => LogTeleportFailure(teleport, plan))
                        .BreakIf(() => ShouldContinueNavigation(states) && !teleport.Succeeded)
                        .Debug("Waiting for lifestream to not be 'busy'")
                        .ConditionalThen(
                            _ => ShouldContinueNavigation(states),
                            new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));
                    chain = AppendPathfinding(chain, destination, pathfinding, states);
                    break;
                }
            }

            chain
                .ConditionalThen(
                    _ => ShouldContinueNavigation(states) && !pathfinding.TransitReachedEnd,
                    GetPathfindingWatcher(states))
                // Each activity owns its precise arrival and dismount timing;
                // this avoids treating the outer edge of a large event radius
                // as arrival while still guaranteeing a combat-ready handoff.
                .ConditionalThen(_ => state != ActivityState.Done, _ => state = GetPostPathfindingState());

            return chain;
        };
    }

    private static void LogReturnFailure(ReturnChain returnChain, NavigationPlan plan)
    {
        if (returnChain.Succeeded)
        {
            return;
        }

        Svc.Log.Error(
            $"Stopping navigation because Return did not complete for {plan.Type}; " +
            $"reason={returnChain.FailureReason ?? "return chain timed out or was aborted"}");
    }

    private static void LogTeleportFailure(TeleportChain teleport, NavigationPlan plan)
    {
        if (teleport.Succeeded)
        {
            return;
        }

        Svc.Log.Error(
            $"Stopping navigation because aethernet teleport did not complete: " +
            $"{plan.SourceAethernet} -> {plan.DestinationAethernet}; " +
            $"reason={teleport.FailureReason ?? "teleport chain timed out or was aborted"}");
    }

    private Chain AppendPathfinding(
        Chain chain,
        Vector3 destination,
        PathfindingChain pathfinding,
        StateManagerModule states)
    {
        return chain
            // Mount before submitting movement. Submitting a ground route and
            // mounting afterwards interrupts vnavmesh and causes visible
            // stop/start motion; teleport routes could previously do it twice.
            .ConditionalThen(
                _ => ShouldContinueNavigation(states) && ShouldMountToPathfindTo(destination),
                ChainHelper.MountChain())
            .ConditionalThen(_ => ShouldContinueNavigation(states), pathfinding)
            .BreakIf(() => pathfinding.TransitAttempted && !pathfinding.TransitReachedEnd);
    }


    private Func<Chain> GetParticipatingChain(StateManagerModule states)
    {
        return () =>
        {
            var lastFateTargetPosition = Vector3.Zero;
            long? unmountStartedAt = null;

            void ApproachFateTarget(IBattleNpc? target)
            {
                if (data.Type != EventType.Fate || target == null || Svc.Condition[ConditionFlag.InCombat])
                {
                    return;
                }

                var centerDistance = Vector3.Distance(Player.Position, target.Position);
                if (FateNavigationPolicy.IsTargetInEngagementRange(
                        centerDistance,
                        target.HitboxRadius,
                        module.Config.EngagementRange))
                {
                    if (vnav.IsRunning())
                    {
                        vnav.Stop();
                    }

                    return;
                }

                var targetMovement = lastFateTargetPosition == Vector3.Zero
                    ? float.MaxValue
                    : Vector3.Distance(target.Position, lastFateTargetPosition);
                if (FateNavigationPolicy.ShouldRepath(IsNavigationActive(), targetMovement)
                    && EzThrottler.Throttle("Participating.FateApproach", 1000)
                    && vnav.PathfindAndMoveTo(target.Position, false))
                {
                    lastFateTargetPosition = target.Position;
                }
            }

            return Chain.Create("Illegal:Participating")
                .Then(new TaskManagerTask(() =>
                {
                    var now = Environment.TickCount64;
                    unmountStartedAt ??= now;
                    var elapsedMs = now - unmountStartedAt.Value;
                    var decision = ActivityParticipationState.GetCombatStartupDecision(
                        Svc.Condition[ConditionFlag.Mounted],
                        elapsedMs);

                    if (decision == CombatStartupDecision.Ready)
                    {
                        return true;
                    }

                    if (decision == CombatStartupDecision.TimedOut)
                    {
                        throw new TimeoutException("Unable to dismount before starting combat automation.");
                    }

                    RetryArrivalDismount("Participating.ArrivalDismount");
                    return false;
                }, new TaskManagerConfiguration
                {
                    TimeLimitMS = ActivityParticipationState.UnmountTimeoutMs + 2000,
                    AbortOnTimeout = true,
                    AbortOnError = true,
                    ShowError = false,
                }))
                .Then(_ => PromeRotationController.Start())
                .ConditionalThen(_ => module.Config.ShouldToggleAiProvider, _ => module.SetAiProviderEnabled(true))
                .ConditionalThen(_ => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded), _ =>
                {
                    Chat.ExecuteCommand("/aeTargetSelector off");
                    Chat.ExecuteCommand("/aepull on");
                })
                .Then(_ => vnav.Stop())
                .Then(new TaskManagerTask(() =>
                {
                    if (HasParticipationEnded(states))
                    {
                        return true;
                    }

                    if (!module.Config.ShouldForceTarget || !EzThrottler.Throttle("Participating.ForceTarget", 500))
                    {
                        return false;
                    }

                    var enemies = GetEnemies();
                    IBattleNpc? target;

                    if (enemies.Any(e => DangerousEnemies.Contains(e.BaseId) && e.CurrentHp > 0))
                    {
                        target = enemies.FirstOrDefault(e => DangerousEnemies.Contains(e.BaseId) && e.CurrentHp > 0);
                    }
                    else
                    {
                        target = module.Config.ShouldForceTargetCentralEnemy ? enemies.Centroid() : enemies.Closest();
                    }

                    Svc.Targets.Target = target;
                    ApproachFateTarget(target);

                    return false;
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

    protected void StopAtArrivalAndTryDismount(string throttleKey)
    {
        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        RetryArrivalDismount(throttleKey);
    }

    protected static void RetryArrivalDismount(string throttleKey)
    {
        if (Svc.Condition[ConditionFlag.Mounted]
            && EzThrottler.Throttle(throttleKey, 750))
        {
            Actions.TryUnmount();
        }
    }

    private bool ShouldMountToPathfindTo(Vector3 destination)
    {
        if (!module.PluginConfig.TeleporterConfig.ShouldMount)
        {
            return false;
        }

        if (Svc.Condition[ConditionFlag.Mounted]
            || Svc.Condition[ConditionFlag.InCombat]
            || Player.IsCasting
            || Player.IsDead)
        {
            return false;
        }

        return Vector3.Distance(Player.Position, destination) > 20f;
    }

    protected abstract float GetRadius();

    protected abstract TaskManagerTask GetPathfindingWatcher(StateManagerModule states);

    protected virtual bool ShouldContinueNavigation(StateManagerModule states)
    {
        return true;
    }

    public abstract bool IsValid();

    protected abstract Vector3 GetPosition();

    public abstract string GetName();

    protected abstract ActivityState GetPostPathfindingState();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        GC.SuppressFinalize(this);
    }
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
    public const int UnmountTimeoutMs = 8000;

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

    public static CombatStartupDecision GetCombatStartupDecision(bool mounted, long elapsedMs)
    {
        if (!mounted)
        {
            return CombatStartupDecision.Ready;
        }

        return elapsedMs >= UnmountTimeoutMs
            ? CombatStartupDecision.TimedOut
            : CombatStartupDecision.WaitingForUnmount;
    }
}

public enum CombatStartupDecision
{
    WaitingForUnmount,
    Ready,
    TimedOut,
}
