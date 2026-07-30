using BOCCHI.ActionHelpers;
using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer.Chains;

public class ReturnTeleportWalkChain(VNavmesh vnav, Lifestream lifestream, Vector3 destination, AethernetData activityShard) : ChainFactory
{
    protected override Chain Create(Chain chain)
    {
        var teleport = ChainHelper.TeleportChain(
            activityShard.Aethernet,
            ZoneData.GetBaseCampAethernet(),
            mountAfterTeleport: false);

        chain.Then(ChainHelper.ReturnChain(new ReturnChainConfig { ApproachAetheryte = true }));
        chain.Then(teleport);
        chain.Then(_ =>
        {
            if (!teleport.Succeeded)
            {
                Svc.Log.Error(
                    $"Stopping return route because aethernet teleport did not complete: " +
                    $"{ZoneData.GetBaseCampAethernet()} -> {activityShard.Aethernet}; " +
                    $"reason={teleport.FailureReason ?? "teleport chain timed out or was aborted"}");
            }
        });
        chain.BreakIf(() => !teleport.Succeeded);
        chain.Debug("Waiting for lifestream to not be 'busy'");
        chain.Then(new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));
        chain.ConditionalThen(_ => Vector3.Distance(Player.Position, destination) > 15f, ChainHelper.MountChain());
        chain.Then(new PathfindAndMoveToChain(vnav, destination));
        chain.WaitUntilNear(vnav, destination, 2f);
        chain.Then(_ => vnav.Stop());
        chain.ConditionalThen(_ => Svc.Condition[ConditionFlag.Mounted], _ => Actions.TryUnmount());

        return chain;
    }
}
