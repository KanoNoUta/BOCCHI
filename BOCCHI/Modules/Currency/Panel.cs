using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;

namespace BOCCHI.Modules.Currency;

public class Panel
{
    public void Draw(CurrencyModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        if (ImGui.BeginTable("##CurrencyMetrics", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            DrawMetricRow(
                module,
                module.T("panel.silver.label"),
                module.Tracker.GetSilverPerHour().ToString("F2"),
                "Silver",
                module.Tracker.ResetSilver);
            DrawMetricRow(
                module,
                module.T("panel.gold.label"),
                module.Tracker.GetGoldPerHour().ToString("F2"),
                "Gold",
                module.Tracker.ResetGold);
            ImGui.EndTable();
        }
    }

    private static void DrawMetricRow(
        CurrencyModule module,
        string label,
        string value,
        string id,
        Action reset)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (BocchiUi.IconButton(
                FontAwesomeIcon.Redo,
                $"Reset{id}",
                string.Format(module.T("panel.reset.tooltip"), label)))
        {
            ImGui.OpenPopup($"##ConfirmReset{id}");
        }
        DrawResetPopup(module, id, label, reset);

        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private static void DrawResetPopup(CurrencyModule module, string id, string label, Action reset)
    {
        if (!ImGui.BeginPopup($"##ConfirmReset{id}"))
        {
            return;
        }

        ImGui.TextUnformatted(string.Format(module.T("panel.reset.confirm"), label));
        if (ImGui.Button($"{module.T("panel.reset.action")}##Confirm{id}"))
        {
            reset();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button($"{module.T("panel.reset.cancel")}##Cancel{id}"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
