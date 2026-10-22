using BOCCHI.ActionHelpers;
using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Modules.MobFarmer.States;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Reflection;
using ECommons.Throttlers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.States;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer;

public class Farmer : IDisposable
{
    public bool Running { get; private set; } = false;

    public Vector3 StartingPoint { get; private set; } = Vector3.Zero;

    /// <summary>Paused while another mode (pots, a treasure hunt, a buff trip) has the player.</summary>
    public bool Suspended { get; private set; }

    public FarmerYieldReason YieldReason { get; private set; } = FarmerYieldReason.None;

    /// <summary>The farmer must walk back to its starting point before the next pull.</summary>
    public bool NeedsApproach { get; private set; }

    /// <summary>The farmer drives navigation; during a pot yield the automator does.</summary>
    public bool OwnsNavigation
    {
        get => Running && YieldReason != FarmerYieldReason.Pots;
    }

    /// <summary>The farmer itself is stopping the automator after a pot; it must keep running.</summary>
    public bool KeepRunningWhenAutomatorStops
    {
        get => Yields.IsReleasingAutomator;
    }

    public DateTimeOffset? LastTreasureSightAt { get; set; }

    public readonly FarmerYields Yields = new();

    private const float ApproachArriveRange = 2f;

    private readonly FarmerApproachTracker approachTracker = new();

    private bool approachSubmitted;

    /// <summary>The rotation is on because enemies attacked us before the fight phase.</summary>
    private bool selfDefenceActive;

    private readonly MobFarmerModule module;

    public readonly IRotationPlugin RotationPlugin;

    private readonly Dictionary<string, Func<IModule, IRotationPlugin>> rotationPlugins = new()
    {
        { "WrathCombo", m => new Wrath(m) },
    };

    public readonly StateMachine<FarmerPhase, MobFarmerModule> StateMachine;

    public Farmer(MobFarmerModule module)
    {
        this.module = module;
        StateMachine = new StateMachine<FarmerPhase, MobFarmerModule>(FarmerPhase.Waiting, module);

        RotationPlugin = new BlankRotationPlugin();
        foreach (var (plugin, factory) in rotationPlugins)
        {
            if (!DalamudReflector.TryGetDalamudPlugin(plugin, out _, false, true))
            {
                continue;
            }

            RotationPlugin = factory(module);
            break;
        }
    }

    public void Update(UpdateContext context)
    {
        if (!context.IsForModule<MobFarmerModule>(out var module))
        {
            return;
        }


        if (!Running)
        {
            return;
        }

        Yields.Tick(module);
        if (Suspended)
        {
            return;
        }

        StateMachine.Update();
        UpdateSelfDefence();
    }

    /// <summary>
    /// Enemies attacking while the farmer gathers or stacks are fought back
    /// instead of ignored; Buffing hands over to Fighting itself. Ported
    /// from upstream BOCCHI 4.2. Movement stays with the farmer, so only the
    /// rotation is started, without auto-pull.
    /// </summary>
    private void UpdateSelfDefence()
    {
        var phase = StateMachine.State;
        var attacked = phase is FarmerPhase.Gathering or FarmerPhase.Stacking
                       && (module.Scanner.InCombat.Any() || Svc.Condition[ConditionFlag.InCombat]);
        if (attacked == selfDefenceActive)
        {
            return;
        }

        selfDefenceActive = attacked;
        if (attacked)
        {
            Svc.Log.Debug($"[MobFarmer] Under attack while {phase}; fighting back.");
            PromeRotationController.StartWithoutAutoPull();
            return;
        }

        // Fighting owns the rotation from here on.
        if (phase != FarmerPhase.Fighting)
        {
            PromeRotationController.Stop();
        }
    }

    private void StopSelfDefence()
    {
        if (!selfDefenceActive)
        {
            return;
        }

        selfDefenceActive = false;
        PromeRotationController.Stop();
    }

    public void Draw(RenderContext context)
    {
        if (!context.IsForModule<MobFarmerModule>(out var module))
        {
            return;
        }

        if (!module.Scanner.Mobs.Any())
        {
            return;
        }

        if (!Running && !module.Config.ShouldRenderDebugLinesWhileNotRunning)
        {
            return;
        }

        if (!module.Config.RenderDebugLines)
        {
            return;
        }

        foreach (var mob in module.Scanner.NotInCombat)
        {
            var color = new Vector4(0.9f, 0.1f, 0.1f, 1f);
            if (module.Config.IsSelectedForTerritory((Mob)mob.NameId, Svc.ClientState.TerritoryType))
            {
                color = new Vector4(0.9f, 0.1f, 0.9f, 1f);
            }

            context.DrawLine(mob.Position, color);
        }

        foreach (var mob in module.Scanner.InCombat)
        {
            context.DrawLine(mob.Position, new Vector4(0.1f, 0.9f, 0.1f, 1f));
        }
    }

