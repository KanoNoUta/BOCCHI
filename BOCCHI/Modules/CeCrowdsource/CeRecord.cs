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
    [property: JsonPropertyName("events")] CeRecord[]? Events);

public sealed record CeStatsResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("online")] int Online,
    [property: JsonPropertyName("islandOnline")] int IslandOnline,
    [property: JsonPropertyName("players")] OnlinePlayer[]? Players,
    [property: JsonPropertyName("instances")] int Instances,
    [property: JsonPropertyName("events")] int Events);

public sealed record OnlinePlayer(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("world")] string? World,
    [property: JsonPropertyName("zoneServerID")] int ZoneServerID,
    [property: JsonPropertyName("territoryID")] uint TerritoryID,
    [property: JsonPropertyName("instanceID")] string? InstanceID,
    [property: JsonPropertyName("lastSeenAt")] long LastSeenAt)
{
    public DateTime LastSeenLocal =>
        DateTimeOffset.FromUnixTimeMilliseconds(LastSeenAt).ToLocalTime().DateTime;
}


