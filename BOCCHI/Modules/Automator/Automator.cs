using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.StateManager;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Automator;

public class Automator
{
    private const int MaxPostActivityReturnAttempts = 3;

    // A CE must still be open after the optional randomized delay and the
    // return/aethernet transit. Selecting one during its final seconds makes
    // Activity.IsValid() abort the route while the player is standing at the
    // base-camp crystal.
    private const double CriticalEncounterTransitReserveSeconds = 45d;

    private static readonly TimeSpan PostActivityReturnRetryDelay = TimeSpan.FromSeconds(5);

    private static bool IsChainActive
    {
        get => AutomatorChainPolicy.IsActive(
            ChainManager.Queues.Values.Select(queue => (queue.IsRunning, queue.QueueCount)));
    }

    private static long lastFateDiagnosticAt;

    public Activity? Activity { get; private set; } = null;

    private int idleTime = 0;

    private bool postActivityReturnPending;

    private int postActivityReturnAttempts;

    private DateTime nextPostActivityReturnAttempt = DateTime.MinValue;

    private string postActivityReturnReason = "activity completed";

    private bool preferFateAfterCriticalEncounter;

    public void PostUpdate(AutomatorModule module, IFramework framework)
    {
        // Module dispatch normally filters disabled modules, but the emergency
        // stop must also be safe when it is clicked during the same update.
        if (!module.IsEnabled)
        {
            return;
        }

        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var lifestream = module.GetIPCSubscriber<Lifestream>();
        if (!vnav.IsReady() || !lifestream.IsReady())
        {
            return;
        }

        var states = module.GetModule<StateManagerModule>();
        if (Activity == null && postActivityReturnPending)
        {
            if (IsChainActive || HandlePostActivityReturn(module, states))
            {
                return;
            }
        }

        if (Activity == null)
        {
            if (states.GetState() == State.InCombat)
            {
                return;
            }

            if (states.GetState() == State.InCriticalEncounter)
            {
                var critical = module.GetModule<CriticalEncountersModule>();
                var encounter = critical.CriticalEncounters.Values.LastOrDefault(ev => ev.State != DynamicEventState.Inactive);
                if (encounter == null)
                {
                    return;
                }

                if (!EventData.CriticalEncounters.TryGetValue(encounter.DynamicEventId, out var data))
                {
                    return;
                }

                Activity = new CriticalEncounter(data, lifestream, vnav, module, critical);

                if (Activity != null)
                {
                    preferFateAfterCriticalEncounter = ActivitySelectionPolicy.AfterActivitySelected(
                        preferFateAfterCriticalEncounter,
                        Activity.data.Type);
                    module.Debug($"Resuming running activity: {Activity.GetName()}");
                }

                return;
            }

            if (states.GetState() == State.InFate)
            {
                Activity ??= FindFate(module, lifestream, vnav);

                if (Activity != null)
                {
                    preferFateAfterCriticalEncounter = ActivitySelectionPolicy.AfterActivitySelected(
                        preferFateAfterCriticalEncounter,
                        Activity.data.Type);
                    module.Debug($"Resuming running activity: {Activity.GetName()}");
                }

                return;
            }
        }

        if (Activity != null && !Activity.IsValid())
        {
            var endedActivityType = Activity.data.Type;
            var shouldReturn = PostActivityReturnPolicy.ShouldQueue(
                Activity.data.Type,
                module.IsIndependentNavigationRunning);
            var returnReason = $"FATE {Activity.data.Id} ended";
            Plugin.Chain.Abort();
            vnav.Stop();
            // A teleport may already have been accepted by Lifestream before
            // the activity expired. Abort it so the player is not stranded at
            // the destination shard with no activity to continue.
            try
            {
                if (lifestream.IsReady())
                {
                    lifestream.Abort();
                }
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Could not abort Lifestream after the activity expired.");
            }
            module.SetAiProviderEnabled(false);
            PromeRotationController.Stop();
            ClearActivity();
            preferFateAfterCriticalEncounter = ActivitySelectionPolicy.AfterActivityEnded(
                preferFateAfterCriticalEncounter,
                endedActivityType);
            if (shouldReturn)
            {
                QueuePostActivityReturn(returnReason);
            }
            // Let the lower-priority FATE/CE trackers refresh before selecting
            // another activity. Continuing in this same update can immediately
            // reselect the just-despawned FATE from their stale cache.
            return;
        }

        if (IsChainActive)
        {
            return;
        }

        if (Activity != null)
        {
            if (Activity.state == ActivityState.Done)
            {
                var endedActivityType = Activity.data.Type;
                var shouldReturn = PostActivityReturnPolicy.ShouldQueue(
                    Activity.data.Type,
                    module.IsIndependentNavigationRunning);
                var returnReason = $"FATE {Activity.data.Id} completed";
                module.SetAiProviderEnabled(false);
                PromeRotationController.Stop();
                ClearActivity();
                preferFateAfterCriticalEncounter = ActivitySelectionPolicy.AfterActivityEnded(
                    preferFateAfterCriticalEncounter,
                    endedActivityType);
                if (shouldReturn)
                {
                    QueuePostActivityReturn(returnReason);
                }
                return;
            }

            var chain = Activity.GetChain(states);
            if (chain == null)
            {
                return;
            }

            Plugin.Chain.Submit(chain);
            return;
        }

        if (!module.Config.ShouldDoFates && !module.Config.ShouldDoCriticalEncounters)
        {
            return;
        }

        // Keep CE as the default priority, but after any CE ends give one
        // eligible FATE the first chance. If none exists, retain that chance
        // while continuing to process available CEs.
        foreach (var activityType in ActivitySelectionPolicy.GetOrder(preferFateAfterCriticalEncounter))
        {
            Activity = activityType switch
            {
                EventType.CriticalEncounter when module.Config.ShouldDoCriticalEncounters
                    => FindCriticalEncounter(module, lifestream, vnav),
                EventType.Fate when module.Config.ShouldDoFates
                    => FindFate(module, lifestream, vnav),
                _ => null,
            };

            if (Activity == null)
            {
                continue;
            }

            preferFateAfterCriticalEncounter = ActivitySelectionPolicy.AfterActivitySelected(
                preferFateAfterCriticalEncounter,
                Activity.data.Type);
            break;
        }
        if (Activity != null)
        {
            Svc.Log.Info($"Selected activity: {Activity.GetName()}");
            return;
        }

        // North Horn's maintained aethernet coordinates can sit a few yalms
        // away from the physical crystal. Prefer the live object table the
        // same way ReturnChain does; otherwise the automator keeps resubmitting
        // the return chain while the player is already standing at the shard.
        if (ZoneData.IsNearAnyAethernetShard(4.5f))
        {
            return;
        }

        idleTime += framework.UpdateDelta.Milliseconds;
        if (idleTime > 3000)
        {
            idleTime = 0;

            Plugin.Chain.Submit(ChainHelper.ReturnChain(new ReturnChainConfig
            {
                ApproachAetheryte = true,
                StopCheck = () => !module.IsEnabled,
            }));
        }
    }

