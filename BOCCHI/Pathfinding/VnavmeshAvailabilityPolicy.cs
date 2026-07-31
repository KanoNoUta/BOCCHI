using System;

namespace BOCCHI.Pathfinding;

public enum VnavmeshAvailabilityStatus
{
    Available,
    Missing,
    NotLoaded,
}

public readonly record struct VnavmeshAvailabilityCheck(
    VnavmeshAvailabilityStatus Status,
    Version? DisplayVersion)
{
    public bool IsAvailable => Status == VnavmeshAvailabilityStatus.Available;
}

/// <summary>
/// Checks only whether vnavmesh is installed and loaded. Its version is kept
/// for display purposes and is deliberately never used as a compatibility
/// gate because CN vnavmesh releases do not share a stable version cadence.
/// </summary>
public static class VnavmeshAvailabilityPolicy
{
    public const string PluginInternalName = "vnavmesh";

    public static VnavmeshAvailabilityCheck Evaluate(
        bool isInstalled,
        bool isLoaded,
        Version? displayVersion)
    {
        if (!isInstalled)
        {
            return new VnavmeshAvailabilityCheck(VnavmeshAvailabilityStatus.Missing, null);
        }

        if (!isLoaded)
        {
            return new VnavmeshAvailabilityCheck(VnavmeshAvailabilityStatus.NotLoaded, displayVersion);
        }

        return new VnavmeshAvailabilityCheck(VnavmeshAvailabilityStatus.Available, displayVersion);
    }
}
