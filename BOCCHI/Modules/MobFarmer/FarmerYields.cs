using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Automator.Pots;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.MobFarmer.Chains;
using BOCCHI.Modules.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.IPC;
using System;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer;

/// <summary>
/// Suspends the farmer for magic pots, a timed treasure hunt or a crystal
/// buff trip, and resumes it when that work is done. Ported from upstream
/// BOCCHI 4.2's MobFarmerYieldService onto this fork's automator, treasure
/// and buff modules.
/// </summary>
public sealed class FarmerYields
{
    /// <summary>An automator that cannot finish starting is given up on after this long.</summary>
    private static readonly TimeSpan AutomatorStartTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PotRetryDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan HuntRetryDelay = TimeSpan.FromSeconds(30);

    private DateTimeOffset nextHuntAt = DateTimeOffset.MinValue;

    private DateTimeOffset nextBuffAt = DateTimeOffset.MinValue;

    private DateTimeOffset nextPotAt = DateTimeOffset.MinValue;

    private bool startedPots;

    private DateTimeOffset potRequestedAt;

    private DateTimeOffset releaseRequestedAt;

    private DateTimeOffset? automatorRunningSince;

    private DateTimeOffset? automatorIdleSince;

    private bool sawPotWork;

    private bool startedHunt;

    private bool startedBuffs;

    /// <summary>The farmer is stopping the automator it started for a pot; the automator must not stop the farmer.</summary>
    public bool IsReleasingAutomator { get; private set; }

    public void Start(MobFarmerModule module)
    {
        Clear();
        nextHuntAt = DateTimeOffset.UtcNow + MobFarmerYieldPolicy.GetInterval(module.Config.TreasureHuntIntervalMinutes);
    }

    /// <summary>The farmer is stopping: end whatever it handed the player to.</summary>
    public void Stop(MobFarmerModule module)
    {
        if (startedPots
            && module.TryGetModule<AutomatorModule>(out var automator)
            && automator != null
            && automator.RequestedEnabled
            && automator.RunMode == AutomatorRunMode.PotsAndTreasure)
        {
            Svc.Log.Info("[MobFarmer] Stopping the Pots & Treasure run the farmer started.");
            automator.RequestPotsAndTreasure(false);
        }

        if (startedHunt
            && module.TryGetModule<TreasureModule>(out var treasure)
            && treasure != null
            && (treasure.IsHuntRunning || treasure.IsHuntPaused))
        {
            treasure.StopHunt();
        }

        if (startedBuffs)
        {
            Plugin.Chain.Abort();
        }

        Clear();
    }

    private void Clear()
    {
        nextHuntAt = DateTimeOffset.MinValue;
        nextBuffAt = DateTimeOffset.MinValue;
        nextPotAt = DateTimeOffset.MinValue;
        startedPots = false;
        startedHunt = false;
        startedBuffs = false;
        IsReleasingAutomator = false;
        ResetPotTracking();
    }

    private void ResetPotTracking()
    {
        automatorRunningSince = null;
        automatorIdleSince = null;
        sawPotWork = false;
    }

    public void Tick(MobFarmerModule module)
    {
        var farmer = module.Farmer;
        if (farmer.Suspended)
        {
            TickSuspended(module);
            return;
        }

        if (!CanAcceptYield(module))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (TryStartPots(module, now))
        {
            return;
        }

        if (TryStartCrystalBuffs(module, now))
        {
            return;
        }

        TryStartTreasureHunt(module, now);
    }

    /// <summary>Only between pulls: a yielded mode does not fight for the farmer.</summary>
    private static bool CanAcceptYield(MobFarmerModule module)
    {
        var farmer = module.Farmer;
        return farmer.Running
               && !farmer.Suspended
               && !farmer.NeedsApproach
               && farmer.StateMachine.State == States.FarmerPhase.Waiting
               && !Svc.Condition[ConditionFlag.InCombat]
               && !module.Scanner.InCombat.Any()
               && !Plugin.Chain.IsRunning
               && Plugin.Chain.QueueCount == 0;
    }

