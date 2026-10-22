using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Automator.Pots;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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

    // Decide "already home" only after teleports, casts and loading screens
    // have been quiet for this long. Judging mid-transition is how a late
    // teleport used to strand the player at the event's shard.
    private const int PostActivityReturnSettleMs = 2_500;

    // Module updates pause during loading screens; a gap this long means the
    // settle window must start again after the transition.
    private const int PostActivityUpdateGapMs = 500;

    // FATE/CE membership can linger (or belong to an overlapping FATE) after
    // the activity ended. Do not wait on it forever.
    private const int PostActivityEventStateGraceMs = 10_000;

    private const int PostActivityInFlightLimitMs = 60_000;

    private const int PostActivityLifestreamBusyLimitMs = 20_000;

    // AI and rotation are released when an activity ends. If enemies keep the
    // player in combat, Return can never be cast, so fight back after this.
    private const int PostActivityCombatDefenseDelayMs = 5_000;

    private const int PostActivityDiagnosticIntervalMs = 10_000;

    internal static bool IsChainActive
    {
        get => AutomatorChainPolicy.IsActive(
            ChainManager.Queues.Values.Select(queue => (queue.IsRunning, queue.QueueCount)));
    }

    private static long lastFateDiagnosticAt;

    public Activity? Activity { get; private set; } = null;

    public PotChestFarm? PotFarm { get; private set; }

    /// <summary>A pot wait or coffer farm owns the player; rotation must wait.</summary>
    public bool IsPotWorkActive => PotFarm != null || Activity is PotWaitActivity;

    // A farm that gave up while Cache Me If You Can is still up must not
    // restart for the same buff.
    private bool potFarmBlockedUntilBuffEnds;

    public bool IsPostActivityReturnPending => postActivityReturnPending;

    private int idleTime = 0;

    private bool postActivityReturnPending;

    private int postActivityReturnAttempts;

    private DateTime nextPostActivityReturnAttempt = DateTime.MinValue;

    private string postActivityReturnReason = "activity completed";

    private bool preferFateAfterCriticalEncounter;

    private int postActivityReturnGeneration;

    private long postActivityQueuedAtMs;

    private long lastPostActivityUpdateMs;

    private long? postActivitySettledSinceMs;

    private long? postActivityEventStateSinceMs;

    private long? postActivityInFlightSinceMs;

    private long? postActivityLifestreamBusySinceMs;

    private long? postActivityCombatSinceMs;

    private AutomatorModule? postActivityDefenseOwner;

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
        if (UpdatePotChestFarm(module, vnav))
        {
            return;
        }

        if (Activity == null && postActivityReturnPending)
        {
            UpdatePostActivityCombatDefense(module);
            if (IsChainActive || HandlePostActivityReturn(module, states, lifestream))
            {
                return;
            }
        }
        else
        {
            ReleasePostActivityCombatDefense("no forced return is pending");
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
            var endedPotFarmFate = GetPotFarmFate(module, Activity);
            // A pot wait ends when its pot spawns; the live pot is picked up
            // next, so there is nothing to return from.
            var shouldReturn = Activity is not PotWaitActivity
                && endedPotFarmFate == 0
                && PostActivityReturnPolicy.ShouldQueue(
                Activity.data.Type,
                module.PluginConfig.TeleporterConfig.ReturnAfterFate,
                module.PluginConfig.TeleporterConfig.ReturnAfterCriticalEncounter,
                module.IsIndependentNavigationRunning);
            var returnReason = $"{endedActivityType} {Activity.data.Id} ended";
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
            if (endedPotFarmFate != 0)
            {
                StartPotChestFarm(module, endedPotFarmFate);
            }
            else if (shouldReturn)
            {
                QueuePostActivityReturn(returnReason);
            }
            // Let the lower-priority FATE/CE trackers refresh before selecting
            // another activity. Continuing in this same update can immediately
            // reselect the just-despawned FATE from their stale cache.
            return;
        }

        if (Activity != null && ShouldYieldActivityToPot(module, states, Activity, out var yieldReason))
        {
            Svc.Log.Info($"[Pots] Leaving {Activity.GetName()}: {yieldReason}.");
            Plugin.Chain.Abort();
            vnav.Stop();
            module.SetAiProviderEnabled(false);
            PromeRotationController.Stop();
            ClearActivity();
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
                var endedPotFarmFate = GetPotFarmFate(module, Activity);
                var shouldReturn = Activity is not PotWaitActivity
                    && endedPotFarmFate == 0
                    && PostActivityReturnPolicy.ShouldQueue(
                    Activity.data.Type,
                    module.PluginConfig.TeleporterConfig.ReturnAfterFate,
                    module.PluginConfig.TeleporterConfig.ReturnAfterCriticalEncounter,
                    module.IsIndependentNavigationRunning);
                var returnReason = $"{endedActivityType} {Activity.data.Id} completed";
                module.SetAiProviderEnabled(false);
                PromeRotationController.Stop();
                ClearActivity();
                preferFateAfterCriticalEncounter = ActivitySelectionPolicy.AfterActivityEnded(
                    preferFateAfterCriticalEncounter,
                    endedActivityType);
                if (endedPotFarmFate != 0)
                {
                    StartPotChestFarm(module, endedPotFarmFate);
                }
                else if (shouldReturn)
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
        Activity ??= FindPotWait(module, lifestream, vnav);
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
                SkipReturnAtLanding = true,
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

        var pots = PotContext.Capture(module);
        if (module.Config.ShouldPreferPotFates && pots.HasStartableLivePot)
        {
            return null;
        }

        if (!PotTimingPolicy.AllowsStart(
                pots.Cycle,
                pots.Now,
                TimeSpan.FromMinutes(module.Config.PotCriticalEncounterCutoffMinutes),
                pots.PotWorkEnabled))
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
        var pots = PotContext.Capture(module);
        // A live pot beats every other FATE.
        if (pots.StartableLivePot is { } livePot
            && source.fates.TryGetValue(livePot, out var potFate)
            && liveFateIds.Contains(livePot))
        {
            return new FateActivity(potFate.Data, lifestream, vnav, module, potFate);
        }

        var fatesAllowed = PotTimingPolicy.AllowsStart(
            pots.Cycle,
            pots.Now,
            TimeSpan.FromMinutes(module.Config.PotFateCutoffMinutes),
            pots.PotWorkEnabled);
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

            // Pots were handled above; a pot that is not startable stays skipped.
            if (PotZoneData.IsPotFate(fate.Id) || !fatesAllowed)
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
        StopPotChestFarm("automator refreshed");
        potFarmBlockedUntilBuffEnds = false;
        ClearActivity();
        idleTime = 0;
        postActivityReturnPending = false;
        postActivityReturnGeneration++;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        postActivityReturnReason = "activity completed";
        preferFateAfterCriticalEncounter = false;
        ResetPostActivityTracking();
        // Every caller already stops the rotation and releases the AI.
        postActivityDefenseOwner = null;
    }

    public void SuspendForIndependentNavigation(string reason)
    {
        StopPotChestFarm(reason);
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
        postActivityReturnGeneration++;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        idleTime = 0;
        ResetPostActivityTracking();
        ReleasePostActivityCombatDefense(reason);
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
        postActivityReturnGeneration++;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        postActivityReturnReason = reason;
        idleTime = 0;
        ResetPostActivityTracking();
        postActivityQueuedAtMs = Environment.TickCount64;
        Svc.Log.Info($"Queued forced base-camp return after {reason}.");
    }

    private void StopPotChestFarm(string reason)
    {
        if (PotFarm == null)
        {
            return;
        }

        PotFarm.Stop(null, reason);
        PotFarm = null;
        potFarmBlockedUntilBuffEnds = PotChestFarm.HasCacheMe();
    }

    /// <summary>The pot FATE whose coffer should be farmed now that it ended, or 0.</summary>
    private static uint GetPotFarmFate(AutomatorModule module, Activity activity)
    {
        return activity is FateActivity
               && activity.HasParticipated
               && PotZoneData.IsPotFate(activity.data.Id)
               && module.Config.ShouldFarmPotChests
            ? activity.data.Id
            : 0;
    }

    private void StartPotChestFarm(AutomatorModule module, uint fateId)
    {
        CancelPostActivityReturn("pot coffer farm");
        ReleasePostActivityCombatDefense("pot coffer farm");
        PotFarm = new PotChestFarm(
            module,
            module.potHints,
            fateId,
            Svc.ClientState.TerritoryType,
            module.Config.ShouldFarmRerollPotChests);
    }

    /// <summary>Runs the coffer farm. Returns true while it owns this update.</summary>
    private bool UpdatePotChestFarm(AutomatorModule module, VNavmesh vnav)
    {
        var hasBuff = PotChestFarm.HasCacheMe();
        if (!hasBuff)
        {
            potFarmBlockedUntilBuffEnds = false;
        }

        if (PotFarm == null)
        {
            // Cache Me If You Can from a pot BOCCHI did not fight in (or one
            // finished by hand) still points at a coffer.
            if (!hasBuff
                || potFarmBlockedUntilBuffEnds
                || !module.Config.ShouldFarmPotChests
                || Activity != null
                || !ZoneData.IsInOccultCrescent()
                || Svc.Condition[ConditionFlag.InCombat]
                || IsChainActive)
            {
                return false;
            }

            var fateId = ResolvePotForBuff(module);
            if (fateId == 0)
            {
                return false;
            }

            Svc.Log.Info($"[Pots] Cache Me If You Can is active; farming the coffer of pot {fateId}.");
            StartPotChestFarm(module, fateId);
        }

        PotFarm!.Update(vnav);
        if (!PotFarm.IsFinished)
        {
            return true;
        }

        var reason = PotFarm.FinishReason ?? "finished";
        PotFarm = null;
        potFarmBlockedUntilBuffEnds = PotChestFarm.HasCacheMe();
        if (module.PluginConfig.TeleporterConfig.ReturnAfterFate)
        {
            QueuePostActivityReturn($"pot coffer farm ({reason})");
        }

        return true;
    }

    private static uint ResolvePotForBuff(AutomatorModule module)
    {
        var territory = Svc.ClientState.TerritoryType;
        var live = Svc.Fates.Select(fate => (uint)fate.FateId).FirstOrDefault(PotZoneData.IsPotFate);
        if (live != 0)
        {
            return live;
        }

        var cycle = module.potCycles.Get(territory);
        if (cycle.HasKnownAnchor
            && cycle.AnchorPotFateId != 0
            && DateTimeOffset.UtcNow - cycle.AnchorSpawnAt < TimeSpan.FromMinutes(12))
        {
            return cycle.AnchorPotFateId;
        }

        var player = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        return PotZoneData.GetPotFates(territory)
            .OrderBy(pot => PotZoneData.GetPrimaryPads(pot.Id)
                .Select(pad => PotTreasureFilter.Distance2D(pad, player))
                .DefaultIfEmpty(PotTreasureFilter.Distance2D(pot.Center, player))
                .Min())
            .Select(pot => pot.Id)
            .FirstOrDefault();
    }

    private static PotWaitActivity? FindPotWait(AutomatorModule module, Lifestream lifestream, VNavmesh vnav)
    {
        var pots = PotContext.Capture(module);
        if (!module.Config.ShouldPrepositionToPots
            || !pots.PotWorkEnabled
            || pots.LivePot != 0
            || !PotTimingPolicy.ShouldPreposition(pots.Cycle, pots.Now, module.Config.PotSpawnLeadMinutes, true)
            || !PotZoneData.TryGetPotFate(pots.Cycle.PredictedNextPotFateId, out var pot))
        {
            return null;
        }

        var territory = Svc.ClientState.TerritoryType;
        var data = EventData.GetFate(pot.Id, territory);
        return new PotWaitActivity(data, pot, lifestream, vnav, module, () =>
        {
            var current = PotContext.Capture(module);
            return module.IsEnabled
                   && current.LivePot == 0
                   && current.Cycle.PredictedNextPotFateId == pot.Id
                   && PotTimingPolicy.ShouldPreposition(
                       current.Cycle,
                       current.Now,
                       module.Config.PotSpawnLeadMinutes,
                       current.PotWorkEnabled);
        });
    }

    /// <summary>
    /// An ordinary FATE BOCCHI is still travelling to gives way to a live pot,
    /// and to the pot's cutoff window.
    /// </summary>
    private static bool ShouldYieldActivityToPot(
        AutomatorModule module,
        StateManagerModule states,
        Activity activity,
        out string reason)
    {
        reason = string.Empty;
        if (activity is not FateActivity
            || PotZoneData.IsPotFate(activity.data.Id)
            || activity.state is not (ActivityState.Idle or ActivityState.Pathfinding)
            || activity.HasParticipated
            || states.GetState() != State.Idle)
        {
            return false;
        }

        var pots = PotContext.Capture(module);
        // Only yield to a pot FindFate can actually pick, or this would loop.
        if (pots.StartableLivePot is { } livePot
            && module.TryGetModule<FatesModule>(out var fates)
            && fates != null
            && fates.fates.ContainsKey(livePot))
        {
            reason = $"pot {pots.StartableLivePot} is live";
            return true;
        }

        if (!PotTimingPolicy.AllowsStart(
                pots.Cycle,
                pots.Now,
                TimeSpan.FromMinutes(module.Config.PotFateCutoffMinutes),
                pots.PotWorkEnabled))
        {
            reason = "the next pot is within the FATE cutoff";
            return true;
        }

        return false;
    }

    private void ClearActivity()
    {
        Activity?.Dispose();
        Activity = null;
    }

    private bool HandlePostActivityReturn(AutomatorModule module, StateManagerModule states, Lifestream lifestream)
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

        var nowMs = Environment.TickCount64;
        if (nowMs - lastPostActivityUpdateMs > PostActivityUpdateGapMs)
        {
            postActivitySettledSinceMs = null;
        }

        lastPostActivityUpdateMs = nowMs;

        // A teleport, cast or loading screen that started before the activity
        // ended is still moving the player. Wait for it instead of judging the
        // position mid-flight.
        var inFlightReason = GetPostActivityInFlightReason(lifestream, nowMs);
        if (inFlightReason != null)
        {
            postActivityInFlightSinceMs ??= nowMs;
            if (nowMs - postActivityInFlightSinceMs.Value < PostActivityInFlightLimitMs)
            {
                postActivitySettledSinceMs = null;
                LogPostActivityWait(inFlightReason);
                return true;
            }

            if (EzThrottler.Throttle("Automator.PostActivityInFlightLimit", PostActivityDiagnosticIntervalMs))
            {
                Svc.Log.Warning(
                    $"Forced base-camp return stopped waiting for {inFlightReason} after "
                    + $"{PostActivityInFlightLimitMs / 1000}s.");
            }
        }
        else
        {
            postActivityInFlightSinceMs = null;
        }

        // Return cannot be cast in combat.
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            postActivitySettledSinceMs = null;
            LogPostActivityWait("combat");
            return true;
        }

        var state = states.GetState();
        if (state != State.Idle)
        {
            postActivityEventStateSinceMs ??= nowMs;
            if (nowMs - postActivityEventStateSinceMs.Value < PostActivityEventStateGraceMs)
            {
                postActivitySettledSinceMs = null;
                LogPostActivityWait($"state {state}");
                return true;
            }
        }
        else
        {
            postActivityEventStateSinceMs = null;
        }

        postActivitySettledSinceMs ??= nowMs;
        if (nowMs - postActivitySettledSinceMs.Value < PostActivityReturnSettleMs)
        {
            return true;
        }

        if (ZoneData.IsNearAethernetShard(
                ZoneData.GetBaseCampAethernet(),
                AethernetData.DISTANCE + 1f))
        {
            CompletePostActivityReturn($"Post-activity return already satisfied after {postActivityReturnReason}.");
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
            CompletePostActivityReturn(null);
            return false;
        }

        postActivityReturnAttempts++;
        var attempt = postActivityReturnAttempts;
        var generation = postActivityReturnGeneration;
        bool Stopped()
        {
            return !module.IsEnabled
                   || !postActivityReturnPending
                   || generation != postActivityReturnGeneration;
        }

        // The native Return can keep failing (an action that stays
        // unavailable, a dialog that never matches); the last attempt uses
        // the aethernet network instead.
        var useAethernet = attempt == MaxPostActivityReturnAttempts;
        var baseCamp = ZoneData.GetBaseCampAethernet();
        var teleport = useAethernet && ZoneData.GetClosestAethernetShard(Player.Position) != baseCamp
            ? ChainHelper.TeleportChain(baseCamp, mountAfterTeleport: false, stopCheck: Stopped)
            : null;
        var returnChain = ChainHelper.ReturnChain(new ReturnChainConfig
        {
            ApproachAetheryte = true,
            ForceReturn = !useAethernet,
            SkipReturnAtLanding = true,
            StopCheck = Stopped,
        });
        Svc.Log.Info(
            $"Starting forced base-camp return after {postActivityReturnReason} " +
            $"({attempt}/{MaxPostActivityReturnAttempts}, method={(useAethernet ? "aethernet" : "Return")}, " +
            $"queued {(nowMs - postActivityQueuedAtMs) / 1000d:F1}s ago).");

        Plugin.Chain.Submit(() =>
        {
            var chain = Chain.Create("Illegal:PostActivityReturn");
            if (teleport != null)
            {
                chain.Then(teleport).BreakIf(() => !teleport.Succeeded);
            }

            return chain
                .Then(returnChain)
                .OnFinally(() =>
                {
                    if (!postActivityReturnPending || generation != postActivityReturnGeneration)
                    {
                        return;
                    }

                    if (returnChain.Succeeded)
                    {
                        CompletePostActivityReturn($"Forced base-camp return completed after {postActivityReturnReason}.");
                        return;
                    }

                    var reason = teleport is { Succeeded: false }
                        ? teleport.FailureReason ?? "aethernet teleport timed out or was aborted"
                        : returnChain.FailureReason ?? "return chain timed out or was aborted";
                    nextPostActivityReturnAttempt = DateTime.UtcNow + PostActivityReturnRetryDelay;
                    // Combat that starts mid-attempt is waited out above; it
                    // must not use up the attempts meant for real failures.
                    var interruptedByCombat = Svc.Condition[ConditionFlag.InCombat];
                    if (interruptedByCombat)
                    {
                        postActivityReturnAttempts = Math.Max(0, postActivityReturnAttempts - 1);
                    }

                    Svc.Log.Warning(
                        $"Forced base-camp return failed after {postActivityReturnReason} " +
                        $"({attempt}/{MaxPostActivityReturnAttempts}{(interruptedByCombat ? ", interrupted by combat and not counted" : "")}); " +
                        $"reason={reason}.");
                });
        });
        return true;
    }

    private string? GetPostActivityInFlightReason(Lifestream lifestream, long nowMs)
    {
        bool lifestreamBusy;
        try
        {
            lifestreamBusy = lifestream.IsBusy();
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, "Lifestream IPC failed while checking for an in-flight teleport.");
            lifestreamBusy = false;
        }

        if (lifestreamBusy)
        {
            postActivityLifestreamBusySinceMs ??= nowMs;
            if (nowMs - postActivityLifestreamBusySinceMs.Value >= PostActivityLifestreamBusyLimitMs)
            {
                postActivityLifestreamBusySinceMs = null;
                Svc.Log.Warning(
                    $"Lifestream stayed busy for {PostActivityLifestreamBusyLimitMs / 1000}s after the activity ended; "
                    + "aborting it so the forced return can proceed.");
                try
                {
                    lifestream.Abort();
                }
                catch (Exception exception)
                {
                    Svc.Log.Warning(exception, "Could not abort the stale Lifestream request.");
                }
            }

            return "Lifestream";
        }

        postActivityLifestreamBusySinceMs = null;

        var player = Svc.Objects.LocalPlayer;
        if (player == null || !player.IsTargetable)
        {
            return "player not ready";
        }

        if (player.IsCasting)
        {
            return "casting";
        }

        if (Svc.Condition[ConditionFlag.BetweenAreas]
            || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Svc.Condition[ConditionFlag.OccupiedInQuestEvent]
            || Svc.Condition[ConditionFlag.OccupiedInEvent]
            || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        {
            return "loading or interaction";
        }

        return null;
    }

    private void LogPostActivityWait(string reason)
    {
        if (EzThrottler.Throttle("Automator.PostActivityReturnWait", PostActivityDiagnosticIntervalMs))
        {
            Svc.Log.Info(
                $"Forced base-camp return after {postActivityReturnReason} is waiting: {reason} "
                + $"(queued {(Environment.TickCount64 - postActivityQueuedAtMs) / 1000d:F1}s ago).");
        }
    }

    private void CompletePostActivityReturn(string? message)
    {
        postActivityReturnPending = false;
        postActivityReturnGeneration++;
        postActivityReturnAttempts = 0;
        nextPostActivityReturnAttempt = DateTime.MinValue;
        idleTime = 0;
        ResetPostActivityTracking();
        ReleasePostActivityCombatDefense("forced return finished");
        if (message != null)
        {
            Svc.Log.Info(message);
        }
    }

    private void ResetPostActivityTracking()
    {
        lastPostActivityUpdateMs = 0;
        postActivitySettledSinceMs = null;
        postActivityEventStateSinceMs = null;
        postActivityInFlightSinceMs = null;
        postActivityLifestreamBusySinceMs = null;
        postActivityCombatSinceMs = null;
    }

    /// <summary>
    /// Ending an activity releases the AI and the rotation. Enemies that keep
    /// attacking afterwards then hold the player in combat forever, and Return
    /// cannot be cast in combat. Fight back (without pulling anything new)
    /// until combat ends, then hand control back to the Return.
    /// </summary>
    private void UpdatePostActivityCombatDefense(AutomatorModule module)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null || player.IsDead || !Svc.Condition[ConditionFlag.InCombat])
        {
            postActivityCombatSinceMs = null;
            ReleasePostActivityCombatDefense("combat ended");
            return;
        }

        var nowMs = Environment.TickCount64;
        postActivityCombatSinceMs ??= nowMs;
        if (nowMs - postActivityCombatSinceMs.Value < PostActivityCombatDefenseDelayMs
            || !EzThrottler.Throttle("Automator.PostActivityCombatDefense", 1000))
        {
            return;
        }

        var attacker = TargetHelper.Enemies.FirstOrDefault(enemy => enemy.TargetObjectId == player.GameObjectId);
        if (attacker == null)
        {
            return;
        }

        if (Svc.Targets.Target is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc
            {
                IsDead: false,
                IsTargetable: true,
            } current
            || current.TargetObjectId != player.GameObjectId)
        {
            Svc.Targets.Target = attacker;
        }

        if (postActivityDefenseOwner == null)
        {
            postActivityDefenseOwner = module;
            module.SetAiProviderEnabled(true);
            Svc.Log.Info(
                $"Forced base-camp return after {postActivityReturnReason} is held by combat; "
                + $"fighting back against {attacker.Name}.");
        }

        if (!PromeRotationController.IsRunning())
        {
            PromeRotationController.StartWithoutAutoPull();
        }
    }

    private void ReleasePostActivityCombatDefense(string reason)
    {
        if (postActivityDefenseOwner is not { } module)
        {
            return;
        }

        postActivityDefenseOwner = null;
        PromeRotationController.Stop();
        module.SetAiProviderEnabled(false);
        Svc.Log.Info($"Released post-activity combat defense: {reason}.");
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
    public static bool ShouldQueue(
        EventType eventType,
        bool returnAfterFate,
        bool returnAfterCriticalEncounter,
        bool independentNavigationRunning = false)
    {
        if (independentNavigationRunning)
        {
            return false;
        }

        return eventType switch
        {
            EventType.Fate => returnAfterFate,
            EventType.CriticalEncounter => returnAfterCriticalEncounter,
            _ => false,
        };
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
