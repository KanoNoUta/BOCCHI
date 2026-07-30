using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Buff.Chains;
using BOCCHI.Modules.Teleporter;
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

        if (shouldReturn)
        {
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
        }

        chain.Then(ChainHelper.TreasureSightChain());
        chain.Then(ApplyBuffs);
        chain.Then(ChangeLowLevelJob);

        if (config.ApproachAetheryte)
        {
            var vnav = module.GetIPCSubscriber<VNavmesh>();
            var position = GetAetherytePosition();
            var approachStartedAt = 0L;
            var navigationObserved = false;

            chain.Then(PathfindAndMoveToChain.RandomNearby(vnav, position, 3));
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
                navigationObserved |= navigationActive;
                if (navigationActive
                    || (!navigationObserved && now - approachStartedAt < 2000))
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

    private Vector3 GetAetherytePosition()
    {
        if (ZoneData.Aetherytes.TryGetValue(Svc.ClientState.TerritoryType, out var position))
        {
            return position;
        }

        throw new Exception("Unable to determine Aetheryte position");
    }

    private float GetCostToReturn()
    {
        if (ZoneData.StartingLocations.TryGetValue(Svc.ClientState.TerritoryType, out var start))
        {
            return Vector3.Distance(start, GetAetherytePosition()) + 75f;
        }


        throw new Exception("Unable to determine Starting position");
    }

    private float GetCostToWalk()
    {
        return Player.DistanceTo(GetAetherytePosition());
    }
}
