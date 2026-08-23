using BOCCHI.Data;
using BOCCHI.Modules.StateManager;
using BOCCHI.Pathfinding;
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

public static class InstanceEntryConfirmationPolicy
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    public static bool CanAttempt(
        InstanceRotationState state,
        DateTimeOffset? commandDispatchedAt,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset now)
    {
        return state == InstanceRotationState.WaitingForEntry
               && commandDispatchedAt is { } dispatchedAt
               && now >= dispatchedAt
               && now - dispatchedAt <= InstanceRotationStateMachine.EntryTimeout
               && now >= nextAttemptAt;
    }
}

public sealed class InstanceRotationController
{
    public const string LeaveCommand = "/pdr leaveduty";
    public const string SouthHornEntryCommand = "/pdrfe ocs";
    public const string NorthHornEntryCommand = "/pdrfe ocn";

    private readonly InstanceRotationStateMachine stateMachine = new();
    private readonly InstancePopulationProvider populationProvider = new();
    private readonly InstanceDutyTimerProvider dutyTimerProvider = new();
    private string? pendingEntryCommand;
    private string? pendingEntryMessageKey;
    private bool pendingExitCommand;
    private bool dailyRoutinesEnableNoticeShown;
    private DateTimeOffset nextDailyRoutinesModuleCheck;
    private DateTimeOffset? entryCommandDispatchedAt;
    private DateTimeOffset nextEntryConfirmationAttemptAt;
    private int entryConfirmationAttempts;

    public static bool IsTransitionActive { get; private set; }

    public InstanceRotationState State => stateMachine.State;

    public InstanceRotationReason Reason => stateMachine.Reason;

    public int? CurrentPopulation => populationProvider.CurrentPopulation;

    public TimeSpan? CurrentInstanceTimeRemaining => dutyTimerProvider.CurrentRemaining;

    public DateTimeOffset? Deadline => stateMachine.Deadline;

    public DateTimeOffset? IslandEnteredAt => stateMachine.IslandEnteredAt;

    public string? FailureReason => stateMachine.FailureReason;

    public bool PostUpdate(AutomatorModule module)
    {
        var now = DateTimeOffset.UtcNow;
        var enabled = module.IsEnabled
                      && (module.Config.ShouldAutoRotateInstance
                          || stateMachine.IsBusy
                          || stateMachine.State == InstanceRotationState.Failed);
        if (!enabled)
        {
            var shouldBlockCurrentFrame = stateMachine.IsBusy || stateMachine.State == InstanceRotationState.Failed;
            Reset();
            return shouldBlockCurrentFrame;
        }

        var previousState = stateMachine.State;
        if (pendingExitCommand)
        {
            TryDispatchPendingExit(module);
        }

        if (pendingEntryCommand != null)
        {
            TryDispatchPendingEntry(module);
        }

        if (ZoneData.IsInOccultCrescent())
        {
            dutyTimerProvider.Update();
        }
        else
        {
            dutyTimerProvider.Reset();
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
            && populationProvider.IsConfirmedBelow(module.Config.MinimumInstancePopulation),
            dutyTimerProvider.CurrentRemaining);
        var action = stateMachine.Update(now, input);

        switch (action)
        {
            case InstanceRotationAction.RequestExit:
                if (!IsDailyRoutinesLoaded())
                {
                    stateMachine.Fail("daily_routines_unavailable");
                    break;
                }

                pendingExitCommand = true;
                TryDispatchPendingExit(module);
                break;

            case InstanceRotationAction.EnterSouthHorn:
                QueueEntryCommand(module, SouthHornEntryCommand, "messages.rotation.entry");
                break;

            case InstanceRotationAction.EnterNorthHorn:
                QueueEntryCommand(module, NorthHornEntryCommand, "messages.rotation.entry");
                break;
        }

        if (previousState != InstanceRotationState.Failed && stateMachine.State == InstanceRotationState.Failed)
        {
            pendingExitCommand = false;
            pendingEntryCommand = null;
            pendingEntryMessageKey = null;
            ResetEntryConfirmation();
            Svc.Chat.PrintError(module.T("messages.rotation.failed"));
        }

        IsTransitionActive = stateMachine.IsBusy
                             || (stateMachine.State == InstanceRotationState.Failed
                                 && !ZoneData.IsInOccultCrescent());
        return stateMachine.IsBusy || stateMachine.State == InstanceRotationState.Failed;
    }

