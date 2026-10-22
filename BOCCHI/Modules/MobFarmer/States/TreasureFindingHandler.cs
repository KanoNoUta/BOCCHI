using BOCCHI.Modules.MobFarmer.Chains;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.States;
using System;

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

        var config = Module.Config;
        var now = DateTimeOffset.UtcNow;
        if (MobFarmerYieldPolicy.IsTreasureSightDue(
                config.CastTreasureSightAtFarm,
                config.TreasureSightIntervalMinutes,
                Module.Farmer.LastTreasureSightAt,
                now))
        {
            Module.Farmer.LastTreasureSightAt = now;
            Plugin.Chain.Submit(new TreasureFindingChain(Module));
        }

        return FarmerPhase.Waiting;
    }
}
