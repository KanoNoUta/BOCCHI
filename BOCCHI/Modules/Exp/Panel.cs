using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace BOCCHI.Modules.Exp;

public class Panel
{
    public void Draw(ExpModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        if (BocchiUi.IconButton(
                FontAwesomeIcon.Redo,
                "ResetExp",
                string.Format(module.T("panel.reset.tooltip"), module.T("panel.exp.label"))))
        {
            ImGui.OpenPopup("##ConfirmResetExp");
        }
        ImGui.SameLine();
        ImGui.TextDisabled(module.T("panel.exp.label"));
        ImGui.SameLine();
        ImGui.TextUnformatted(module.tracker.GetExpPerHour().ToString("F2"));

        if (!ImGui.BeginPopup("##ConfirmResetExp"))
        {
            return;
        }

        ImGui.TextUnformatted(string.Format(module.T("panel.reset.confirm"), module.T("panel.exp.label")));
        if (ImGui.Button($"{module.T("panel.reset.action")}##ConfirmExp"))
        {
            module.tracker.Reset();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button($"{module.T("panel.reset.cancel")}##CancelExp"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
