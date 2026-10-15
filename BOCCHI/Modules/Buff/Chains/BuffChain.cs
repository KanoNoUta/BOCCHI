using BOCCHI.Chains;
using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using ECommons.Automation.NeoTaskManager;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace BOCCHI.Modules.Buff.Chains;

public abstract class BuffChain(Job job, PlayerStatus buff, Action action) : OwnedChainFactory
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

        // The parent stops waiting after 15 seconds. Bound the status checks
        // too, so a cast that never lands ends this chain instead of polling
        // for the rest of the session.
        return action
            .CastOnChain(chain)
            .Then(new TaskManagerTask(() => Player.Status.Has(buff), StatusWait()))
            .Then(new TaskManagerTask(() => Player.Status.Get(buff)?.RemainingTime >= 1780, StatusWait()));
    }

    private static TaskManagerConfiguration StatusWait()
    {
        return new TaskManagerConfiguration
        {
            TimeLimitMS = 10_000,
            AbortOnTimeout = true,
            ShowError = false,
        };
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration { TimeLimitMS = 15000 };
    }

    protected abstract bool ShouldRun();
}
