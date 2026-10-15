using BOCCHI.Chains;

internal static class ReturnSmoke
{
    public static void Run()
    {
        static void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
        }

        static ReturnCastDecision Tick(ReturnCastTracker tracker, long now,
            bool loading = false, bool casting = false, bool landed = false, bool ready = true)
            => tracker.Update(now, loading, casting, landed, ready);

        var rejected = new ReturnCastTracker(0, false);
        Check(Tick(rejected, 0, ready: false) == ReturnCastDecision.Wait,
            "Return must wait for combat, movement, dismount and existing confirmations to settle.");
        Check(Tick(rejected, 0) == ReturnCastDecision.Request && !rejected.CanConfirm(0),
            "Return must not authorize a confirmation before any request.");
        rejected.RecordRequest(0, false);
        Check(rejected.CanConfirm(1) && Tick(rejected, 1_499) == ReturnCastDecision.Wait
              && Tick(rejected, 1_500) == ReturnCastDecision.Request,
            "A confirmation that opens after a rejected UseAction still answers it, and the request retries.");
        rejected.RecordRequest(1_500, false);
        Check(Tick(rejected, 3_000) == ReturnCastDecision.Request, "A second rejection must remain retryable.");
        rejected.RecordRequest(3_000, false);
        Check(Tick(rejected, 4_500) == ReturnCastDecision.Failed && rejected.Attempts == 3,
            "Rejected actions must stop after the bounded attempt budget.");

        var delayed = new ReturnCastTracker(0, false);
        delayed.RecordRequest(0, true);
        Check(delayed.CanConfirm(8_000) && Tick(delayed, 8_000, ready: false) == ReturnCastDecision.Wait,
            "Delayed button readiness must keep the accepted confirmation window alive.");
        delayed.RecordConfirmation(8_000);
        Check(!delayed.CanConfirm(8_001) && Tick(delayed, 19_999) == ReturnCastDecision.Wait,
            "A confirmation must click once and give the cast a fresh response window.");
        Check(Tick(delayed, 20_000) == ReturnCastDecision.Request,
            "An accepted action with no cast or transition must retry.");

        var overlap = new ReturnCastTracker(0, false);
        overlap.RecordRequest(0, true);
        Check(Tick(overlap, 100, casting: true) == ReturnCastDecision.Wait && !overlap.CanConfirm(100),
            "Starting a cast must revoke stale confirmation ownership.");
        Check(Tick(overlap, 200, loading: true, casting: true) == ReturnCastDecision.Wait
              && overlap.SawTransition,
            "Loading must be captured even when it overlaps the last casting frame.");
        Check(Tick(overlap, 500, landed: false) == ReturnCastDecision.Wait,
            "An intermediate or wrong-shard landing must not count as success.");
        Check(Tick(overlap, 1_000, landed: true) == ReturnCastDecision.Complete,
            "The source island and verified camp landing must complete the observed transition.");
        Check(Tick(overlap, 2_000) == ReturnCastDecision.Complete && !overlap.CanConfirm(2_000),
            "Completed Return must never request or confirm another action.");

        var fast = new ReturnCastTracker(0, false);
        fast.RecordRequest(0, true);
        Check(Tick(fast, 100, landed: true) == ReturnCastDecision.Complete,
            "A verified relocation must handle loading shorter than a framework frame.");
        var alreadyHome = new ReturnCastTracker(0, true);
        alreadyHome.RecordRequest(0, true);
        Check(Tick(alreadyHome, 100, landed: true) == ReturnCastDecision.Wait,
            "An unchanged starting position must not falsely complete an accepted request.");
        Tick(alreadyHome, 200, loading: true);
        Check(Tick(alreadyHome, 300, landed: true) == ReturnCastDecision.Complete,
            "Return issued at camp still completes after an observed loading cycle.");

