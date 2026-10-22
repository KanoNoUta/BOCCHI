using BOCCHI.Data;
using BOCCHI.Enums;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using MonsterNote = BOCCHI.Enums.MonsterNote;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Automator;

/// <summary>
/// Which field notes (monster notes) the player already owns, for
/// Completionist. Ported from upstream BOCCHI 4.2's FieldNoteTracker.
/// </summary>
public static class FieldNotes
{
    public static bool HasNote(MonsterNote note)
    {
        var itemId = (uint)note;
        if (GetInventoryCount(itemId) > 0)
        {
            return true;
        }

        if (!Svc.Data.GetExcelSheet<Item>().TryGetRow(itemId, out var item))
        {
            return false;
        }

        try
        {
            return Svc.UnlockState.IsItemUnlocked(item);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool ShouldPursue(EventData data)
    {
        return CompletionistPolicy.ShouldPursue(data.Note, HasNote);
    }

    /// <summary>Every note this zone's FATEs and CEs drop, with the events that drop it.</summary>
    public static IReadOnlyList<(MonsterNote Note, IReadOnlyList<EventData> Sources)> ForTerritory(uint territoryId)
    {
        return EventData.GetFatesForTerritory(territoryId)
            .Concat(EventData.GetCriticalEncountersForTerritory(territoryId))
            .Where(data => data.Note != null)
            .GroupBy(data => data.Note!.Value)
            .Select(group => (group.Key, (IReadOnlyList<EventData>)group.ToList()))
            .ToList();
    }

    public static string GetNoteName(MonsterNote note)
    {
        return Svc.Data.GetExcelSheet<Item>().TryGetRow((uint)note, out var item)
               && item.Name.ToString() is { Length: > 0 } name
            ? name
            : note.ToString();
    }

    private static unsafe int GetInventoryCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetInventoryItemCount(itemId);
    }
}

public static class CompletionistPolicy
{
    /// <summary>Only events that drop a note the player does not have yet.</summary>
    public static bool ShouldPursue(MonsterNote? note, Func<MonsterNote, bool> hasNote)
    {
        return note is { } value && !hasNote(value);
    }
}
