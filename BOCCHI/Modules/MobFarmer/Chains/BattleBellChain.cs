using BOCCHI.Chains;
using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace BOCCHI.Modules.MobFarmer.Chains;

public class BattleBellChain(MobFarmerModule module) : OwnedChainFactory
{
    protected override Chain Create(Chain chain)
    {
        chain.BreakIf(() => Actions.Geomancer.BattleBell.GetRecastTime() >= module.Config.MaximumBattleBellWaitTime);
        chain.BreakIf(() => Actions.Geomancer.RingingRespite.GetRecastTime() >= module.Config.MaximumBattleBellWaitTime);
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            chain.Then(_ => Actions.TryUnmount()).Wait(1500);
        }
        chain.Then(Job.Geomancer.ChangeToChain);
        chain.Then(Actions.Geomancer.BattleBell.GetCastChain()).Wait(1000);
        chain.Then(Actions.Geomancer.RingingRespite.GetCastChain()).Wait(1000);
        chain.Then(Job.Cannoneer.ChangeToChain);

        return chain;
    }
}
