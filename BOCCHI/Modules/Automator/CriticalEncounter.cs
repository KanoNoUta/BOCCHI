using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.IPC;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

public enum FinalApproachDecision
{
    Waiting,
    Arrived,
    StoppedBeforeArrival,
}

public static class CriticalEncounterNavigationPolicy
{
    public const int NavigationStartGraceMs = 2000;

    public const float FinalArrivalDistance = 5f;

    public const float MinFinalOffset = 6f;

    public const float MaxFinalOffset = 17f;

    public static bool IsInsideNavigationStartGrace(bool hasObservedNavigation, long elapsedMs)
    {
        return !hasObservedNavigation && elapsedMs < NavigationStartGraceMs;
    }

    public static long StartAtFirstTick(long? startedAt, long now)
    {
        return startedAt ?? now;
    }

    public static FinalApproachDecision EvaluateFinalApproach(
        Vector3 playerPosition,
        Vector3 finalTarget,
        bool navigationActive,
        bool hasObservedNavigation,
        long elapsedMs)
    {
        var distance = Vector3.Distance(playerPosition, finalTarget);
        if (float.IsFinite(distance) && distance <= FinalArrivalDistance)
        {
            return FinalApproachDecision.Arrived;
        }

        if (navigationActive || IsInsideNavigationStartGrace(hasObservedNavigation, elapsedMs))
        {
            return FinalApproachDecision.Waiting;
        }

        return FinalApproachDecision.StoppedBeforeArrival;
    }

    public static bool CanSubmitApproach(bool registrationOpen, bool playerInEncounter)
    {
        return registrationOpen && !playerInEncounter;
    }

    public static bool CanSubmitFinalApproach(
        bool registrationOpen,
        bool finalDestinationSubmitted,
        bool isCloseToZone,
        bool pathfindingInProgress)
    {
        return registrationOpen
               && !finalDestinationSubmitted
               && isCloseToZone
               && !pathfindingInProgress;
    }

    public static Vector3 CreateFinalTarget(Vector3 center, float angle, float distance)
    {
        distance = Math.Clamp(distance, MinFinalOffset, MaxFinalOffset);
        return new Vector3(
            center.X + MathF.Cos(angle) * distance,
            center.Y,
            center.Z + MathF.Sin(angle) * distance);
    }

    public static bool ShouldAbandon(bool registrationOpen, bool isInZone, bool playerInEncounter)
    {
        return !registrationOpen && !isInZone && !playerInEncounter;
    }
}

public class CriticalEncounter : Activity
{
    private readonly CriticalEncountersModule source;

    private CriticalEncounterSnapshot Encounter
    {
        get => source.CriticalEncounters[data.Id];
    }

    public CriticalEncounter(EventData data, Lifestream lifestream, VNavmesh vnav, AutomatorModule module, CriticalEncountersModule source)
        : base(data, lifestream, vnav, module)
    {
        this.source = source;

        handlers.Add(ActivityState.WaitingToStartCriticalEncounter, GetWaitingToStartCriticalEncounterChain);
    }

