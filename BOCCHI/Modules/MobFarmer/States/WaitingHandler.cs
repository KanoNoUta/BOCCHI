using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.States;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Waiting)]
public class WaitingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    public override FarmerPhase? Handle()
    {
        var startingPoint = Module.Farmer.StartingPoint;
        if (Player.DistanceTo(startingPoint) > 2f)
        {
            return null;
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return FarmerPhase.Fighting;
        }

        var mobs = Module.Scanner.Mobs;

        return mobs.Count() >= Module.Config.MinimumMobsToStartLoop ? FarmerPhase.Buffing : null;
    }
}
