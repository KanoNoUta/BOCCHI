using Dalamud.Bindings.ImGui;
using Ocelot.Ui;
using System;

namespace BOCCHI.Modules.Automator;

public class Panel
{
    public void Draw(AutomatorModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            OcelotUi.Title($"{module.T("panel.activity.label")}:");
            try
            {
                var name = module.automator.Activity?.GetName() ?? module.T("panel.activity.none");
                ImGui.SameLine();
                ImGui.TextUnformatted(name);
            }
            catch (AccessViolationException)
            {
                return;
            }

            OcelotUi.Title($"{module.T("panel.activity_state.label")}:");
            ImGui.SameLine();
            ImGui.TextUnformatted(module.automator.Activity?.state.ToLabel() ?? module.T("panel.activity_state.none"));

            if (!module.Config.ShouldAutoRotateInstance)
            {
                return;
            }

            OcelotUi.Title($"{module.T("panel.rotation.state")}:");
            ImGui.SameLine();
            ImGui.TextUnformatted(module.instanceRotation.GetStateLabel(module));

            OcelotUi.Title($"{module.T("panel.rotation.remaining")}:");
            ImGui.SameLine();
            ImGui.TextUnformatted(module.instanceRotation.GetRemainingLabel(module));

            OcelotUi.Title($"{module.T("panel.rotation.population")}:");
            ImGui.SameLine();
            ImGui.TextUnformatted(module.instanceRotation.CurrentPopulation?.ToString() ?? "--");
        });
    }
}
