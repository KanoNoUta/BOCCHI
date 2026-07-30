using ECommons.DalamudServices;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Data;

/// <summary>
/// Reads Occult Crescent treasure coffer nodes straight from the game's packaged
/// LGB layout files instead of the in-memory <c>LayoutWorld.ActiveLayout</c>.
/// <para>
/// North Horn is large enough that the runtime layout can stream treasures in and
/// out, so an in-memory snapshot is frequently incomplete. The packaged layout
/// always enumerates the full, position-stable set and exposes the treasure row id
/// through Lumina's typed API, removing the fragile <c>+0x30</c> pointer offset the
/// runtime reader relied on.
/// </para>
/// </summary>
public static class TreasureLayoutData
{
    public readonly record struct TreasureNode(uint Id, Vector3 Position, uint Sgb);

    // SGB models that identify a lootable bronze / silver coffer. Any other SGB
    // (decorative props, quest chests, ...) is intentionally ignored.
    private const uint BronzeSgb = 1596;

    private const uint SilverSgb = 1597;

    public static List<TreasureNode> Read(uint territoryId)
    {
        var results = new List<TreasureNode>();
        if (!ZoneData.IsOccultCrescentTerritory(territoryId))
        {
            return results;
        }

        var territoryRow = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRow(territoryId);
        var bg = territoryRow?.Bg.ExtractText();
        if (string.IsNullOrEmpty(bg))
        {
            Svc.Log.Warning($"Could not resolve the background path for territory {territoryId}; treasure layout unavailable.");
            return results;
        }

        var levelIndex = bg.IndexOf("/level/", StringComparison.Ordinal);
        if (levelIndex < 0)
        {
            Svc.Log.Warning($"Could not determine the level path for territory {territoryId} (bg='{bg}').");
            return results;
        }

        var levelPath = "bg/" + bg[..(levelIndex + 1)] + "level/";
        var treasureSheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Treasure>();

        // Below-field placeholders are clearly invalid coffers. North Horn hosts
        // its playable field far below the origin, so its floor is much lower.
        var minimumFieldHeight = territoryId == ZoneData.NORTHHORN ? -500f : -10f;

        // South Horn keeps treasure layout rows in planevent; North Horn moved
        // several layers to planmap in 7.55. Read both so the complete set is
        // captured regardless of which layer a given coffer lives in.
        foreach (var filename in new[] { "planevent.lgb", "planmap.lgb" })
        {
            var lgb = Svc.Data.GetFile<LgbFile>(levelPath + filename);
            if (lgb == null)
            {
                continue;
            }

            foreach (var layer in lgb.Layers)
            {
                foreach (var instanceObject in layer.InstanceObjects)
                {
                    if (instanceObject.AssetType != LayerEntryType.Treasure
                        || instanceObject.Object is not LayerCommon.TreasureInstanceObject treasure)
                    {
                        continue;
                    }

                    var rowId = treasure.ParentData.BaseId;
                    if (rowId == 0 || !treasureSheet.TryGetRow(rowId, out var row))
                    {
                        continue;
                    }

                    var sgb = row.SGB.RowId;
                    if (sgb != BronzeSgb && sgb != SilverSgb)
                    {
                        continue;
                    }

                    var translation = instanceObject.Transform.Translation;
                    var position = new Vector3(translation.X, translation.Y, translation.Z);
                    if (!float.IsFinite(position.X)
                        || !float.IsFinite(position.Y)
                        || !float.IsFinite(position.Z)
                        || position.Y <= minimumFieldHeight)
                    {
                        continue;
                    }

                    results.Add(new TreasureNode(rowId, position, sgb));
                }
            }
        }

        // The same logical layer may be referenced by more than one LGB. The
        // hunt/pathfinder data model uses Treasure row id as its stable node
        // key, so collapse duplicate references deterministically.
        var unique = results
            .GroupBy(node => node.Id)
            .Select(group => group.First())
            .OrderBy(node => node.Id)
            .ToList();
        if (unique.Count != results.Count)
        {
            Svc.Log.Warning(
                $"Collapsed {results.Count - unique.Count} duplicate treasure layout " +
                $"entries in territory {territoryId}.");
        }

        return unique;
    }
}
