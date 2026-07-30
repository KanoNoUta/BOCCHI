using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using ECommons.Automation.NeoTaskManager;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace BOCCHI.Modules.Buff.Chains;

public abstract class BuffChain(Job job, PlayerStatus buff, Action action) : ChainFactory
{
    protected abstract BuffModule Module { get; }

    protected override Chain Create(Chain chain)
    {
        chain
            // AllBuffsChain can be requested because one buff is missing or
            // expiring. Re-check each individual status at execution time so
            // healthy buffs are not recast on every return.
            .RunIf(() => ShouldRun() && Module.BuffManager.NeedsRefresh(Module, buff))
            .Then(job.ChangeToChain);

        return action
            .CastOnChain(chain)
            .Then(_ => Player.Status.Has(buff))
            .Then(_ => Player.Status.Get(buff)?.RemainingTime >= 1780);
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration { TimeLimitMS = 15000 };
    }

    protected abstract bool ShouldRun();
}
