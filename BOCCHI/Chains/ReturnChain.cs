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
            chain.Then(new TaskManagerTask(
                () => ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 8) == 0,
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
                var range = AethernetData.DISTANCE + 1f;
                if (ZoneData.IsNearAethernetShard(ZoneData.GetBaseCampAethernet(), range))
                {
                    return true;
                }

                var now = Environment.TickCount64;
                if (approachStartedAt == 0)
                {
                    approachStartedAt = now;
                }

                var navigationActive = vnav.IsRunning()
                                       || vnav.IsSimpleMoveInProgress()
                                       || vnav.IsPathfinding();
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
            }, new TaskManagerConfiguration
            {
                TimeLimitMS = 60000,
                AbortOnTimeout = true,
                ShowError = false,
                OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                {
                    FailureReason = "Timed out approaching the base-camp aethernet.";
                    vnav.Stop();
                },
            }));
            chain.Then(_ =>
            {
                if (!ZoneData.IsNearAethernetShard(
                        ZoneData.GetBaseCampAethernet(),
                        AethernetData.DISTANCE + 1f))
                {
                    vnav.Stop();
                    throw new InvalidOperationException(
                        FailureReason ?? "Return did not reach the base-camp aethernet.");
                }

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
                    AethernetData.DISTANCE + 1f))
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

        var chain = Chain.Create();
        chain.BreakIf(() => !buffs.ShouldRefreshBuffs() || !vnav.IsReady() || closestKnowledgeCrystal == null);
        chain.Then(_ => Actions.TryUnmount());

        chain.Then(PathfindAndMoveToChain.RandomNearby(vnav, closestKnowledgeCrystal!.Position, 3));
        chain.WaitUntilNear(vnav, closestKnowledgeCrystal!.Position, 3);
        chain.Then(_ => vnav.Stop());

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
