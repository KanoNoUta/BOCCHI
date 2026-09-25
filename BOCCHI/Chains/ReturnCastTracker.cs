using System;

namespace BOCCHI.Chains;

public enum ReturnCastDecision
{
    Wait,
    Request,
    Complete,
    Failed,
}

/// <summary>Observes the whole Return attempt, including loading that overlaps the cast.</summary>
public sealed class ReturnCastTracker(long startedAtMs, bool startedAtLanding)
{
    public const int TimeoutMs = 120_000;
    public const int MaxAttempts = 3;
    public const int ResponseTimeoutMs = 12_000;
    private long nextRequestAtMs;
    private long confirmationDeadlineMs;
    private ReturnCastDecision? result;

    public int Attempts { get; private set; }
    public bool HasAcceptedRequest { get; private set; }
    public bool SawCast { get; private set; }
    public bool SawTransition { get; private set; }
    public bool IsFinished => result.HasValue;
    public bool HasTimedOut(long nowMs) => nowMs - startedAtMs >= TimeoutMs;

    public bool CanConfirm(long nowMs) => !IsFinished
        && confirmationDeadlineMs > 0 && nowMs < confirmationDeadlineMs;

    public void RecordRequest(long nowMs, bool accepted)
    {
        Attempts++;
        HasAcceptedRequest |= accepted;
        confirmationDeadlineMs = accepted ? nowMs + ResponseTimeoutMs : 0;
        nextRequestAtMs = nowMs + (accepted ? ResponseTimeoutMs : 1_500);
    }

    public void RecordConfirmation(long nowMs)
    {
        confirmationDeadlineMs = 0;
        nextRequestAtMs = nowMs + ResponseTimeoutMs;
    }

    public ReturnCastDecision Update(
        long nowMs, bool betweenAreas, bool casting, bool atLanding, bool canRequest)
    {
        if (result is { } finished)
            return finished;

        if (HasTimedOut(nowMs))
            return Finish(ReturnCastDecision.Failed);

        if (HasAcceptedRequest && betweenAreas)
        {
            SawTransition = true;
            confirmationDeadlineMs = 0;
            return ReturnCastDecision.Wait;
        }

        if (HasAcceptedRequest && casting)
        {
            SawCast = true;
            confirmationDeadlineMs = 0;
            nextRequestAtMs = Math.Max(nextRequestAtMs, nowMs + 2_000);
            return ReturnCastDecision.Wait;
        }

        // A fast loading cycle may fit between framework frames. A verified
        // move from outside camp into the landing area is sufficient evidence.
        if (HasAcceptedRequest && atLanding && (SawTransition || !startedAtLanding))
            return Finish(ReturnCastDecision.Complete);

        // Once loading starts, never issue another Return into that transition.
        if (SawTransition || betweenAreas || casting || nowMs < nextRequestAtMs)
            return ReturnCastDecision.Wait;

        if (Attempts >= MaxAttempts)
            return Finish(ReturnCastDecision.Failed);

        return canRequest ? ReturnCastDecision.Request : ReturnCastDecision.Wait;
    }

    private ReturnCastDecision Finish(ReturnCastDecision decision)
    {
        result = decision;
        confirmationDeadlineMs = 0;
        return decision;
    }
}
