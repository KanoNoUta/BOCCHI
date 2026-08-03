using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using System.Linq;

namespace BOCCHI.Modules.Carrots;

public class Panel
{
    public void Draw(CarrotsModule module)
    {
        BocchiUi.SectionHeading(module.T("panel.title"));
        var validCarrots = module.carrots.Where(carrot => carrot.IsValid()).ToList();
        if (validCarrots.Count == 0)
        {
            BocchiUi.EmptyState(module.T("panel.none"), module.T("panel.empty_detail"));
            return;
        }

        ImGui.TextDisabled(string.Format(module.T("panel.found"), validCarrots.Count));
        foreach (var carrot in validCarrots)
        {
            var position = carrot.GetPosition();
            ImGui.BulletText($"{module.T("panel.label")}  {position.X:F1}, {position.Y:F1}, {position.Z:F1}");
        }
    }
}
