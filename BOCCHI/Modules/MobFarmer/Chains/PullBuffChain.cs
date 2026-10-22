using BOCCHI.ActionHelpers;
using BOCCHI.Chains;
using BOCCHI.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace BOCCHI.Modules.MobFarmer.Chains;

/// <summary>
/// Buffs before a pull: Quickstep (Dancer), Battle Bell and Ringing Respite
/// (Geomancer), then Counterstance (Monk) last so Fleetfooted covers the
/// start of the pull, and back to the job the pull started on. A buff whose
/// cooldown is longer than the maximum wait is skipped for this pull.
/// Replaces the Battle Bell chain; the extra buffs are ported from upstream
/// BOCCHI 4.2.
/// </summary>
public class PullBuffChain(MobFarmerModule module) : OwnedChainFactory
{
    private readonly Job startingJob = Job.Current;

    public static bool AnyEnabled(MobFarmerConfig config)
    {
        return config.ApplyBattleBell
               || config.ShouldApplyRingingRespite
               || config.ApplyQuickstep
               || config.ApplyCounterstance;
    }

    protected override Chain Create(Chain chain)
    {
        var config = module.Config;
        var maximumWait = config.MaximumBattleBellWaitTime;
        var quickstep = config.ApplyQuickstep
                        && Job.Dancer.Level >= MobFarmerPullPolicy.QuickstepUnlockLevel
                        && !MobFarmerPullPolicy.ShouldSkipQuickstep(
                            config.QuickstepSkipIfRemainingMinutes,
                            Player.Status.Get(PlayerStatus.QuickerStep)?.RemainingTime ?? 0f);
        var bell = config.ApplyBattleBell;
        var respite = config.ShouldApplyRingingRespite
                      && Job.Geomancer.Level >= MobFarmerPullPolicy.RingingRespiteUnlockLevel;
        var counterstance = config.ApplyCounterstance
                            && Job.Monk.Level >= MobFarmerPullPolicy.CounterstanceUnlockLevel;
        if (!quickstep && !bell && !respite && !counterstance)
        {
            return chain;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            chain.Then(_ => Actions.TryUnmount()).Wait(1500);
        }

        // Recasts are read after the job change: the phantom action slots
        // belong to whichever support job is active.
        if (quickstep)
        {
            chain.Then(Job.Dancer.ChangeToChain);
            chain.ConditionalThen(
                    _ => Actions.Dancer.Quickstep.GetRecastTime() <= maximumWait,
                    Actions.Dancer.Quickstep.GetCastChain())
                .Wait(1000);
        }

        if (bell || respite)
        {
            chain.Then(Job.Geomancer.ChangeToChain);
            if (bell)
            {
                chain.ConditionalThen(
                        _ => Actions.Geomancer.BattleBell.GetRecastTime() <= maximumWait,
                        Actions.Geomancer.BattleBell.GetCastChain())
                    .Wait(1000);
            }

            if (respite)
            {
                chain.ConditionalThen(
                        _ => Actions.Geomancer.RingingRespite.GetRecastTime() <= maximumWait,
                        Actions.Geomancer.RingingRespite.GetCastChain())
                    .Wait(1000);
            }
        }

        if (counterstance)
        {
            chain.Then(Job.Monk.ChangeToChain);
            chain.ConditionalThen(
                    _ => !Player.Status.Has(PlayerStatus.Fleetfooted)
                         && Actions.Monk.Counterstance.GetRecastTime() <= maximumWait,
                    Actions.Monk.Counterstance.GetCastChain())
                .Wait(1000);
        }

        chain.Then(startingJob.ChangeToChain);
        return chain;
    }
}
