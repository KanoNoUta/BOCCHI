using BOCCHI.Data;
using BOCCHI.Modules.ForkedTower;
using BOCCHI.Modules.Teleporter;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.CriticalEncounters;

public class Panel
{
    private readonly Dictionary<uint, TowerCapture.CaptureResult> lastTowerCaptures = [];

    public void Draw(CriticalEncountersModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            var active = module.CriticalEncounters.Values.Count(ev => ev.State != DynamicEventState.Inactive);
            var hasTrackedTower = module.Config.TrackForkedTower
                                  && module.CriticalEncounters.Values.Any(ev => ev.EventType >= 4);
            if (active <= 0 && !hasTrackedTower)
            {
                ImGui.TextUnformatted(module.T("panel.none"));
                return;
            }

            foreach (var ev in module.CriticalEncounters.Values)
            {
                if (!ZoneData.IsInOccultCrescent())
                {
                    module.CriticalEncounters.Clear();
                    return;
                }

                if (ev.EventType >= 4)
                {
                    HandleTower(ev, module);
                    continue;
                }

                if (ev.State == DynamicEventState.Inactive)
                {
                    continue;
                }

                var data = EventData.GetCriticalEncounter(ev.DynamicEventId, ECommons.DalamudServices.Svc.ClientState.TerritoryType);

                ImGui.TextUnformatted(ev.Name.ToString());

                switch (ev.State)
                {
                    case DynamicEventState.Register:
                        {
                            var start = DateTimeOffset.FromUnixTimeSeconds(ev.StartTimestamp).DateTime;
                            var timeUntilStart = start - DateTime.UtcNow;
                            var formattedTime = $"{timeUntilStart.Minutes:D2}:{timeUntilStart.Seconds:D2}";

                            ImGui.SameLine();
                            ImGui.TextUnformatted($"({module.T("panel.register")}: {formattedTime})");
                            break;
                        }
                    case DynamicEventState.Warmup:
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"({module.T("panel.warmup")})");
                        break;
                    case DynamicEventState.Battle:
                        {
                            ImGui.SameLine();
                            ImGui.TextUnformatted($"({ev.Progress}%)");

                            if (module.Progress.TryGetValue(ev.DynamicEventId, out var progress))
                            {
                                var estimate = progress.EstimateTimeToCompletion();
                                if (estimate != null)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextUnformatted($"({module.T("panel.estimated")} {estimate.Value:mm\\:ss})");
                                }
                            }

                            break;
                        }
                    case DynamicEventState.Inactive:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (ev.State != DynamicEventState.Register)
                {
                    OcelotUi.Indent(() => EventIconRenderer.Drops(data, module.PluginConfig.EventDropConfig));
                    continue;
                }

                if (module.TryGetModule<TeleporterModule>(out var teleporter) && teleporter!.IsReady())
                {
                    var start = ev.MapMarker.Position;

                    teleporter.teleporter.Button(data.Aethernet, start, ev.Name.ToString(), $"ce_{ev.DynamicEventId}", data);
                }

                OcelotUi.Indent(() => EventIconRenderer.Drops(data, module.PluginConfig.EventDropConfig));
            }
        });
    }


    private void HandleTower(CriticalEncounterSnapshot ev, CriticalEncountersModule module)
    {
        if (!module.Config.TrackForkedTower || ev.State == DynamicEventState.Battle)
        {
            return;
        }

        var eventId = (uint)ev.DynamicEventId;
        var state = module.Tracker.TowerTimer.GetState(eventId);

        if (ev.State == DynamicEventState.Inactive)
        {
            ImGui.TextUnformatted($"{ev.Name}:");

            var time = module.Tracker.TowerTimer.GetTimeToForkedTowerSpawn(eventId, ev.State);
            OcelotUi.Indent(() => { OcelotUi.LabelledValue("两歧塔出现预计还需", $"{time:mm\\:ss}"); });
        }
        else
        {
            ImGui.TextUnformatted($"{ev.Name}:");

            var time = module.Tracker.TowerTimer.GetTimeRemainingToRegister(ev);
            OcelotUi.Indent(() => { OcelotUi.LabelledValue("两歧塔报名时间", $"{time:mm\\:ss}"); });
        }

        OcelotUi.Indent(32, () =>
        {
            OcelotUi.LabelledValue("紧急遭遇战已完成", state.CriticalEncountersCompleted);
            OcelotUi.LabelledValue("FATE已完成", state.FatesCompleted);
        });

        if (!TowerHelper.TryGetDefinitionByEventId(eventId, out var definition))
        {
            return;
        }

        if (definition.HasPlatformGeometry && TowerHelper.IsPlayerNearTower(definition.Type))
        {
            OcelotUi.Indent(() =>
            {
                OcelotUi.LabelledValue("平台上的玩家", TowerHelper.GetPlayersInTowerZone(definition.Type));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("包括你的角色");
                }

                OcelotUi.LabelledValue("平台附近的玩家", TowerHelper.GetPlayersNearTowerZone(definition.Type));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("包括你的角色");
                }
            });
            return;
        }

        // Event IDs are verified, while the two North Horn platform shapes
        // still require a live sample.  Surface the runtime marker and export
        // all nearby PCs/EObjs/traps instead of inventing geometry.
        if (definition.TerritoryId == ZoneData.NORTHHORN)
        {
            var marker = ev.MapMarker.Position;
            OcelotUi.Indent(() =>
            {
                OcelotUi.LabelledValue("平台范围", "待实机精准采集");
                OcelotUi.LabelledValue("事件标记坐标", $"{marker.X:F3}, {marker.Y:F3}, {marker.Z:F3}");

                if (ImGui.Button($"保存并复制塔采集数据##tower-capture-{eventId}"))
                {
                    try
                    {
                        lastTowerCaptures[eventId] = TowerCapture.Save(eventId, ev.State.ToString(), marker);
                    }
                    catch (Exception exception)
                    {
                        ECommons.DalamudServices.Svc.Log.Error($"Failed to capture tower runtime data: {exception}");
                    }
                }

                if (lastTowerCaptures.TryGetValue(eventId, out var capture))
                {
                    ImGui.TextWrapped($"已保存：{capture.Path}");
                    if (!string.IsNullOrEmpty(capture.ClipboardError))
                    {
                        ImGui.TextWrapped($"剪贴板复制失败：{capture.ClipboardError}");
                    }
                }
            });
        }
    }
}
