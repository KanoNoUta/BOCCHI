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
            "Return must not authorize a confirmation before an accepted action.");
        rejected.RecordRequest(0, false);
        Check(!rejected.CanConfirm(1) && Tick(rejected, 1_499) == ReturnCastDecision.Wait
              && Tick(rejected, 1_500) == ReturnCastDecision.Request,
            "Rejected UseAction must retry with a delay, without confirming unrelated dialogs.");
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