    public void EnableFarmerMode()
    {
        if (Running)
            return;

        Running = true;
        selfDefenceActive = false;
        Suspended = false;
        YieldReason = FarmerYieldReason.None;
        NeedsApproach = false;
        approachSubmitted = false;
        approachTracker.Reset();
        LastTreasureSightAt = null;
        StateMachine.Reset();

        StartingPoint = Player.Position;
        Yields.Start(module);
        RotationPlugin.PhantomJobOff();
        PromeRotationController.Stop();
        if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
        {
            Chat.ExecuteCommand("/aepull off");
            Chat.ExecuteCommand("/aeTargetSelector off");
            Chat.ExecuteCommand("/occs 炮击 on");
            Chat.ExecuteCommand("/occs 神圣炮 off");
            Chat.ExecuteCommand("/occs 暗黑炮 on");
            Chat.ExecuteCommand("/occs 冲击炮 off");
            Chat.ExecuteCommand("/occs 老化炮 on");
        }
    }

    public void DisableFarmerMode()
    {
        var wasRunning = Running;
        Running = false;
        if (wasRunning)
        {
            // Stopping a Pots & Treasure run the farmer started stops the
            // automator, which calls back here; Running is already false then.
            Yields.Stop(module);
        }

        selfDefenceActive = false;
        PromeRotationController.Stop();
        BOCCHI.Pathfinding.AggroAvoidanceNavigation.Stop();
        Suspended = false;
        YieldReason = FarmerYieldReason.None;
        NeedsApproach = false;
        approachSubmitted = false;
        StartingPoint = Vector3.Zero;
        StateMachine.Reset();
    }

    public void SetSuspended(bool suspended, FarmerYieldReason reason = FarmerYieldReason.None)
    {
        if (!Running)
        {
            return;
        }

        StateMachine.Handlers[StateMachine.State].Exit();
        StopSelfDefence();
        ChainManager.Get("MobFarmer+Farmer").Abort();
        BOCCHI.Pathfinding.AggroAvoidanceNavigation.Stop();
        approachSubmitted = false;
        Suspended = suspended;
        YieldReason = suspended ? reason : FarmerYieldReason.None;
        StateMachine.Reset();

        if (!suspended && Player.DistanceTo(StartingPoint) > ApproachArriveRange)
        {
            NeedsApproach = true;
            approachTracker.Reset();
        }
    }

    /// <summary>Stops the walk back so a fight can take over; it restarts afterwards.</summary>
    public void PauseApproach()
    {
        if (!approachSubmitted)
        {
            return;
        }

        approachSubmitted = false;
        Plugin.Chain.Abort();
        BOCCHI.Pathfinding.AggroAvoidanceNavigation.Stop();
    }

    /// <summary>
    /// Walks back to the starting point after a yield. Returns true while the
    /// walk is still going.
    /// </summary>
    public bool TickApproach()
    {
        if (!NeedsApproach)
        {
            return false;
        }

        var distance = Player.DistanceTo(StartingPoint);
        if (distance <= ApproachArriveRange)
        {
            NeedsApproach = false;
            approachSubmitted = false;
            approachTracker.Reset();
            return false;
        }

        if (Plugin.Chain.IsRunning || Plugin.Chain.QueueCount > 0)
        {
            return true;
        }

        if (approachSubmitted)
        {
            approachSubmitted = false;
            if (approachTracker.RecordAttempt(distance))
            {
                Svc.Log.Info($"[MobFarmer] Could not get back to the starting point ({distance:0.0}y away); farming from here.");
                StartingPoint = Player.Position;
                NeedsApproach = false;
                approachTracker.Reset();
                return false;
            }
        }

        if (Svc.Condition[ConditionFlag.InCombat] || !EzThrottler.Throttle("MobFarmer.Approach", 1000))
        {
            return true;
        }

        var destination = StartingPoint;
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        Plugin.Chain.Submit(() =>
            Chain.Create("MobFarmer.Approach")
                .ConditionalThen(_ => Vector3.Distance(Player.Position, destination) > 15f, ChainHelper.MountChain())
                .Then(new PathfindAndMoveToChain(vnav, destination))
                .WaitUntilNear(vnav, destination, ApproachArriveRange)
                .Then(_ => vnav.Stop())
                .ConditionalThen(_ => Svc.Condition[ConditionFlag.Mounted], _ => Actions.TryUnmount())
        );
        approachSubmitted = true;
        return true;
    }

    public void Toggle()
    {
        if (!Running)
        {
            EnableFarmerMode();
        }
        else
        {
            DisableFarmerMode();
        }
    }

    public void Dispose()
    {
        PromeRotationController.Stop();
        RotationPlugin.Dispose();
    }
}