    private bool TryStartPots(MobFarmerModule module, DateTimeOffset now)
    {
        if (!module.Config.YieldToPots
            || now < nextPotAt
            || !module.TryGetModule<AutomatorModule>(out var automator)
            || automator == null
            // An automator the player already runs owns pots itself.
            || automator.RequestedEnabled
            || !IsPotDue(automator, now))
        {
            return false;
        }

        Svc.Log.Info("[MobFarmer] A magic pot is live or about to spawn; handing over to Pots & Treasure.");
        module.Farmer.SetSuspended(true, FarmerYieldReason.Pots);
        startedPots = true;
        potRequestedAt = now;
        ResetPotTracking();
        automator.RequestPotsAndTreasure(true);
        if (!automator.RequestedEnabled)
        {
            Svc.Log.Info("[MobFarmer] Pots & Treasure did not start; farming on.");
            startedPots = false;
            nextPotAt = now + PotRetryDelay;
            module.Farmer.SetSuspended(false);
        }

        return true;
    }

    private static bool IsPotDue(AutomatorModule automator, DateTimeOffset now)
    {
        var territory = Svc.ClientState.TerritoryType;
        if (!PotZoneData.GetPotFates(territory).Any())
        {
            return false;
        }

        var liveFate = Svc.Fates.FirstOrDefault(fate => PotZoneData.IsPotFate(fate.FateId));
        var startable = liveFate != null
                        && MobFarmerYieldPolicy.IsLivePotStartable(
                            liveFate.TimeRemaining,
                            automator.Config.MinPotFateMinutesRemaining);
        return MobFarmerYieldPolicy.ShouldYieldToPots(
            true,
            startable,
            automator.potCycles.Get(territory),
            now,
            automator.Config.PotSpawnLeadMinutes);
    }

    private bool TryStartCrystalBuffs(MobFarmerModule module, DateTimeOffset now)
    {
        if (!module.TryGetModule<BuffModule>(out var buffs) || buffs == null)
        {
            return false;
        }

        if (!MobFarmerYieldPolicy.ShouldYieldToCrystalBuffs(
                module.Config.YieldToCrystalBuffs,
                buffs.IsEnabled,
                buffs.ShouldRefreshBuffs(),
                now,
                nextBuffAt))
        {
            return false;
        }

        var startingPoint = module.Farmer.StartingPoint;
        var shard = AethernetData.AllByDistance(startingPoint).FirstOrDefault();
        if (shard == null)
        {
            nextBuffAt = now + MobFarmerYieldPolicy.CrystalBuffCooldown;
            return false;
        }

        Svc.Log.Info("[MobFarmer] Crystal buffs are low; returning to base camp for them.");
        module.Farmer.SetSuspended(true, FarmerYieldReason.CrystalBuffs);
        startedBuffs = true;
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var lifestream = module.GetIPCSubscriber<Lifestream>();
        Plugin.Chain.Submit(new ReturnTeleportWalkChain(vnav, lifestream, startingPoint, shard));
        return true;
    }

    private void TryStartTreasureHunt(MobFarmerModule module, DateTimeOffset now)
    {
        if (!module.Config.YieldToTreasureHunt
            || !module.TryGetModule<TreasureModule>(out var treasure)
            || treasure == null
            || treasure.IsHuntRunning
            || treasure.IsHuntPaused)
        {
            return;
        }

        var tracker = treasure.Tracker;
        if (!MobFarmerYieldPolicy.IsTreasureHuntDue(
                true,
                now,
                nextHuntAt,
                tracker.CountInitialised,
                TreasureHuntFillGate.MeetsMinimumFill(tracker, treasure.Config),
                tracker.BronzeChests + tracker.SilverChests))
        {
            return;
        }

        // Suspend first: suspending stops navigation, which must not cut off
        // the hunt's first route.
        module.Farmer.SetSuspended(true, FarmerYieldReason.TreasureHunt);
        if (!treasure.TryStartManagedHunt(out var error))
        {
            Svc.Log.Info($"[MobFarmer] Timed treasure hunt could not start: {error}");
            nextHuntAt = now + HuntRetryDelay;
            module.Farmer.SetSuspended(false);
            return;
        }

        Svc.Log.Info("[MobFarmer] Timed treasure hunt started; farming resumes when it ends.");
        startedHunt = true;
        nextHuntAt = now + MobFarmerYieldPolicy.GetInterval(module.Config.TreasureHuntIntervalMinutes);
    }

