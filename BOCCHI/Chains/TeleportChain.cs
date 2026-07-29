using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Teleporter;
using Dalamud.Game.ClientState.Conditions;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;

namespace BOCCHI.Chains;

public class TeleportChain(
    Aethernet aethernet,
    Lifestream lifestream,
    TeleporterModule module,
    Aethernet? sourceAethernet = null) : ChainFactory
{
    protected override Chain Create(Chain chain)
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        // Resolve the source from the navigation plan when available. Generic
        // manual callers are already required to be near a shard, so their
        // closest known shard is the correct fallback.
        var source = sourceAethernet?.GetData() ?? AethernetData.GetClosestToPlayer();

        chain.Then(_ => lifestream.Abort());
        chain.ConditionalThen(
            _ => source.DistanceToPlayer() > AethernetData.DISTANCE,
            new PathfindAndMoveToChain(vnav, source.Position));

        // The active custom-aetheryte query is optional in some CN plugin load
        // orders. Validate proximity from the live game object instead, then
        // keep using Lifestream for the actual aethernet teleport.
        chain.BreakIf(() => !ZoneData.IsNearAethernetShard(source.Aethernet, AethernetData.DISTANCE + 1f));

        chain.Then(_ => vnav.Stop());
        chain.Then(_ => lifestream.AethernetTeleportByPlaceNameId((uint)aethernet));
        chain.WaitToCycleCondition(ConditionFlag.BetweenAreas);
        // Mount if we should mount and not pathfind, otherwise let the pathfinder handle it
        chain.ConditionalThen(_ => module.Config is { ShouldMount: true, PathToDestination: false }, ChainHelper.MountChain());

        return chain;
    }
}
