using BOCCHI.Data;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.IPC;
using System;
using System.Linq;

namespace BOCCHI.Modules.Automator;

public sealed class InstanceRotationController
{
    public const string LeaveCommand = "/pdr leaveduty";
    public const string SouthHornEntryCommand = "/pdrfe ocs";
    public const string NorthHornEntryCommand = "/pdrfe ocn";

    private readonly InstanceRotationStateMachine stateMachine = new();
    private readonly InstancePopulationProvider populationProvider = new();

    public static bool IsTransitionActive { get; private set; }

    public InstanceRotationState State => stateMachine.State;

    public InstanceRotationReason Reason => stateMachine.Reason;

    public int? CurrentPopulation => populationProvider.CurrentPopulation;

    public DateTimeOffset? Deadline => stateMachine.Deadline;

    public DateTimeOffset? IslandEnteredAt => stateMachine.IslandEnteredAt;

    public string? FailureReason => stateMachine.FailureReason;

    public bool PostUpdate(AutomatorModule module)
    {
        var now = DateTimeOffset.UtcNow;
        var enabled = module.IsEnabled && module.Config.ShouldAutoRotateInstance;
        if (!enabled)
        {
            var shouldBlockCurrentFrame = stateMachine.IsBusy || stateMachine.State == InstanceRotationState.Failed;
            Reset();
            return shouldBlockCurrentFrame;
        }

        if (stateMachine.State == InstanceRotationState.Monitoring
            && module.Config.ShouldRotateWhenPopulationLow
            && ZoneData.IsInOccultCrescent())
        {
            populationProvider.Update(now, module.Config.MinimumInstancePopulation);
        }

        var input = new InstanceRotationInput(
            enabled,
            Svc.ClientState.TerritoryType,
            CanStart(module),
            TimeSpan.FromMinutes(module.Config.InstanceStayMinutes),
            module.Config.ShouldRotateWhenPopulationLow
            && populationProvider.IsConfirmedBelow(module.Config.MinimumInstancePopulation));
        var previousState = stateMachine.State;
        var action = stateMachine.Update(now, input);

        switch (action)
        {
            case InstanceRotationAction.RequestExit:
                if (!IsDailyRoutinesLoaded())
                {
                    stateMachine.Fail("daily_routines_unavailable");
                    break;
                }

                PrepareForTransition(module);
                Chat.ExecuteCommand(LeaveCommand);
                Svc.Chat.Print(module.T("messages.rotation.exit"));
                break;

            case InstanceRotationAction.EnterSouthHorn:
                SendEntryCommand(module, SouthHornEntryCommand);
                break;

            case InstanceRotationAction.EnterNorthHorn:
                SendEntryCommand(module, NorthHornEntryCommand);
                break;
        }

        if (previousState != InstanceRotationState.Failed && stateMachine.State == InstanceRotationState.Failed)
        {
            Svc.Chat.PrintError(module.T("messages.rotation.failed"));
        }

        IsTransitionActive = stateMachine.IsBusy
                             || (stateMachine.State == InstanceRotationState.Failed
                                 && !ZoneData.IsInOccultCrescent());
        return stateMachine.IsBusy || stateMachine.State == InstanceRotationState.Failed;
    }

    public void OnTerritoryChanged(uint territoryId)
    {
        populationProvider.Reset();

        if (!stateMachine.IsBusy)
        {
            if (!ZoneData.IsOccultCrescentTerritory(territoryId))
            {
                stateMachine.Reset();
            }

            IsTransitionActive = false;
            return;
        }

        var input = new InstanceRotationInput(true, territoryId, false, TimeSpan.MaxValue, false);
        stateMachine.Update(DateTimeOffset.UtcNow, input);
        IsTransitionActive = stateMachine.IsBusy;
    }

    public void Reset()
    {
        stateMachine.Reset();
        populationProvider.Reset();
        IsTransitionActive = false;
    }

    public string GetStateLabel(AutomatorModule module)
    {
        return module.T($"panel.rotation.states.{State.ToString().ToLowerInvariant()}");
    }

    public string GetRemainingLabel(AutomatorModule module)
    {
        var now = DateTimeOffset.UtcNow;
        TimeSpan? remaining = State switch
        {
            InstanceRotationState.Monitoring when IslandEnteredAt is { } enteredAt =>
                TimeSpan.FromMinutes(module.Config.InstanceStayMinutes) - (now - enteredAt),
            InstanceRotationState.WaitingForExit or InstanceRotationState.Cooldown
                or InstanceRotationState.WaitingForEntry when Deadline is { } deadline => deadline - now,
            _ => null,
        };

        if (remaining == null)
        {
            return "--";
        }

        var value = remaining.Value < TimeSpan.Zero ? TimeSpan.Zero : remaining.Value;
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }

    private static unsafe bool CanStart(AutomatorModule module)
    {
        if (!ZoneData.IsInOccultCrescent()
            || ZoneData.IsInForkedTower()
            || module.automator.Activity != null
            || ChainManager.Queues.Count > 0
            || module.GetModule<StateManagerModule>().GetState() != BOCCHI.Modules.StateManager.State.Idle
            || Svc.Condition[ConditionFlag.InCombat]
            || Svc.Condition[ConditionFlag.BetweenAreas]
            || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Svc.Condition[ConditionFlag.OccupiedInEvent]
            || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        {
            return false;
        }

        var player = Svc.Objects.LocalPlayer;
        if (player == null || player.CurrentHp == 0 || player.IsCasting || !player.IsTargetable)
        {
            return false;
        }

        var fateManager = FateManager.Instance();
        var eventContainer = DynamicEventContainer.GetInstance();
        return (fateManager == null || fateManager->GetCurrentFateId() == 0)
               && (eventContainer == null || eventContainer->CurrentEventId == 0);
    }

    private static bool IsDailyRoutinesLoaded()
    {
        return Svc.PluginInterface.InstalledPlugins.Any(plugin =>
            plugin.InternalName == "DailyRoutines" && plugin.IsLoaded);
    }

    private static void PrepareForTransition(AutomatorModule module)
    {
        Plugin.Chain.Abort();
        ChainManager.AbortAll();
        module.automator.Refresh();

        if (module.TryGetIPCSubscriber<VNavmesh>(out var navigation)
            && navigation != null
            && navigation.IsReady())
        {
            navigation.Stop();
        }

        if (module.Config.ShouldToggleAiProvider)
        {
            module.Config.AiProvider.Off();
        }

        PromeRotationController.Stop();

        if (Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "AEAssistV3" && plugin.IsLoaded))
        {
            Chat.ExecuteCommand("/aeTargetSelector off");
            Chat.ExecuteCommand("/aepull off");
        }
    }

    private void SendEntryCommand(AutomatorModule module, string command)
    {
        if (!IsDailyRoutinesLoaded())
        {
            stateMachine.Fail("daily_routines_unavailable");
            return;
        }

        Chat.ExecuteCommand(command);
        Svc.Chat.Print(module.T("messages.rotation.entry"));
    }
}
