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

    private static readonly TimeSpan PostActivityReturnRetryDelay = TimeSpan.FromSeconds(5);

    private static bool IsChainActive
    {
        get => AutomatorChainPolicy.IsActive(
            ChainManager.Queues.Values.Select(queue => (queue.IsRunning, queue.QueueCount)));
    }

    public Activity? Activity { get; private set; } = null;

    private int idleTime = 0;

    private bool postActivityReturnPending;

    private int postActivityReturnAttempts;

    private DateTime nextPostActivityReturnAttempt = DateTime.MinValue;

    private string postActivityReturnReason = "activity completed";

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
                    module.Debug($"Resuming running activity: {Activity.GetName()}");
                }

                return;
            }

            if (states.GetState() == State.InFate)
            {
                Activity ??= FindFate(module, lifestream, vnav);

                if (Activity != null)
                {
                    module.Debug($"Resuming running activity: {Activity.GetName()}");
                }

                return;
            }
        }

        if (Activity != null && !Activity.IsValid())
        {
            var shouldReturn = PostActivityReturnPolicy.ShouldQueue(Activity.data.Type);
            var returnReason = $"FATE {Activity.data.Id} ended";
            Plugin.Chain.Abort();
            vnav.Stop();
            module.SetAiProviderEnabled(false);
            PromeRotationController.Stop();
            ClearActivity();
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
                var shouldReturn = PostActivityReturnPolicy.ShouldQueue(Activity.data.Type);
                var returnReason = $"FATE {Activity.data.Id} completed";
                module.SetAiProviderEnabled(false);
                PromeRotationController.Stop();
                ClearActivity();
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

        // Try and get the next activity
        Activity ??= module.Config.ShouldDoCriticalEncounters ? FindCriticalEncounter(module, lifestream, vnav) : null;
        Activity ??= module.Config.ShouldDoFates ? FindFate(module, lifestream, vnav) : null;
        if (Activity != null)
        {
            Svc.Log.Info($"Selected activity: {Activity.GetName()}");
            return;
        }

        var closest = AethernetData.GetClosestToPlayer();
        if (closest.DistanceToPlayer() <= 4.5f)
        {
            return;
        }

        idleTime += framework.UpdateDelta.Milliseconds;
        if (idleTime > 3000)
        {
            idleTime = 0;

            Plugin.Chain.Submit(ChainHelper.ReturnChain(new ReturnChainConfig { ApproachAetheryte = true }));
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

            return new FateActivity(fate.Data, lifestream, vnav, module, fate);
        }

        return null;
    }

    public void Refresh()
    {
        ClearActivity();
        idleTime = 0;
        postActivityReturnPending = false;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        postActivityReturnReason = "activity completed";
    }

    public void QueuePostActivityReturn(string reason)
    {
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

        var returnChain = ChainHelper.ReturnChain(new ReturnChainConfig
        {
            ApproachAetheryte = true,
            ForceReturn = true,
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
    public static bool ShouldQueue(EventType eventType)
    {
        return eventType == EventType.Fate;
    }
}
