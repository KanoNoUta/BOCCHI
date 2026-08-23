using ECommons.Automation;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.Treasure;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using BOCCHI.Pathfinding;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot;
using Ocelot.Chain;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using BocchiActions = BOCCHI.ActionHelpers.Actions;

namespace BOCCHI.Modules.Automator;

[OcelotModule(int.MaxValue - 1)]
public class AutomatorModule : Module
{
    private const long DeathReturnRetryMs = 10_000;

    private bool vnavmeshFailureReported;

    private readonly AutomatorRunStateMachine runState = new();

    private bool disableAiProviderOnStop;

    private bool startSideEffectsPrepared;

    private int stopDrainAttempts;

    private bool movementProvidersStopped;

    private readonly DeathReturnTracker deathReturnTracker = new();

    private bool deathAutomationStopped;

    private int deathStopDrainAttempts;

    private bool deathMovementProvidersStopped;

    private bool deathReturnPending;

    private long deathReturnConfirmationDeadlineMs;

    public override AutomatorConfig Config
    {
        get => PluginConfig.AutomatorConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public readonly Automator automator = new();

    public readonly InstanceRotationController instanceRotation = new();

    public readonly Panel panel = new();

    public readonly Random random = new();

    public AutomatorRunState RunState => runState.State;

    public string? RunStateDetail => runState.Detail;

    public bool RequestedEnabled => runState.TargetEnabled;

    public bool IsIndependentNavigationRunning
    {
        get
        {
            var treasureRunning = TryGetModule<TreasureModule>(out var treasure)
                                  && treasure?.IsHuntRunning == true;
            var carrotsRunning = TryGetModule<CarrotsModule>(out var carrots)
                                 && carrots?.IsHuntRunning == true;
            var mobFarmerRunning = TryGetModule<MobFarmerModule>(out var mobFarmer)
                                   && mobFarmer?.Farmer.Running == true;
            return treasureRunning || carrotsRunning || mobFarmerRunning;
        }
    }

    public AutomatorModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        config.AutomatorConfig.Enabled = false;
        config.Save();

        Svc.AddonLifecycle.RegisterListener(
            AddonEvent.PostDraw,
            "ContentsFinderConfirm",
            OnContentsFinderConfirmPostDraw);
        Svc.AddonLifecycle.RegisterListener(
            AddonEvent.PostDraw,
            "SelectYesno",
            OnDeathReturnSelectYesnoPostDraw);
    }

