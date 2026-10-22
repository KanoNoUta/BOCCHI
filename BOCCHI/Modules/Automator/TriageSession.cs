using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.IPC;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

public enum TriageRaiseJob
{
    Chemist,
    WhiteMage,
}

/// <summary>
/// After a FATE or CE, raises nearby dead players with Phantom Chemist or
/// Phantom White Mage, then restores the previous job. Ported from upstream
/// BOCCHI 4.2's Triage Mode.
/// </summary>
public sealed class TriageSession
{
    public const float CastRange = 28f;

    public const float SearchRange = CastRange + 15f;

    private const ushort RaiseStatusId = 148;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private bool armed;

    private DateTime startedAt;

    private Job? raiseJob;

    private Job? previousJob;

    public bool IsArmed => armed;

    public void Arm()
    {
        armed = true;
        startedAt = DateTime.UtcNow;
        raiseJob = null;
        previousJob = null;
    }

    public void Reset()
    {
        armed = false;
        raiseJob = null;
        previousJob = null;
    }

    /// <summary>Returns true while triage owns the player.</summary>
    public bool Update(AutomatorModule module, VNavmesh vnav)
    {
        if (!armed)
        {
            return false;
        }

        // A fight ends triage; the normal post-activity handling takes over.
        if (!module.Config.EnableTriageMode
            || DateTime.UtcNow - startedAt >= Timeout
            || Svc.Condition[ConditionFlag.InCombat])
        {
            return Finish(vnav, "stopped");
        }

        if (Player.IsDead || !EzThrottler.Throttle("Automator.Triage", 250))
        {
            return true;
        }

        var corpse = FindNearestCorpse();
        if (corpse == null)
        {
            return Finish(vnav, "no one left to raise");
        }

        if (raiseJob == null)
        {
            raiseJob = SelectRaiseJob(module.Config.PreferredTriageRaiseJob);
            if (raiseJob == null)
            {
                Svc.Log.Info("[Triage] Neither Phantom Chemist nor Phantom White Mage is unlocked.");
                return Finish(vnav, "no raise job");
            }

            Svc.Log.Info($"[Triage] Raising nearby players as {raiseJob.id}.");
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            if (EzThrottler.Throttle("Automator.Triage.Dismount", 750))
            {
                Actions.TryUnmount();
            }

            return true;
        }

        if (Job.Current.id != raiseJob.id)
        {
            if (EzThrottler.Throttle("Automator.Triage.JobSwap", 2500))
            {
                previousJob ??= Job.Current;
                raiseJob.ChangeTo();
            }

            return true;
        }

        if (Svc.Condition[ConditionFlag.Casting] || Svc.Condition[ConditionFlag.Casting87])
        {
            return true;
        }

        var distance = Distance2D(Player.Position, corpse.Position);
        if (distance > CastRange)
        {
            if (!vnav.IsRunning() && !vnav.IsPathfinding())
            {
                vnav.PathfindAndMoveTo(corpse.Position, false);
            }

            return true;
        }

        vnav.Stop();
        Svc.Targets.Target = corpse;
        var raise = raiseJob.id == JobId.WhiteMage ? Actions.WhiteMage.Raise : Actions.Chemist.Revive;
        if (raise.CanCast())
        {
            Cast(raise, corpse.GameObjectId);
        }

        return true;
    }

    private static unsafe void Cast(ActionHelpers.Action action, ulong targetId)
    {
        var manager = ActionManager.Instance();
        if (manager != null)
        {
            manager->UseAction(action.Type, action.Id, targetId);
        }
    }

    private bool Finish(VNavmesh vnav, string reason)
    {
        if (raiseJob != null)
        {
            Svc.Log.Info($"[Triage] Done: {reason}.");
        }

        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        // Put the previous job back when nothing stops the swap.
        if (previousJob != null
            && Job.Current.id != previousJob.id
            && !Svc.Condition[ConditionFlag.InCombat]
            && !Player.IsDead)
        {
            previousJob.ChangeTo();
        }

        Reset();
        return false;
    }

    private static unsafe Job? SelectRaiseJob(TriageRaiseJob preference)
    {
        var state = PublicContentOccultCrescent.GetState();
        if (state == null)
        {
            return null;
        }

        var preferred = preference == TriageRaiseJob.WhiteMage ? Job.WhiteMage : Job.Chemist;
        var fallback = preference == TriageRaiseJob.WhiteMage ? Job.Chemist : Job.WhiteMage;
        if (state->SupportJobLevels[preferred.ByteId] >= 1)
        {
            return preferred;
        }

        return state->SupportJobLevels[fallback.ByteId] >= 1 ? fallback : null;
    }

    public static IPlayerCharacter? FindNearestCorpse()
    {
        var self = Svc.Objects.LocalPlayer;
        if (self == null)
        {
            return null;
        }

        IPlayerCharacter? best = null;
        var bestDistance = float.MaxValue;
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IPlayerCharacter player
                || player.EntityId == self.EntityId
                || !player.IsDead
                || !player.IsTargetable
                || player.StatusList.Any(status => status.StatusId == RaiseStatusId))
            {
                continue;
            }

            var distance = Distance2D(self.Position, player.Position);
            if (!TriagePolicy.IsInSearchRange(distance) || distance >= bestDistance)
            {
                continue;
            }

            best = player;
            bestDistance = distance;
        }

        return best;
    }

    private static float Distance2D(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }
}

public static class TriagePolicy
{
    public static bool IsInSearchRange(float distance)
    {
        return distance <= TriageSession.SearchRange;
    }

    /// <summary>Only a FATE or CE BOCCHI actually took part in arms triage.</summary>
    public static bool ShouldArm(bool enabled, bool participated, bool isWaitActivity)
    {
        return enabled && participated && !isWaitActivity;
    }
}
