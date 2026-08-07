using BOCCHI.Data;
using BOCCHI.Ui.Lumin;
using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Windows;

namespace BOCCHI.Modules.Debug;

#if DEBUG_BUILD
[OcelotWindow]
#endif
public class DebugWindow(Plugin priamryPlugin, Config config) : OcelotWindow(priamryPlugin, config)
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
        if (!ZoneData.IsInOccultCrescent())
        {
            ImGui.TextUnformatted(I18N.T("generic.label.not_in_zone"));
            return;
        }

        if (Plugin.Modules.TryGetModule<DebugModule>(out var module) && module != null)
        {
            module.DrawPanels();
        }
    }

    protected override string GetWindowName()
    {
        return "OCH Debug";
    }
}
