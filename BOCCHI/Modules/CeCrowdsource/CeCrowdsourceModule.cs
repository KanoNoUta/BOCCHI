using BOCCHI.Data;
using BOCCHI.Modules.CriticalEncounters;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Modules;
using Ocelot.Windows;
using System;
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

    private readonly Dictionary<string, string> lastUploadedStates = new();

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
        Svc.Framework.Update += OnFrameworkTick;
        frameworkTickRegistered = true;
        if (TryGetModule<CriticalEncountersModule>(out var ceModule) && ceModule != null)
        {
            ceModule.Tracker.OnInactiveState += OnLocalCeInactive;
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

    public override void Dispose()
    {
        if (TryGetModule<CriticalEncountersModule>(out var ceModule) && ceModule != null)
        {
            ceModule.Tracker.OnInactiveState -= OnLocalCeInactive;
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

        if (ev.EventType >= 4)
        {
            return;
        }

        var territoryId = Svc.ClientState.TerritoryType;
        var key = $"{territoryId}:CE:{ev.DynamicEventId}";
        lastUploadedStates[key] = "Inactive";
        _ = UploadObservationAsync(territoryId, ev.DynamicEventId, DynamicEventState.Inactive, ev.StartTimestamp, ev.Name);
    }

    private async Task SendHeartbeatAsync()
    {
        try
        {
            var player = Svc.Objects.LocalPlayer;
            var world = player?.HomeWorld.Value.Name.ToString() ?? "";
            var name = player?.Name.TextValue ?? "unknown";
            var payload = new
            {
                name,
                world,
                dataCenterID = DataCenterID,
                zoneServerID = CeZoneServerId.Current,
                territoryID = Svc.ClientState.TerritoryType,
                instanceID = GetInstanceIdString(),
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
                        OnlineCount = stats.Online;
                        IslandOnlineCount = stats.IslandOnline;
                        // The heartbeat renews the upload lease, so this flag
                        // is what gates uploading until the next beat.
                        IsUploader = stats.IsUploader;
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
            SetError(ex.Message);
        }
    }

    private async Task FetchAsync()
    {
        try
        {
            var baseUrl = Config.ServerUrl.TrimEnd('/');
            var inIsland = ZoneData.IsInOccultCrescent();
            var territory = inIsland ? Svc.ClientState.TerritoryType : 0;
            var zone = CeZoneServerId.Current;
            var instance = GetInstanceIdString();
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
                    LastSyncAt = DateTime.Now;
                    Connected = true;
                    LastError = null;
                }
                await FetchStatsAsync(statsUrl);
                return;
            }

            if (!ceResponse.IsSuccessStatusCode)
            {
                SetError($"CE 接口 {ceResponse.StatusCode}");
                return;
            }

            var ceBody = await ceResponse.Content.ReadAsStringAsync(cts.Token);
            var ceList = JsonSerializer.Deserialize<CeListResponse>(ceBody, JsonOptions);
            if (ceList == null)
            {
                SetError("CE 响应解析失败");
                return;
            }

            ceETag = ceResponse.Headers.ETag?.Tag;
            ceETagScope = scope;

            await FetchStatsAsync(statsUrl);

            lock (sync)
            {
                if (ceList.RetentionMinutes > 0)
                {
                    RetentionMinutes = ceList.RetentionMinutes;
                }

                Records = (ceList.Events ?? [])
                    .Where(r => r.DataCenterID == DataCenterID)
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
            SetError(ex.Message);
        }
    }

    private async Task FetchStatsAsync(string statsUrl)
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
            OnlineCount = stats.Online;
            IslandOnlineCount = stats.IslandOnline;
            InstanceCount = stats.Instances;
            if (stats.RetentionMinutes > 0)
            {
                RetentionMinutes = stats.RetentionMinutes;
            }
        }
    }

    private static string GetInstanceIdString()
    {
        var id = GetCurrentInstanceId();
        return id > 0 ? id.ToString() : "";
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
    private void SetError(string message)
    {
        lock (sync)
        {
            Connected = false;
            LastError = message;
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

                // 只上报 CE；塔等事件的 EventType >= 4，且其 StartTimestamp 是
                // 未来的开始时间，误当出现时间上传会显示成"凌晨出现"。
                if (ev.EventType >= 4)
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
                _ = UploadObservationAsync(territoryId, ev.DynamicEventId, ev.State, ev.StartTimestamp, ev.Name);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[CeCrowdsource] upload scan failed: {ex.Message}");
        }
    }

    private async Task UploadObservationAsync(uint territoryId, uint eventId, DynamicEventState state, int startTimestamp, string name)
    {
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
                eventType = "CE",
                eventID = eventId,
                name,
                lastSpawnedAt = spawnedAt,
                observedState = state.ToString(),
                instanceID = GetInstanceIdString(),
                playerName = Svc.Objects.LocalPlayer?.Name.TextValue ?? "unknown",
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
                    lastUploadedStates.Remove($"{territoryId}:CE:{eventId}");
                    return;
                }

                Interlocked.Increment(ref uploadCountField);
                Svc.Log.Info($"[CeCrowdsource] uploaded CE {eventId} state {state}");
            }
            else
            {
                Svc.Log.Debug($"[CeCrowdsource] upload failed: {response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Svc.Log.Debug($"[CeCrowdsource] upload error: {ex.Message}");
        }
    }


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}










