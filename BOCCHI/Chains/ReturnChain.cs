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
using System.Linq;
using System.Numerics;

namespace BOCCHI.Chains;

public class ReturnChain(TeleporterModule module, ReturnChainConfig config) : ChainFactory
{
    protected override unsafe Chain Create(Chain chain)
    {
        chain.BreakIf(() => Player.IsDead);

        var shouldReturn = GetCostToReturn() < GetCostToWalk();

        if (shouldReturn)
        {
            chain.Then(_ => ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 8) == 0);
            chain = Actions.Return.CastOnChain(chain);
            chain.WaitToCast().WaitToCycleCondition(ConditionFlag.BetweenAreas);
        }

        chain.Then(ChainHelper.TreasureSightChain());
        chain.Then(ApplyBuffs);
        chain.Then(ChangeLowLevelJob);

        if (config.ApproachAetheryte)
        {
            var vnav = module.GetIPCSubscriber<VNavmesh>();
            var position = GetAetherytePosition();

            chain.Then(PathfindAndMoveToChain.RandomNearby(vnav, position, 3));
            chain.Then(_ => Svc.Targets.Target = Svc.Objects.FirstOrDefault(o => o.BaseId == AethernetData.GetClosestToPlayer().BaseId));
            chain.Then(_ => vnav.Stop());
        }


        return chain;
    }

    private unsafe Chain ChangeLowLevelJob()
    {
        var auto = module.GetModule<AutomatorModule>();
        var state = PublicContentOccultCrescent.GetState();
        var currentJob = Job.Current;
        var chain = Chain.Create();

        if (!auto.Config.ShouldChangeLowLevelJob
            || state->SupportJobLevels[currentJob.ByteId] < Svc.Data.GetExcelSheet<MKDSupportJob>().GetRow(currentJob.ByteId).LevelMax)
            return chain;

        foreach (var job in Svc.Data.GetExcelSheet<MKDSupportJob>())
        {
            var level = state->SupportJobLevels[(byte)job.RowId];
            if (level == 0 || level >= job.LevelMax)
            {
                continue;
            }

            chain.Then(_ => PublicContentOccultCrescent.ChangeSupportJob((byte)job.RowId));
            return chain;
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
        return new TaskManagerConfiguration { TimeLimitMS = 60000 };
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
