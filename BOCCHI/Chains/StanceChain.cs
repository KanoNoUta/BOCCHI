using BOCCHI.ActionHelpers;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Chain;
using Ocelot.Extensions;
using Job = ECommons.ExcelServices.Job;

namespace BOCCHI.Chains;

public class StanceChain(bool stance) : ChainFactory
{
    protected override Chain Create(Chain chain)
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            chain.Then(_ => Actions.TryUnmount()).Wait(750);
        }

        switch (Player.Job)
        {
            case Job.PLD:
                {
                    var on = new Action(ActionType.Action, 28);
                    var off = new Action(ActionType.Action, 32065);
                    if (Player.Status.HasStatus(79) != stance)
                    {
                        chain.Then((stance ? on : off).GetCastChain()).Wait(1000);
                    }
                    break;
                }
            case Job.WAR:
                {
                    var on = new Action(ActionType.Action, 48);
                    var off = new Action(ActionType.Action, 32066);
                    if (Player.Status.HasStatus(91) != stance)
                    {
                        chain.Then((stance ? on : off).GetCastChain()).Wait(1000);
                    }
                    break;
                }
            case Job.DRK:
                {
                    var on = new Action(ActionType.Action, 3629);
                    var off = new Action(ActionType.Action, 32067);
                    if (Player.Status.HasStatus(743) != stance)
                    {
                        chain.Then((stance ? on : off).GetCastChain()).Wait(1000);
                    }
                    break;
                }
            case Job.GNB:
                {
                    var on = new Action(ActionType.Action, 16142);
                    var off = new Action(ActionType.Action, 32068);
                    if (Player.Status.HasStatus(1833) != stance)
                    {
                        chain.Then((stance ? on : off).GetCastChain()).Wait(1000);
                    }
                    break;
                }
        }

        return chain;
    }



}
