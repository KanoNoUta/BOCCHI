using BOCCHI.ActionHelpers;
using BOCCHI.Chains;
using BOCCHI.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using System;

namespace BOCCHI.Modules.Mount.Chains;

public class MountChain(MountConfig config, Func<bool>? stopCheck = null) : RetryChainFactory
{
    public override Func<Chain> Factory()
    {
        var factory = base.Factory();
        return () => factory().DisposeWhenFinished();
    }

    protected override Chain Create(Chain chain)
    {
        chain.BreakIf(Breaker);

        return !config.MountRoulette ? Actions.Mount(config.Mount).CastOnChain(chain) : Actions.MountRoulette.CastOnChain(chain);
    }

    private bool Breaker()
    {
        return IsStopped()
               || Svc.Condition[ConditionFlag.Mounted]
               || Svc.Condition[ConditionFlag.BetweenAreas]
               || Svc.Condition[ConditionFlag.BetweenAreas51]
               || Svc.Condition[ConditionFlag.InCombat]
               || Player.Status.Has(PlayerStatus.HoofingIt)
               || Player.IsCasting
               || Player.IsDead;
    }

    private bool IsStopped()
    {
        return stopCheck?.Invoke() == true;
    }

    public override int GetThrottle()
    {
        return attempt == 0 ? 500 : 5000;
    }

    // A stopped owner (for example an expired activity) ends the retry loop
    // instead of mounting the player after the work it was for has ended.
    public override bool IsComplete()
    {
        return Svc.Condition[ConditionFlag.Mounted] || IsStopped();
    }
}
