using ECommons.DalamudServices;
using System;

namespace BOCCHI.Modules.CeCrowdsource;

/// <summary>
/// Reads the current zone server (instance) ID from the ContentReplyManager
/// using the same signature approach as DailyRoutines' GameState.ZoneServerID.
/// Resolved once, then read live on every access so instance switches are seen.
/// </summary>
public static unsafe class CeZoneServerId
{
    private const string ContentReplyManagerSig =
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C0 48 8D 57 ?? 41 8B CE E8 ?? ?? ?? ?? 48 8D 8F";

    private const string ZoneServerIdOffsetSig =
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? " +
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? " +
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F";

    private static readonly bool Resolved;
    private static readonly nint ContentReplyManagerPtr;
    private static readonly nint ZoneServerIdOffset;

    static CeZoneServerId()
    {
        try
        {
            ContentReplyManagerPtr = Svc.SigScanner.GetStaticAddressFromSig(ContentReplyManagerSig);
            ZoneServerIdOffset = Svc.SigScanner.GetStaticAddressFromSig(ZoneServerIdOffsetSig);
            Resolved = ContentReplyManagerPtr != nint.Zero && ZoneServerIdOffset != nint.Zero;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[CeZoneServerId] signature resolve failed: {ex.Message}");
        }
    }

    public static uint Current
    {
        get
        {
            if (!Resolved)
            {
                return 0;
            }

            try
            {
                var ptr = ContentReplyManagerPtr + ZoneServerIdOffset;
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
}
