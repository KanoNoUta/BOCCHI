using BOCCHI.Modules.Automator.Pots;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using System;

namespace BOCCHI.Modules.Automator;

public class Panel
{
    public void Draw(AutomatorModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        var name = module.T("panel.activity.none");
        var stateLabel = module.T("panel.activity_state.none");
        try
        {
            var activityState = module.automator.Activity?.state;
            name = module.automator.Activity?.GetName() ?? module.T("panel.activity.none");
            stateLabel = activityState is { } state
                ? module.T($"panel.activity_state.states.{state.ToTranslationKey()}")
                : module.T("panel.activity_state.none");
        }
        catch (AccessViolationException)
        {
            name = module.T("panel.activity.refreshing");
        }

        if (ImGui.BeginTable("##AutomatorStatus", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            DrawRow(module.T("panel.activity.label"), name);
            DrawRow(module.T("panel.activity_state.label"), stateLabel);
            DrawPotRows(module);
            if (module.Config.ShouldAutoRotateInstance)
            {
                DrawRow(module.T("panel.rotation.state"), module.instanceRotation.GetStateLabel(module));
                DrawRow(module.T("panel.rotation.remaining"), module.instanceRotation.GetRemainingLabel(module));
                DrawRow(module.T("panel.rotation.population"), module.instanceRotation.CurrentPopulation?.ToString() ?? "--");
            }
            ImGui.EndTable();
        }
    }

    private static void DrawPotRows(AutomatorModule module)
    {
        var cycle = module.potCycles.Get(ECommons.DalamudServices.Svc.ClientState.TerritoryType);
        if (module.automator.PotFarm is { } farm)
        {
            DrawRow(module.T("panel.pots.label"), string.Format(
                module.T("panel.pots.farming"),
                PotName(module, farm.FateId),
                farm.RemainingCandidates));
            return;
        }

        if (cycle.CurrentActivePotFateId != 0)
        {
            DrawRow(module.T("panel.pots.label"), string.Format(
                module.T("panel.pots.active"),
                PotName(module, cycle.CurrentActivePotFateId)));
            return;
        }

        if (!cycle.HasPredictedNextPot)
        {
            return;
        }

        var remaining = cycle.PredictedNextSpawnAt - DateTimeOffset.UtcNow;
        var countdown = remaining <= TimeSpan.Zero
            ? module.T("panel.pots.any_moment")
            : $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        var source = cycle.IsRemoteAnchor ? module.T("panel.pots.remote") : module.T("panel.pots.local");
        DrawRow(module.T("panel.pots.label"), string.Format(
            module.T("panel.pots.next"),
            PotName(module, cycle.PredictedNextPotFateId),
            countdown,
            source));
    }

    private static string PotName(AutomatorModule module, uint fateId)
    {
        return module.T($"panel.pots.names.{fateId}");
    }

    private static void DrawRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }
}
