using System;

namespace BOCCHI.Modules.Currency;

internal sealed class CurrencyRateAccumulator
{
    private int? lastBalance;

    private long gained;

    private DateTime startTime;

    public void Observe(int currentBalance, DateTime utcNow)
    {
        if (!lastBalance.HasValue)
        {
            lastBalance = currentBalance;
            startTime = utcNow;
            return;
        }

        var delta = currentBalance - lastBalance.Value;
        if (delta > 0)
        {
            gained += delta;
        }

        lastBalance = currentBalance;
    }

    public void Reset(DateTime utcNow)
    {
        lastBalance = null;
        gained = 0;
        startTime = utcNow;
    }

    public float GetPerHour(DateTime utcNow)
    {
        if (!lastBalance.HasValue)
        {
            return 0;
        }

        var elapsed = (utcNow - startTime).TotalHours;
        return elapsed > 0 ? (float)(gained / elapsed) : 0;
    }
}
