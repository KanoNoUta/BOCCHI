using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Ui;

/// <summary>
/// Recent gains bucketed into a per-hour rate for a small trend graph.
/// Ported from upstream BOCCHI 4.2's DeltaRateTracker history.
/// </summary>
public sealed class RateHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Bucket = TimeSpan.FromSeconds(15);

    private readonly List<(DateTime Time, double Amount)> gains = [];

    public void Record(double amount, DateTime utcNow)
    {
        if (amount <= 0)
        {
            return;
        }

        gains.Add((utcNow, amount));
        gains.RemoveAll(gain => gain.Time < utcNow - Window);
    }

    public void Clear()
    {
        gains.Clear();
    }

    /// <summary>One per-hour rate per bucket over the last <see cref="Window"/>, oldest first.</summary>
    public float[] GetPerHour(DateTime utcNow)
    {
        var bucketCount = (int)(Window.TotalSeconds / Bucket.TotalSeconds);
        var totals = new double[bucketCount];
        var start = utcNow - Window;
        foreach (var (time, amount) in gains)
        {
            var index = (int)((time - start).TotalSeconds / Bucket.TotalSeconds);
            if (index >= 0 && index < bucketCount)
            {
                totals[index] += amount;
            }
        }

        return totals.Select(total => (float)(total / Bucket.TotalSeconds * 3600.0)).ToArray();
    }

    public static void Plot(string id, float[] history, float height = 30f)
    {
        if (history.Length == 0)
        {
            return;
        }

        var max = Math.Max(history.Max(), 1f);
        ImGui.PlotLines(id, history, history.Length, string.Empty, 0f, max, new Vector2(ImGui.GetContentRegionAvail().X, height));
    }
}
