using System;
using System.Text.Json.Serialization;

namespace BOCCHI.Modules.CeCrowdsource;

public sealed record CeRecord(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("dataCenterID")] int DataCenterID,
    [property: JsonPropertyName("zoneServerID")] int ZoneServerID,
    [property: JsonPropertyName("territoryID")] uint TerritoryID,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("eventID")] uint EventID,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("lastSpawnedAt")] long LastSpawnedAt,
    [property: JsonPropertyName("observedState")] string? ObservedState,
    [property: JsonPropertyName("sourceCount")] int SourceCount,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("updatedAt")] long UpdatedAt,
    [property: JsonPropertyName("uploader")] string? Uploader,
    [property: JsonPropertyName("instanceID")] string? InstanceID,
    [property: JsonPropertyName("isActive")] bool? ServerIsActive)
{
    [JsonIgnore]
    public bool IsActive =>
        ServerIsActive ?? ObservedState is "Register" or "Warmup" or "Battle" or "Running";

    public DateTime LastSpawnedLocal
    {
        get
        {
            var seconds = LastSpawnedAt > 0 ? LastSpawnedAt : UpdatedAt / 1000L;
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().DateTime;
        }
    }
}

public sealed record CeListResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("updatedAt")] long UpdatedAt,
    [property: JsonPropertyName("instanceID")] string? InstanceID,
    [property: JsonPropertyName("retentionMinutes")] int RetentionMinutes,
    [property: JsonPropertyName("events")] CeRecord[]? Events);

// The server reports aggregate counts only. It deliberately does not return a
// per-player list any more: uploader identities are recorded server-side and
// never sent back out.
public sealed record CeStatsResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("online")] int Online,
    [property: JsonPropertyName("islandOnline")] int IslandOnline,
    [property: JsonPropertyName("instances")] int Instances,
    [property: JsonPropertyName("events")] int Events,
    [property: JsonPropertyName("retentionMinutes")] int RetentionMinutes);

/// <summary>
/// Heartbeat reply. The server leases a small number of upload slots per
/// island and renews them here, so <see cref="IsUploader"/> is the client's
/// authority on whether it should be uploading at all.
/// </summary>
public sealed record CeHeartbeatResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("online")] int Online,
    [property: JsonPropertyName("islandOnline")] int IslandOnline,
    [property: JsonPropertyName("isUploader")] bool IsUploader,
    [property: JsonPropertyName("uploaderSlots")] int UploaderSlots,
    [property: JsonPropertyName("pollIntervalSeconds")] int PollIntervalSeconds,
    [property: JsonPropertyName("heartbeatIntervalSeconds")] int HeartbeatIntervalSeconds,
    [property: JsonPropertyName("retentionMinutes")] int RetentionMinutes);

public sealed record CeObserveResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("isUploader")] bool IsUploader,
    [property: JsonPropertyName("reason")] string? Reason);


