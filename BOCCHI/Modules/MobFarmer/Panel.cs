using BOCCHI.Ui;
using BOCCHI.Modules.MobFarmer.States;
using Dalamud.Bindings.ImGui;
using Ocelot;
using System.Linq;
using System.Numerics;
using BOCCHI.Ui.Lumin;

namespace BOCCHI.Modules.MobFarmer;

public class Panel
{
    public void Draw(MobFarmerModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        var running = module.Farmer.Running;
        ImGui.PushStyleColor(
            ImGuiCol.Button,
            running ? new Vector4(0.58f, 0.20f, 0.18f, 0.92f) : new Vector4(0.16f, 0.48f, 0.30f, 0.92f));
        if (ImGui.Button(
                running ? $"{I18N.T("generic.label.stop")}##MobFarmer" : $"{I18N.T("generic.label.start")}##MobFarmer",
                new Vector2(120f, 0f)))
        {
            module.Farmer.Toggle();
        }
        ImGui.PopStyleColor();

        ImGui.Spacing();
        var availableWidth = LuminTheme.ToDesign(ImGui.GetContentRegionAvail().X);
        var columns = availableWidth >= 720f ? 3 : BocchiUiPolicy.GetWorkspaceColumns(availableWidth);
        if (ImGui.BeginTable(
                "##MobFarmerMetrics",
                columns,
                ImGuiTableFlags.SizingStretchSame | (columns > 1 ? ImGuiTableFlags.BordersInnerV : ImGuiTableFlags.None)))
        {
            ImGui.TableNextColumn();
            BocchiUi.Metric(
                module.T("panel.state"),
                running ? GetStateLabel(module) : module.T("panel.stopped"));
            ImGui.TableNextColumn();
            BocchiUi.Metric(module.T("panel.not_in_combat"), module.Scanner.NotInCombat.Count().ToString());
            ImGui.TableNextColumn();
            BocchiUi.Metric(module.T("panel.in_combat"), module.Scanner.InCombat.Count().ToString());
            ImGui.EndTable();
        }
    }

    private static string GetStateLabel(MobFarmerModule module)
    {
        return module.Farmer.StateMachine.State switch
        {
            FarmerPhase.Waiting => module.T("panel.states.waiting"),
            FarmerPhase.Buffing => module.T("panel.states.buffing"),
            FarmerPhase.Gathering => module.T("panel.states.gathering"),
            FarmerPhase.Stacking => module.T("panel.states.stacking"),
            FarmerPhase.Fighting => module.T("panel.states.fighting"),
            FarmerPhase.TreasureFinding => module.T("panel.states.treasure_finding"),
            _ => module.T("panel.stopped"),
        };
    }
}
