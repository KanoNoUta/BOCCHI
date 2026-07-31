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
        // Baseline of Lifestream's teleport-request sequence captured right
        // before we submit, so the status poll below knows it is observing our
        // request and not a stale result. Null means the status IPC is absent.
        uint? acceptedSequence = null;

        chain.Then(_ => lifestream.Abort());
        // Lifestream performs NO approach of its own for custom aethernets: it
        // targets the crystal and calls the game's InteractWithObject, which
        // only fires inside the game's ~4.5m interaction range. The maintained
        // North Horn coordinates can sit several metres from the physical
        // crystal, so we navigate to the crystal's real object position and only
        // hand off to Lifestream once we are comfortably inside interaction
        // range, otherwise the request is "accepted" but silently stalls.
        const float interactRange = 3.5f;
        var lastRepathAt = 0L;
        chain.ConditionalThen(
            _ => ZoneData.GetDistanceToAethernetShard(source) > interactRange,
            new PathfindAndMoveToChain(vnav, ZoneData.GetAethernetShardApproachPosition(source)));
        chain.Then(new TaskManagerTask(() =>
        {
            if (ZoneData.GetDistanceToAethernetShard(source) <= interactRange)
            {
                vnav.Stop();
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
            if (navigationActive)
            {
                return false;
            }

            // Navigation settled but we are still outside interaction range;
            // the crystal's live position may have resolved after the initial
            // pathfind. Re-issue a path toward the best-known crystal position.
            if (now - lastRepathAt > 1500
                && AggroAvoidanceNavigation.PathfindAndMoveTo(
                    vnav,
                    ZoneData.GetAethernetShardApproachPosition(source),
                    false))
            {
                lastRepathAt = now;
                sourceNavigationInactiveSince = null;
                return false;
            }

            if (!NavigationStopPolicy.HasStopped(
                    sourceApproachStartedAt,
                    sourceNavigationInactiveSince,
                    now))
            {
                return false;
            }

            FailureReason =
                $"vnavmesh could not reach interaction range of source aethernet {source.Aethernet} " +
                $"(distance={ZoneData.GetDistanceToAethernetShard(source):F2}, range={interactRange:F2}).";
            vnav.Stop();
            Svc.Log.Warning($"Aethernet teleport aborted before IPC: {FailureReason}");
            throw new InvalidOperationException(FailureReason);
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

        chain.Then(_ => vnav.Stop());
        chain.Then(_ =>
        {
            // Snapshot the sequence before submitting; a successful submit bumps
            // it, letting the poll below distinguish our attempt from a stale one.
            acceptedSequence = LifestreamTeleportStatus.GetSequence();
            if (!lifestream.AethernetTeleportByPlaceNameId((uint)aethernet))
            {
                FailureReason = $"Lifestream rejected aethernet teleport to {aethernet}.";
                throw new InvalidOperationException(FailureReason);
            }

            Svc.Log.Info($"Aethernet teleport accepted: {source.Aethernet} -> {aethernet}");
        });
        // "Accepted" from Lifestream only means the task was enqueued, not that
        // the current shard/destination were resolved. Poll Lifestream's real
        // execution status so a queued-but-doomed request (the "未找到目的地 (3)"
        // race) fails fast with a classified reason instead of blocking on a
        // zone transition that will never happen. When the status IPC is not
        // exposed (older Lifestream / mid-reload), fall back to the legacy
        // zone-transition detection below.
        chain.Then(new TaskManagerTask(() =>
        {
            if (acceptedSequence == null)
            {
                return true;
            }

            var status = LifestreamTeleportStatus.GetStatus();
            switch (status)
            {
                case LifestreamTeleportStatus.Status.Unknown:
                    // Gate vanished mid-flight; defer to legacy detection.
                    return true;

                case LifestreamTeleportStatus.Status.Dispatched:
                    // The game teleport command was actually issued. Proceed to
                    // wait for the zone transition and verify the landing shard.
                    return true;

                case LifestreamTeleportStatus.Status.Failed:
                {
                    var failure = LifestreamTeleportStatus.GetFailure();
                    FailureReason =
                        $"Lifestream failed to dispatch teleport to {aethernet}: " +
                        $"{LifestreamTeleportStatus.Describe(failure)} ({(int)failure}).";
                    Svc.Log.Warning(FailureReason);
                    throw new InvalidOperationException(FailureReason);
                }

                default:
                    // None/Queued: still waiting for the shard/zone data to
                    // become ready. Keep polling until this task times out.
                    return false;
            }
        }, new TaskManagerConfiguration
        {
            TimeLimitMS = 15000,
            AbortOnTimeout = true,
            ShowError = false,
            OnTaskTimeout = (TaskManagerTask _, ref long _) =>
            {
                FailureReason =
                    $"Timed out waiting for Lifestream to dispatch teleport to {aethernet} " +
                    $"(last status={LifestreamTeleportStatus.GetStatus()}).";
                Svc.Log.Warning(FailureReason);
            },
        }));
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
