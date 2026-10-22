using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Linq;
using System.Numerics;
using Action = BOCCHI.ActionHelpers.Action;

namespace BOCCHI.Modules.Treasure;

/// <summary>Pure rules for Ninja Hide, kept free of game types so they can be tested.</summary>
public static class NinjaHidePolicy
{
    public const float MountedThreatEnterBonus = 5f;

    public const float RemountClearMinYalms = 35f;

    public static readonly TimeSpan ClearDebounce = TimeSpan.FromSeconds(2.5);

    /// <summary>Mobs at this Knowledge level or above are worth hiding from.</summary>
    public static int HideAtOrAbove(int playerKnowledge, int offset)
    {
        return Math.Max(1, playerKnowledge + offset);
    }

    public static float EnterDistance(float configured, bool mounted)
    {
        return mounted ? configured + MountedThreatEnterBonus : configured;
    }

    public static float ExitDistance(float configured, float enter)
    {
        return Math.Max(configured, enter);
    }

    public static float RemountClearDistance(float exit)
    {
        return Math.Max(exit, RemountClearMinYalms);
    }

    /// <summary>
    /// Hide stays required until no threat has been inside the exit distance
    /// for <see cref="ClearDebounce"/>, so a mob drifting at the edge does not
    /// toggle Hide on and off.
    /// </summary>
    public static bool StayRequired(bool threatInsideExit, ref DateTime? clearSince, DateTime now)
    {
        if (threatInsideExit)
        {
            clearSince = null;
            return true;
        }

        clearSince ??= now;
        if (now - clearSince.Value < ClearDebounce)
        {
            return true;
        }

        clearSince = null;
        return false;
    }
}

/// <summary>Finds hostile mobs strong enough to be dangerous. Ported from upstream BOCCHI 4.2.</summary>
public static class KnowledgeThreat
{
    public const uint OccultIsleblazerBaseId = 17900;

    public const uint CrescentHauntNameId = 14901;

    public const float IsleblazerUnhideDistance = 5f;

    private const uint ContentValueCurrentKnowledge = 7;

    /// <summary>
    /// The player's real Knowledge level. ForayInfo follows the South Horn
    /// level sync, so it is only a fallback when the content value is missing.
    /// </summary>
    public static unsafe int? GetPlayerKnowledgeLevel()
    {
        var state = PlayerState.Instance();
        if (state != null)
        {
            var current = state->GetContentValue(ContentValueCurrentKnowledge);
            if (current > 0)
            {
                return (int)current;
            }
        }

        var player = Svc.Objects.LocalPlayer;
        if (player == null || player.Address == nint.Zero)
        {
            return null;
        }

        var level = ((BattleChara*)player.Address)->ForayInfo.Level;
        return level > 0 ? level : null;
    }

