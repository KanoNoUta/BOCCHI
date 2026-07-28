using Dalamud.Bindings.ImGui;
using Ocelot.IPC;
using Ocelot.Ui;
using System.Numerics;

namespace BOCCHI.Modules.Debug.Panels;

public class VnavmeshPanel : Panel
{
    public override string GetName()
    {
        return "Vnavmesh";
    }

    public override void Render(DebugModule module)
    {
        if (module.TryGetIPCSubscriber<VNavmesh>(out var vnav) && vnav!.IsReady())
        {
            OcelotUi.Title("Vnav state:");
            ImGui.SameLine();
            ImGui.TextUnformatted(vnav.IsRunning() ? "Running" : "Pending");


            if (ImGui.Button("Test vnav thingy"))
            {
                vnav.FollowPath([new Vector3(815.2f, 72.5f, -705.15f)], false);
            }
        }
    }
}
