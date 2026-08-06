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

    private readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

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

    public List<OnlinePlayer> OnlinePlayers { get; private set; } = [];

    public int OnlineCount { get; private set; }

    public int IslandOnlineCount { get; private set; }

    public uint CurrentZoneServerId => CeZoneServerId.Current;

    public uint CurrentInstanceId => GetCurrentInstanceId();

    public int InstanceCount { get; private set; }

    public DateTime? LastSyncAt { get; private set; }

    public bool Connected { get; private set; }

    public string? LastError { get; private set; }

    private int uploadCountField;

    public int UploadCount => Volatile.Read(ref uploadCountField);

    private readonly Dictionary<string, string> lastUploadedStates = new();

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
            nextHeartbeatAt = now.AddSeconds(30);
            if (activeHeartbeat is not { IsCompleted: false })
            {
                activeHeartbeat = SendHeartbeatAsync();
            }
        }

        if (now >= nextPollAt)
        {
            nextPollAt = now.AddSeconds(Math.Max(5, Config.PollIntervalSeconds));
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
        if (!IsEnabled || !Config.UploadObservations || !ZoneData.IsInOccultCrescent())
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
                zoneServerID = CeZoneServerId.Current,
                territoryID = Svc.ClientState.TerritoryType,
                instanceID = GetInstanceIdString(),
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{Config.ServerUrl}/api/heartbeat", content, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                var stats = JsonSerializer.Deserialize<CeStatsResponse>(body, JsonOptions);
                if (stats != null)
                {
                    lock (sync)
                    {
                        OnlineCount = stats.Online;
                        IslandOnlineCount = stats.IslandOnline;
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
            var ceUrl = $"{baseUrl}/api/ce?dc={DataCenterID}&zone={CeZoneServerId.Current}";
            var statsUrl = $"{baseUrl}/api/stats?zone={CeZoneServerId.Current}";

            using var ceResponse = await client.GetAsync(ceUrl, cts.Token);
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

            using var statsResponse = await client.GetAsync(statsUrl, cts.Token);
            if (statsResponse.IsSuccessStatusCode)
            {
                var statsBody = await statsResponse.Content.ReadAsStringAsync(cts.Token);
                var stats = JsonSerializer.Deserialize<CeStatsResponse>(statsBody, JsonOptions);
                if (stats != null)
                {
                    lock (sync)
                    {
                        OnlineCount = stats.Online;
                        IslandOnlineCount = stats.IslandOnline;
                        OnlinePlayers = (stats.Players ?? []).ToList();
                        InstanceCount = stats.Instances;
                    }
                }
            }
            lock (sync)
            {
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
        if (!ZoneData.IsInOccultCrescent() || DateTime.UtcNow < nextUploadAt)
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
            var payload = new
            {
                dataCenterID = DataCenterID,
                zoneServerID = CeZoneServerId.Current,
                territoryID = territoryId,
                eventType = "CE",
                eventID = eventId,
                name,
                lastSpawnedAt = startTimestamp > 0 ? startTimestamp : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                observedState = state.ToString(),
                instanceID = GetInstanceIdString(),
                playerName = Svc.Objects.LocalPlayer?.Name.TextValue ?? "unknown",
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{Config.ServerUrl}/api/ce/observe", content, cts.Token);
            if (response.IsSuccessStatusCode)
            {
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