    public static unsafe bool TryFindThreat(Vector3 origin, int hideAtOrAbove, float radius)
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc battle
                || battle.IsDead
                || !battle.IsTargetable
                || !battle.IsHostile()
                || battle.Address == nint.Zero)
            {
                continue;
            }

            // Isleblazers are handled separately; Haunts see through Hide.
            if (battle.BaseId == OccultIsleblazerBaseId || battle.NameId == CrescentHauntNameId)
            {
                continue;
            }

            if (((BattleChara*)battle.Address)->ForayInfo.Level < hideAtOrAbove)
            {
                continue;
            }

            if (Distance2D(origin, battle.Position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsNearIsleblazer(Vector3 origin)
    {
        return Svc.Objects.OfType<IBattleNpc>().Any(battle =>
            battle.BaseId == OccultIsleblazerBaseId
            && !battle.IsDead
            && battle.IsTargetable
            && Distance2D(origin, battle.Position) <= IsleblazerUnhideDistance);
    }

    public static float Distance2D(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }
}

/// <summary>Decides when a route needs Hide, with hysteresis between entering and leaving danger.</summary>
public sealed class NinjaHideRouteGate
{
    private DateTime? clearSince;

    public bool UpdateRequired(
        Vector3 position,
        bool currentlyRequired,
        bool mounted,
        TreasureConfig config,
        float enterFloor = 0f)
    {
        if (KnowledgeThreat.IsNearIsleblazer(position)
            || KnowledgeThreat.GetPlayerKnowledgeLevel() is not { } knowledge)
        {
            Reset();
            return false;
        }

        var hideAt = NinjaHidePolicy.HideAtOrAbove(knowledge, config.KnowledgeHideOffset);
        var enter = NinjaHidePolicy.EnterDistance(Math.Max(config.KnowledgeThreatEnterDistance, enterFloor), mounted);
        if (currentlyRequired)
        {
            var exit = NinjaHidePolicy.ExitDistance(config.KnowledgeThreatExitDistance, enter);
            return NinjaHidePolicy.StayRequired(
                KnowledgeThreat.TryFindThreat(position, hideAt, exit),
                ref clearSince,
                DateTime.UtcNow);
        }

        Reset();
        return KnowledgeThreat.TryFindThreat(position, hideAt, enter);
    }

    /// <summary>Mounting cancels Hide, so remount only well clear of threats.</summary>
    public bool ShouldKeepStealth(Vector3 position, TreasureConfig config)
    {
        if (KnowledgeThreat.IsNearIsleblazer(position)
            || KnowledgeThreat.GetPlayerKnowledgeLevel() is not { } knowledge)
        {
            return false;
        }

        var hideAt = NinjaHidePolicy.HideAtOrAbove(knowledge, config.KnowledgeHideOffset);
        return KnowledgeThreat.TryFindThreat(
            position,
            hideAt,
            NinjaHidePolicy.RemountClearDistance(config.KnowledgeThreatExitDistance));
    }

    public void Reset()
    {
        clearSince = null;
    }
}

/// <summary>
/// Ninja Hide for one kind of travel (treasure hunt, pot coffer farm): call
/// <see cref="Prepare"/> every tick before moving and respect
/// <see cref="CanMount"/>.
/// </summary>
public sealed class NinjaHideTravel(string name, float enterFloor = 0f)
{
    private readonly NinjaHideAssist assist = new();

    private readonly NinjaHideRouteGate gate = new();

    private bool required;

    /// <summary>True while the last <see cref="Prepare"/> held the player still.</summary>
    public bool IsHolding { get; private set; }

    /// <summary>Returns false while the player must stand still to swap gear or cast Hide.</summary>
    public bool Prepare(TreasureConfig config, Ocelot.IPC.VNavmesh vnav)
    {
        IsHolding = !PrepareCore(config, vnav);
        return !IsHolding;
    }

    private bool PrepareCore(TreasureConfig config, Ocelot.IPC.VNavmesh vnav)
    {
        if (!config.UseNinjaHideOnDangerousRoutes)
        {
            Reset();
            return true;
        }

        required = gate.UpdateRequired(Player.Position, required, NinjaHideAssist.IsMounted, config, enterFloor);
        if (!required)
        {
            // Out of danger: drop Hide and the borrowed gearset so the player
            // can mount again.
            if (assist.TryEndStealthForTravel(() => gate.ShouldKeepStealth(Player.Position, config)))
            {
                assist.RestoreAll();
            }

            return true;
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return true;
        }

        if (assist.EnsureReady(config.NinjaGearsetNumber))
        {
            if (config.UseOccultSprintWhileHidden)
            {
                assist.TryOccultSprintWhileHidden();
            }

            return true;
        }

        if (config.NinjaGearsetNumber <= 0 && !NinjaHideAssist.IsNinja)
        {
            if (EzThrottler.Throttle($"NinjaHide.NoGearset.{name}", 10000))
            {
                Svc.Log.Warning($"[NinjaHide] {name}: Hide is on but no Ninja gearset is set and you are not a Ninja; travelling unhidden.");
            }

            Reset();
            return true;
        }

        if (vnav.IsRunning() || vnav.IsPathfinding())
        {
            vnav.Stop();
        }

        return false;
    }

    /// <summary>Mounting cancels Hide and widens the danger radius; stay on foot until well clear.</summary>
    public bool CanMount(TreasureConfig config)
    {
        return !config.UseNinjaHideOnDangerousRoutes
               || (!required && !NinjaHideAssist.IsStealthed && !gate.ShouldKeepStealth(Player.Position, config));
    }

    public void BeforeInteract(TreasureConfig config)
    {
        if (config.UseNinjaHideOnDangerousRoutes)
        {
            assist.RestoreSupportJob();
        }
    }

    /// <summary>Ends the travel: forget the danger and put the previous gear back.</summary>
    public void Finish()
    {
        Reset();
        assist.RestoreAll();
    }

    private void Reset()
    {
        IsHolding = false;
        required = false;
        gate.Reset();
    }
}

/// <summary>
/// Swaps to a Ninja gearset, casts Hide and optionally Occult Sprint while
/// hidden, then puts the previous gearset and phantom job back. Ported from
/// upstream BOCCHI 4.2's NinjaHideAssist.
/// </summary>
public sealed class NinjaHideAssist
{
    public const uint HiddenStatusId = 614;

    public const uint NinjaClassJobId = 30;

    private static readonly Action Hide = new(ActionType.Action, 2245);

    private int? gearsetBeforeNinja;

    private Job? supportJobBeforeThief;

    public static bool IsStealthed =>
        Svc.Objects.LocalPlayer?.StatusList.Any(status => status.StatusId == HiddenStatusId) == true;

    public static bool IsNinja => Svc.Objects.LocalPlayer?.ClassJob.RowId == NinjaClassJobId;

    public static bool IsMounted => Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.Mounting];

    /// <summary>Returns true once the player is a hidden Ninja.</summary>
    public bool EnsureReady(int ninjaGearsetNumber)
    {
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return false;
        }

        if (!IsNinja)
        {
            if (ninjaGearsetNumber > 0)
            {
                RememberCurrentGearset();
                TryEquipGearset(ninjaGearsetNumber, requireNinja: true);
            }

            return false;
        }

        if (Player.IsJumping)
        {
            return false;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            if (EzThrottler.Throttle("NinjaHide.Dismount", 750))
            {
                Actions.TryUnmount();
            }

            return false;
        }

        if (IsStealthed)
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.Mounting]
            && EzThrottler.Throttle("NinjaHide.Cast", 750)
            && Hide.CanCast())
        {
            Hide.Cast();
        }

        return false;
    }

    public void TryOccultSprintWhileHidden()
    {
        if (!IsStealthed
            || Svc.Condition[ConditionFlag.InCombat]
            || Svc.Condition[ConditionFlag.Casting]
            || Svc.Condition[ConditionFlag.Casting87]
            || Svc.Condition[ConditionFlag.BetweenAreas]
            || Player.IsJumping)
        {
            return;
        }

        if (Job.Current.id != Job.Thief.id)
        {
            if (EzThrottler.Throttle("NinjaHide.Thief", 750))
            {
                supportJobBeforeThief ??= Job.Current;
                Job.Thief.ChangeTo();
            }

            return;
        }

        if (EzThrottler.Throttle("NinjaHide.OccultSprint", 750) && Actions.Thief.OccultSprint.CanCast())
        {
            Actions.Thief.OccultSprint.Cast();
        }
    }

    /// <summary>Drops Hide (by recasting it) once nothing dangerous is near, so the player can remount.</summary>
    public bool TryEndStealthForTravel(Func<bool> stillThreatened)
    {
        if (!IsStealthed)
        {
            return true;
        }

        if (stillThreatened()
            || Svc.Condition[ConditionFlag.InCombat]
            || Player.IsJumping
            || IsMounted)
        {
            return false;
        }

        if (!IsNinja)
        {
            return true;
        }

        if (EzThrottler.Throttle("NinjaHide.EndHide", 750) && Hide.CanCast())
        {
            Hide.Cast();
        }

        return !IsStealthed;
    }

    public void RestoreSupportJob()
    {
        if (supportJobBeforeThief is not { } previous)
        {
            return;
        }

        if (Job.Current.id == previous.id)
        {
            supportJobBeforeThief = null;
            return;
        }

        if (!Svc.Condition[ConditionFlag.InCombat] && EzThrottler.Throttle("NinjaHide.RestoreSupportJob", 750))
        {
            previous.ChangeTo();
        }
    }

    public void RestoreAll()
    {
        RestoreSupportJob();
        if (gearsetBeforeNinja is not { } previous)
        {
            return;
        }

        if (GetActiveGearsetNumber() == previous)
        {
            gearsetBeforeNinja = null;
            return;
        }

        if (!Svc.Condition[ConditionFlag.InCombat])
        {
            TryEquipGearset(previous, requireNinja: false);
        }
    }

    private void RememberCurrentGearset()
    {
        if (gearsetBeforeNinja == null && GetActiveGearsetNumber() is { } current)
        {
            gearsetBeforeNinja = current;
        }
    }

    private static unsafe int? GetActiveGearsetNumber()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return null;
        }

        int slot = module->CurrentGearsetIndex;
        return slot >= 0 ? slot + 1 : null;
    }

    private static unsafe void TryEquipGearset(int gearsetNumber, bool requireNinja)
    {
        if (gearsetNumber <= 0 || !EzThrottler.Throttle("NinjaHide.Gearset", 1500))
        {
            return;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return;
        }

        // Gearset numbers are 1-based in the UI and 0-based in the module.
        var slot = gearsetNumber - 1;
        if (!module->IsValidGearset(slot))
        {
            Svc.Log.Warning($"[NinjaHide] Gearset {gearsetNumber} is empty.");
            return;
        }

        var entry = module->GetGearset(slot);
        if (entry == null || entry->ClassJob == 0)
        {
            return;
        }

        if (requireNinja && entry->ClassJob != NinjaClassJobId)
        {
            Svc.Log.Warning($"[NinjaHide] Gearset {gearsetNumber} is not a Ninja gearset.");
            return;
        }

        module->EquipGearset(slot);
    }
}
