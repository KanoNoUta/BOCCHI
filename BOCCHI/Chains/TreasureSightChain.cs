using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Modules.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace BOCCHI.Chains;

public class TreasureSightChain(TreasureModule module, bool force = false) : ChainFactory
{
    private readonly Job StartingJob = Job.Current;

    protected override Chain Create(Chain chain)
    {
        chain.RunIf(() =>
            (force || module.Config.CastTreasureSightUponReturn)
            && !Svc.Condition[ConditionFlag.InCombat]
            && Actions.Freelancer.Treasuresight.CanCast());

        chain.ConditionalThen(_ => Svc.Condition[ConditionFlag.Mounted], _ => Actions.TryUnmount());
        chain.WaitUntilNotCondition(ConditionFlag.Mounted, timeout: 5000);
        chain.Then(Job.Freelancer.ChangeToChain);
        chain.Then(Actions.Freelancer.Treasuresight.GetCastChain()).Wait(1500);
        chain.Then(StartingJob.ChangeToChain);

        return chain;
    }
}
