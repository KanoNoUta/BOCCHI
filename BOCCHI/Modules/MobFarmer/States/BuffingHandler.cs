using BOCCHI.ActionHelpers;
using BOCCHI.Modules.MobFarmer.Chains;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.Chain;
using Ocelot.IPC;
using Ocelot.States;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Buffing)]
public class BuffingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    private bool HasRunBuff = false;
    private bool HasWait = false;
    public override void Enter()
    {
        base.Enter();
        HasRunBuff = false;
        HasWait = false;
    }

    public override FarmerPhase? Handle()
    {
        // Attacked before the pull: fight instead of buffing on. A job-change
        // buff chain is left to finish; only the extra wait is cut short.
        if (Svc.Condition[ConditionFlag.InCombat] || Module.Scanner.InCombat.Any())
        {
            if (HasWait && !HasRunBuff && Plugin.Chain.IsRunning)
            {
                Plugin.Chain.Abort();
            }

            if (Plugin.Chain.IsRunning || Plugin.Chain.QueueCount > 0)
            {
                return null;
            }

            HasRunBuff = false;
            HasWait = false;
            Module.Farmer.RotationPlugin.PhantomJobOn();
            return FarmerPhase.Fighting;
        }

        var vnav = Module.GetIPCSubscriber<VNavmesh>();
        if (vnav.IsRunning())
        {
            return null;
        }

        if (Plugin.Chain.IsRunning || Plugin.Chain.QueueCount > 0)
        {
            return null;
        }

        if (!HasWait)
        {
            HasWait = true;
            Plugin.Chain.Submit(() => Chain.Create("ExtraTimeToWait").Wait(Module.Config.ExtraTimeToWait * 1000));
            return null;
        }

        if (!PullBuffChain.AnyEnabled(Module.Config))
        {
            HasWait = false;
            return FarmerPhase.Gathering;
        }


        if (HasRunBuff)
        {
            HasRunBuff = false;
            if (Actions.Sprint.GetRecastTime() == 0)
            {
                Plugin.Chain.Submit(Actions.Sprint.GetCastChain());
            }
            HasWait = false;
            return FarmerPhase.Gathering;
        }

        Plugin.Chain.Submit(new PullBuffChain(Module));
        HasRunBuff = true;

        return null;
    }
}
