using BOCCHI.ActionHelpers;
using BOCCHI.Modules.Automator;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using Ocelot.States;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Gathering)]
public class GatheringHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    private ChainQueue ChainQueue
    {
        get => ChainManager.Get("MobFarmer+Farmer");
    }

    private DateTimeOffset enteredAt;

    public override void Enter()
    {
        base.Enter();
        enteredAt = DateTimeOffset.UtcNow;
        var auto = Module.GetModule<AutomatorModule>();
        auto.Config.AiProvider.Off();
        PromeRotationController.Stop();

        if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
        {
            Chat.ExecuteCommand("/aepull on");
            Chat.ExecuteCommand("/aeTargetSelector off");
            Chat.ExecuteCommand("/occs off");
        }
    }

    public override FarmerPhase? Handle()
    {
        var vnav = Module.GetIPCSubscriber<VNavmesh>();

        var InCombat = Module.Scanner.InCombat;
        var NotInCombat = Module.Scanner.NotInCombat;
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            ChainQueue.Submit(() => Chain.Create().Then(_ => Actions.TryUnmount()));
        }

        var config = Module.Config;
        var pulled = MobFarmerPullPolicy.CountTowardMinimum(
            InCombat.Select(o => o.NameId),
            config.CountSpecialMobsTowardMinimum);
        var timedOut = InCombat.Any()
                       && MobFarmerPullPolicy.IsStackingTimedOut(config.StackingTimeoutSeconds, DateTimeOffset.UtcNow - enteredAt);
        if (pulled >= Module.Farmer.Spots.EffectiveMinimumMobsToStartFight(config) || timedOut || !NotInCombat.Any())
        {
            vnav.Stop();
            ChainQueue.Abort();
            return FarmerPhase.Stacking;
        }

        if (Svc.Targets.Target?.IsTargetingPlayer() == true)
        {
            Svc.Targets.Target = null;
            ChainQueue.Abort();
        }

        var next = NotInCombat.First();
        Svc.Targets.Target = next;
        if (FarmerPullAssist.IsEnabled(config)
            && FarmSpotPolicy.Distance2D(Player.Position, next.Position) <= MobFarmerPullPolicy.PullRange)
        {
            FarmerPullAssist.TryPull(next, config);
        }

        if (ChainQueue.IsRunning || Svc.Targets.Target == null)
        {
            return null;
        }

        var target = Svc.Targets.Target;
        if (!target.IsTargetingPlayer() && !EzThrottler.Throttle("Repath", 500))
        {
            return null;
        }

        Task<List<Vector3>>? task = null;
        List<Vector3> path = [];
        ChainQueue.Submit(() =>
            Chain.Create()
                .Then(_ => task = vnav.Pathfind(Player.Position, target.Position, false))
                .Then(_ => task!.IsCompleted)
                .Then(_ => path = task!.Result)
                .BreakIf(() => path.Count <= 1)
                .Then(_ => path.RemoveAt(0))
                .Then(_ => BOCCHI.Pathfinding.AggroAvoidanceNavigation.FollowPath(vnav, path, false))
        );

        return null;
    }
}
