using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Teleporter;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;

namespace BOCCHI.Chains;

public class TeleportChain(
    Aethernet aethernet,
    Lifestream lifestream,
    TeleporterModule module,
    Aethernet? sourceAethernet = null,
    bool mountAfterTeleport = true) : ChainFactory
{
    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    protected override Chain Create(Chain chain)
    {
        Succeeded = false;
        FailureReason = null;

        var vnav = module.GetIPCSubscriber<VNavmesh>();
        // Resolve the source from the navigation plan when available. Generic
        // manual callers are already required to be near a shard, so their
        // closest known shard is the correct fallback.
        var source = sourceAethernet?.GetData() ?? AethernetData.GetClosestToPlayer();
        var sourceApproachStartedAt = 0L;
        var sourceNavigationObserved = false;

        chain.Then(_ => lifestream.Abort());
        chain.ConditionalThen(
            _ => source.DistanceToPlayer() > AethernetData.DISTANCE,
            new PathfindAndMoveToChain(vnav, source.Position));
        chain.Then(new TaskManagerTask(() =>
        {
            var range = AethernetData.DISTANCE + 1f;
            if (source.DistanceToPlayer() <= range)
            {
                return true;
            }

            var now = Environment.TickCount64;
            if (sourceApproachStartedAt == 0)
            {
                sourceApproachStartedAt = now;
            }

            var navigationActive = vnav.IsRunning()
                                   || vnav.IsSimpleMoveInProgress()
                                   || vnav.IsPathfinding();
            sourceNavigationObserved |= navigationActive;
            if (navigationActive
                || (!sourceNavigationObserved && now - sourceApproachStartedAt < 2000))
            {
                return false;
            }

            FailureReason =
                $"vnavmesh stopped before reaching source aethernet {source.Aethernet}.";
            return true;
        }, new TaskManagerConfiguration
        {
            TimeLimitMS = 60000,
            AbortOnTimeout = true,
            ShowError = false,
            OnTaskTimeout = (TaskManagerTask _, ref long _) =>
            {
                FailureReason =
                    $"Timed out approaching source aethernet {source.Aethernet}.";
                vnav.Stop();
            },
        }));

        // North Horn custom shards are not always exposed through Dalamud's
        // live object table. ZoneData also validates the maintained shard
        // coordinate, so a missing runtime BaseId cannot silently skip the
        // teleport and make the outer activity walk across the whole map.
        chain.Then(_ =>
        {
            var range = AethernetData.DISTANCE + 1f;
            if (ZoneData.IsNearAethernetShard(source.Aethernet, range))
            {
                return;
            }

            var distance = source.DistanceToPlayer();
            FailureReason ??=
                $"Player did not reach source aethernet {source.Aethernet} " +
                $"(baseId={source.BaseId}, distance={distance:F2}, range={range:F2}).";
            vnav.Stop();
            Svc.Log.Warning($"Aethernet teleport aborted before IPC: {FailureReason}");
            throw new InvalidOperationException(FailureReason);
        });

        chain.Then(_ => vnav.Stop());
        chain.Then(_ =>
        {
            if (!lifestream.AethernetTeleportByPlaceNameId((uint)aethernet))
            {
                FailureReason = $"Lifestream rejected aethernet teleport to {aethernet}.";
                throw new InvalidOperationException(FailureReason);
            }

            Svc.Log.Info($"Aethernet teleport accepted: {source.Aethernet} -> {aethernet}");
        });
        // North Horn loading times can comfortably exceed Ocelot's five
        // second default. Keep the chain alive until both sides of the zone
        // transition have completed, then verify the actual landing shard.
        chain.WaitToCycleCondition(ConditionFlag.BetweenAreas, timeout: 60000);
        chain.Then(_ =>
        {
            var range = AethernetData.DISTANCE + 1f;
            if (!ZoneData.IsNearAethernetShard(aethernet, range))
            {
                var destination = aethernet.GetData();
                FailureReason =
                    $"Teleport completed outside destination aethernet {aethernet} " +
                    $"(distance={destination.DistanceToPlayer():F2}, range={range:F2}).";
                throw new InvalidOperationException(FailureReason);
            }

            Succeeded = true;
            FailureReason = null;
            Svc.Log.Info($"Aethernet teleport completed: {source.Aethernet} -> {aethernet}");
        });
        // Mount if we should mount and not pathfind, otherwise let the pathfinder handle it
        chain.ConditionalThen(
            _ => mountAfterTeleport && module.Config is { ShouldMount: true, PathToDestination: false },
            ChainHelper.MountChain());

        return chain;
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration
        {
            TimeLimitMS = 240000,
            AbortOnTimeout = true,
        };
    }
}
