using BOCCHI.ActionHelpers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using Ocelot.Chain;
using Ocelot.IPC;
using System;
using System.Numerics;

namespace BOCCHI.Chains;

public enum AutoRepairMethod
{
    SelfRepair,
    MenderNpc,
    PreferMender,
}

/// <summary>
/// Repairs gear at camp, by self-repair or at a mender NPC. Ported from
/// upstream BOCCHI 4.2's RepairService, RepairStep and NpcRepairStep.
/// </summary>
public static class RepairChain
{
    public const uint RepairEventId = 720915;

    public const float MenderSearchRadius = 50f;

    public const float MenderInteractRadius = 3.5f;

    private const uint RepairGeneralActionId = 6;

    public static unsafe int? GetLowestConditionPercent()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return null;
        }

        var equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null || !equipped->IsLoaded)
        {
            return null;
        }

        var items = Svc.Data.GetExcelSheet<Item>();
        int? lowest = null;
        for (var i = 0; i < equipped->Size; i++)
        {
            var item = equipped->GetInventorySlot(i);
            if (item == null || item->ItemId == 0)
            {
                continue;
            }

            // Soul crystals and other gear without durability never need repair.
            if (!items.TryGetRow(item->ItemId, out var row) || row.ItemRepair.RowId == 0)
            {
                continue;
            }

            var percent = RepairPolicy.ConditionPercent(item->Condition);
            lowest = lowest == null ? percent : Math.Min(lowest.Value, percent);
        }

        return lowest;
    }

    public static bool ShouldRepair(int threshold)
    {
        return GetLowestConditionPercent() is { } lowest && RepairPolicy.NeedsRepair(lowest, threshold);
    }

    public static Chain Create(AutoRepairMethod method, int threshold, VNavmesh vnav)
    {
        var useMender = method != AutoRepairMethod.SelfRepair && TryFindMender(out _, out _);
        if (method == AutoRepairMethod.MenderNpc && !useMender)
        {
            Svc.Log.Warning("[Repair] No mender NPC nearby; falling back to self-repair.");
        }

        Svc.Log.Info($"[Repair] Repairing gear ({(useMender ? "mender NPC" : "self-repair")}).");
        long lastAction = 0;
        var clickedRepairAll = false;
        return Chain.Create("Repair")
            .Then(new TaskManagerTask(() =>
            {
                if (!Svc.Condition[ConditionFlag.Mounted])
                {
                    return true;
                }

                Actions.TryUnmount();
                return false;
            }, new TaskManagerConfiguration { TimeLimitMS = 8000, ShowError = false }))
            .Then(new TaskManagerTask(() =>
            {
                var now = Environment.TickCount64;
                if (now - lastAction < 600)
                {
                    return false;
                }

                var done = Tick(useMender, threshold, vnav, ref clickedRepairAll);
                lastAction = now;
                return done;
            }, new TaskManagerConfiguration { TimeLimitMS = 60000, ShowError = false }));
    }

    private static unsafe bool Tick(bool useMender, int threshold, VNavmesh vnav, ref bool clickedRepairAll)
    {
        if (Svc.Condition[ConditionFlag.Occupied39])
        {
            return false;
        }

        var repair = (AddonRepair*)Svc.GameGui.GetAddonByName("Repair", 1).Address;
        var repairOpen = repair != null && repair->AtkUnitBase.IsVisible;
        if (repairOpen)
        {
            vnav.Stop();
            var yesno = (AddonSelectYesno*)Svc.GameGui.GetAddonByName("SelectYesno", 1).Address;
            if (yesno != null && yesno->AtkUnitBase.IsVisible)
            {
                new AddonMaster.SelectYesno((nint)yesno).Yes();
                return false;
            }

            if (!repair->RepairAllButton->IsEnabled || (clickedRepairAll && !ShouldRepair(threshold)))
            {
                if (ShouldRepair(threshold) && !clickedRepairAll)
                {
                    // Nothing to repair with (no dark matter), or the window is not ready.
                    return false;
                }

                repair->AtkUnitBase.Close(true);
                Svc.Log.Info("[Repair] Done.");
                return true;
            }

            new AddonMaster.Repair((nint)repair).RepairAll();
            clickedRepairAll = true;
            return false;
        }

        if (clickedRepairAll && !ShouldRepair(threshold))
        {
            return true;
        }

        if (!useMender)
        {
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, RepairGeneralActionId);
            return false;
        }

        var select = Svc.GameGui.GetAddonByName("SelectIconString", 1).Address;
        if (select != nint.Zero)
        {
            vnav.Stop();
            if (TryFindMender(out _, out var menuIndex) && menuIndex >= 0)
            {
                new AddonMaster.SelectIconString(select).Entries[menuIndex].Select();
            }

            return false;
        }

        if (!TryFindMender(out var npc, out _))
        {
            Svc.Log.Warning("[Repair] Mender NPC lost; giving up.");
            return true;
        }

        var player = Player.Position;
        var distance = Vector2.Distance(new Vector2(npc.Position.X, npc.Position.Z), new Vector2(player.X, player.Z));
        if (distance > MenderInteractRadius)
        {
            if (!vnav.IsRunning())
            {
                vnav.PathfindAndMoveTo(npc.Position, false);
            }

            return false;
        }

        vnav.Stop();
        TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address, false);
        return false;
    }

    public static bool TryFindMender(out IGameObject npc, out int repairMenuIndex)
    {
        npc = null!;
        repairMenuIndex = -1;
        var best = float.MaxValue;
        var player = Player.Position;
        var sheet = Svc.Data.GetExcelSheet<ENpcBase>();
        foreach (var obj in Svc.Objects)
        {
            if (obj is not { ObjectKind: ObjectKind.EventNpc, IsTargetable: true })
            {
                continue;
            }

            var distance = Vector2.Distance(new Vector2(obj.Position.X, obj.Position.Z), new Vector2(player.X, player.Z));
            if (distance > MenderSearchRadius || distance >= best || !sheet.TryGetRow(obj.BaseId, out var row))
            {
                continue;
            }

            for (var i = 0; i < row.ENpcData.Count; i++)
            {
                if (row.ENpcData[i].RowId != RepairEventId)
                {
                    continue;
                }

                best = distance;
                npc = obj;
                repairMenuIndex = i;
                break;
            }
        }

        return npc != null;
    }
}

public static class RepairPolicy
{
    /// <summary>Gear condition runs 0-30000; the game shows it as 0-100%.</summary>
    public static int ConditionPercent(ushort condition)
    {
        return (int)Math.Round(condition / 30000.0 * 100.0);
    }

    /// <summary>0 turns auto repair off.</summary>
    public static bool NeedsRepair(int lowestPercent, int threshold)
    {
        return threshold > 0 && lowestPercent <= threshold;
    }
}
