using BOCCHI.Data;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace BOCCHI.Modules.Buff;

public class Panel
{
    public void Draw(BuffModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        var isNearKnowledgeCrystal = ZoneData.IsNearKnowledgeCrystal();
        var isQueued = module.BuffManager.IsQueued();
        var enabled = isNearKnowledgeCrystal && !isQueued;

        if (BocchiUi.IconButton(FontAwesomeIcon.Redo, "ApplyBuffs", module.T("panel.button.tooltip"), enabled))
        {
            module.BuffManager.QueueBuffs();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(
            isQueued
                ? module.T("panel.status.queued")
                : isNearKnowledgeCrystal
                    ? module.T("panel.status.available")
                    : module.T("panel.status.move_near_crystal"));
    }
}
