using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;

namespace BOCCHI.Modules.Treasure;

public class Panel
{
    public void Draw(TreasureModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        if (!module.Tracker.CountInitialised)
        {
            BocchiUi.EmptyState(module.T("panel.detecting_title"), module.T("panel.detecting_detail"));
            return;
        }

        var columns = BocchiUiPolicy.GetWorkspaceColumns(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginTable(
                "##TreasureCounts",
                columns,
                ImGuiTableFlags.SizingStretchSame | (columns > 1 ? ImGuiTableFlags.BordersInnerV : ImGuiTableFlags.None)))
        {
            ImGui.TableNextColumn();
            BocchiUi.Metric(
                module.T("panel.active_bronze.label"),
                FormatCount(module.Tracker.BronzeChests, 30, module.Config.ShowPercentageActiveTreasureCount));
            ImGui.TableNextColumn();
            BocchiUi.Metric(
                module.T("panel.active_silver.label"),
                FormatCount(module.Tracker.SilverChests, 8, module.Config.ShowPercentageActiveTreasureCount));
            ImGui.EndTable();
        }
    }

    private static string FormatCount(int count, int maximum, bool includePercentage)
    {
        return includePercentage
            ? $"{count}/{maximum}  ({count / (float)maximum * 100f:F1}%)"
            : $"{count}/{maximum}";
    }
}
