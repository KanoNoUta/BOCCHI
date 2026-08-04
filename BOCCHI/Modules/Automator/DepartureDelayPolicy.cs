using System;

namespace BOCCHI.Modules.Automator;

public static class DepartureDelayPolicy
{
    private const double MaximumSeconds = 30d;

    public static int GetDelayMilliseconds(
        bool enabled,
        float minimumSeconds,
        float maximumSeconds,
        double randomSample)
    {
        if (!enabled)
        {
            return 0;
        }

        var minimum = NormalizeSeconds(minimumSeconds);
        var maximum = NormalizeSeconds(maximumSeconds);
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        var sample = double.IsFinite(randomSample)
            ? Math.Clamp(randomSample, 0d, 1d)
            : 0d;
        var seconds = minimum + ((maximum - minimum) * sample);
        return (int)Math.Round(seconds * 1000d, MidpointRounding.AwayFromZero);
    }

    private static double NormalizeSeconds(float seconds)
    {
        return float.IsFinite(seconds)
            ? Math.Clamp(seconds, 0f, (float)MaximumSeconds)
            : 0d;
    }
}
