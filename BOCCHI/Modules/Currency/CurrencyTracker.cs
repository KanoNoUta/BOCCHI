using BOCCHI.ItemHelpers;
using Dalamud.Plugin.Services;
using System;

namespace BOCCHI.Modules.Currency;

public class CurrencyTracker
{
    private readonly CurrencyRateAccumulator goldRate = new();

    private readonly CurrencyRateAccumulator silverRate = new();

    private readonly Func<int?> goldReader;

    private readonly Func<int?> silverReader;

    private readonly Func<DateTime> utcNow;

    public CurrencyTracker()
        : this(ReadGold, ReadSilver, () => DateTime.UtcNow)
    {
    }

    public CurrencyTracker(Func<int?> goldReader, Func<int?> silverReader, Func<DateTime> utcNow)
    {
        this.goldReader = goldReader;
        this.silverReader = silverReader;
        this.utcNow = utcNow;
        Reset();
    }

    public void Tick(IFramework _)
    {
        Tick();
    }

    public void Tick()
    {
        var now = utcNow();
        var currentGold = goldReader();
        var currentSilver = silverReader();

        if (currentGold.HasValue)
        {
            goldRate.Observe(currentGold.Value, now);
        }

        if (currentSilver.HasValue)
        {
            silverRate.Observe(currentSilver.Value, now);
        }
    }

    public void ResetSilver()
    {
        silverRate.Reset(utcNow());
    }

    public void ResetGold()
    {
        goldRate.Reset(utcNow());
    }

    public void Reset()
    {
        ResetSilver();
        ResetGold();
    }

    public float GetGoldPerHour()
    {
        return goldRate.GetPerHour(utcNow());
    }

    public float GetSilverPerHour()
    {
        return silverRate.GetPerHour(utcNow());
    }

    private static int? ReadGold()
    {
        return Items.Gold.TryCount(out var count) ? count : null;
    }

    private static int? ReadSilver()
    {
        return Items.Silver.TryCount(out var count) ? count : null;
    }
}
