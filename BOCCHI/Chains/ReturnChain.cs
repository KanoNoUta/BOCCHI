using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Buff.Chains;
using BOCCHI.Modules.Teleporter;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel.Sheets;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Chains;

public class ReturnChain(TeleporterModule module, ReturnChainConfig config) : ChainFactory
{
    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    protected override unsafe Chain Create(Chain chain)
    {
        Succeeded = false;
        FailureReason = "Return chain did not reach the base-camp aethernet.";
        chain.BreakIf(() => Player.IsDead);

        var shouldReturn = config.ForceReturn || GetCostToReturn() < GetCostToWalk();
        var returnTerritory = Svc.ClientState.TerritoryType;
        var lifestream = module.GetIPCSubscriber<Lifestream>();

        if (shouldReturn)
        {
            // A stale delayed-CE teleport must not race Return and provide the
            // BetweenAreas cycle that this chain is waiting for.
            chain.Then(_ => lifestream.Abort());
            // Residual vnavmesh movement or a mount interrupts the Return cast
            // and leaves the player where they were, which later fails the
            // landing verification and loops the CE back into reselection.
            var vnav = module.GetIPCSubscriber<VNavmesh>();
            chain.Then(_ => vnav.Stop());
            chain.Then(_ => Actions.TryUnmount());
            chain.Then(new TaskManagerTask(
                () => config.StopCheck?.Invoke() == true
                      || ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 8) == 0,
                new TaskManagerConfiguration
                {
                    TimeLimitMS = 30000,
                    AbortOnTimeout = true,
                }));
            chain = Actions.Return.CastOnChain(chain);
            chain
                .WaitToCast(timeout: 10000)
                .WaitToCycleCondition(ConditionFlag.BetweenAreas, timeout: 60000);
            chain.Then(_ => VerifyReturnLanding(returnTerritory, lifestream));
        }

        chain.Then(ChainHelper.TreasureSightChain());
        chain.Then(ApplyBuffs);
        chain.Then(ChangeLowLevelJob);

        if (config.ApproachAetheryte)
        {
            var vnav = module.GetIPCSubscriber<VNavmesh>();
            var position = ZoneData.GetBaseCampAethernet().GetData().NavigationPosition;
            var approachStartedAt = 0L;
            long? navigationInactiveSince = null;

            chain.Then(new PathfindAndMoveToChain(vnav, position));
            chain.Then(new TaskManagerTask(() =>
            {
                try
                {
                    if (config.StopCheck?.Invoke() == true)
                    {
                        vnav.Stop();
                        return true;
                    }

                    if (!ZoneData.IsInOccultCrescent())
                    {
                        FailureReason = "Left the Occult Crescent while approaching the base-camp aethernet.";
                        AggroAvoidanceNavigation.Stop(vnav);
                        return true;
                    }

                    // Match the automator's idle "am I home" acceptance (4.5y).
                    // DISTANCE (4.2y) forces the walk onto the navigation point
                    // (~2.9y from the shard), inside both checks and the game's
                    // interaction range.
                    var range = AethernetData.DISTANCE;
                    if (ZoneData.IsNearAethernetShard(ZoneData.GetBaseCampAethernet(), range))
                    {
                        return true;
                    }

                    var now = Environment.TickCount64;
                    if (approachStartedAt == 0)
                    {
                        approachStartedAt = now;
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
                        // reloading. Treat that as "navigation state unknown"
                        // and keep waiting without advancing the stop timer.
                        Svc.Log.Warning(exception, "vnavmesh IPC unavailable while approaching the base-camp aethernet.");
                        return false;
                    }

                    navigationInactiveSince = NavigationStopPolicy.UpdateInactiveSince(
                        navigationActive,
                        now,
                        navigationInactiveSince);
                    if (!NavigationStopPolicy.HasStopped(
                            approachStartedAt,
                            navigationInactiveSince,
                            now))
                    {
                        return false;
                    }

                    FailureReason = "vnavmesh stopped before reaching the base-camp aethernet.";
                    return true;
                }
                catch (Exception exception)
                {
                    // Any transient fault (object-table churn while scanning
                    // aethernet objects, game state transitions, etc.) must not
                    // AbortOnError the whole return chain - that stranded the
                    // player at the knowledge crystal for over a dozen
                    // releases. Log it, keep waiting, and re-evaluate next frame.
                    Svc.Log.Warning(exception, "Transient fault while approaching the base-camp aethernet; retrying.");
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
                    FailureReason = "Timed out approaching the base-camp aethernet.";
                    vnav.Stop();
                },
            }));
            // A navigation stop is an expected outcome when the user presses
            // emergency stop. End the return chain as unsuccessful without
            // throwing from a stale nested chain after its parent was aborted.
            // BreakIf runs sequentially after the navigation wait above, so it
            // only fires when navigation already stopped without reaching the
            // shard - it cannot interrupt an in-progress approach.
            chain.BreakIf(() =>
            {
                if (config.StopCheck?.Invoke() == true)
                {
                    AggroAvoidanceNavigation.Stop(vnav);
                    return true;
                }

                if (!ZoneData.IsInOccultCrescent())
                {
                    AggroAvoidanceNavigation.Stop(vnav);
                    return true;
                }

                if (ZoneData.IsNearAethernetShard(
                        ZoneData.GetBaseCampAethernet(),
                        AethernetData.DISTANCE))
                {
                    return false;
                }

                AggroAvoidanceNavigation.Stop(vnav);
                return true;
            });
            chain.Then(_ =>
            {
                Svc.Targets.Target = Svc.Objects.FirstOrDefault(
                    o => o.BaseId == AethernetData.GetClosestToPlayer().BaseId);
            });
            chain.Then(_ => vnav.Stop());
        }

