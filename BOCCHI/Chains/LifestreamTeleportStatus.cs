using ECommons.DalamudServices;
using System;

namespace BOCCHI.Chains;

/// <summary>
/// Thin, defensive wrapper around Lifestream's aethernet-teleport status IPC.
///
/// The <see cref="Ocelot.IPC.Lifestream"/> subscriber shipped in the Ocelot
/// package is compiled against a fixed set of gates and therefore does not
/// expose the newer <c>GetAethernetTeleportStatus/FailureReason/Sequence</c>
/// endpoints. We reach those through Dalamud's raw call-gate API instead.
///
/// Every call is guarded: if the installed Lifestream build predates these
/// gates (or the plugin is mid-reload), the getters simply report
/// <see cref="Status.Unknown"/> and callers fall back to the legacy
/// zone-transition based detection. Optional IPC must never throw into a chain.
/// </summary>
internal static class LifestreamTeleportStatus
{
    // Mirrors Lifestream.Systems.AethernetTeleportStatus.
    internal enum Status
    {
        Unknown = -1,
        None = 0,
        Queued = 1,
        Dispatched = 2,
        Failed = 3,
    }

    // Mirrors Lifestream.Systems.AethernetTeleportFailure.
    internal enum Failure
    {
        Unknown = -1,
        None = 0,
        CurrentAetheryteUnknown = 1,
        DestinationNotFound = 2,
        DestinationNotInNetwork = 3,
        NotInRange = 4,
        ZoneDataNotReady = 5,
    }

    private const string StatusGate = "Lifestream.GetAethernetTeleportStatus";
    private const string FailureGate = "Lifestream.GetAethernetTeleportFailureReason";
    private const string SequenceGate = "Lifestream.GetAethernetTeleportSequence";

    /// <summary>
    /// True when the installed Lifestream exposes the status gates. When false,
    /// callers should keep using the legacy zone-transition detection only.
    /// </summary>
    internal static bool IsAvailable => GetStatus() != Status.Unknown;

    internal static Status GetStatus()
    {
        try
        {
            return (Status)Svc.PluginInterface.GetIpcSubscriber<int>(StatusGate).InvokeFunc();
        }
        catch (Exception)
        {
            // Gate not registered (older Lifestream) or plugin reloading.
            return Status.Unknown;
        }
    }

    internal static Failure GetFailure()
    {
        try
        {
            return (Failure)Svc.PluginInterface.GetIpcSubscriber<int>(FailureGate).InvokeFunc();
        }
        catch (Exception)
        {
            return Failure.Unknown;
        }
    }

    /// <summary>
    /// Monotonic request id. Returns null when the gate is unavailable so the
    /// caller can tell "no status IPC" apart from "sequence 0".
    /// </summary>
    internal static uint? GetSequence()
    {
        try
        {
            return Svc.PluginInterface.GetIpcSubscriber<uint>(SequenceGate).InvokeFunc();
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string Describe(Failure failure)
    {
        return failure switch
        {
            Failure.CurrentAetheryteUnknown => "current aethernet shard was not identified in time",
            Failure.DestinationNotFound => "destination place name does not exist",
            Failure.DestinationNotInNetwork => "destination is not part of the current aethernet network",
            Failure.NotInRange => "player is not within range of an aethernet shard",
            Failure.ZoneDataNotReady => "zone aethernet data has not finished initialising",
            Failure.None => "no failure reported",
            _ => "unknown failure",
        };
    }
}