    protected override TaskManagerTask GetPathfindingWatcher(StateManagerModule states)
    {
        long? watcherStartedAt = null;
        var hasObservedInitialNavigation = false;
        Vector3? finalDestination = null;
        var finalDestinationSubmitted = false;
        var finalDestinationSubmittedAt = 0L;
        var hasObservedFinalNavigation = false;

        return new TaskManagerTask(() =>
        {
            var now = Environment.TickCount64;
            watcherStartedAt = CriticalEncounterNavigationPolicy.StartAtFirstTick(watcherStartedAt, now);
            var navigationActive = IsNavigationActive();
            if (!finalDestinationSubmitted)
            {
                hasObservedInitialNavigation |= navigationActive;
            }

            var critical = module.GetModule<CriticalEncountersModule>();
            var encounter = critical.CriticalEncounters[data.Id];
            var registrationOpen = encounter.State == DynamicEventState.Register;
            var playerInEncounter = IsPlayerInThisEncounter(states);

            if (playerInEncounter && !registrationOpen)
            {
                StopAtArrivalAndTryDismount("CriticalEncounter.ArrivalDismount");
                return true;
            }

            if (CriticalEncounterNavigationPolicy.ShouldAbandon(
                    registrationOpen,
                    IsInZone(),
                    playerInEncounter))
            {
                if (navigationActive)
                {
                    vnav.Stop();
                }

                module.Debug($"Skipping started critical encounter outside its area: {GetName()}");
                state = ActivityState.Done;
                return true;
            }

            if (!IsValid())
            {
                state = ActivityState.Done;
                return true;
            }

            if (!registrationOpen)
            {
                StopAtArrivalAndTryDismount("CriticalEncounter.ArrivalDismount");
                return true;
            }

            if (finalDestination == null)
            {
                var rand = module.GetModule<AutomatorModule>().random;
                var angle = (float)(rand.NextDouble() * MathF.PI * 2);
                var distance = CriticalEncounterNavigationPolicy.MinFinalOffset
                               + (float)rand.NextDouble()
                               * (CriticalEncounterNavigationPolicy.MaxFinalOffset
                                  - CriticalEncounterNavigationPolicy.MinFinalOffset);
                var candidate = CriticalEncounterNavigationPolicy.CreateFinalTarget(
                    GetPosition(),
                    angle,
                    distance);
                finalDestination = vnav.FindPointOnFloor(candidate, false, 0.5f) ?? candidate;
                module.Debug($"Selected CE final random point: {finalDestination.Value}");
            }

            if (CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
                    registrationOpen,
                    finalDestinationSubmitted,
                    IsCloseToZone(),
                    IsPathfindingInProgress()))
            {
                if (BOCCHI.Pathfinding.AggroAvoidanceNavigation.PathfindAndMoveTo(
                        vnav,
                        finalDestination.Value,
                        false))
                {
                    module.Debug($"Pathfinding to CE final random point: {finalDestination.Value}");
                    finalDestinationSubmitted = true;
                    finalDestinationSubmittedAt = now;
                    hasObservedFinalNavigation = false;
                }
            }

            if (finalDestinationSubmitted && finalDestination is { } finalTarget)
            {
                navigationActive = IsNavigationActive();
                hasObservedFinalNavigation |= navigationActive;
                var decision = CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
                    Player.Position,
                    finalTarget,
                    navigationActive,
                    hasObservedFinalNavigation,
                    now - finalDestinationSubmittedAt);

                if (decision == FinalApproachDecision.Arrived)
                {
                    StopAtArrivalAndTryDismount("CriticalEncounter.ArrivalDismount");
                    return true;
                }

                if (decision == FinalApproachDecision.Waiting)
                {
                    return false;
                }

                throw new VnavmeshStoppedException();
            }

            if (!navigationActive)
            {
                if (CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(
                        hasObservedInitialNavigation,
                        now - watcherStartedAt.Value))
                {
                    return false;
                }

                throw new VnavmeshStoppedException();
            }

            return false;
        }, new TaskManagerConfiguration { TimeLimitMS = 180000, ShowError = false });
    }

    protected override bool ShouldContinueNavigation(StateManagerModule states)
    {
        return CriticalEncounterNavigationPolicy.CanSubmitApproach(
            Encounter.State == DynamicEventState.Register,
            IsPlayerInThisEncounter(states));
    }


    private Func<Chain> GetWaitingToStartCriticalEncounterChain(StateManagerModule states)
    {
        return () =>
        {
            return Chain.Create("Illegal:WaitingToStartCriticalEncounter")
                .Then(new TaskManagerTask(() =>
                    {
                        var critical = module.GetModule<CriticalEncountersModule>();
                        var encounter = critical.CriticalEncounters[data.Id];
                        var registrationOpen = encounter.State == DynamicEventState.Register;
                        var playerInEncounter = IsPlayerInThisEncounter(states);

                        if (playerInEncounter)
                        {
                            if (IsNavigationActive())
                            {
                                vnav.Stop();
                            }

                            return true;
                        }

                        if (CriticalEncounterNavigationPolicy.ShouldAbandon(
                                registrationOpen,
                                IsInZone(),
                                playerInEncounter))
                        {
                            if (IsNavigationActive())
                            {
                                vnav.Stop();
                            }

                            module.Debug($"Skipping started critical encounter outside its area: {GetName()}");
                            state = ActivityState.Done;
                            return true;
                        }

                        if (!IsValid())
                        {
                            state = ActivityState.Done;
                            return true;
                        }

                        if (!vnav.IsRunning() && states.GetState() == State.InCombat)
                        {
                            if (module.Config.ShouldToggleAiProvider)
                            {
                                module.SetAiProviderEnabled(true);
                            }

                            if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
                            {
                                Chat.ExecuteCommand("/aeTargetSelector off");
                                Chat.ExecuteCommand("/aepull on");
                            }
                        }

                        // Arrival can coincide with mount/action animation
                        // lock. Keep retrying at a controlled rate throughout
                        // CE registration and the transition into battle.
                        if (!IsNavigationActive())
                        {
                            RetryArrivalDismount("CriticalEncounter.ArrivalDismount");
                        }

                        return false;
                    },
                    new TaskManagerConfiguration
                    {
                        TimeLimitMS = 180000,
                    }))
                .ConditionalThen(_ => state != ActivityState.Done, _ => PromeRotationController.Start())
                .ConditionalThen(_ => state != ActivityState.Done, _ => state = ActivityState.Participating);
        };
    }

    public override unsafe bool IsValid()
    {
        if (Encounter.State == DynamicEventState.Register)
        {
            return true;
        }

        var dec = DynamicEventContainer.GetInstance();
        return dec != null && Encounter.DynamicEventId == dec->CurrentEventId;
    }

    private unsafe bool IsPlayerInThisEncounter(StateManagerModule states)
    {
        if (states.GetState() == State.InCriticalEncounter)
        {
            return true;
        }

        var container = DynamicEventContainer.GetInstance();
        return container != null && Encounter.DynamicEventId == container->CurrentEventId;
    }

    protected override float GetRadius()
    {
        // This is kind of an assumption, but it seems accurate enough for most encounters.
        // return Encounter.Unknown4;
        return 19f;
    }

    protected override Vector3 GetPosition()
    {
        return Encounter.MapMarker.Position;
    }

    public override string GetName()
    {
        return Encounter.Name.ToString();
    }

    private bool IsCloseToZone(float radius = 50f)
    {
        return Player.DistanceTo(GetPosition()) <= radius;
    }


    protected override unsafe bool IsActivityTarget(IBattleNpc obj)
    {
        try
        {
            var battleChara = (BattleChara*)obj.Address;

            var isRelatedToCurrentEvent = battleChara->EventId.EntryId == Player.BattleChara->EventId.EntryId;

            return obj.SubKind == (byte)BattleNpcSubKind.Combatant && isRelatedToCurrentEvent;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex.Message);
            return false;
        }
    }

    protected override ActivityState GetPostPathfindingState()
    {
        var states = module.GetModule<StateManagerModule>();
        if (CriticalEncounterNavigationPolicy.ShouldAbandon(
                Encounter.State == DynamicEventState.Register,
                IsInZone(),
                IsPlayerInThisEncounter(states)))
        {
            module.Debug($"Skipping started critical encounter outside its area: {GetName()}");
            return ActivityState.Done;
        }

        return ActivityState.WaitingToStartCriticalEncounter;
    }
}