    private static CriticalEncounter? FindCriticalEncounter(AutomatorModule module, Lifestream lifestream, VNavmesh vnav)
    {
        if (!module.TryGetModule<CriticalEncountersModule>(out var source) || source == null)
        {
            return null;
        }

        foreach (var encounter in source.CriticalEncounters.Values)
        {
            if (encounter.EventType >= 4)
            {
                continue;
            }

            if (!module.Config.CriticalEncountersMap.TryGetValue(encounter.DynamicEventId, out var enabled) || !enabled)
            {
                continue;
            }

            if (encounter.State != DynamicEventState.Register)
            {
                continue;
            }

            var registrationRemaining = source.Tracker.TowerTimer.GetTimeRemainingToRegister(encounter);
            if (!CriticalEncounterSelectionPolicy.HasEnoughRegistrationTime(
                    registrationRemaining,
                    module.Config.ShouldDelayCriticalEncounters,
                    module.Config.MaxDelay,
                    CriticalEncounterTransitReserveSeconds))
            {
                continue;
            }

            if (!EventData.CriticalEncounters.TryGetValue(encounter.DynamicEventId, out var data))
            {
                continue;
            }

            return new CriticalEncounter(data, lifestream, vnav, module, source);
        }

        return null;
    }

    private static FateActivity? FindFate(AutomatorModule module, Lifestream lifestream, VNavmesh vnav)
    {
        if (!module.TryGetModule<FatesModule>(out var source) || source == null)
        {
            return null;
        }

        var liveFateIds = Svc.Fates.Select(fate => (uint)fate.FateId).ToHashSet();
        Fate? best = null;
        foreach (var fate in source.fates.Values)
        {
            if (!liveFateIds.Contains(fate.Id))
            {
                continue;
            }

            if (!module.Config.FatesMap.TryGetValue(fate.Id, out var enabled) || !enabled)
            {
                continue;
            }

            // IFate.TimeRemaining is unreliable for the custom North Horn
            // FATEs (a live FATE can read ~1.1s for its whole lifetime), so it
            // must not gate eligibility. Presence in Svc.Fates plus the user's
            // FatesMap checkbox is the liveness contract; a FATE that actually
            // despawns mid-travel is handled by Activity.IsValid().
            if (best == null)
            {
                best = fate;
            }
        }

        if (best == null)
        {
            var now = Environment.TickCount64;
            if (now - lastFateDiagnosticAt > 10000)
            {
                lastFateDiagnosticAt = now;
                var live = Svc.Fates
                    .Select(f => $"#{f.FateId} t={f.TimeRemaining}ms p={f.Progress}%")
                    .ToArray();
                var tracked = source.fates.Values
                    .Select(f => $"#{f.Id} t={f.TimeRemaining}ms name={f.Name}")
                    .ToArray();
                Svc.Log.Info(
                    $"FindFate returned no activity: live=[{string.Join(", ", live)}] " +
                    $"tracked=[{string.Join(", ", tracked)}]");
            }
        }

        return best == null ? null : new FateActivity(best.Data, lifestream, vnav, module, best);
    }

