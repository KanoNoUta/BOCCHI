using BOCCHI.Data;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Fates;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Modules;
using Ocelot.Windows;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BOCCHI.Modules.CeCrowdsource;

[OcelotModule(1007)]
public sealed class CeCrowdsourceModule(Plugin plugin, Config config) : Module(plugin, config)
{
    private const int DataCenterID = 101;

    private static readonly HttpClient client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        return http;
    }

    private readonly Panel panel = new();
    private readonly object sync = new();
    private readonly CancellationTokenSource cts = new();

    private DateTime nextHeartbeatAt = DateTime.UtcNow;
    private DateTime nextPollAt = DateTime.UtcNow;
    private DateTime nextUploadAt = DateTime.UtcNow;
    private Task? activeFetch;
    private Task? activeHeartbeat;
    private bool frameworkTickRegistered;
    private long presenceRevision;
    private uint presenceTerritoryId;
    private CeCrowdsourcePresenceScope lastPresenceScope;
    private bool hasPresenceScope;
    private string lastHeartbeatPlayerName = "unknown";
    private string lastHeartbeatWorld = string.Empty;

    public override CeCrowdsourceConfig Config
    {
        get => PluginConfig.CeCrowdsourceConfig;
    }

    public override bool ShouldUpdate => true;

    public override bool IsEnabled => Config.IsPropertyEnabled(nameof(Config.Enabled));

    public List<CeRecord> Records { get; private set; } = [];

    public int OnlineCount { get; private set; }

    public int IslandOnlineCount { get; private set; }

    /// <summary>
    /// True when the server has leased this client one of the island's upload
    /// slots. Only a handful of players per island upload; everyone else reads.
    /// </summary>
    public bool IsUploader { get; private set; }

    public int UploaderSlots { get; private set; } = 3;

    /// <summary>How long the server keeps records, in minutes (island lifetime).</summary>
    public int RetentionMinutes { get; private set; } = 180;

    public uint CurrentZoneServerId => CeZoneServerId.Current;

    public uint CurrentTerritoryId => Svc.ClientState.TerritoryType;

    public uint CurrentInstanceId => GetCurrentInstanceId();

    public int InstanceCount { get; private set; }

    public DateTime? LastSyncAt { get; private set; }

    public bool Connected { get; private set; }

    public string? LastError { get; private set; }

    private int uploadCountField;

    public int UploadCount => Volatile.Read(ref uploadCountField);

    private readonly ConcurrentDictionary<string, string> lastUploadedStates = new();

    // Server-driven pacing: the heartbeat reply carries the cadence, so load
    // can be shed centrally without shipping a new plugin build.
    private int serverPollIntervalSeconds = 30;
    private int serverHeartbeatIntervalSeconds = 30;

    // Cached ETag for the CE list. An unchanged island answers 304 with an
    // empty body, which is most of the bandwidth saving at scale.
    private string? ceETag;
    private string? ceETagScope;

    public override void PostInitialize()
    {
        base.PostInitialize();
        presenceTerritoryId = Svc.ClientState.TerritoryType;
        Svc.Framework.Update += OnFrameworkTick;
        frameworkTickRegistered = true;
        if (TryGetModule<CriticalEncountersModule>(out var ceModule) && ceModule != null)
        {
            ceModule.Tracker.OnInactiveState += OnLocalCeInactive;
        }

        if (TryGetModule<FatesModule>(out var fatesModule) && fatesModule != null)
        {
            fatesModule.tracker.OnFateSpawned += OnLocalPotSpawned;
            fatesModule.tracker.OnFateDespawned += OnLocalPotDespawned;
        }
    }

    public DynamicEventState? GetLocalCeState(uint eventId, uint territoryId)
    {
        if (territoryId != Svc.ClientState.TerritoryType || !ZoneData.IsInOccultCrescent())
        {
            return null;
        }

        if (!TryGetModule<CriticalEncountersModule>(out var ceModule) || ceModule == null)
        {
            return null;
        }

        if (ceModule.CriticalEncounters.TryGetValue(eventId, out var snapshot))
        {
            return snapshot.State;
        }

        // 同区域但本地已不在事件列表：该 CE 已结束
        return DynamicEventState.Inactive;
    }

    public bool IsEffectivelyActive(CeRecord record)
    {
        if (!string.Equals(record.EventType, "CE", StringComparison.OrdinalIgnoreCase))
        {
            return record.IsActive;
        }

        var local = GetLocalCeState(record.EventID, record.TerritoryID);
        return local.HasValue ? local.Value != DynamicEventState.Inactive : record.IsActive;
    }

    public override void Update(UpdateContext context)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (Config.UploadObservations)
        {
            TryUploadObservations();
        }
    }

    private void OnFrameworkTick(IFramework framework)
    {
        if (!IsEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        RefreshPresenceScope(now);

        if (Config.SendHeartbeat && now >= nextHeartbeatAt)
        {
            nextHeartbeatAt = now.AddSeconds(Math.Max(15, serverHeartbeatIntervalSeconds));
            if (activeHeartbeat is not { IsCompleted: false })
            {
                activeHeartbeat = SendHeartbeatAsync();
            }
        }

        if (now >= nextPollAt)
        {
            // The server's cadence wins when it asks for a slower one; the
            // user's setting can only be as aggressive as the server allows.
            var interval = Math.Max(Math.Max(5, Config.PollIntervalSeconds), serverPollIntervalSeconds);
            nextPollAt = now.AddSeconds(interval);
            if (activeFetch is not { IsCompleted: false })
            {
                activeFetch = FetchAsync();
            }
        }
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        Volatile.Write(ref presenceTerritoryId, id);
        RestartConnection(DateTime.UtcNow);
    }

    public override void Dispose()
    {
        if (TryGetModule<CriticalEncountersModule>(out var ceModule) && ceModule != null)
        {
            ceModule.Tracker.OnInactiveState -= OnLocalCeInactive;
        }

        if (TryGetModule<FatesModule>(out var fatesModule) && fatesModule != null)
        {
            fatesModule.tracker.OnFateSpawned -= OnLocalPotSpawned;
            fatesModule.tracker.OnFateDespawned -= OnLocalPotDespawned;
        }

        if (frameworkTickRegistered)
        {
            Svc.Framework.Update -= OnFrameworkTick;
            frameworkTickRegistered = false;
        }

        cts.Cancel();
        cts.Dispose();
        client.Dispose();
        base.Dispose();
    }

    private void OnLocalCeInactive(CriticalEncounterSnapshot ev)
    {
        if (!IsEnabled || !IsUploader || !Config.UploadObservations || !ZoneData.IsInOccultCrescent())
        {
            return;
        }

        var territoryId = Svc.ClientState.TerritoryType;
        if (!CeCrowdsourceObservationPolicy.ShouldUploadCriticalEncounter(
                territoryId,
                ev.EventType,
                ev.DynamicEventId))
        {
            return;
        }

        var key = $"{territoryId}:CE:{ev.DynamicEventId}";
        lastUploadedStates[key] = "Inactive";
        _ = UploadObservationAsync(territoryId, ev.DynamicEventId, "Inactive", ev.StartTimestamp, ev.Name, "CE");
    }

    private void OnLocalPotSpawned(Fate fate)
    {
        if (!fate.IsPotFate())
        {
            return;
        }

        UploadPotObservation(fate, "Running");
    }

    private void OnLocalPotDespawned(Fate fate)
    {
        if (!fate.IsPotFate())
        {
            return;
        }

        UploadPotObservation(fate, "Inactive");
    }

    private void TryUploadPotObservations()
    {
        if (!TryGetModule<FatesModule>(out var fatesModule) || fatesModule == null)
        {
            return;
        }

        foreach (var fate in fatesModule.fates.Values.ToArray())
        {
            if (fate.IsPotFate())
            {
                UploadPotObservation(fate, "Running");
            }
        }
    }

    private void UploadPotObservation(Fate fate, string state)
    {
        if (!IsEnabled || !IsUploader || !Config.UploadObservations || !ZoneData.IsInOccultCrescent())
        {
            return;
        }

        var territoryId = Svc.ClientState.TerritoryType;
        var key = $"{territoryId}:FATE:{fate.Id}";
        if (lastUploadedStates.TryGetValue(key, out var last) && last == state)
        {
            return;
        }

        lastUploadedStates[key] = state;
        _ = UploadObservationAsync(territoryId, fate.Id, state, (int)Math.Min(fate.SpawnedAt, int.MaxValue), fate.Name, "FATE");
    }

    private async Task SendHeartbeatAsync()
    {
        var revision = Volatile.Read(ref presenceRevision);
        var presence = CapturePresenceScope();
        if (presence.IsIsland && presence.ZoneServerId == 0)
        {
            // A zero zone ID cannot identify the current island. Sending it
            // would make the server classify this client as outside and a
            // stats request with zone=0 would return the all-island aggregate.
            nextHeartbeatAt = DateTime.UtcNow.AddSeconds(2);
            return;
        }

        try
        {
            var (name, world) = CacheHeartbeatIdentity();
            var payload = new
            {
                name,
                world,
                dataCenterID = DataCenterID,
                zoneServerID = presence.ZoneServerId,
                territoryID = presence.TerritoryId,
                instanceID = FormatInstanceId(presence.InstanceId),
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(Config.ApiToken))
            {
                content.Headers.TryAddWithoutValidation("X-Auth-Token", Config.ApiToken);
            }
            using var response = await client.PostAsync($"{Config.ServerUrl}/api/heartbeat", content, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                var stats = JsonSerializer.Deserialize<CeHeartbeatResponse>(body, JsonOptions);
                if (stats != null)
                {
                    lock (sync)
                    {
                        if (revision != Volatile.Read(ref presenceRevision))
                        {
                            return;
                        }

                        OnlineCount = stats.Online;
                        if (CeCrowdsourcePresencePolicy.CanPublishIslandPresence(
                                presence.IsIsland, presence.ZoneServerId))
                        {
                            IslandOnlineCount = stats.IslandOnline;
                        }
                        // The heartbeat renews the upload lease, so this flag
                        // is what gates uploading until the next beat.
                        IsUploader = CeCrowdsourcePresencePolicy.CanPublishIslandPresence(
                                         presence.IsIsland, presence.ZoneServerId)
                                     && stats.IsUploader;
                        if (stats.UploaderSlots > 0)
                        {
                            UploaderSlots = stats.UploaderSlots;
                        }

                        if (stats.RetentionMinutes > 0)
                        {
                            RetentionMinutes = stats.RetentionMinutes;
                        }

                        if (stats.PollIntervalSeconds > 0)
                        {
                            serverPollIntervalSeconds = stats.PollIntervalSeconds;
                        }

                        if (stats.HeartbeatIntervalSeconds > 0)
                        {
                            serverHeartbeatIntervalSeconds = stats.HeartbeatIntervalSeconds;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (TrySetError(revision, ex.Message))
            {
                nextHeartbeatAt = DateTime.UtcNow.AddSeconds(5);
            }
        }
    }

    private async Task FetchAsync()
    {
        var revision = Volatile.Read(ref presenceRevision);
        var presence = CapturePresenceScope();
        if (!presence.IsIsland)
        {
            // Island event history is scoped data. Do not query /api/ce with
            // zone=0 from outside the island, which would return every record.
            lock (sync)
            {
                if (revision != Volatile.Read(ref presenceRevision))
                {
                    return;
                }

                Records = [];
                IslandOnlineCount = 0;
                InstanceCount = 0;
                Connected = false;
                LastSyncAt = null;
            }

            nextPollAt = DateTime.UtcNow.AddSeconds(5);
            return;
        }

        if (presence.IsIsland && presence.ZoneServerId == 0)
        {
            // Wait until the zone server identity is available before making
            // an island-scoped request. zone=0 is an aggregate query on the
            // bridge server, not a valid current-island scope.
            nextPollAt = DateTime.UtcNow.AddSeconds(2);
            return;
        }

        try
        {
            var baseUrl = Config.ServerUrl.TrimEnd('/');
            var territory = presence.IsIsland ? presence.TerritoryId : 0;
            var zone = presence.IsIsland ? presence.ZoneServerId : 0;
            var instance = presence.IsIsland ? FormatInstanceId(presence.InstanceId) : string.Empty;
            // Scope the query to this exact island. The server answers with
            // that island's records only, already limited to the retention
            // window, so no client-side instance filtering is needed.
            var scope = $"{zone}:{instance}:{territory}";
            var ceUrl = $"{baseUrl}/api/ce?dc={DataCenterID}&zone={zone}&instance={instance}&territory={territory}";
            var statsUrl = $"{baseUrl}/api/stats?dc={DataCenterID}&zone={zone}&instance={instance}";

            using var ceRequest = new HttpRequestMessage(HttpMethod.Get, ceUrl);
            // A cached ETag only applies to the island it was issued for.
            if (ceETag != null && ceETagScope == scope)
            {
                ceRequest.Headers.TryAddWithoutValidation("If-None-Match", ceETag);
            }

            using var ceResponse = await client.SendAsync(ceRequest, cts.Token);

            if (ceResponse.StatusCode == HttpStatusCode.NotModified)
            {
                // Nothing changed on this island; keep the records we have.
                lock (sync)
                {
                    if (revision != Volatile.Read(ref presenceRevision))
                    {
                        return;
                    }

                    LastSyncAt = DateTime.Now;
                    Connected = true;
                    LastError = null;
                }
                await FetchStatsAsync(statsUrl, revision, presence.IsIsland);
                return;
            }

            if (!ceResponse.IsSuccessStatusCode)
            {
                if (TrySetError(revision, $"CE 接口 {ceResponse.StatusCode}"))
                {
                    nextPollAt = DateTime.UtcNow.AddSeconds(5);
                }
                return;
            }

            var ceBody = await ceResponse.Content.ReadAsStringAsync(cts.Token);
            var ceList = JsonSerializer.Deserialize<CeListResponse>(ceBody, JsonOptions);
            if (ceList == null)
            {
                if (TrySetError(revision, "CE 响应解析失败"))
                {
                    nextPollAt = DateTime.UtcNow.AddSeconds(5);
                }
                return;
            }

            if (revision != Volatile.Read(ref presenceRevision))
            {
                return;
            }

            ceETag = ceResponse.Headers.ETag?.Tag;
            ceETagScope = scope;

            await FetchStatsAsync(statsUrl, revision, presence.IsIsland);

            lock (sync)
            {
                if (revision != Volatile.Read(ref presenceRevision))
                {
                    return;
                }

                if (ceList.RetentionMinutes > 0)
                {
                    RetentionMinutes = ceList.RetentionMinutes;
                }

                Records = (ceList.Events ?? [])
                    .Where(r => r.DataCenterID == DataCenterID)
                    .Where(CeCrowdsourceDisplayPolicy.ShouldDisplayRecord)
                    .OrderBy(r => r.TerritoryID)
                    .ThenBy(r => r.EventID)
                    .ToList();
                LastSyncAt = DateTime.Now;
                Connected = true;
                LastError = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (TrySetError(revision, ex.Message))
            {
                nextPollAt = DateTime.UtcNow.AddSeconds(5);
            }
        }
    }

    private async Task FetchStatsAsync(string statsUrl, long revision, bool isIslandScope)
    {
        using var statsResponse = await client.GetAsync(statsUrl, cts.Token);
        if (!statsResponse.IsSuccessStatusCode)
        {
            return;
        }

        var statsBody = await statsResponse.Content.ReadAsStringAsync(cts.Token);
        var stats = JsonSerializer.Deserialize<CeStatsResponse>(statsBody, JsonOptions);
        if (stats == null)
        {
            return;
        }

        lock (sync)
        {
            if (revision != Volatile.Read(ref presenceRevision))
            {
                return;
            }

            OnlineCount = stats.Online;
            IslandOnlineCount = isIslandScope ? stats.IslandOnline : 0;
            InstanceCount = isIslandScope ? stats.Instances : 0;
            if (stats.RetentionMinutes > 0)
            {
                RetentionMinutes = stats.RetentionMinutes;
            }
        }
    }

    private static string GetInstanceIdString()
    {
        return FormatInstanceId(GetCurrentInstanceId());
    }

    private static string FormatInstanceId(uint id)
    {
        return id > 0 ? id.ToString() : string.Empty;
    }

    private CeCrowdsourcePresenceScope CapturePresenceScope()
    {
        var territoryId = Volatile.Read(ref presenceTerritoryId);
        if (!ZoneData.IsOccultCrescentTerritory(territoryId))
        {
            return new CeCrowdsourcePresenceScope(false, territoryId, 0, 0);
        }

        // Territory membership is authoritative. Nearby player objects are
        // streamed and may disappear briefly; they must never clear presence.
        return new CeCrowdsourcePresenceScope(
            true,
            territoryId,
            CeZoneServerId.Current,
            GetCurrentInstanceId());
    }

    private void RefreshPresenceScope(DateTime now)
    {
        var current = CapturePresenceScope();
        if (!hasPresenceScope)
        {
            lastPresenceScope = current;
            hasPresenceScope = true;
            return;
        }

        if (!CeCrowdsourcePresencePolicy.ShouldRestartConnection(lastPresenceScope, current))
        {
            return;
        }

        lastPresenceScope = current;
        RestartConnection(now);
    }

    private void RestartConnection(DateTime now)
    {
        Interlocked.Increment(ref presenceRevision);

        // Old requests are revision-guarded, so release their scheduling slots
        // immediately. This lets a newly resolved island scope connect without
        // waiting for the previous request's timeout or regular cadence.
        activeHeartbeat = null;
        activeFetch = null;
        nextHeartbeatAt = now;
        nextPollAt = now;
        nextUploadAt = now;
        ceETag = null;
        ceETagScope = null;
        lastUploadedStates.Clear();

        lock (sync)
        {
            IslandOnlineCount = 0;
            InstanceCount = 0;
            IsUploader = false;
            Records = [];
            Connected = false;
            LastSyncAt = null;
            LastError = null;
        }
    }

    private (string Name, string World) CacheHeartbeatIdentity()
    {
        try
        {
            var player = Svc.Objects?.LocalPlayer;
            if (player == null)
            {
                return (lastHeartbeatPlayerName, lastHeartbeatWorld);
            }

            try
            {
                var name = player.Name.TextValue;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    lastHeartbeatPlayerName = name;
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"[CeCrowdsource] player identity not ready: {ex.Message}");
            }

            try
            {
                var world = player.HomeWorld.Value.Name.ToString();
                if (!string.IsNullOrWhiteSpace(world))
                {
                    lastHeartbeatWorld = world;
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"[CeCrowdsource] player world not ready: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // ObjectTable itself can be unavailable while Dalamud is still
            // constructing the plugin. Identity is optional; loading BOCCHI is
            // not, so keep the defaults and retry on the next heartbeat.
            Svc.Log.Debug($"[CeCrowdsource] identity cache failed: {ex.Message}");
        }

        return (lastHeartbeatPlayerName, lastHeartbeatWorld);
    }

    private static unsafe uint GetCurrentInstanceId()
    {
        try
        {
            return FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance()->PublicInstance.InstanceId;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[CeCrowdsource] instance read failed: {ex.Message}");
            return 0;
        }
    }
    private bool TrySetError(long revision, string message)
    {
        lock (sync)
        {
            if (revision != Volatile.Read(ref presenceRevision))
            {
                return false;
            }

            Connected = false;
            LastError = message;
            return true;
        }
    }

    private void TryUploadObservations()
    {
        // Only lease holders upload. On a busy island most clients are pure
        // readers, which is what keeps write traffic flat as the population
        // grows instead of scaling with it.
        if (!IsUploader || !ZoneData.IsInOccultCrescent() || DateTime.UtcNow < nextUploadAt)
        {
            return;
        }

        nextUploadAt = DateTime.UtcNow.AddSeconds(10);
        var territoryId = Svc.ClientState.TerritoryType;

        try
        {
            if (!TryGetModule<CriticalEncountersModule>(out var ceModule) || ceModule == null)
            {
                return;
            }

            var snapshots = ceModule.CriticalEncounters.Values.ToArray();
            foreach (var ev in snapshots)
            {
                if (ev.State == DynamicEventState.Inactive)
                {
                    continue;
                }

                // Ordinary CEs and known Forked Tower events share the CE
                // history feed. Other high-type dynamic events stay excluded.
                if (!CeCrowdsourceObservationPolicy.ShouldUploadCriticalEncounter(
                        territoryId,
                        ev.EventType,
                        ev.DynamicEventId))
                {
                    continue;
                }

                var key = $"{territoryId}:CE:{ev.DynamicEventId}";
                var stateName = ev.State.ToString();
                if (lastUploadedStates.TryGetValue(key, out var last) && last == stateName)
                {
                    continue;
                }

                lastUploadedStates[key] = stateName;
                _ = UploadObservationAsync(territoryId, ev.DynamicEventId, stateName, ev.StartTimestamp, ev.Name, "CE");
            }

            TryUploadPotObservations();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[CeCrowdsource] upload scan failed: {ex.Message}");
        }
    }

    private async Task UploadObservationAsync(
        uint territoryId,
        uint eventId,
        string state,
        int startTimestamp,
        string name,
        string eventType)
    {
        var observationKey = $"{territoryId}:{eventType}:{eventId}";
        try
        {
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Once a CE is in Battle, StartTimestamp holds the countdown
            // deadline rather than the spawn time, so it points into the
            // future. Never report a spawn that hasn't happened yet.
            var spawnedAt = startTimestamp > 0 && startTimestamp <= nowSec ? startTimestamp : nowSec;
            var payload = new
            {
                dataCenterID = DataCenterID,
                zoneServerID = CeZoneServerId.Current,
                territoryID = territoryId,
                eventType,
                eventID = eventId,
                name,
                lastSpawnedAt = spawnedAt,
                observedState = state,
                instanceID = GetInstanceIdString(),
                playerName = CacheHeartbeatIdentity().Name,
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(Config.ApiToken))
            {
                content.Headers.TryAddWithoutValidation("X-Auth-Token", Config.ApiToken);
            }
            using var response = await client.PostAsync($"{Config.ServerUrl}/api/ce/observe", content, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                var result = JsonSerializer.Deserialize<CeObserveResponse>(body, JsonOptions);
                if (result is { IsUploader: false })
                {
                    // The island already has enough reporters. Stand down now
                    // rather than retrying until the next heartbeat renews.
                    lock (sync)
                    {
                        IsUploader = false;
                    }

                    // Forget the state we just recorded so the observation is
                    // re-sent if this client later regains a slot.
                    lastUploadedStates.TryRemove(observationKey, out _);
                    return;
                }

                Interlocked.Increment(ref uploadCountField);
                Svc.Log.Info($"[CeCrowdsource] uploaded {eventType} {eventId} state {state}");
            }
            else
            {
                lastUploadedStates.TryRemove(observationKey, out _);
                Svc.Log.Debug($"[CeCrowdsource] upload failed: {response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            lastUploadedStates.TryRemove(observationKey, out _);
            Svc.Log.Debug($"[CeCrowdsource] upload error: {ex.Message}");
        }
    }


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

}










