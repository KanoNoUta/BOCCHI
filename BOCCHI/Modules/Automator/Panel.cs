using BOCCHI.Modules.Automator.Pots;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Linq;

namespace BOCCHI.Modules.Automator;

public class Panel
{
    public void Draw(AutomatorModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        DrawPotsAndTreasureButton(module);
        ImGui.SameLine();
        DrawCompletionistButton(module);
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
            DrawHuntRows(module);
            if (module.Config.ShouldAutoRotateInstance)
            {
                DrawRow(module.T("panel.rotation.state"), module.instanceRotation.GetStateLabel(module));
                DrawRow(module.T("panel.rotation.remaining"), module.instanceRotation.GetRemainingLabel(module));
                DrawRow(module.T("panel.rotation.population"), module.instanceRotation.CurrentPopulation?.ToString() ?? "--");
            }
            ImGui.EndTable();
        }

        DrawCompletionistChecklist(module);
    }

    private static void DrawCompletionistButton(AutomatorModule module)
    {
        var active = module.RunMode == AutomatorRunMode.Completionist && module.RequestedEnabled;
        if (ImGui.Button(active
                ? $"{module.T("panel.completionist.stop")}##Completionist"
                : $"{module.T("panel.completionist.start")}##Completionist"))
        {
            module.RequestCompletionist(!active);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(module.T("panel.completionist.tooltip"));
        }
    }

    private static unsafe void DrawCompletionistChecklist(AutomatorModule module)
    {
        if (!ImGui.CollapsingHeader($"{module.T("panel.completionist.checklist")}##CompletionistChecklist"))
        {
            return;
        }

        var territory = ECommons.DalamudServices.Svc.ClientState.TerritoryType;
        var notes = FieldNotes.ForTerritory(territory);
        if (notes.Count == 0)
        {
            ImGui.TextDisabled(module.T("panel.completionist.outside_zone"));
        }

        foreach (var (note, sources) in notes)
        {
            var owned = FieldNotes.HasNote(note);
            var kinds = sources.Any(source => source.Type == Enums.EventType.CriticalEncounter) ? "CE" : "FATE";
            DrawChecklistRow(
                owned ? FontAwesomeIcon.Check : FontAwesomeIcon.Times,
                owned ? Owned : Missing,
                $"{FieldNotes.GetNoteName(note)} ({kinds})");
        }

        var state = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetState();
        if (state == null)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted(module.T("panel.completionist.jobs"));
        foreach (var job in ECommons.DalamudServices.Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.MKDSupportJob>())
        {
            var name = job.Name.ToString();
            if (job.RowId == 0 || string.IsNullOrWhiteSpace(name) || job.RowId >= (uint)state->SupportJobLevels.Length)
            {
                continue;
            }

            var level = state->SupportJobLevels[(int)job.RowId];
            if (level == 0)
            {
                DrawChecklistRow(FontAwesomeIcon.Times, Missing, name);
                continue;
            }

            var maxed = job.LevelMax > 0 && level >= job.LevelMax;
            DrawChecklistRow(maxed ? FontAwesomeIcon.Star : FontAwesomeIcon.Check, Owned, $"{name} {level}/{job.LevelMax}");
        }
    }

    private static readonly System.Numerics.Vector4 Owned = new(0.45f, 0.85f, 0.45f, 1f);

    private static readonly System.Numerics.Vector4 Missing = new(0.6f, 0.6f, 0.6f, 1f);

    private static void DrawChecklistRow(FontAwesomeIcon icon, System.Numerics.Vector4 color, string label)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(color, icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(color, label);
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

    private static void DrawPotsAndTreasureButton(AutomatorModule module)
    {
        var active = module.RunMode == AutomatorRunMode.PotsAndTreasure && module.RequestedEnabled;
        if (ImGui.Button(active
                ? $"{module.T("panel.pots_and_treasure.stop")}##PotsAndTreasure"
                : $"{module.T("panel.pots_and_treasure.start")}##PotsAndTreasure"))
        {
            module.RequestPotsAndTreasure(!active);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(module.T("panel.pots_and_treasure.tooltip"));
        }
    }

    private static void DrawHuntRows(AutomatorModule module)
    {
        if (module.RunMode == AutomatorRunMode.PotsAndTreasure)
        {
            DrawRow(module.T("panel.pots_and_treasure.mode"), module.T("panel.pots_and_treasure.label"));
        }
        else if (module.RunMode == AutomatorRunMode.Completionist)
        {
            DrawRow(module.T("panel.pots_and_treasure.mode"), module.T("panel.completionist.label"));
        }

        var hunt = module.automator.ManagedHunt;
        if (hunt.IsHunting(module))
        {
            DrawRow(module.T("panel.managed_hunt.label"), module.T("panel.managed_hunt.running"));
        }
        else if (hunt.IsPaused(module))
        {
            DrawRow(module.T("panel.managed_hunt.label"), string.Format(
                module.T("panel.managed_hunt.paused"),
                hunt.PauseReason ?? "-"));
        }
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