    public void Refresh()
    {
        ClearActivity();
        idleTime = 0;
        postActivityReturnPending = false;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        postActivityReturnReason = "activity completed";
        preferFateAfterCriticalEncounter = false;
    }

    public void SuspendForIndependentNavigation(string reason)
    {
        var hadActivity = Activity != null;
        ClearActivity();
        var cancelledReturn = CancelPostActivityReturn(reason);
        idleTime = 0;
        preferFateAfterCriticalEncounter = false;

        if (hadActivity && !cancelledReturn)
        {
            Svc.Log.Info($"Cleared active FATE/CE state because {reason} owns navigation.");
        }
    }

    public bool CancelPostActivityReturn(string reason)
    {
        if (!postActivityReturnPending)
        {
            return false;
        }

        postActivityReturnPending = false;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        idleTime = 0;
        Svc.Log.Info($"Cancelled forced base-camp return because {reason} owns navigation.");
        return true;
    }

    public void QueuePostActivityReturn(string reason, bool independentNavigationRunning = false)
    {
        if (independentNavigationRunning)
        {
            CancelPostActivityReturn("independent navigation");
            return;
        }

        if (postActivityReturnPending)
        {
            return;
        }

        postActivityReturnPending = true;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        postActivityReturnReason = reason;
        idleTime = 0;
        Svc.Log.Info($"Queued forced base-camp return after {reason}.");
    }

    private void ClearActivity()
    {
        Activity?.Dispose();
        Activity = null;
    }

