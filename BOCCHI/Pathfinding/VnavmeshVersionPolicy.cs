using System;

namespace BOCCHI.Pathfinding;

public enum VnavmeshVersionStatus
{
    Compatible,
    Missing,
    NotLoaded,
    VersionMismatch,
}

public readonly record struct VnavmeshVersionCheck(
    VnavmeshVersionStatus Status,
    Version? ActualVersion)
{
    public bool IsCompatible => Status == VnavmeshVersionStatus.Compatible;
}

public static class VnavmeshVersionPolicy
{
    public const string PluginInternalName = "vnavmesh";

    // 0.7.6.0 is the first CN build whose territory 1346 customization uses
    // the LAYERS partition expected by North Horn navigation coordinates.
    public static readonly Version RequiredVersion = new(0, 7, 6, 0);

    public static VnavmeshVersionCheck Evaluate(
        bool isInstalled,
        bool isLoaded,
        Version? actualVersion)
    {
        if (!isInstalled)
        {
            return new VnavmeshVersionCheck(VnavmeshVersionStatus.Missing, null);
        }

        if (!isLoaded)
        {
            return new VnavmeshVersionCheck(VnavmeshVersionStatus.NotLoaded, actualVersion);
        }

        return new VnavmeshVersionCheck(
            actualVersion == RequiredVersion
                ? VnavmeshVersionStatus.Compatible
                : VnavmeshVersionStatus.VersionMismatch,
            actualVersion);
    }
}