    private void TickSuspended(MobFarmerModule module)
    {
        switch (module.Farmer.YieldReason)
        {
            case FarmerYieldReason.Pots:
                TickPots(module);
                break;

            case FarmerYieldReason.TreasureHunt:
                if (!module.TryGetModule<TreasureModule>(out var treasure)
                    || treasure == null
                    || (!treasure.IsHuntRunning && !treasure.IsHuntPaused))
                {
                    startedHunt = false;
                    Resume(module, "the treasure hunt ended");
                }

                break;

            case FarmerYieldReason.CrystalBuffs:
                if (!Plugin.Chain.IsRunning && Plugin.Chain.QueueCount == 0)
                {
                    startedBuffs = false;
                    nextBuffAt = DateTimeOffset.UtcNow + MobFarmerYieldPolicy.CrystalBuffCooldown;
                    Resume(module, "the crystal buff trip ended");
                }

                break;

            default:
                Resume(module, "no yield is active");
                break;
        }
    }

    private void TickPots(MobFarmerModule module)
    {
        var now = DateTimeOffset.UtcNow;
        if (!module.TryGetModule<AutomatorModule>(out var automator) || automator == null)
        {
            startedPots = false;
            Resume(module, "the automator is unavailable");
            return;
        }

        if (IsReleasingAutomator)
        {
            // Wait for the stop to finish draining vnavmesh before walking back.
            if (automator.RunState == AutomatorRunState.Stopped || now - releaseRequestedAt >= AutomatorStartTimeout)
            {
                IsReleasingAutomator = false;
                Resume(module, "the magic pot is done");
            }

            return;
        }

        if (automator.RunState == AutomatorRunState.Starting)
        {
            if (now - potRequestedAt >= AutomatorStartTimeout)
            {
                Svc.Log.Info("[MobFarmer] Pots & Treasure is still starting; giving the player back to the farmer.");
                nextPotAt = now + PotRetryDelay;
                ReleaseAutomator(automator);
            }

            return;
        }

        if (!automator.RequestedEnabled || automator.RunMode != AutomatorRunMode.PotsAndTreasure)
        {
            // Stopped or switched by something else. A player's stop also
            // stops the farmer, so reaching here means farming can go on.
            startedPots = false;
            nextPotAt = now + PotRetryDelay;
            Resume(module, "Pots & Treasure is no longer running");
            return;
        }

        if (automator.RunState != AutomatorRunState.Running)
        {
            return;
        }

        var work = automator.automator;
        automatorRunningSince ??= now;
        sawPotWork |= work.Activity != null || work.PotFarm != null;

        var idle = work.Activity == null
                   && work.PotFarm == null
                   && !Svc.Condition[ConditionFlag.InCombat]
                   && !Automator.Automator.IsChainActive
                   && !IsPotDue(automator, now);
        automatorIdleSince = idle ? automatorIdleSince ?? now : null;

        if (!MobFarmerYieldPolicy.ShouldReleasePotYield(
                sawPotWork,
                now - automatorRunningSince.Value,
                automatorIdleSince == null ? TimeSpan.Zero : now - automatorIdleSince.Value,
                work.ManagedHunt.IsManaging))
        {
            return;
        }

        if (!sawPotWork)
        {
            // The automator never took the pot; do not hand over again at once.
            nextPotAt = now + PotRetryDelay;
        }

        ReleaseAutomator(automator);
    }

    private void ReleaseAutomator(AutomatorModule automator)
    {
        Svc.Log.Info("[MobFarmer] Stopping Pots & Treasure and going back to the farm spot.");
        startedPots = false;
        ResetPotTracking();
        IsReleasingAutomator = true;
        releaseRequestedAt = DateTimeOffset.UtcNow;
        automator.RequestPotsAndTreasure(false);
    }

    private static void Resume(MobFarmerModule module, string reason)
    {
        Svc.Log.Info($"[MobFarmer] Resuming: {reason}.");
        module.Farmer.SetSuspended(false);
    }
}