    public override void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(
            AddonEvent.PostDraw,
            "ContentsFinderConfirm",
            OnContentsFinderConfirmPostDraw);
        Svc.AddonLifecycle.UnregisterListener(
            AddonEvent.PostDraw,
            "SelectYesno",
            OnDeathReturnSelectYesnoPostDraw);
    }


    public override void PostUpdate(UpdateContext context)
    {
        if (HandleDeathReturn())
        {
            return;
        }

        if (!Config.Enabled)
        {
            return;
        }

        if (IsIndependentNavigationRunning)
        {
            automator.SuspendForIndependentNavigation("independent navigation");
            return;
        }

        if (!EnsureVnavmeshAvailable())
        {
            return;
        }

        if (RunState == AutomatorRunState.Starting && !AdvanceStartRequest())
        {
            return;
        }

        if (!runState.CanRunWork)
        {
            return;
        }

        instanceRotation.PollDailyRoutinesCommandModules(this);
        if (instanceRotation.PostUpdate(this))
        {
            return;
        }

        automator.PostUpdate(this, context.Framework);
    }


    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        ResetDeathReturn();
        var preservingPostActivityReturn = automator.IsPostActivityReturnPending;

        if (preservingPostActivityReturn)
        {
            // Lifestream can expose an intermediate territory while Return is
            // still resolving. Keep the nested return chain alive until it
            // confirms the original Occult Crescent territory and landing.
            Svc.Log.Info($"Preserving post-activity return across territory change to {id}.");
            return;
        }

        // Navigation and submitted activity chains are territory-bound. Always
        // terminate them before refreshing, including South Horn <-> North Horn.
        Plugin.Chain.Abort();
        AggroAvoidanceNavigation.Stop();
        if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null)
        {
            TryStopStep(() => AggroAvoidanceNavigation.Stop(navigation), "stop vnavmesh after territory change");
        }
        SetAiProviderEnabled(false);
        PromeRotationController.Stop();

        automator.Refresh();
        instanceRotation.OnTerritoryChanged(id);

        if (BOCCHI.Data.ZoneData.IsOccultCrescentTerritory(id))
        {
            return;
        }

        if (InstanceRotationController.IsTransitionActive)
        {
            return;
        }

        RequestEnabled(false);
    }

    public static void ToggleIllegalMode(OcelotPlugin plugin)
    {
        var module = plugin.Modules.GetModule<AutomatorModule>();
        module.RequestEnabled(!module.RequestedEnabled);

        if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
        {
            Chat.ExecuteCommand("/aeTargetSelector off");
        }
    }

    public void RequestEnabled(bool enabled)
    {
        var action = runState.RequestEnabled(enabled);
        switch (action)
        {
            case AutomatorRunAction.BeginStart:
                BeginStartRequest();
                break;
            case AutomatorRunAction.BeginStop:
                BeginStopRequest();
                break;
        }
    }

    public void EnableIllegalMode()
    {
        RequestEnabled(true);
    }

    public void DisableIllegalMode()
    {
        RequestEnabled(false);
    }

    private void BeginStartRequest()
    {
        var vnavmesh = GetVnavmeshAvailability();
        var readiness = AutomatorStartPolicy.Evaluate(
            vnavmesh.IsAvailable,
            IsPluginLoaded("Lifestream"));
        if (readiness != AutomatorStartReadiness.Ready)
        {
            var reason = readiness == AutomatorStartReadiness.VnavmeshUnavailable
                ? vnavmesh.Status == VnavmeshAvailabilityStatus.Missing
                    ? T("run_state.vnavmesh_missing")
                    : T("run_state.vnavmesh_not_loaded")
                : T("run_state.lifestream_not_loaded");
            FailStartRequest(reason, cleanupRuntime: false);
            if (readiness == AutomatorStartReadiness.VnavmeshUnavailable)
            {
                ReportVnavmeshFailure(vnavmesh);
            }
            else
            {
                var message = string.Format(T("messages.start_failed"), reason);
                Svc.Log.Warning(message);
                Svc.Chat.PrintError(message);
            }
            return;
        }

        vnavmeshFailureReported = false;
        Config.Enabled = true;
        startSideEffectsPrepared = false;
        runState.SetStartingDetail(BOCCHI.Data.ZoneData.IsInOccultCrescent()
            ? T("run_state.waiting_dependencies")
            : T("run_state.preparing_entry"));
    }

    private bool AdvanceStartRequest()
    {
        if (!TryGetIPCSubscriber<VNavmesh>(out var navigation)
            || navigation == null
            || !navigation.IsReady())
        {
            runState.SetStartingDetail(T("run_state.waiting_vnavmesh"));
            return false;
        }

        if (!TryGetIPCSubscriber<Lifestream>(out var lifestream)
            || lifestream == null
            || !lifestream.IsReady())
        {
            runState.SetStartingDetail(T("run_state.waiting_lifestream"));
            return false;
        }

        if (!startSideEffectsPrepared)
        {
            if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
            {
                SetAiProviderEnabled(false);
            }
            PromeRotationController.Stop();
            startSideEffectsPrepared = true;
        }

        if (!BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            runState.SetStartingDetail(T("run_state.preparing_entry"));
            if (!instanceRotation.TryStartFromOutside(this))
            {
                FailStartRequest(T("run_state.entry_request_failed"));
                return false;
            }

            instanceRotation.PollDailyRoutinesCommandModules(this);
            instanceRotation.PostUpdate(this);
            if (instanceRotation.State == InstanceRotationState.Failed)
            {
                FailStartRequest(instanceRotation.FailureReason ?? T("run_state.entry_preparation_failed"));
            }
            return false;
        }

        runState.CompleteStart();
        Svc.Chat.Print(T("messages.on"));
        return true;
    }

    private void FailStartRequest(string reason, bool cleanupRuntime = true)
    {
        Config.Enabled = false;
        if (cleanupRuntime)
        {
            instanceRotation.Reset();
            automator.Refresh();
            PromeRotationController.Stop();
        }
        startSideEffectsPrepared = false;
        runState.FailStart(reason);
        PluginConfig.Save();
    }

    private void BeginStopRequest()
    {
        ResetDeathReturn();
        disableAiProviderOnStop = Config.ShouldToggleAiProvider;
        Config.Enabled = false;
        startSideEffectsPrepared = false;
        stopDrainAttempts = 0;
        movementProvidersStopped = false;
        Svc.Log.Info("Automation stop requested; cancelling local work immediately.");
        StopLocalAutomation();
        movementProvidersStopped = TryStopMovementProviders();
        _ = Svc.Framework.RunOnTick(CompleteStopRequest);
    }

    private void StopLocalAutomation()
    {
        AggroAvoidanceNavigation.Stop();
        TryStopStep(instanceRotation.Reset, "reset instance rotation");
        TryStopStep(automator.Refresh, "refresh the automator");

        if (TryGetModule<TreasureModule>(out var treasure) && treasure != null)
        {
            TryStopStep(treasure.StopHunt, "stop treasure hunting");
        }
        if (TryGetModule<CarrotsModule>(out var carrots) && carrots != null)
        {
            TryStopStep(carrots.StopHunt, "stop carrot hunting");
        }
        if (TryGetModule<MobFarmerModule>(out var mobFarmer) && mobFarmer != null)
        {
            TryStopStep(mobFarmer.Farmer.DisableFarmerMode, "stop mob farming");
        }
        if (TryGetModule<BuffModule>(out var buffs) && buffs != null)
        {
            TryStopStep(buffs.BuffManager.CancelPending, "cancel pending buffs");
        }

        TryStopStep(PromeRotationController.Stop, "stop rotation");
        TryStopStep(Plugin.Chain.Abort, "abort the plugin chain");
        TryStopStep(ChainManager.AbortAll, "abort automation queues");
        TryStopStep(() => Svc.Targets.Target = null, "clear the current target");
    }

    private bool HandleDeathReturn()
    {
        var nowMs = Environment.TickCount64;
        var eligible = Config.Enabled
                       && runState.CanRunWork
                       && Config.ShouldAutoReturnAfterDeath
                       && BOCCHI.Data.ZoneData.IsInOccultCrescent();
        var decision = deathReturnTracker.Update(
            eligible,
            Player.IsDead,
            nowMs,
            DeathReturnPolicy.GetTimeoutMs(Config.DeathReturnMinutes),
            DeathReturnRetryMs);

        if (decision == DeathReturnDecision.Reset)
        {
            ResetDeathReturn();
            return false;
        }

        if (!deathAutomationStopped)
        {
            StopLocalAutomationForDeath();
            deathAutomationStopped = true;
        }
        else
        {
            DrainMovementProvidersForDeath();
        }

        if (decision == DeathReturnDecision.Trigger)
        {
            deathReturnPending = true;
            deathReturnConfirmationDeadlineMs = nowMs + DeathReturnRetryMs;
            try
            {
                BocchiActions.Return.Cast();
                Svc.Log.Info(
                    $"Player remained dead for {Config.DeathReturnMinutes} minute(s); requesting Return.");
            }
            catch (Exception exception)
            {
                deathReturnPending = false;
                Svc.Log.Warning(exception, "Could not request Return for the dead player; the request will retry.");
            }
        }

        return true;
    }

    private void StopLocalAutomationForDeath()
    {
        Svc.Log.Info("Player is dead; suspending local automation while waiting for resurrection or timed Return.");
        StopLocalAutomation();
        TryStopStep(() => SetAiProviderEnabled(false), "release the AI provider while dead");
        DrainMovementProvidersForDeath();
    }

    private void DrainMovementProvidersForDeath()
    {
        if (!AutomatorStopPolicy.ShouldRetry(deathMovementProvidersStopped, deathStopDrainAttempts))
        {
            return;
        }

        deathStopDrainAttempts++;
        deathMovementProvidersStopped = TryStopMovementProviders(deathStopDrainAttempts);
        if (!deathMovementProvidersStopped && deathStopDrainAttempts == AutomatorStopPolicy.MaxAttempts)
        {
            Svc.Log.Warning(
                "Death recovery exhausted movement-provider drain attempts; local tasks remain cancelled.");
        }
    }

    private void ResetDeathReturn()
    {
        deathReturnTracker.Reset();
        deathAutomationStopped = false;
        deathStopDrainAttempts = 0;
        deathMovementProvidersStopped = false;
        deathReturnPending = false;
        deathReturnConfirmationDeadlineMs = 0;
    }

    private void CompleteStopRequest()
    {
        if (!movementProvidersStopped)
        {
            stopDrainAttempts++;
            movementProvidersStopped = TryStopMovementProviders(stopDrainAttempts);
            if (AutomatorStopPolicy.ShouldRetry(movementProvidersStopped, stopDrainAttempts))
            {
                _ = Svc.Framework.RunOnTick(CompleteStopRequest);
                return;
            }

            if (!movementProvidersStopped)
            {
                Svc.Log.Warning(
                    $"Automation stop exhausted {AutomatorStopPolicy.MaxAttempts} IPC drain attempts; "
                    + "local work remains cancelled.");
            }
        }

        try
        {
            if (disableAiProviderOnStop)
            {
                TryStopStep(() => Config.AiProvider.Off(), "release the AI provider");
            }

            if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
            {
                TryStopStep(() => Chat.ExecuteCommand("/aeTargetSelector off"), "disable AE target selection");
                TryStopStep(() => Chat.ExecuteCommand("/aepull off"), "disable AE pulling");
            }

            PluginConfig.Save();
        }
        finally
        {
            disableAiProviderOnStop = false;
            stopDrainAttempts = 0;
            movementProvidersStopped = false;
            runState.CompleteStop();
            Svc.Log.Info("Automation stop completed.");
            Svc.Chat.Print(T("messages.off"));
        }
    }

    public void RequestStopAll()
    {
        if (runState.RequestStopAll() == AutomatorRunAction.BeginStop)
        {
            BeginStopRequest();
        }
    }

    public void PrepareForIndependentNavigation(string owner)
    {
        Svc.Log.Info($"{owner} is taking navigation ownership; cancelling automatic activity and return work.");
        automator.SuspendForIndependentNavigation(owner);
        TryStopStep(instanceRotation.Reset, "reset instance rotation for independent navigation");
        TryStopStep(() => { AggroAvoidanceNavigation.Stop(); }, "stop automatic pathfinding for independent navigation");
        TryStopStep(PromeRotationController.Stop, "stop automatic combat for independent navigation");
        TryStopStep(() => SetAiProviderEnabled(false), "release the AI provider for independent navigation");
        TryStopStep(Plugin.Chain.Abort, "abort the automatic chain for independent navigation");
        TryStopStep(ChainManager.AbortAll, "abort automatic queues for independent navigation");
        _ = TryStopMovementProviders();
    }

    private bool TryStopMovementProviders(int completedAttempts = 0)
    {
        var navigationStopped = !IsPluginLoaded(VnavmeshAvailabilityPolicy.PluginInternalName);
        if (!navigationStopped)
        {
            try
            {
                if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null)
                {
                    AggroAvoidanceNavigation.Stop(navigation);
                    navigationStopped = true;
                }
            }
            catch (Exception exception)
            {
                LogStopDrainFailure(exception, "vnavmesh", completedAttempts);
            }
        }

        var lifestreamStopped = !IsPluginLoaded("Lifestream");
        if (!lifestreamStopped)
        {
            try
            {
                if (TryGetIPCSubscriber<Lifestream>(out var lifestream) && lifestream != null)
                {
                    lifestream.Abort();
                    lifestreamStopped = true;
                }
            }
            catch (Exception exception)
            {
                LogStopDrainFailure(exception, "Lifestream", completedAttempts);
            }
        }

        return navigationStopped && lifestreamStopped;
    }

    private static void LogStopDrainFailure(Exception exception, string provider, int completedAttempts)
    {
        if (completedAttempts is 0 or 1 || completedAttempts == AutomatorStopPolicy.MaxAttempts)
        {
            Svc.Log.Warning(exception, $"Automation stop is waiting for {provider} IPC to become available.");
        }
    }

    private static void TryStopStep(Action action, string description)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, $"Could not {description} while stopping automation.");
        }
    }

    public VnavmeshAvailabilityCheck GetVnavmeshAvailability()
    {
        var plugin = Svc.PluginInterface.InstalledPlugins.FirstOrDefault(p =>
            string.Equals(p.InternalName, VnavmeshAvailabilityPolicy.PluginInternalName, StringComparison.OrdinalIgnoreCase));

        return VnavmeshAvailabilityPolicy.Evaluate(
            plugin != null,
            plugin?.IsLoaded == true,
            plugin?.Version);
    }

    private bool EnsureVnavmeshAvailable()
    {
        var check = GetVnavmeshAvailability();
        if (check.IsAvailable)
        {
            vnavmeshFailureReported = false;
            return true;
        }

        if (Config.Enabled)
        {
            RequestEnabled(false);
        }

        ReportVnavmeshFailure(check);
        return false;
    }

    private static bool IsPluginLoaded(string internalName)
    {
        return Svc.PluginInterface.InstalledPlugins.Any(plugin =>
            string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase)
            && plugin.IsLoaded);
    }

    private void ReportVnavmeshFailure(VnavmeshAvailabilityCheck check)
    {
        if (vnavmeshFailureReported)
        {
            return;
        }

        var reason = check.Status switch
        {
            VnavmeshAvailabilityStatus.Missing => T("run_state.vnavmesh_missing"),
            VnavmeshAvailabilityStatus.NotLoaded => T("run_state.vnavmesh_not_loaded"),
            _ => T("run_state.vnavmesh_unknown"),
        };
        var message = string.Format(T("messages.automation_disabled"), reason);
        Svc.Log.Warning(message);
        Svc.Chat.PrintError(message);
        vnavmeshFailureReported = true;
    }

    public void SetAiProviderEnabled(bool enabled)
    {
        if (!Config.ShouldToggleAiProvider)
        {
            return;
        }

        if (enabled)
        {
            Config.AiProvider.On();
        }
        else
        {
            Config.AiProvider.Off();
        }
    }

    private unsafe void OnContentsFinderConfirmPostDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonContentsFinderConfirm*)args.Addon.Address;
        if (addon == null
            || !addon->AtkUnitBase.IsVisible
            || addon->CommenceButton == null
            || !addon->CommenceButton->IsEnabled
            || addon->CommenceButton->AtkResNode == null
            || !addon->CommenceButton->AtkResNode->IsVisible()
            || !instanceRotation.TryReserveEntryConfirmationAttempt())
        {
            return;
        }

        addon->AtkUnitBase.FireCallbackInt(8);
        instanceRotation.RecordEntryConfirmationClick();
    }

    private unsafe void OnDeathReturnSelectYesnoPostDraw(AddonEvent type, AddonArgs args)
    {
        if (!(deathReturnPending && Player.IsDead)
            || Environment.TickCount64 > deathReturnConfirmationDeadlineMs
            || !Config.Enabled
            || !Config.ShouldAutoReturnAfterDeath
            || !BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            return;
        }

        var addon = (AddonSelectYesno*)args.Addon.Address;
        if (addon == null
            || !addon->AtkUnitBase.IsVisible
            || addon->YesButton == null
            || !addon->YesButton->IsEnabled
            || addon->YesButton->AtkResNode == null
            || !addon->YesButton->AtkResNode->IsVisible())
        {
            return;
        }

        addon->AtkUnitBase.FireCallbackInt(0);
        deathReturnPending = false;
        Svc.Log.Info("Confirmed the timed dead-player Return request.");
    }
}
