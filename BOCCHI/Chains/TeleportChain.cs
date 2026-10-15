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
    bool mountAfterTeleport = true,
    Func<bool>? stopCheck = null) : OwnedChainFactory
{
    // North Horn loading times can comfortably exceed Ocelot's five second
    // default, on both sides of the zone transition.
    private const int LoadingTimeoutMs = 60_000;

    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// An owner that stops (an expired activity, emergency stop) must not
    /// leave this chain running on its own: a teleport submitted after the
    /// owner ended strands the player at the destination shard.
    /// </summary>
    private bool IsStopped()
    {
        return stopCheck?.Invoke() == true;
    }

    private bool CancelIfStopped()
    {
        if (!IsStopped())
        {
            return false;
        }

        FailureReason = $"Aethernet teleport to {aethernet} cancelled because its owner stopped.";
        Svc.Log.Info(FailureReason);
        return true;
    }

    protected override Chain Create(Chain chain)
    {
        Succeeded = false;
        FailureReason = null;

        var destinationData = aethernet.GetData();
        // The previous chain may already have completed this teleport before
        // being aborted (for example when the activity expired mid-flight).
        // Do not submit a second teleport request to the same aethernet once
        // the player has already landed at the destination.
        if (destinationData.IsPlayerWithinLandingRange(AethernetData.DISTANCE))
        {
            Succeeded = true;
            Svc.Log.Info($"Aethernet teleport skipped: already at {aethernet}.");
            return chain;
        }

        if (CancelIfStopped())
        {
            return chain;
        }

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

        // A previously aborted chain may have left a teleport in flight (for
        // example when the activity expired mid-flight). Aborting it here and
        // immediately submitting the same destination is what causes the
        // double-teleport loop. Instead, first let an in-flight request finish;
        // the "already at destination" check above then completes without a
        // second submission. Only abort after a bounded wait so a hung
        // Lifestream request cannot stall this chain forever.
        chain.Then(new TaskManagerTask(() =>
        {
            if (IsStopped() || !lifestream.IsBusy())
            {
                return true;
            }

            Svc.Log.Info("Waiting for the previous Lifestream teleport request to finish before submitting a new one.");
            return false;
        }, new TaskManagerConfiguration
        {
            TimeLimitMS = 30000,
            AbortOnTimeout = true,
            ShowError = false,
            OnTaskTimeout = (TaskManagerTask _, ref long _) =>
            {
                Svc.Log.Warning("Previous Lifestream teleport request did not finish within 30s; aborting it.");
                try
                {
                    lifestream.Abort();
                }
                catch (Exception exception)
                {
                    Svc.Log.Warning(exception, "Could not abort the stale Lifestream teleport request.");
                }
            },
        }));
        chain.BreakIf(CancelIfStopped);
        // Lifestream performs NO approach of its own for custom aethernets: it
        // targets the crystal and calls the game's InteractWithObject, which
        // only fires inside the game's ~4.5m interaction range. The maintained
        // North Horn coordinates can sit several metres from the physical
        // crystal, so we navigate to the crystal's real object position and only
        // hand off to Lifestream once we are inside interaction range, otherwise
        // the request is "accepted" but silently stalls. Stay slightly below the
        // game's range: a tighter gate than 4.5y makes vnavmesh fail while the
        // player is standing next to the crystal (e.g. 3.54y vs 3.50y).
        const float interactRange = 4.2f;
        var lastRepathAt = 0L;
        chain.ConditionalThen(
            _ => !IsStopped() && ZoneData.GetDistanceToAethernetShard(source) > interactRange,
            new PathfindAndMoveToChain(vnav, ZoneData.GetAethernetShardApproachPosition(source), stopCheck: stopCheck));
        chain.Then(new TaskManagerTask(() =>
        {
            try
            {
                if (IsStopped())
                {
                    return true;
                }

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

                bool navigationActive;
                try
                {
                    navigationActive = vnav.IsRunning()
                                       || vnav.IsSimpleMoveInProgress()
                                       || vnav.IsPathfinding();
                }
                catch (Exception exception)
                {
                    // vnavmesh can briefly unregister its IPC methods while
                    // reloading. Keep waiting instead of letting AbortOnError
                    // kill the approach chain.
                    Svc.Log.Warning(exception, "vnavmesh IPC unavailable while approaching the source aethernet.");
                    return false;
                }

                sourceNavigationInactiveSince = NavigationStopPolicy.UpdateInactiveSince(
                    navigationActive,
                    now,
                    sourceNavigationInactiveSince);
                if (navigationActive)
                {
                    return false;
                }

                // Navigation settled but we are still outside interaction
                // range; the crystal's live position may have resolved after
                // the initial pathfind. Re-issue a path toward the best-known
                // crystal position.
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
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Transient fault while approaching the source aethernet; retrying.");
                return false;
            }
        }, new TaskManagerConfiguration
        {
            TimeLimitMS = 60000,
            AbortOnError = false,
            AbortOnTimeout = true,
            ShowError = false,
            OnTaskTimeout = (TaskManagerTask _, ref long _) =>
            {
                FailureReason =
                    $"Timed out approaching source aethernet {source.Aethernet}.";
                vnav.Stop();
            },
        }));

        chain.BreakIf(CancelIfStopped);
        chain.Then(_ => vnav.Stop());
        chain.Then(context =>
        {
            if (CancelIfStopped())
            {
                context.source.Cancel();
                return;
            }

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
            // The owner that stopped this chain also aborted Lifestream.
            if (acceptedSequence == null || IsStopped())
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
        // Keep the chain alive until both sides of the zone transition have
        // completed, then verify the actual landing shard. Until loading
        // starts, a stopped owner no longer needs this teleport.
        var sawLoading = false;
        chain.Then(new TaskManagerTask(() =>
        {
            if (IsLoading())
            {
                sawLoading = true;
                return true;
            }

            return IsStopped();
        }, LoadingWait("start")));
        chain.BreakIf(() => !sawLoading && CancelIfStopped());
        chain.Then(new TaskManagerTask(() => !IsLoading(), LoadingWait("finish")));
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
            _ => mountAfterTeleport && !IsStopped() && module.Config is { ShouldMount: true, PathToDestination: false },
            ChainHelper.MountChain(stopCheck));

        return chain;
    }

    private static bool IsLoading()
    {
        return Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
    }

    private TaskManagerConfiguration LoadingWait(string phase)
    {
        return new TaskManagerConfiguration
        {
            TimeLimitMS = LoadingTimeoutMs,
            AbortOnTimeout = true,
            ShowError = false,
            OnTaskTimeout = (TaskManagerTask _, ref long _) =>
            {
                FailureReason = $"Timed out waiting for the aethernet loading screen to {phase} ({aethernet}).";
                Svc.Log.Warning(FailureReason);
            },
        };
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
