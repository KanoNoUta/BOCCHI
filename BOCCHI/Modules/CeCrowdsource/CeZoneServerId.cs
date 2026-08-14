using ECommons.DalamudServices;
using System;

namespace BOCCHI.Modules.CeCrowdsource;

/// <summary>
/// Reads the current zone server (instance) ID from the ContentReplyManager
/// using the same signature approach as DailyRoutines' GameState.ZoneServerID.
/// Resolution is retried because the first access can happen while Dalamud is
/// still bringing the game services online during a territory transition.
/// </summary>
public static unsafe class CeZoneServerId
{
    private static readonly object Sync = new();
    private static readonly TimeSpan ResolveRetryDelay = TimeSpan.FromSeconds(2);

    private const string ContentReplyManagerSig =
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C0 48 8D 57 ?? 41 8B CE E8 ?? ?? ?? ?? 48 8D 8F";

    private const string ZoneServerIdOffsetSig =
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? " +
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? " +
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F";

    private static nint contentReplyManagerPtr;
    private static nint zoneServerIdOffset;
    private static DateTime nextResolveAt;

    public static uint Current
    {
        get
        {
            if (!TryResolve())
            {
                return 0;
            }

            try
            {
                var ptr = contentReplyManagerPtr + zoneServerIdOffset;
                var high = *(ushort*)ptr;
                var low = *(ushort*)(ptr + 4);
                return (uint)((high << 16) | low);
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"[CeZoneServerId] read failed: {ex.Message}");
                return 0;
            }
        }
    }

    private static bool TryResolve()
    {
        if (contentReplyManagerPtr != nint.Zero && zoneServerIdOffset != nint.Zero)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (now < nextResolveAt)
        {
            return false;
        }

        lock (Sync)
        {
            if (contentReplyManagerPtr != nint.Zero && zoneServerIdOffset != nint.Zero)
            {
                return true;
            }

            now = DateTime.UtcNow;
            if (now < nextResolveAt)
            {
                return false;
            }

            nextResolveAt = now.Add(ResolveRetryDelay);
            try
            {
                var manager = Svc.SigScanner.GetStaticAddressFromSig(ContentReplyManagerSig);
                var offset = Svc.SigScanner.GetStaticAddressFromSig(ZoneServerIdOffsetSig);
                if (manager == nint.Zero || offset == nint.Zero)
                {
                    return false;
                }

                contentReplyManagerPtr = manager;
                zoneServerIdOffset = offset;
                Svc.Log.Info("[CeZoneServerId] signature resolved");
                return true;
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"[CeZoneServerId] signature resolve failed; retrying: {ex.Message}");
                return false;
            }
        }
    }
}
