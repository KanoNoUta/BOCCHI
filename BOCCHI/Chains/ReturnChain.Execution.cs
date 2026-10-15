using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot.Chain;
using Ocelot.IPC;
using System;

namespace BOCCHI.Chains;

public partial class ReturnChain
{
    private const int WaitDiagnosticIntervalMs = 10_000;

    private Chain? executionChain;
    private ReturnCastTracker? returnTracker;
    private uint returnTerritory;
    private long nextPreparationAtMs;
    private bool movementPrepared;
    private bool returnCompleted;

    public bool ShouldPreserveTransition(uint territory) => executionChain != null
        && !executionChain.IsMainComplete()
        && returnTracker is { HasAcceptedRequest: true, IsFinished: false }
        && !returnTracker.HasTimedOut(Environment.TickCount64)
        && config.StopCheck?.Invoke() != true
        && !Player.IsDead
        && (territory == returnTerritory || territory == 0
            || Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]);

    private unsafe bool PollReturn()
    {
        if (config.StopCheck?.Invoke() == true || Player.IsDead)
        {
            FailureReason = "Return cancelled because automation stopped or the player died.";
            return true;
        }

        var now = Environment.TickCount64;
        var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas]
                           || Svc.Condition[ConditionFlag.BetweenAreas51];
        var inSourceTerritory = Svc.ClientState.TerritoryType == returnTerritory;
        var player = Svc.Objects.LocalPlayer;
        var targetable = player?.IsTargetable == true;
        var atLanding = !betweenAreas && targetable && IsAtReturnLanding(returnTerritory);
        returnTracker ??= new ReturnCastTracker(now, atLanding);
        module.ActiveReturn = this;

        if (!betweenAreas && Svc.ClientState.TerritoryType != 0
            && (!inSourceTerritory || ZoneData.IsInForkedTower()))
        {
            FailureReason = $"Return cancelled after leaving the source island (expected={returnTerritory}, current={Svc.ClientState.TerritoryType}).";
            Svc.Log.Warning(FailureReason);
            return true;
        }

        var casting = player?.IsCasting == true;
        var dialog = Svc.GameGui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        var dialogOpen = dialog != null && dialog->AtkUnitBase.IsVisible;
        var manager = ActionManager.Instance();
        var actionStatus = manager != null && player != null && inSourceTerritory && !betweenAreas
            ? manager->GetActionStatus(ActionType.GeneralAction, 8)
            : uint.MaxValue;

        if (inSourceTerritory && player != null && !betweenAreas && !casting
            && !returnTracker.SawTransition && now >= nextPreparationAtMs)
        {
            nextPreparationAtMs = now + 1_000;
            movementPrepared = TryStopReturnMovement();
            Actions.TryUnmount();
        }

        var moving = Player.IsMoving;
        var mounted = Svc.Condition[ConditionFlag.Mounted];
        var inCombat = Svc.Condition[ConditionFlag.InCombat];
        var canRequest = inSourceTerritory && targetable && movementPrepared
                         && !dialogOpen && !moving && !mounted && !inCombat && actionStatus == 0;
        var decision = returnTracker.Update(now, betweenAreas, casting, atLanding, canRequest);

        if (decision == ReturnCastDecision.Complete)
        {
            returnCompleted = true;
            Svc.Log.Info(returnTracker.Attempts == 0
                ? "Return landing reached at base camp without a new request (a Return was already in flight)."
                : $"Return landed at base camp after {returnTracker.Attempts} request(s).");
            return true;
        }

        if (decision == ReturnCastDecision.Failed)
        {
            FailureReason = $"Return failed: requests={returnTracker.Attempts}, actionStatus={actionStatus}, "
                + $"cast={returnTracker.SawCast}, transition={returnTracker.SawTransition}, "
                + $"dialog={dialogOpen}, combat={inCombat}, moving={moving}, mounted={mounted}, "
                + $"territory={Svc.ClientState.TerritoryType}, expected={returnTerritory}.";
            Svc.Log.Warning(FailureReason);
            return true;
        }

        // A confirmation that opens after a BOCCHI Return request belongs to
        // it, whatever UseAction reported. Requests are only made while no
        // dialog is open, so a pre-existing unrelated dialog is never clicked;
        // a Return confirmation left open by an earlier BOCCHI attempt is
        // recognised through the ledger instead of blocking every retry.
        if (inSourceTerritory && !betweenAreas && !casting && dialogOpen
            && (returnTracker.CanConfirm(now)
                || ReturnRequestLedger.RequestedWithin(now, ReturnRequestLedger.StaleConfirmationWindowMs))
            && !ReturnRequestLedger.ConfirmedWithin(now, ReturnRequestLedger.ConfirmationCooldownMs)
            && IsReturnConfirmationReady(dialog))
        {
            returnTracker.RecordConfirmation(now);
            ReturnRequestLedger.RecordConfirmation(now);
            dialog->AtkUnitBase.FireCallbackInt(0);
            Svc.Log.Info("Confirmed BOCCHI Return after the confirmation button became ready.");
        }
        else if (decision == ReturnCastDecision.Wait && !canRequest && !casting && !betweenAreas
                 && !returnTracker.IsAwaitingResponse(now)
                 && EzThrottler.Throttle("ReturnChain.WaitDiagnostics", WaitDiagnosticIntervalMs))
        {
            Svc.Log.Info(
                $"Return is waiting before its next request: requests={returnTracker.Attempts}, "
                + $"territory={inSourceTerritory}, targetable={targetable}, prepared={movementPrepared}, "
                + $"dialog={dialogOpen}, moving={moving}, mounted={mounted}, combat={inCombat}, "
                + $"actionStatus={actionStatus}.");
        }

        if (decision == ReturnCastDecision.Request)
        {
            var accepted = false;
            try
            {
                accepted = Actions.Return.TryCast();
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Return action request threw an exception; retrying within the attempt budget.");
            }
            returnTracker.RecordRequest(now, accepted);
            ReturnRequestLedger.RecordRequest(now);
            Svc.Log.Info($"Return request {returnTracker.Attempts}/{ReturnCastTracker.MaxAttempts}: accepted={accepted}, actionStatus={actionStatus}.");
        }

        return false;
    }

    private static unsafe bool IsReturnConfirmationReady(AddonSelectYesno* dialog)
    {
        return dialog != null
               && dialog->AtkUnitBase.IsVisible
               && dialog->AtkUnitBase.AtkValuesCount > 7
               && dialog->AtkUnitBase.AtkValues != null
               && dialog->AtkUnitBase.AtkValues[7].Type == AtkValueType.Int
               && dialog->AtkUnitBase.AtkValues[7].Int == -1
               && dialog->YesButton != null
               && dialog->YesButton->IsEnabled
               && dialog->YesButton->AtkResNode != null
               && dialog->YesButton->AtkResNode->IsVisible();
    }

    private bool TryStopReturnMovement()
    {
        try
        {
            // The native Return action does not require navigation IPC.
            if (module.TryGetIPCSubscriber<Lifestream>(out var lifestream) && lifestream != null && lifestream.IsReady())
                lifestream.Abort();
            if (module.TryGetIPCSubscriber<VNavmesh>(out var vnav) && vnav != null && vnav.IsReady())
                AggroAvoidanceNavigation.Stop(vnav);
            return true;
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, "Could not stop movement before Return; retrying.");
            return false;
        }
    }

    internal static bool IsAtReturnLanding(uint expectedTerritory)
    {
        var player = Svc.Objects.LocalPlayer;
        return player != null && Svc.ClientState.TerritoryType == expectedTerritory
            && ZoneData.IsInOccultCrescent()
            && ZoneData.StartingLocations.TryGetValue(expectedTerritory, out var start)
            && TransitCompletionPolicy.IsVerifiedReturnLanding(player.Position, start,
                ZoneData.GetClosestAethernetShard(player.Position), ZoneData.GetBaseCampAethernet());
    }
}