    private bool HandlePostActivityReturn(AutomatorModule module, StateManagerModule states)
    {
        if (!postActivityReturnPending)
        {
            return false;
        }

        if (module.IsIndependentNavigationRunning)
        {
            CancelPostActivityReturn("independent navigation");
            return false;
        }

        // Do not select or walk toward another event while the completion
        // return is pending. Wait for combat/event state to settle, then use
        // Return explicitly rather than walking to a nearby shard.
        if (states.GetState() != State.Idle || Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
        {
            return true;
        }

        if (ZoneData.IsNearAethernetShard(
                ZoneData.GetBaseCampAethernet(),
                AethernetData.DISTANCE + 1f))
        {
            postActivityReturnPending = false;
            postActivityReturnAttempts = 0;
            Svc.Log.Info($"Post-activity return already satisfied after {postActivityReturnReason}.");
            return false;
        }

        var now = DateTime.UtcNow;
        if (now < nextPostActivityReturnAttempt)
        {
            return true;
        }

        if (postActivityReturnAttempts >= MaxPostActivityReturnAttempts)
        {
            Svc.Log.Error(
                $"Giving up forced base-camp return after {postActivityReturnReason}: " +
                $"{postActivityReturnAttempts} attempts failed.");
            postActivityReturnPending = false;
            postActivityReturnAttempts = 0;
            return false;
        }

        if (module.IsIndependentNavigationRunning)
        {
            CancelPostActivityReturn("independent navigation");
            return false;
        }

        var returnChain = ChainHelper.ReturnChain(new ReturnChainConfig
        {
            ApproachAetheryte = true,
            ForceReturn = true,
            StopCheck = () => !module.IsEnabled,
        });
        postActivityReturnAttempts++;
        var attempt = postActivityReturnAttempts;
        Svc.Log.Info(
            $"Starting forced base-camp return after {postActivityReturnReason} " +
            $"({attempt}/{MaxPostActivityReturnAttempts}).");

        Plugin.Chain.Submit(() =>
            Chain.Create("Illegal:PostActivityReturn")
                .Then(returnChain)
                .OnFinally(() =>
                {
                    if (!postActivityReturnPending)
                    {
                        return;
                    }

                    if (returnChain.Succeeded)
                    {
                        postActivityReturnPending = false;
                        postActivityReturnAttempts = 0;
                        nextPostActivityReturnAttempt = DateTime.MinValue;
                        idleTime = 0;
                        Svc.Log.Info($"Forced base-camp return completed after {postActivityReturnReason}.");
                        return;
                    }

                    nextPostActivityReturnAttempt = DateTime.UtcNow + PostActivityReturnRetryDelay;
                    Svc.Log.Warning(
                        $"Forced base-camp return failed after {postActivityReturnReason} " +
                        $"({attempt}/{MaxPostActivityReturnAttempts}); " +
                        $"reason={returnChain.FailureReason ?? "return chain timed out or was aborted"}.");
                }));
        return true;
    }
}

public static class AutomatorChainPolicy
{
    public static bool IsActive(IEnumerable<(bool IsRunning, int QueueCount)> queues)
    {
        // ChainManager keeps completed/empty queues alive briefly before its
        // cleanup tick removes them. Counting dictionary entries therefore
        // stalls the automator even though no work is actually running.
        return queues.Any(queue => queue.IsRunning || queue.QueueCount > 0);
    }
}

public static class PostActivityReturnPolicy
{
    public static bool ShouldQueue(EventType eventType, bool independentNavigationRunning = false)
    {
        return !independentNavigationRunning && eventType == EventType.Fate;
    }
}

public static class ActivitySelectionPolicy
{
    private static readonly IReadOnlyList<EventType> CriticalEncounterFirst =
        Array.AsReadOnly([EventType.CriticalEncounter, EventType.Fate]);

    private static readonly IReadOnlyList<EventType> FateFirst =
        Array.AsReadOnly([EventType.Fate, EventType.CriticalEncounter]);

    public static IReadOnlyList<EventType> GetOrder(bool preferFate)
    {
        return preferFate ? FateFirst : CriticalEncounterFirst;
    }

    public static bool AfterActivityEnded(bool preferFate, EventType activityType)
    {
        return preferFate || activityType == EventType.CriticalEncounter;
    }

    public static bool AfterActivitySelected(bool preferFate, EventType activityType)
    {
        return activityType == EventType.Fate ? false : preferFate;
    }
}
