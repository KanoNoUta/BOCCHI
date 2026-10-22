using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.IPC;
using Ocelot.States;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Stacking)]
public class StackingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    private bool HasRunStack = false;
    private const float ArrivalRadius = 2.5f;
    private Vector3? StackGoal = null;
    private DateTimeOffset enteredAt;

    public override void Enter()
    {
        base.Enter();
        HasRunStack = false;
        enteredAt = DateTimeOffset.UtcNow;
    }

    public override FarmerPhase? Handle()
    {
        var vnav = Module.GetIPCSubscriber<VNavmesh>();

        if (HasRunStack)
        {
            if (StackGoal is { } goal && Player.DistanceTo(goal) <= ArrivalRadius)
            {
                if (vnav.IsRunning())
                {
                    vnav.Stop();
                }

                HasRunStack = false;
                StackGoal = null;
                Module.Farmer.RotationPlugin.PhantomJobOn();
                return FarmerPhase.Fighting;
            }

            // A stack point that cannot be reached must not hold the pack forever.
            if (MobFarmerPullPolicy.IsStackingTimedOut(Module.Config.StackingTimeoutSeconds, DateTimeOffset.UtcNow - enteredAt))
            {
                vnav.Stop();
                HasRunStack = false;
                StackGoal = null;
                Module.Farmer.RotationPlugin.PhantomJobOn();
                return FarmerPhase.Fighting;
            }

            return null;
        }

        var furthest = Module.Scanner.InCombat
            .Where(o => o.Address != Svc.Targets.Target?.Address)
            .OrderBy(Player.DistanceTo)
            .LastOrDefault();

        if (furthest == null)
        {
            return FarmerPhase.Fighting;
        }

        // A spot's stack point (caves, towers) beats the furthest enemy.
        StackGoal = Module.Farmer.Spots.StackPoint ?? furthest.Position;
        BOCCHI.Pathfinding.AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, StackGoal.Value, false);
        HasRunStack = true;
        return null;
    }
}
