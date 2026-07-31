using System;
using System.Collections.Generic;

namespace BOCCHI.Modules.AggroRange;

[Serializable]
public sealed class AggroRangeCalibration
{
    private const int MaximumSamples = 9;

    public List<float> EdgeRangeSamples { get; set; } = [];

    public bool AddSample(float edgeRange)
    {
        if (!float.IsFinite(edgeRange) || edgeRange is < 4f or > 20f)
        {
            return false;
        }

        EdgeRangeSamples ??= [];
        if (EdgeRangeSamples.Count >= MaximumSamples)
        {
            EdgeRangeSamples.RemoveAt(0);
        }

        EdgeRangeSamples.Add(MathF.Round(edgeRange * 10f) / 10f);
        return true;
    }

    public float ResolveEdgeRange(float fallback)
    {
        if (EdgeRangeSamples is not { Count: > 0 })
        {
            return fallback;
        }

        var samples = EdgeRangeSamples
            .FindAll(sample => float.IsFinite(sample) && sample is >= 4f and <= 20f);
        if (samples.Count == 0)
        {
            return fallback;
        }

        samples.Sort();
        // A transition is observed one framework frame after the server-side
        // threshold. The upper quartile rejects small movement overshoots and
        // 0.5y restores a conservative boundary allowance.
        var index = (int)MathF.Ceiling((samples.Count - 1) * 0.75f);
        return Math.Clamp(samples[index] + 0.5f, 4.5f, 20.5f);
    }
}

public static class AggroRangeResolver
{
    public static float ResolveEdgeRange(CommonMobProfile profile, AggroRangeConfig config)
    {
        if (config.Calibrations != null
            && config.Calibrations.TryGetValue(profile.NameId, out var calibration)
            && calibration != null)
        {
            return calibration.ResolveEdgeRange(profile.FallbackEdgeRange);
        }

        return profile.FallbackEdgeRange;
    }

    public static float ResolveTriggerRadius(
        CommonMobProfile profile,
        float hitboxRadius,
        AggroRangeConfig config)
    {
        return Math.Max(
            0.5f,
            ResolveEdgeRange(profile, config)
            + Math.Max(0f, hitboxRadius)
            + config.RadiusAdjustment);
    }
}
