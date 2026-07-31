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
            TreasureSightRefreshPolicy.ShouldCast(
                force || module.Config.CastTreasureSightUponReturn,
                module.Tracker.CountInitialised)
            && !Svc.Condition[ConditionFlag.InCombat]
            && Actions.Freelancer.Treasuresight.CanCast());

        chain.ConditionalThen(_ => Svc.Condition[ConditionFlag.Mounted], _ => Actions.TryUnmount());
        chain.WaitUntilNotCondition(ConditionFlag.Mounted, timeout: 5000);
        chain.Then(Job.Freelancer.ChangeToChain);
        chain.Then(Actions.Freelancer.Treasuresight.GetCastChain()).Wait(1500);
        chain.Then(StartingJob.ChangeToChain);
        chain.OnFinally(() => module.QueueTreasureSightJobRestore(StartingJob));

        return chain;
    }
}

public static class TreasureSightRefreshPolicy
{
    public static bool ShouldCast(bool requested, bool countInitialised)
    {
        // The count is reset on territory changes and then maintained locally
        // as coffers are opened. Recasting on every return/start only causes
        // repeated Freelancer swaps without providing newer route data.
        return requested && !countInitialised;
    }
}
