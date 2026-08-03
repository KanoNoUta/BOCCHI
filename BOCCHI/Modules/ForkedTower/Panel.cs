using BOCCHI.Data;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Ocelot.Ui;
using System;
using System.Collections.Generic;

namespace BOCCHI.Modules.ForkedTower;

public class Panel
{
    private readonly Dictionary<uint, TowerCapture.CaptureResult> lastCaptures = [];

    public void Draw(ForkedTowerModule module)
    {
        if (!ZoneData.IsInForkedTower())
        {
            return;
        }

        BocchiUi.SectionHeading(module.T("panel.title"));
        OcelotUi.Indent(() =>
        {
            var eventId = module.TowerRun.DynamicEventId != 0
                ? module.TowerRun.DynamicEventId
                : ZoneData.GetCurrentForkedTowerEventId();
            if (TowerHelper.TryGetDefinitionByEventId(eventId, out var definition))
            {
                OcelotUi.LabelledValue(module.T("panel.tower"), definition.DisplayName);
            }

            OcelotUi.LabelledValue(module.T("panel.discovered_traps"), module.TowerRun.DiscoveredTrapCount);
            if (!module.PluginConfig.ShowAdvancedUi)
            {
                return;
            }

            BocchiUi.SectionHeading("采集诊断");
            OcelotUi.LabelledValue(module.T("panel.dynamic_event_id"), eventId);
            var state = OcelotUi.LabelledValue(module.T("panel.tower_id"), module.TowerRun.Hash);
            if (state == UiState.Hovered)
            {
                ImGui.SetTooltip(module.T("panel.tower_id_tooltip"));
            }
            OcelotUi.LabelledValue(module.T("panel.unmapped_traps"), module.TowerRun.DiscoveredUnmappedTrapCount);

            if (eventId != 0 && ImGui.Button($"保存并复制当前塔采集数据##tower-run-capture-{eventId}"))
            {
                try
                {
                    lastCaptures[eventId] = TowerCapture.Save(
                        eventId,
                        "Battle",
                        capturedTraps: module.TowerRun.CapturedTrapSnapshots);
                }
                catch (Exception exception)
                {
                    Svc.Log.Error($"Failed to capture tower runtime data: {exception}");
                }
            }

            if (!lastCaptures.TryGetValue(eventId, out var capture))
            {
                return;
            }

            ImGui.TextWrapped($"已保存：{capture.Path}");
            if (!string.IsNullOrEmpty(capture.ClipboardError))
            {
                ImGui.TextWrapped($"剪贴板复制失败：{capture.ClipboardError}");
            }
        });
    }

    public void Reset()
    {
        lastCaptures.Clear();
    }
}
