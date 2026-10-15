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
    public const int RejectedRetryMs = 1_500;
    public const int CastSettleMs = 2_000;

    // After loading ends away from the camp landing, the transition was not a
    // Return to camp (or it landed somewhere else). Fail fast instead of
    // waiting out the whole two-minute budget.
    public const int ForeignLandingTimeoutMs = 10_000;

    private long nextRequestAtMs;
    private long lastRequestAtMs;
    private long confirmationDeadlineMs;
    private long? transitionEndedAtMs;
    private ReturnCastDecision? result;

    public int Attempts { get; private set; }
    public bool HasAcceptedRequest { get; private set; }
    public bool SawCast { get; private set; }
    public bool SawTransition { get; private set; }
    public bool IsFinished => result.HasValue;
    public bool HasTimedOut(long nowMs) => nowMs - startedAtMs >= TimeoutMs;

    /// <summary>
    /// A request owns the game's response for a short window whatever UseAction
    /// returned: in the Occult Crescent the confirmation can open (and the cast
    /// can start) even when UseAction reports false.
    /// </summary>
    public bool IsAwaitingResponse(long nowMs) => Attempts > 0 && nowMs - lastRequestAtMs < ResponseTimeoutMs;

    public bool CanConfirm(long nowMs) => !IsFinished
        && confirmationDeadlineMs > 0 && nowMs < confirmationDeadlineMs;

    public void RecordRequest(long nowMs, bool accepted)
    {
        Attempts++;
        lastRequestAtMs = nowMs;
        HasAcceptedRequest |= accepted;
        // Requests are only issued while no confirmation is open, so a
        // confirmation that appears afterwards answers this request.
        confirmationDeadlineMs = nowMs + ResponseTimeoutMs;
        nextRequestAtMs = nowMs + (accepted ? ResponseTimeoutMs : RejectedRetryMs);
    }

    public void RecordConfirmation(long nowMs)
    {
        HasAcceptedRequest = true;
        confirmationDeadlineMs = 0;
        nextRequestAtMs = Math.Max(nextRequestAtMs, nowMs + ResponseTimeoutMs);
    }

    public ReturnCastDecision Update(
        long nowMs, bool betweenAreas, bool casting, bool atLanding, bool canRequest)
    {
        if (result is { } finished)
            return finished;

        if (HasTimedOut(nowMs))
            return Finish(ReturnCastDecision.Failed);

        // Casting or loading that starts right after our own request is the
        // game accepting it, even when UseAction reported a rejection.
        if (!HasAcceptedRequest && IsAwaitingResponse(nowMs) && (casting || betweenAreas))
            HasAcceptedRequest = true;

        if (HasAcceptedRequest && betweenAreas)
        {
            SawTransition = true;
            transitionEndedAtMs = null;
            confirmationDeadlineMs = 0;
            return ReturnCastDecision.Wait;
        }

        if (HasAcceptedRequest && casting && !SawTransition)
        {
            SawCast = true;
            confirmationDeadlineMs = 0;
            nextRequestAtMs = Math.Max(nextRequestAtMs, nowMs + CastSettleMs);
            return ReturnCastDecision.Wait;
        }

        // The goal is the verified camp landing. Reaching it after starting
        // elsewhere completes whichever Return got there (including one that
        // was already in flight), without casting another. When the attempt
        // began at the landing, only an observed loading cycle proves arrival.
        if (atLanding && (SawTransition || !startedAtLanding))
            return Finish(ReturnCastDecision.Complete);

        // Once loading starts, never issue another Return into that transition.
        if (SawTransition)
        {
            transitionEndedAtMs ??= nowMs;
            return nowMs - transitionEndedAtMs.Value >= ForeignLandingTimeoutMs
                ? Finish(ReturnCastDecision.Failed)
                : ReturnCastDecision.Wait;
        }

        if (betweenAreas || casting || nowMs < nextRequestAtMs)
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

/// <summary>
/// Remembers the last Return BOCCHI itself requested, so a confirmation left
/// open by an earlier attempt can be recognised instead of blocking every later
/// Return until the user clicks it.
/// </summary>
public static class ReturnRequestLedger
{
    public const long StaleConfirmationWindowMs = 30_000;

    // The dialog can stay visible for a frame after its callback fires.
    public const long ConfirmationCooldownMs = 2_000;

    private static long? lastRequestAtMs;

    private static long? lastConfirmationAtMs;

    public static void RecordRequest(long nowMs)
    {
        lastRequestAtMs = nowMs;
    }

    public static void RecordConfirmation(long nowMs)
    {
        lastConfirmationAtMs = nowMs;
    }

    public static bool RequestedWithin(long nowMs, long windowMs)
    {
        return IsWithin(lastRequestAtMs, nowMs, windowMs);
    }

    public static bool ConfirmedWithin(long nowMs, long windowMs)
    {
        return IsWithin(lastConfirmationAtMs, nowMs, windowMs);
    }

    public static void Reset()
    {
        lastRequestAtMs = null;
        lastConfirmationAtMs = null;
    }

    private static bool IsWithin(long? timestamp, long nowMs, long windowMs)
    {
        return timestamp is { } at && nowMs >= at && nowMs - at <= windowMs;
    }
}