    public bool TryStartFromOutside(AutomatorModule module)
    {
        if (ZoneData.IsInOccultCrescent())
        {
            return true;
        }

        if (stateMachine.IsBusy)
        {
            return true;
        }

        // A failed test entry must be retryable from the settings button without
        // requiring the user to toggle the whole automation mode.
        if (stateMachine.State == InstanceRotationState.Failed)
        {
            Reset();
        }

        if (!IsDailyRoutinesLoaded())
        {
            Svc.Chat.PrintError(module.T("messages.rotation.entry_unavailable"));
            return false;
        }

        var targetTerritoryId = module.Config.InitialInstanceArea.ToTerritoryId();
        var action = stateMachine.BeginEntryFromOutside(DateTimeOffset.UtcNow, targetTerritoryId);
        var command = action switch
        {
            InstanceRotationAction.EnterSouthHorn => SouthHornEntryCommand,
            InstanceRotationAction.EnterNorthHorn => NorthHornEntryCommand,
            _ => null,
        };

        if (command == null)
        {
            Reset();
            Svc.Chat.PrintError(module.T("messages.rotation.entry_unavailable"));
            return false;
        }

        pendingEntryCommand = command;
        pendingEntryMessageKey = "messages.rotation.initial_entry";
        IsTransitionActive = true;
        TryDispatchPendingEntry(module);
        return stateMachine.State != InstanceRotationState.Failed;
    }

    public DailyRoutinesModuleStatus EnsureDailyRoutinesCommandModules(AutomatorModule module)
    {
        var status = DailyRoutinesModuleBridge.EnsureRequiredModules();
        if (status == DailyRoutinesModuleStatus.Enabling && !dailyRoutinesEnableNoticeShown)
        {
            dailyRoutinesEnableNoticeShown = true;
            Svc.Chat.Print(module.T("messages.rotation.modules_enabling"));
        }
        else if (status == DailyRoutinesModuleStatus.Ready)
        {
            dailyRoutinesEnableNoticeShown = false;
        }

        return status;
    }

    public void PumpPendingEntryFromUi(AutomatorModule module)
    {
        if (pendingEntryCommand != null)
        {
            TryDispatchPendingEntry(module);
        }
    }

    public bool TryReserveEntryConfirmationAttempt()
    {
        var now = DateTimeOffset.UtcNow;
        if (!InstanceEntryConfirmationPolicy.CanAttempt(
                stateMachine.State,
                entryCommandDispatchedAt,
                nextEntryConfirmationAttemptAt,
                now))
        {
            return false;
        }

        nextEntryConfirmationAttemptAt = now + InstanceEntryConfirmationPolicy.RetryInterval;
        entryConfirmationAttempts++;
        return true;
    }

    public void RecordEntryConfirmationClick()
    {
        Svc.Log.Info(
            $"Instance rotation clicked ContentsFinderConfirm Commence " +
            $"(attempt={entryConfirmationAttempts}, targetTerritory={stateMachine.OriginalTerritoryId}).");
    }

    public void PollDailyRoutinesCommandModules(AutomatorModule module)
    {
        if (!module.Config.ShouldAutoRotateInstance
            && !stateMachine.IsBusy
            && !pendingExitCommand
            && pendingEntryCommand == null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < nextDailyRoutinesModuleCheck)
        {
            return;
        }

        var status = EnsureDailyRoutinesCommandModules(module);
        nextDailyRoutinesModuleCheck = now + (status == DailyRoutinesModuleStatus.Ready
            ? TimeSpan.FromSeconds(5)
            : TimeSpan.FromMilliseconds(250));
    }

