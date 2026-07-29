using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Numerics;

namespace BOCCHI.Modules.CriticalEncounters;

/// <summary>
/// Managed copy of the client-owned DynamicEvent data used across frames.
/// DynamicEvent contains Utf8String storage and native pointers whose lifetime
/// ends when the container refreshes, so retaining the native struct can crash
/// UI/automation callbacks after an event despawns or the plugin reloads.
/// </summary>
public sealed record CriticalEncounterSnapshot(
    uint DynamicEventId,
    byte DynamicEventType,
    byte EventType,
    DynamicEventState State,
    byte Participants,
    byte Progress,
    int StartTimestamp,
    uint SecondsLeft,
    uint SecondsDuration,
    uint SecondsRegistrationTime,
    uint SecondsWarmupTime,
    string Name,
    string Description,
    uint NameOffset,
    uint DescriptionOffset,
    uint LGBEventObject,
    uint LGBMapRange,
    uint Quest,
    uint Announce,
    byte EnemyType,
    byte MaxParticipants,
    byte SingleBattle,
    uint IconObjective0,
    byte MaxParticipants2,
    MapMarkerSnapshot MapMarker)
{
    public static unsafe CriticalEncounterSnapshot From(DynamicEvent ev)
    {
        // These fields are private in the CN 7.55 generated bindings but are
        // present in the verified native layout at 0x6C and 0x70.
        const int registrationSecondsOffset = 0x6C;
        const int warmupSecondsOffset = 0x70;
        var native = (byte*)&ev;

        return new CriticalEncounterSnapshot(
            (uint)ev.DynamicEventId,
            ev.DynamicEventType,
            ev.EventType,
            ev.State,
            ev.Participants,
            ev.Progress,
            ev.StartTimestamp,
            ev.SecondsLeft,
            ev.SecondsDuration,
            *(uint*)(native + registrationSecondsOffset),
            *(uint*)(native + warmupSecondsOffset),
            ev.Name.ToString(),
            ev.Description.ToString(),
            ev.NameOffset,
            ev.DescriptionOffset,
            ev.LGBEventObject,
            ev.LGBMapRange,
            ev.Quest,
            ev.Announce,
            ev.EnemyType,
            ev.MaxParticipants,
            ev.SingleBattle,
            ev.IconObjective0,
            ev.MaxParticipants2,
            MapMarkerSnapshot.From(ev.MapMarker));
    }
}

public sealed record MapMarkerSnapshot(
    Vector3 Position,
    string LevelId,
    string ObjectiveId,
    string TooltipString,
    string IconId,
    string Radius,
    string MapId,
    string PlaceNameZoneId,
    string PlaceNameId,
    string EndTimestamp,
    string RecommendedLevel,
    string TerritoryTypeId,
    string DataId,
    string MarkerType,
    string EventState,
    string Flags)
{
    public static unsafe MapMarkerSnapshot From(MapMarkerData marker)
    {
        return new MapMarkerSnapshot(
            marker.Position,
            marker.LevelId.ToString(),
            marker.ObjectiveId.ToString(),
            marker.TooltipString != null ? marker.TooltipString->ToString() : "null",
            marker.IconId.ToString(),
            marker.Radius.ToString("F2"),
            marker.MapId.ToString(),
            marker.PlaceNameZoneId.ToString(),
            marker.PlaceNameId.ToString(),
            marker.EndTimestamp.ToString(),
            marker.RecommendedLevel.ToString(),
            marker.TerritoryTypeId.ToString(),
            marker.DataId.ToString(),
            marker.MarkerType.ToString(),
            marker.EventState.ToString(),
            marker.Flags.ToString());
    }
}
