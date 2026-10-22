using BOCCHI.Data;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.IPC;
using System;
using System.Numerics;

namespace BOCCHI.Modules.Automator.Pots;

/// <summary>
/// Travels to a ring around the next magic pot before it spawns and waits
/// there. When the pot appears the activity ends and the automator picks the
/// live pot FATE up like any other FATE.
/// </summary>
public sealed class PotWaitActivity : Activity
{
    public const float PrepositionMinRadius = 12f;

    public const float PrepositionMaxRadius = 32f;

    private readonly PotZoneData.PotFate pot;

    private readonly Func<bool> stillWanted;

    private readonly Vector3 standPosition;

    public PotWaitActivity(
        EventData data,
        PotZoneData.PotFate pot,
        Lifestream lifestream,
        VNavmesh vnav,
        AutomatorModule module,
        Func<bool> stillWanted)
        : base(data, lifestream, vnav, module)
    {
        this.pot = pot;
        this.stillWanted = stillWanted;
        // The base constructor treats a player already inside a FATE as
        // participating; a pot wait never fights.
        state = ActivityState.Idle;
        var player = Svc.Objects.LocalPlayer?.Position ?? pot.Center;
        standPosition = GetStandPosition(pot.Center, player, Random.Shared.NextSingle(), Random.Shared.NextSingle());
        handlers[ActivityState.WaitingForPot] = GetWaitingChain;
    }

    public uint PotFateId => pot.Id;

    /// <summary>
    /// A random point 12-32 yalms from the pot, or the player's own position
    /// when they already stand in that ring.
    /// </summary>
    public static Vector3 GetStandPosition(Vector3 potCenter, Vector3 from, float rangeRoll, float angleRoll)
    {
        var distance = PotTreasureFilter.Distance2D(from, potCenter);
        if (distance is >= PrepositionMinRadius and <= PrepositionMaxRadius)
        {
            return from;
        }

        var range = PrepositionMinRadius + rangeRoll * (PrepositionMaxRadius - PrepositionMinRadius);
        var angle = angleRoll * MathF.PI * 2f;
        return potCenter + new Vector3(MathF.Cos(angle) * range, 0f, MathF.Sin(angle) * range);
    }

    public override bool IsValid()
    {
        return stillWanted();
    }

    public override string GetName()
    {
        return string.Format(module.T("panel.pots.waiting_for"), module.T($"panel.pots.names.{pot.Id}"));
    }

    protected override Vector3 GetPosition()
    {
        var floor = vnav.FindPointOnFloor(standPosition with { Y = pot.Center.Y + 10f }, false, 8f);
        return floor ?? standPosition;
    }

    protected override float GetRadius()
    {
        return PrepositionMaxRadius;
    }

    protected override ActivityState GetPostPathfindingState()
    {
        return ActivityState.WaitingForPot;
    }

    protected override bool IsActivityTarget(IBattleNpc obj)
    {
        return false;
    }

    protected override TaskManagerTask GetPathfindingWatcher(StateManagerModule states)
    {
        long? startedAt = null;
        var observedNavigation = false;
        return new TaskManagerTask(() =>
        {
            if (!module.IsEnabled || !IsValid())
            {
                vnav.Stop();
                return true;
            }

            if (PotTreasureFilter.Distance2D(Player.Position, pot.Center) <= PrepositionMaxRadius)
            {
                StopAtArrivalAndTryDismount("Pots.WaitArrivalDismount");
                return true;
            }

            var now = Environment.TickCount64;
            startedAt ??= now;
            observedNavigation |= IsNavigationActive();
            if (!IsNavigationActive()
                && !FateNavigationPolicy.IsInsideNavigationStartGrace(observedNavigation, now - startedAt.Value))
            {
                throw new VnavmeshStoppedException();
            }

            return false;
        }, new TaskManagerConfiguration { TimeLimitMS = 300000, ShowError = false });
    }

    private Func<Chain> GetWaitingChain(StateManagerModule states)
    {
        return () => Chain.Create("Pots:WaitForSpawn")
            .Then(new TaskManagerTask(() =>
            {
                if (!module.IsEnabled || !IsValid())
                {
                    return true;
                }

                if (Svc.Condition[ConditionFlag.Mounted])
                {
                    StopAtArrivalAndTryDismount("Pots.WaitDismount");
                }

                return false;
            }, new TaskManagerConfiguration { TimeLimitMS = int.MaxValue, ShowError = false }))
            .Then(_ => state = ActivityState.Done);
    }
}
