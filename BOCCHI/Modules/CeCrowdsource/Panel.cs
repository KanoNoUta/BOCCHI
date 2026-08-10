using BOCCHI.Data;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.CeCrowdsource;

public class Panel
{
    public void Draw(CeCrowdsourceModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        DrawHeader(module);

        if (!module.IsEnabled)
        {
            BocchiUi.EmptyState(module.T("panel.disabled_title"), module.T("panel.disabled_detail"));
            return;
        }

        if (!module.Connected)
        {
            BocchiUi.EmptyState(
                module.T("panel.server_error"),
                string.IsNullOrWhiteSpace(module.LastError)
                    ? module.T("panel.connecting")
                    : $"{module.T("panel.last_error")}: {module.LastError}");
            return;
        }

        // 服务端已按当前岛（区服+副本ID）和保留时长过滤，这里不再重复筛选实例。
        var records = module.Records
            .Where(r => r.EventType == "CE")
            .Where(CeCrowdsourceDisplayPolicy.ShouldDisplayRecord)
            .Where(r => !module.Config.ShowOnlyActive || module.IsEffectivelyActive(r))
            .ToArray();

        if (records.Length == 0)
        {
            BocchiUi.EmptyState(module.T("panel.no_data"), module.T("panel.no_data_detail"));
            return;
        }

        foreach (var group in records.GroupBy(r => r.TerritoryID).OrderBy(g => g.Key))
        {
            var zoneLabel = GetZoneLabel(group.Key, module);
            BocchiUi.SectionHeading(zoneLabel);
            OcelotUi.Indent(() =>
            {
                foreach (var record in group.OrderByDescending(r => r.LastSpawnedAt))
                {
                    DrawRecord(record, module);
                }
            });
        }
    }

    private void DrawHeader(CeCrowdsourceModule module)
    {
        if (ImGui.BeginTable(
                "##CeCrowdsourceHeader",
                4,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableNextColumn();
            OcelotUi.LabelledValue(module.T("panel.online"), module.OnlineCount.ToString());

            ImGui.TableNextColumn();
            OcelotUi.LabelledValue(module.T("panel.island_online"), module.IslandOnlineCount.ToString());

            ImGui.TableNextColumn();
            OcelotUi.LabelledValue(module.T("panel.events"), module.Records.Count.ToString());

            ImGui.TableNextColumn();
            var lastSync = module.LastSyncAt?.ToString("HH:mm:ss") ?? "--";
            OcelotUi.LabelledValue(module.T("panel.last_sync"), lastSync);
            ImGui.EndTable();
        }

        if (module.UploadCount > 0)
        {
            ImGui.TextDisabled(string.Format(module.T("panel.uploaded"), module.UploadCount));
        }

        // 上传名额由服务端分配：每个岛只有少数人上传，其余只读取。
        if (module.Config.UploadObservations && ZoneData.IsInOccultCrescent())
        {
            ImGui.TextDisabled(module.IsUploader
                ? string.Format(module.T("panel.uploader_active"), module.UploaderSlots)
                : module.T("panel.uploader_standby"));
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    private static void DrawRecord(CeRecord record, CeCrowdsourceModule module)
    {
        var name = ResolveName(record, module);
        var localState = module.GetLocalCeState(record.EventID, record.TerritoryID);
        var effectiveState = CeCrowdsourceDisplayPolicy.ResolveState(
            record.ObservedState,
            localState?.ToString(),
            record.IsActive);
        var (label, color) = GetStatePresentation(effectiveState, module);

        ImGui.TextColored(color, name);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 1f), label);

        OcelotUi.Indent(() =>
        {
            OcelotUi.LabelledValue(module.T("panel.spawned_at"), record.LastSpawnedLocal.ToString("HH:mm:ss"));
        });
        ImGui.Spacing();
    }

    private static string ResolveName(CeRecord record, CeCrowdsourceModule module)
    {
        if (!string.IsNullOrWhiteSpace(record.Name))
        {
            return record.Name;
        }

        var data = EventData.GetCriticalEncounter(record.EventID, record.TerritoryID);
        if (!data.InternalName.StartsWith("Critical Encounter"))
        {
            return data.InternalName;
        }

        return $"{module.T("panel.ce")} {record.EventID}";
    }

    private static (string Label, Vector4 Color) GetStatePresentation(string? state, CeCrowdsourceModule module)
    {
        return state switch
        {
            "Register" => (module.T("panel.state_register"), new Vector4(0.95f, 0.85f, 0.2f, 1f)),
            "Warmup" => (module.T("panel.state_warmup"), new Vector4(1f, 0.65f, 0.15f, 1f)),
            "Battle" => (module.T("panel.state_battle"), new Vector4(0.95f, 0.35f, 0.3f, 1f)),
            "Running" => (module.T("panel.state_running"), new Vector4(0.5f, 0.85f, 0.45f, 1f)),
            "Inactive" => (module.T("panel.ended"), new Vector4(0.45f, 0.45f, 0.45f, 1f)),
            _ => (state ?? "--", new Vector4(0.6f, 0.6f, 0.6f, 1f)),
        };
    }

    private static string GetZoneLabel(uint territoryId, CeCrowdsourceModule module)
    {
        return territoryId switch
        {
            ZoneData.SOUTHHORN => module.T("panel.south"),
            ZoneData.NORTHHORN => module.T("panel.north"),
            _ => $"{module.T("panel.unknown_zone")} {territoryId}",
        };
    }
}
