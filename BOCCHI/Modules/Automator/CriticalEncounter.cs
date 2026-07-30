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
        var finalDestinationSubmittedAt = 0L;
        var hasObservedFinalNavigation = false;

        return new TaskManagerTask(() =>
        {
            var now = Environment.TickCount64;
            watcherStartedAt = CriticalEncounterNavigationPolicy.StartAtFirstTick(watcherStartedAt, now);
            var navigationActive = IsNavigationActive();
            if (finalDestination == null)
            {
                hasObservedInitialNavigation |= navigationActive;
            }

            if (!IsValid())
            {
                throw new Exception("Activity is no longer valid.");
            }

            if (finalDestination == null
                && !IsInZone()
                && IsCloseToZone()
                && !IsPathfindingInProgress())
            {
                var rand = module.GetModule<AutomatorModule>().random;
                var angle = (float)(rand.NextDouble() * MathF.PI * 2);
                var distance = (float)(rand.NextDouble() * 20f);
                var offsetX = MathF.Cos(angle) * distance;
                var offsetZ = MathF.Sin(angle) * distance;

                var randomPoint = new Vector3(GetPosition().X + offsetX, GetPosition().Y, GetPosition().Z + offsetZ);
                module.Debug($"Pathfinding to random point: {randomPoint}");

                if (vnav.PathfindAndMoveTo(randomPoint, false))
                {
                    finalDestination = randomPoint;
                    finalDestinationSubmittedAt = now;
                    hasObservedFinalNavigation = false;
                }
            }

            if (finalDestination == null && IsInZone())
            {
                StopAtArrivalAndTryDismount("CriticalEncounter.ArrivalDismount");

                return true;
            }

            var critical = module.GetModule<CriticalEncountersModule>();
            var encounter = critical.CriticalEncounters[data.Id];

            if (encounter.State != DynamicEventState.Register)
            {
                if (IsPlayerInThisEncounter(states))
                {
                    StopAtArrivalAndTryDismount("CriticalEncounter.ArrivalDismount");
                    return true;
                }

                throw new Exception("This event started without you");
            }

            if (finalDestination is { } finalTarget)
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


    private Func<Chain> GetWaitingToStartCriticalEncounterChain(StateManagerModule states)
    {
        return () =>
        {
            return Chain.Create("Illegal:WaitingToStartCriticalEncounter")
                .Then(new TaskManagerTask(() =>
                    {
                        if (!IsValid())
                        {
                            throw new Exception("The critical encounter appears to have started without you.");
                        }

                        var critical = module.GetModule<CriticalEncountersModule>();
                        var encounter = critical.CriticalEncounters[data.Id];

                        if (encounter.State == DynamicEventState.Battle
                            && !IsPlayerInThisEncounter(states))
                        {
                            throw new Exception("The critical encounter appears to have started without you.");
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

                        return states.GetState() == State.InCriticalEncounter;
                    },
                    new TaskManagerConfiguration
                    {
                        TimeLimitMS = 180000,
                    }))
                .Then(_ => PromeRotationController.Start())
                .Then(_ => state = ActivityState.Participating);
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
        return ActivityState.WaitingToStartCriticalEncounter;
    }
}
