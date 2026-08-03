using BOCCHI.Data;
using BOCCHI.Modules.Teleporter;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;
using System;
using System.Linq;

namespace BOCCHI.Modules.Fates;

public class Panel
{
    public void Draw(FatesModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        if (!ZoneData.IsInOccultCrescent())
        {
            module.fates.Clear();
            BocchiUi.EmptyState(module.T("panel.none"), module.T("panel.outside_detail"));
            return;
        }

        if (module.fates.Count <= 0)
        {
            BocchiUi.EmptyState(module.T("panel.none"), module.T("panel.waiting_detail"));
            return;
        }

        foreach (var fate in module.fates.Values.ToArray())
        {
            try
            {
                ImGui.TextUnformatted(fate.Name);
                ImGui.SameLine();
                ImGui.TextDisabled($"{fate.CurrentProgress}%");

                var estimate = fate.Progress.EstimateTimeToCompletion();
                if (estimate != null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"{module.T("panel.estimated")} {estimate.Value:mm\\:ss}");
                }

                if (module.TryGetModule<TeleporterModule>(out var teleporter) && teleporter!.IsReady())
                {
                    teleporter.teleporter.Button(
                        fate.Data.Aethernet,
                        fate.StartPosition,
                        fate.Name,
                        $"fate_{fate.Id}",
                        fate.Data);
                }

                OcelotUi.Indent(() => EventIconRenderer.Drops(fate.Data, module.PluginConfig.EventDropConfig));
                ImGui.Separator();
            }
            catch (AccessViolationException)
            {
                // Event objects can disappear while Dalamud refreshes the table.
            }
        }
    }
}
