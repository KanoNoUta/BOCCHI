using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.IPC;
using Ocelot.States;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Stacking)]
public class StackingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    private bool HasRunStack = false;
    private const float ArrivalRadius = 2.5f;
    private Vector3? StackGoal = null;

    public override void Enter()
    {
        base.Enter();
        HasRunStack = false;
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

        StackGoal = furthest.Position;
        BOCCHI.Pathfinding.AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, StackGoal.Value, false);
        HasRunStack = true;
        return null;
    }
}