    public void OnTerritoryChanged(uint territoryId)
    {
        populationProvider.Reset();
        dutyTimerProvider.Reset();

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
        if (stateMachine.State != InstanceRotationState.WaitingForEntry)
        {
            pendingEntryCommand = null;
            pendingEntryMessageKey = null;
            ResetEntryConfirmation();
        }
        if (stateMachine.State != InstanceRotationState.WaitingForExit)
        {
            pendingExitCommand = false;
        }
        IsTransitionActive = stateMachine.IsBusy;
    }

    public void Reset()
    {
        stateMachine.Reset();
        populationProvider.Reset();
        dutyTimerProvider.Reset();
        pendingExitCommand = false;
        pendingEntryCommand = null;
        pendingEntryMessageKey = null;
        ResetEntryConfirmation();
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
            InstanceRotationState.Monitoring => dutyTimerProvider.CurrentRemaining,
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
            try
            {
                AggroAvoidanceNavigation.Stop(navigation);
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Instance rotation could not stop vnavmesh because its IPC became unavailable.");
            }
        }

        if (module.Config.ShouldToggleAiProvider)
        {
            module.SetAiProviderEnabled(false);
        }

        PromeRotationController.Stop();

        if (Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "AEAssistV3" && plugin.IsLoaded))
        {
            Chat.ExecuteCommand("/aeTargetSelector off");
            Chat.ExecuteCommand("/aepull off");
        }
    }

    private void QueueEntryCommand(AutomatorModule module, string command, string messageKey)
    {
        pendingEntryCommand = command;
        pendingEntryMessageKey = messageKey;
        TryDispatchPendingEntry(module);
    }

    private bool TryDispatchPendingExit(AutomatorModule module)
    {
        if (!pendingExitCommand)
        {
            return true;
        }

        var status = EnsureDailyRoutinesCommandModules(module);
        if (status == DailyRoutinesModuleStatus.Enabling)
        {
            return false;
        }

        if (status == DailyRoutinesModuleStatus.Unavailable)
        {
            pendingExitCommand = false;
            stateMachine.Fail("daily_routines_unavailable");
            return false;
        }

        pendingExitCommand = false;
        PrepareForTransition(module);
        Chat.ExecuteCommand(LeaveCommand);
        Svc.Chat.Print(module.T("messages.rotation.exit"));
        return true;
    }

    private bool TryDispatchPendingEntry(AutomatorModule module)
    {
        if (pendingEntryCommand == null)
        {
            return true;
        }

        var status = EnsureDailyRoutinesCommandModules(module);
        if (status == DailyRoutinesModuleStatus.Enabling)
        {
            return false;
        }

        if (status == DailyRoutinesModuleStatus.Unavailable)
        {
            stateMachine.Fail("daily_routines_unavailable");
            pendingEntryCommand = null;
            pendingEntryMessageKey = null;
            Svc.Chat.PrintError(module.T("messages.rotation.entry_unavailable"));
            return false;
        }

        var command = pendingEntryCommand;
        var messageKey = pendingEntryMessageKey ?? "messages.rotation.entry";
        pendingEntryCommand = null;
        pendingEntryMessageKey = null;
        Chat.ExecuteCommand(command);
        entryCommandDispatchedAt = DateTimeOffset.UtcNow;
        nextEntryConfirmationAttemptAt = entryCommandDispatchedAt.Value;
        entryConfirmationAttempts = 0;
        Svc.Chat.Print(module.T(messageKey));
        return true;
    }

    private void ResetEntryConfirmation()
    {
        entryCommandDispatchedAt = null;
        nextEntryConfirmationAttemptAt = default;
        entryConfirmationAttempts = 0;
    }
}