        var interrupted = new ReturnCastTracker(0, false);
        interrupted.RecordRequest(0, true);
        Tick(interrupted, 1_000, casting: true);
        Check(Tick(interrupted, 11_999) == ReturnCastDecision.Wait
              && Tick(interrupted, 12_000) == ReturnCastDecision.Request,
            "An interrupted cast must retry instead of waiting for loading that will never happen.");
        var longCast = new ReturnCastTracker(0, false);
        longCast.RecordRequest(0, true);
        Check(Tick(longCast, 15_000, casting: true) == ReturnCastDecision.Wait,
            "An active cast must never be interrupted by a second request.");

        var lost = new ReturnCastTracker(0, false);
        lost.RecordRequest(0, true);
        Tick(lost, 1_000, loading: true);
        Check(Tick(lost, 50_000) == ReturnCastDecision.Wait && lost.Attempts == 1,
            "Loading without a verified landing must not trigger another Return.");
        Check(Tick(lost, ReturnCastTracker.TimeoutMs) == ReturnCastDecision.Failed,
            "Lost transitions must fail within the overall deadline.");
        var unavailable = new ReturnCastTracker(0, false);
        Check(Tick(unavailable, ReturnCastTracker.TimeoutMs, ready: false) == ReturnCastDecision.Failed,
            "A permanently unavailable action must have a finite timeout.");

        // In the Occult Crescent UseAction can report false while the game
        // still opens the confirmation and casts. That must not be ignored.
        var falseNegative = new ReturnCastTracker(0, false);
        falseNegative.RecordRequest(0, false);
        Check(Tick(falseNegative, 800, casting: true) == ReturnCastDecision.Wait
              && falseNegative.HasAcceptedRequest && falseNegative.SawCast,
            "A cast right after a rejected request is the game accepting it.");
        Check(Tick(falseNegative, 6_000, loading: true) == ReturnCastDecision.Wait
              && Tick(falseNegative, 9_000, landed: true) == ReturnCastDecision.Complete
              && falseNegative.Attempts == 1,
            "A Return the game accepted despite UseAction=false must complete without a second request.");
        var confirmedAfterRejection = new ReturnCastTracker(0, false);
        confirmedAfterRejection.RecordRequest(0, false);
        confirmedAfterRejection.RecordConfirmation(500);
        Check(confirmedAfterRejection.HasAcceptedRequest && Tick(confirmedAfterRejection, 12_499) == ReturnCastDecision.Wait,
            "Clicking the confirmation must hold off another request while the cast starts.");

        var inFlight = new ReturnCastTracker(0, false);
        Check(Tick(inFlight, 100, casting: true, ready: false) == ReturnCastDecision.Wait
              && Tick(inFlight, 5_000, loading: true, ready: false) == ReturnCastDecision.Wait
              && Tick(inFlight, 8_000, landed: true) == ReturnCastDecision.Complete
              && inFlight.Attempts == 0,
            "A Return already in flight when the chain starts must complete without casting again.");

        var foreign = new ReturnCastTracker(0, false);
        foreign.RecordRequest(0, true);
        Tick(foreign, 1_000, loading: true);
        Check(Tick(foreign, 3_000) == ReturnCastDecision.Wait
              && Tick(foreign, 3_000 + ReturnCastTracker.ForeignLandingTimeoutMs) == ReturnCastDecision.Failed,
            "Loading that ends away from camp must fail quickly instead of waiting two minutes.");

        ReturnRequestLedger.Reset();
        Check(!ReturnRequestLedger.RequestedWithin(1_000, ReturnRequestLedger.StaleConfirmationWindowMs),
            "A reset ledger must not claim any confirmation.");
        ReturnRequestLedger.RecordRequest(10_000);
        Check(ReturnRequestLedger.RequestedWithin(10_000 + ReturnRequestLedger.StaleConfirmationWindowMs,
                  ReturnRequestLedger.StaleConfirmationWindowMs)
              && !ReturnRequestLedger.RequestedWithin(10_001 + ReturnRequestLedger.StaleConfirmationWindowMs,
                  ReturnRequestLedger.StaleConfirmationWindowMs),
            "A Return confirmation left open by BOCCHI must stay recognisable only for a bounded window.");
        ReturnRequestLedger.RecordConfirmation(20_000);
        Check(ReturnRequestLedger.ConfirmedWithin(21_000, ReturnRequestLedger.ConfirmationCooldownMs)
              && !ReturnRequestLedger.ConfirmedWithin(22_001, ReturnRequestLedger.ConfirmationCooldownMs),
            "A confirmation must not be clicked twice while the dialog closes.");
        ReturnRequestLedger.Reset();

