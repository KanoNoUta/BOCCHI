using BOCCHI.Data;
using BOCCHI.Pathfinding;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;

namespace BOCCHI.Modules.AggroRange;

public sealed class Panel
{
    public void Draw(AggroRangeModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        if (!ZoneData.IsInNorthHorn())
        {
            BocchiUi.EmptyState(module.T("panel.outside_title"), module.T("panel.outside_detail"));
            return;
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columns = availableWidth >= 800f ? 4 : BocchiUiPolicy.GetWorkspaceColumns(availableWidth);
        if (ImGui.BeginTable(
                "##AggroRangeMetrics",
                columns,
                ImGuiTableFlags.SizingStretchSame | (columns > 1 ? ImGuiTableFlags.BordersInnerV : ImGuiTableFlags.None)))
        {
            ImGui.TableNextColumn();
            BocchiUi.Metric(module.T("panel.visible"), module.VisibleMobCount.ToString());
            ImGui.TableNextColumn();
            BocchiUi.Metric(module.T("panel.inside"), module.InsideRangeCount.ToString());
            ImGui.TableNextColumn();
            BocchiUi.Metric(module.T("panel.calibrated"), module.CalibratedMobCount.ToString());
            ImGui.TableNextColumn();
            BocchiUi.Metric(
                module.T("panel.route_planning"),
                AggroAvoidanceNavigation.IsPlanning
                    ? module.T("panel.planning")
                    : module.T("panel.idle"));
            ImGui.EndTable();
        }
        ImGui.TextDisabled(string.Format(module.T("panel.catalog_summary"), CommonMobCatalog.Count));
    }
}