        chain.Then(_ =>
        {
            if (config.ApproachAetheryte
                && !ZoneData.IsNearAethernetShard(
                    ZoneData.GetBaseCampAethernet(),
                    AethernetData.DISTANCE))
            {
                FailureReason = "Return chain finished outside the base-camp aethernet range.";
                throw new InvalidOperationException(FailureReason);
            }

            Succeeded = true;
            FailureReason = null;
        });


        return chain;
    }

    private void VerifyReturnLanding(uint expectedTerritory, Lifestream lifestream)
    {
        var player = Svc.Objects.LocalPlayer;
        var currentTerritory = Svc.ClientState.TerritoryType;
        var hasStartingLocation = ZoneData.StartingLocations.TryGetValue(
            expectedTerritory,
            out var startingLocation);
        var isExpectedOccultTerritory = currentTerritory == expectedTerritory
                                        && ZoneData.IsInOccultCrescent();
        var baseCamp = isExpectedOccultTerritory
            ? ZoneData.GetBaseCampAethernet()
            : Aethernet.Unknown;
        var closestAethernet = player == null || !isExpectedOccultTerritory
            ? Aethernet.Unknown
            : ZoneData.GetClosestAethernetShard(player.Position);
        var verified = player != null
                       && currentTerritory == expectedTerritory
                       && hasStartingLocation
                       && TransitCompletionPolicy.IsVerifiedReturnLanding(
                           player.Position,
                           startingLocation,
                           closestAethernet,
                           baseCamp);

        if (verified)
        {
            return;
        }

        lifestream.Abort();
        var distance = player != null && hasStartingLocation
            ? Vector3.Distance(player.Position, startingLocation)
            : float.NaN;
        FailureReason =
            $"Return transition completed outside the base-camp landing area " +
            $"(expectedTerritory={expectedTerritory}, currentTerritory={currentTerritory}, " +
            $"closestAethernet={closestAethernet}, distance={distance:F2}, " +
            $"range={TransitCompletionPolicy.MaximumReturnLandingDistance:F2}).";
        Svc.Log.Error(FailureReason);
        throw new InvalidOperationException(FailureReason);
    }

    private unsafe Chain ChangeLowLevelJob()
    {
        var auto = module.GetModule<AutomatorModule>();
        var state = PublicContentOccultCrescent.GetState();
        var currentJob = Job.Current;
        var supportJobs = Svc.Data.GetExcelSheet<MKDSupportJob>();
        var currentJobData = supportJobs.GetRow(currentJob.ByteId);
        var chain = Chain.Create();

        if (!auto.Config.ShouldChangeLowLevelJob
            || SupportJobLevelingPolicy.ShouldKeepCurrent(
                currentJob.id,
                state->SupportJobLevels[currentJob.ByteId],
                currentJobData.LevelMax))
            return chain;

        var candidates = new List<SupportJobLevelCandidate>();
        foreach (var job in supportJobs)
        {
            var rowId = (byte)job.RowId;
            candidates.Add(new SupportJobLevelCandidate(
                rowId,
                state->SupportJobLevels[rowId],
                job.LevelMax));
        }

        var nextJob = SupportJobLevelingPolicy.SelectLowestIncomplete(candidates);
        if (nextJob.HasValue)
        {
            var nextJobId = nextJob.Value;
            var lastAttemptAt = 0L;
            chain.Then(new TaskManagerTask(() =>
            {
                if (config.StopCheck?.Invoke() == true)
                {
                    return true;
                }

                if (PublicContentOccultCrescent.GetState()->CurrentSupportJob == nextJobId)
                {
                    return true;
                }

                var now = Environment.TickCount64;
                if (lastAttemptAt == 0 || now - lastAttemptAt >= 1000)
                {
                    PublicContentOccultCrescent.ChangeSupportJob(nextJobId);
                    lastAttemptAt = now;
                }

                return false;
            }, new TaskManagerConfiguration
            {
                TimeLimitMS = 10000,
                AbortOnTimeout = true,
                ShowError = false,
            }));
            chain.Then(_ =>
            {
                if (PublicContentOccultCrescent.GetState()->CurrentSupportJob == nextJobId)
                {
                    return;
                }

                FailureReason = $"Failed to switch to support job row {nextJobId}.";
                throw new InvalidOperationException(FailureReason);
            });
        }

        return chain;
    }

    private Chain ApplyBuffs()
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var buffs = module.GetModule<BuffModule>();

        var closestKnowledgeCrystal = ZoneData.GetNearbyKnowledgeCrystal(60f).FirstOrDefault();
        var crystalPosition = closestKnowledgeCrystal?.Position ?? default;
        var approachPosition = default(Vector3);
        var approachStartedAt = 0L;
        long? navigationInactiveSince = null;

        var chain = Chain.Create();
        chain.BreakIf(() => !buffs.ShouldRefreshBuffs() || !vnav.IsReady() || closestKnowledgeCrystal == null);
        chain.Then(_ => Actions.TryUnmount());

        if (config.StopCheck != null)
        {
            chain.BreakIf(() => config.StopCheck());
        }

        chain.Then(_ =>
        {
            if (config.StopCheck?.Invoke() == true)
            {
                vnav.Stop();
                return;
            }

            approachPosition = KnowledgeCrystalApproachPolicy.GetDesiredApproachPosition(
                Player.Position,
                crystalPosition);
            var projected = vnav.FindPointOnFloor(approachPosition, false, 1.5f);
            if (projected.HasValue
                && KnowledgeCrystalApproachPolicy.IsWithin(projected.Value, crystalPosition, 2.5f))
            {
                approachPosition = projected.Value;
            }

            approachStartedAt = Environment.TickCount64;
            navigationInactiveSince = null;
            AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, approachPosition, false);
            Svc.Log.Info(
                $"Approaching knowledge crystal for buffs: "
                + $"distance={Vector3.Distance(Player.Position, crystalPosition):F2}, "
                + $"destination={approachPosition}.");
        });
        chain.Then(new TaskManagerTask(() =>
        {
            try
            {
                if (config.StopCheck?.Invoke() == true)
                {
                    vnav.Stop();
                    return true;
                }

                if (KnowledgeCrystalApproachPolicy.HasArrived(Player.Position, crystalPosition))
                {
                    return true;
                }

                var now = Environment.TickCount64;
                bool navigationActive;
                try
                {
                    navigationActive = vnav.IsRunning()
                                       || vnav.IsSimpleMoveInProgress()
                                       || vnav.IsPathfinding();
                }
                catch (Exception exception)
                {
                    // Same reload-window guard as the aethernet approach: do
                    // not let a transient IPC failure tear down the buff chain.
                    Svc.Log.Warning(exception, "vnavmesh IPC unavailable while approaching the knowledge crystal.");
                    return false;
                }

                navigationInactiveSince = NavigationStopPolicy.UpdateInactiveSince(
                    navigationActive,
                    now,
                    navigationInactiveSince);
                if (!NavigationStopPolicy.HasStopped(
                        approachStartedAt,
                        navigationInactiveSince,
                        now))
                {
                    return false;
                }

                var distance = Vector3.Distance(Player.Position, crystalPosition);
                FailureReason =
                    $"vnavmesh stopped {distance:F2}y from the knowledge crystal; "
                    + $"buffs require <= {KnowledgeCrystalApproachPolicy.ArrivalDistance:F1}y.";
                Svc.Log.Warning(FailureReason);
                throw new InvalidOperationException(FailureReason);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Transient fault while approaching the knowledge crystal; retrying.");
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
                var distance = Vector3.Distance(Player.Position, crystalPosition);
                FailureReason =
                    $"Timed out approaching the knowledge crystal "
                    + $"(distance={distance:F2}, required<={KnowledgeCrystalApproachPolicy.ArrivalDistance:F1}).";
                vnav.Stop();
            },
        }));
        chain.Then(_ =>
        {
            vnav.Stop();
            var distance = Vector3.Distance(Player.Position, crystalPosition);
            if (!KnowledgeCrystalApproachPolicy.HasArrived(Player.Position, crystalPosition)
                || !ZoneData.IsNearKnowledgeCrystal(KnowledgeCrystalApproachPolicy.MaximumCastDistance))
            {
                FailureReason =
                    $"Knowledge-crystal arrival validation failed before buffs "
                    + $"(distance={distance:F2}).";
                throw new InvalidOperationException(FailureReason);
            }

            Svc.Log.Info($"Knowledge crystal arrival confirmed for buffs: distance={distance:F2}.");
        });

        chain.Then(new AllBuffsChain(buffs));

        return chain;
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration
        {
            TimeLimitMS = 300000,
            AbortOnTimeout = true,
        };
    }

    private Vector3 GetAetheryteNavigationPosition()
    {
        if (ZoneData.IsOccultCrescentTerritory(Svc.ClientState.TerritoryType))
        {
            return ZoneData.GetBaseCampAethernet().GetData().NavigationPosition;
        }

        throw new Exception("Unable to determine Aetheryte position");
    }

    private float GetCostToReturn()
    {
        if (ZoneData.StartingLocations.TryGetValue(Svc.ClientState.TerritoryType, out var start))
        {
            return Vector3.Distance(start, GetAetheryteNavigationPosition()) + 75f;
        }


        throw new Exception("Unable to determine Starting position");
    }

    private float GetCostToWalk()
    {
        return Player.DistanceTo(GetAetheryteNavigationPosition());
    }
}
