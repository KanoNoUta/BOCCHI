using BOCCHI.Modules.MobFarmer.Chains;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.States;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.TreasureFinding)]
public class TreasureFindingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    public override FarmerPhase? Handle()
    {
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return FarmerPhase.Fighting;
        }

        Plugin.Chain.Submit(new TreasureFindingChain(Module));

        return FarmerPhase.Waiting;
    }
}
