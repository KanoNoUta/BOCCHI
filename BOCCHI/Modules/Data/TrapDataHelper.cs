using BOCCHI.Data;
using ECommons.DalamudServices;
using System.Collections.Generic;
using System.IO;

namespace BOCCHI.Modules.Data;

public class TrapDataHelper : DataHelper<string>
{
    protected override Dictionary<uint, string> Paths
    {
        get => new()
        {
            { ZoneData.SOUTHHORN, Path.Join(Svc.PluginInterface.ConfigDirectory.FullName, "southhorn_traps.json") },
            { ZoneData.NORTHHORN, Path.Join(Svc.PluginInterface.ConfigDirectory.FullName, "northhorn_traps.json") },
        };
    }

    public bool HasSharedData(Trap trap)
    {
        return HasSharedData(trap.Key);
    }

    public void MarkSharedData(Trap trap)
    {
        MarkSharedData(trap.Key);
    }
}
