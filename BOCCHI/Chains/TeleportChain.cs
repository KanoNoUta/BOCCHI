using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Teleporter;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Numerics;

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
        long? sourceNavigationInactiveSince = null;

        chain.Then(_ => lifestream.Abort());
        chain.ConditionalThen(
            _ => source.DistanceToPlayer() > AethernetData.DISTANCE,
            new PathfindAndMoveToChain(vnav, source.NavigationPosition));
        chain.Then(new TaskManagerTask(() =>
        {
            // Stay inside Lifestream's custom-aethernet interaction range.
            // The previous one-yalm allowance could stop navigation at 5.2,
            // while Lifestream only recognizes these shards within 4.6.
            var range = AethernetData.DISTANCE;
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
            sourceNavigationInactiveSince = NavigationStopPolicy.UpdateInactiveSince(
                navigationActive,
                now,
                sourceNavigationInactiveSince);
            if (!NavigationStopPolicy.HasStopped(
                    sourceApproachStartedAt,
                    sourceNavigationInactiveSince,
                    now))
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
            var range = AethernetData.DISTANCE;
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
            var destination = aethernet.GetData();
            var range = AethernetData.DISTANCE;
            if (!destination.IsPlayerWithinLandingRange(range))
            {
                FailureReason =
                    $"Teleport completed outside destination landing point {aethernet} " +
                    $"(distance={Vector3.Distance(Player.Position, destination.Destination):F2}, " +
                    $"range={range:F2}).";
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
