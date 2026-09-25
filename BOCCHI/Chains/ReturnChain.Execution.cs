using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot.Chain;
using Ocelot.IPC;
using System;

namespace BOCCHI.Chains;

public partial class ReturnChain
{
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
        var atLanding = !betweenAreas && player?.IsTargetable == true && IsAtReturnLanding(returnTerritory);
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

        var decision = returnTracker.Update(now, betweenAreas, casting, atLanding,
            inSourceTerritory && player?.IsTargetable == true && movementPrepared
            && !dialogOpen && !Player.IsMoving && !Svc.Condition[ConditionFlag.Mounted]
            && !Svc.Condition[ConditionFlag.InCombat] && actionStatus == 0);

        if (decision == ReturnCastDecision.Complete)
        {
            returnCompleted = true;
            Svc.Log.Info($"Return landed at base camp after {returnTracker.Attempts} request(s).");
            return true;
        }

        if (decision == ReturnCastDecision.Failed)
        {
            FailureReason = $"Return failed: requests={returnTracker.Attempts}, actionStatus={actionStatus}, "
                + $"cast={returnTracker.SawCast}, transition={returnTracker.SawTransition}, "
                + $"dialog={dialogOpen}, territory={Svc.ClientState.TerritoryType}, expected={returnTerritory}.";
            Svc.Log.Warning(FailureReason);
            return true;
        }

        // Pre-existing confirmations block the request. Only a dialog opened
        // after an accepted BOCCHI Return may be confirmed, once the actual
        // Yes button is visible and enabled. Do not depend on PostSetup timing.
        if (returnTracker.CanConfirm(now) && inSourceTerritory && !betweenAreas && !casting
            && dialog != null && dialog->AtkUnitBase.IsVisible
            && dialog->AtkUnitBase.AtkValuesCount > 7 && dialog->AtkUnitBase.AtkValues != null
            && dialog->AtkUnitBase.AtkValues[7].Type == AtkValueType.Int
            && dialog->AtkUnitBase.AtkValues[7].Int == -1
            && dialog->YesButton != null && dialog->YesButton->IsEnabled
            && dialog->YesButton->AtkResNode != null && dialog->YesButton->AtkResNode->IsVisible())
        {
            returnTracker.RecordConfirmation(now);
            dialog->AtkUnitBase.FireCallbackInt(0);
            Svc.Log.Info("Confirmed BOCCHI Return after the confirmation button became ready.");
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
            Svc.Log.Info($"Return request {returnTracker.Attempts}/{ReturnCastTracker.MaxAttempts}: accepted={accepted}, actionStatus={actionStatus}.");
        }

        return false;
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

    private static bool IsAtReturnLanding(uint expectedTerritory)
    {
        var player = Svc.Objects.LocalPlayer;
        return player != null && Svc.ClientState.TerritoryType == expectedTerritory
            && ZoneData.IsInOccultCrescent()
            && ZoneData.StartingLocations.TryGetValue(expectedTerritory, out var start)
            && TransitCompletionPolicy.IsVerifiedReturnLanding(player.Position, start,
                ZoneData.GetClosestAethernetShard(player.Position), ZoneData.GetBaseCampAethernet());
    }
}
