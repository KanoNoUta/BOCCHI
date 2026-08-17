using System;

namespace BOCCHI.Modules.Automator;

public enum DeathReturnDecision
{
    Reset,
    Wait,
    Trigger,
}

public static class DeathReturnPolicy
{
    public static long GetTimeoutMs(int configuredMinutes)
    {
        return Math.Clamp(configuredMinutes, 1, 60) * 60_000L;
    }
}

public sealed class DeathReturnTracker
{
    private long? deathStartedAtMs;
    private long? nextAttemptAtMs;

    public DeathReturnDecision Update(
        bool eligible,
        bool isDead,
        long nowMs,
        long timeoutMs,
        long retryMs)
    {
        if (!eligible || !isDead)
        {
            Reset();
            return DeathReturnDecision.Reset;
        }

        if (deathStartedAtMs == null)
        {
            deathStartedAtMs = nowMs;
            return DeathReturnDecision.Wait;
        }

        if (nowMs - deathStartedAtMs.Value < Math.Max(0, timeoutMs))
        {
            return DeathReturnDecision.Wait;
        }

        if (nextAttemptAtMs is { } nextAttempt && nowMs < nextAttempt)
        {
            return DeathReturnDecision.Wait;
        }

        nextAttemptAtMs = nowMs + Math.Max(1, retryMs);
        return DeathReturnDecision.Trigger;
    }

    public void Reset()
    {
        deathStartedAtMs = null;
        nextAttemptAtMs = null;
    }
}
