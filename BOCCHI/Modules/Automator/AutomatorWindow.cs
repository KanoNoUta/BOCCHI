using BOCCHI.Ui;
using BOCCHI.Ui.Lumin;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.Treasure;
using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

[OcelotWindow]
public class AutomatorWindow(Plugin _plugin, Config _config) : OcelotWindow(_plugin, _config)
{
    private LuminUiStyleScope? luminStyleScope;

    public override void PreDraw()
    {
        luminStyleScope?.Dispose();
        luminStyleScope = LuminTheme.PushGlobalStyle();
        base.PreDraw();
    }

    public override void PostDraw()
    {
        base.PostDraw();
        luminStyleScope?.Dispose();
        luminStyleScope = null;
    }
    protected override void Render(RenderContext context)
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        BocchiUi.PageHeading(T("automator_window.title"), T("automator_window.subtitle"));

        var state = automator.RunState switch
        {
            AutomatorRunState.Starting => BocchiOperationState.Starting,
            AutomatorRunState.Running => BocchiOperationState.Running,
            AutomatorRunState.Stopping => BocchiOperationState.Stopping,
            _ when !string.IsNullOrWhiteSpace(automator.RunStateDetail) => BocchiOperationState.Failed,
            _ => BocchiOperationState.Stopped,
        };
        BocchiUi.StatusDot(state);
        ImGui.SameLine();
        ImGui.TextUnformatted(state switch
        {
            BocchiOperationState.Starting => T("state.starting"),
            BocchiOperationState.Running => T("state.running"),
            BocchiOperationState.Stopping => T("state.stopping"),
            BocchiOperationState.Failed => T("state.failed"),
            _ => T("state.stopped"),
        });

        ImGui.SameLine(0f, 12f);
        ImGui.BeginDisabled(automator.RunState == AutomatorRunState.Stopping);
        ImGui.PushStyleColor(
            ImGuiCol.Button,
            automator.RequestedEnabled
                ? new Vector4(0.52f, 0.25f, 0.20f, 0.92f)
                : new Vector4(0.16f, 0.48f, 0.30f, 0.92f));
        if (ImGui.Button(
                $"{(automator.RequestedEnabled ? T("buttons.stop_automatic") : T("buttons.start_automatic"))}##AutomatorWindow",
                new Vector2(132f, 0f)))
        {
            automator.RequestEnabled(!automator.RequestedEnabled);
        }
        ImGui.PopStyleColor();
        ImGui.EndDisabled();

        var hasIndependentWork = Plugin.Modules.GetModule<TreasureModule>().IsHuntRunning
                                 || Plugin.Modules.GetModule<CarrotsModule>().IsHuntRunning
                                 || Plugin.Modules.GetModule<MobFarmerModule>().Farmer.Running;
        ImGui.SameLine();
        ImGui.BeginDisabled((state is BocchiOperationState.Stopped or BocchiOperationState.Failed)
                            && !hasIndependentWork);
        if (ImGui.Button($"{T("buttons.stop_all")}##AutomatorWindowStopAll", new Vector2(96f, 0f)))
        {
            automator.RequestStopAll();
        }
        ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(automator.RunStateDetail))
        {
            ImGui.TextColored(BocchiUi.GetStatusColor(state), automator.RunStateDetail);
        }

        if (!BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            BocchiUi.EmptyState(T("pages.overview.outside_title"), T("automator_window.outside_detail"));
            return;
        }

        automator.panel.Draw(automator);
    }

    protected override string GetWindowName()
    {
        return Plugin.Modules.GetModule<AutomatorModule>().T("panel.lens.title");
    }

    private static string T(string key) => I18N.T($"windows.main.{key}");
}
