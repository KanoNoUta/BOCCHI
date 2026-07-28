using BOCCHI.Data;
using BOCCHI.Modules.Teleporter;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Debug.Panels;

public class FatesPanel : Panel
{
    public Dictionary<uint, Vector3> FateLocations = [];

    public FatesPanel()
    {
        ProcessLgbData(Svc.ClientState.TerritoryType);
    }

    public void ProcessLgbData(uint id)
    {
        if (!ZoneData.IsOccultCrescentTerritory(id))
        {
            FateLocations.Clear();
            return;
        }

        FateLocations.Clear();

        var territorySheet = Svc.Data.GetExcelSheet<TerritoryType>();
        var territoryRow = territorySheet?.GetRow(id);
        if (territoryRow == null)
        {
            Svc.Log.Error($"Could not load TerritoryType for ID {id}");
            return;
        }

        Dictionary<uint, uint> locations = [];
        foreach (var fate in EventData.GetFatesForTerritory(id))
        {
            var fateRow = Svc.Data.GetExcelSheet<Fate>().FirstOrDefault(f => f.RowId == fate.Id);
            locations[fate.Id] = fateRow.Location;
        }

        var bg = territoryRow?.Bg.ExtractText();
        var levelIndex = bg?.IndexOf("/level/", StringComparison.Ordinal) ?? -1;
        if (levelIndex < 0)
        {
            Svc.Log.Error($"Could not determine level path for territory {id}");
            return;
        }

        var levelPath = "bg/" + bg![..(levelIndex + 1)] + "level/";
        var locationToFate = locations
            .Where(kv => kv.Value != 0)
            .GroupBy(kv => kv.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);

        // South Horn stores FATE layout rows in planevent; North Horn moved
        // them to planmap in 7.55.  Read both so future zone layouts remain
        // backwards compatible.
        foreach (var filename in new[] { "planevent.lgb", "planmap.lgb" })
        {
            var lgb = Svc.Data.GetFile<LgbFile>(levelPath + filename);
            foreach (var layer in lgb?.Layers ?? [])
            {
                foreach (var instanceObject in layer.InstanceObjects)
                {
                    if (!locationToFate.TryGetValue(instanceObject.InstanceId, out var fateId))
                    {
                        continue;
                    }

                    var transform = instanceObject.Transform;
                    var pos = transform.Translation;
                    FateLocations[fateId] = new Vector3(pos.X, pos.Y, pos.Z);
                }
            }
        }
    }

    public override string GetName()
    {
        return "Fates";
    }

    public override void Render(DebugModule module)
    {
        OcelotUi.Title("Fates:");
        OcelotUi.Indent(() =>
        {
            var fates = EventData.GetFatesForTerritory(Svc.ClientState.TerritoryType).ToList();
            foreach (var data in fates)
            {
                ImGui.TextUnformatted(data.InternalName);

                var start = FateLocations.TryGetValue(data.Id, out var layoutPosition)
                    ? layoutPosition
                    : data.StartPosition;
                if (start != null
                    && module.TryGetModule<TeleporterModule>(out var teleporter)
                    && teleporter!.IsReady())
                {
                    teleporter.teleporter.Button(data.Aethernet, start.Value, data.InternalName, $"fate_{data.Id}", data);
                }

                OcelotUi.Indent(() => EventIconRenderer.Drops(data, module.PluginConfig.EventDropConfig));

                if (!data.Equals(fates.Last()))
                {
                    OcelotUi.VSpace();
                }
            }
        });
    }

    public override void OnTerritoryChanged(uint id, DebugModule module)
    {
        ProcessLgbData(id);
    }
}