        var source = (params string[] path) => File.ReadAllText(Path.Combine(path));
        var activitySource = source("BOCCHI", "Modules", "Automator", "Activity.cs");
        Check(activitySource.Contains("return disposed || !module.IsEnabled;", StringComparison.Ordinal)
              && activitySource.Contains("stopCheck: ShouldStopNestedWork", StringComparison.Ordinal)
              && activitySource.Contains("StopCheck = ShouldStopNestedWork", StringComparison.Ordinal)
              && activitySource.Contains("ChainHelper.MountChain(ShouldStopNestedWork)", StringComparison.Ordinal),
            "An ended activity must stop its nested Return, teleport, mount and route chains.");
        var teleportSource = source("BOCCHI", "Chains", "TeleportChain.cs");
        Check(teleportSource.Contains("if (CancelIfStopped())", StringComparison.Ordinal)
              && !teleportSource.Contains("WaitToCycleCondition", StringComparison.Ordinal),
            "A stopped teleport must never submit a request and must not sit in an uninterruptible wait.");
        var automatorSource = source("BOCCHI", "Modules", "Automator", "Automator.cs");
        Check(automatorSource.Contains("PostActivityReturnSettleMs", StringComparison.Ordinal)
              && automatorSource.Contains("GetPostActivityInFlightReason", StringComparison.Ordinal)
              && automatorSource.Contains("PostActivityEventStateGraceMs", StringComparison.Ordinal)
              && automatorSource.Contains("UpdatePostActivityCombatDefense(module);", StringComparison.Ordinal)
              && automatorSource.Contains("StartWithoutAutoPull", StringComparison.Ordinal)
              && automatorSource.Contains("var useAethernet = attempt == MaxPostActivityReturnAttempts;", StringComparison.Ordinal),
            "The post-activity return must settle first, fight back in combat and fall back to the aethernet.");
        var deathSource = source("BOCCHI", "Modules", "Automator", "AutomatorModule.cs");
        Check(!deathSource.Contains("deathReturnPending = BocchiActions.Return.TryCast();", StringComparison.Ordinal)
              && deathSource.Contains("ReturnRequestLedger.RecordRequest(nowMs);", StringComparison.Ordinal),
            "The dead-player Return confirmation must stay armed even when UseAction reports false.");
        var lifetimeSource = source("BOCCHI", "Chains", "ChainLifetime.cs");
        Check(lifetimeSource.Contains("chain.OnFinally(chain.Dispose)", StringComparison.Ordinal)
              && source("BOCCHI", "Chains", "ReturnChain.cs").Contains(": OwnedChainFactory", StringComparison.Ordinal)
              && source("BOCCHI", "Modules", "Mount", "Chains", "MountChain.cs").Contains("DisposeWhenFinished", StringComparison.Ordinal),
            "Nested chains must release their frame handlers when they finish.");

        var returnSource = File.ReadAllText(Path.Combine("BOCCHI", "Modules", "Teleporter", "Teleporter.cs"));
        var explicitReturn = returnSource[returnSource.IndexOf("public void Return()", StringComparison.Ordinal)..];
        Check(explicitReturn.Contains("ForceReturn = true", StringComparison.Ordinal)
              && explicitReturn.Contains("ApproachAetheryte = module.Config.ApproachAetheryte", StringComparison.Ordinal),
            "Explicit Return must not become a no-op near camp when approach is disabled.");
        var moduleSource = File.ReadAllText(Path.Combine("BOCCHI", "Modules", "Teleporter", "TeleporterModule.cs"));
        Check(!moduleSource.Contains("OnSelectYesnoPostSetup", StringComparison.Ordinal),
            "Living Return confirmation must not depend on a one-shot addon initialization callback.");
        Console.WriteLine("BOCCHI Return request, confirmation, retry and transition smoke tests passed.");
    }
}
