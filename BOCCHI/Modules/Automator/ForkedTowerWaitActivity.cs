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

namespace BOCCHI.Modules.Automator;

/// <summary>
/// Walks to the Forked Tower pad while registration is open and waits there
/// until the tower pulls the player in or registration lapses. Blood still
/// needs ciphers offered by hand; this only reminds the player.
/// </summary>
public sealed class ForkedTowerWaitActivity : Activity
{
    private readonly ForkedTowerRegistration.Pad pad;

    private readonly Func<bool> stillWanted;

    private bool reminderPrinted;

    public ForkedTowerWaitActivity(
        EventData data,
        ForkedTowerRegistration.Pad pad,
        Lifestream lifestream,
        VNavmesh vnav,
        AutomatorModule module,
        Func<bool> stillWanted)
        : base(data, lifestream, vnav, module)
    {
        this.pad = pad;
        this.stillWanted = stillWanted;
        // The tower event can already be current on the pad; waiting never fights.
        state = ActivityState.Idle;
        handlers[ActivityState.WaitingForForkedTower] = GetWaitingChain;
    }

    public override bool IsValid()
    {
        return stillWanted();
    }

    public override string GetName()
    {
        return string.Format(module.T("panel.forked_tower.waiting_for"), data.InternalName);
    }

    protected override Vector3 GetPosition()
    {
        return vnav.FindPointOnFloor(pad.Position with { Y = pad.Position.Y + 5f }, false, 4f) ?? pad.Position;
    }

    protected override float GetRadius()
    {
        return ForkedTowerRegistration.ArrivalRadius;
    }

    protected override ActivityState GetPostPathfindingState()
    {
        return ActivityState.WaitingForForkedTower;
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

            if (ForkedTowerRegistration.IsOnPad(Player.Position, pad.Position))
            {
                if (module.Config.StayMountedWhileWaiting)
                {
                    vnav.Stop();
                }
                else
                {
                    StopAtArrivalAndTryDismount("ForkedTower.ArrivalDismount");
                }

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
        return () => Chain.Create("ForkedTower:WaitOnPad")
            .Then(new TaskManagerTask(() =>
            {
                if (!module.IsEnabled || !IsValid())
                {
                    return true;
                }

                // Walked off (to a reliquary is fine): end so the automator
                // walks back to the pad.
                if (ForkedTowerRegistration.Distance2D(Player.Position, pad.Position) > ForkedTowerRegistration.LeashRadius)
                {
                    return true;
                }

                if (Svc.Condition[ConditionFlag.Mounted] && !module.Config.StayMountedWhileWaiting)
                {
                    StopAtArrivalAndTryDismount("ForkedTower.WaitDismount");
                }

                if (!reminderPrinted)
                {
                    reminderPrinted = true;
                    Svc.Chat.Print(module.T(pad.NeedsCiphers
                        ? "messages.forked_tower_offer_ciphers"
                        : "messages.forked_tower_on_pad"));
                }

                return false;
            }, new TaskManagerConfiguration { TimeLimitMS = int.MaxValue, ShowError = false }))
            .Then(_ => state = ActivityState.Done);
    }
}
