using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.States;
using System;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Waiting)]
public class WaitingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    public override FarmerPhase? Handle()
    {
        if (Module.Farmer.NeedsApproach)
        {
            if (Svc.Condition[ConditionFlag.InCombat] && Module.Scanner.InCombat.Any())
            {
                Module.Farmer.PauseApproach();
                return FarmerPhase.Fighting;
            }

            if (Module.Farmer.TickApproach())
            {
                return null;
            }
        }

        var startingPoint = Module.Farmer.StartingPoint;
        if (Player.DistanceTo(startingPoint) > 2f)
        {
            return null;
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return FarmerPhase.Fighting;
        }

        // Let Treasuresight or a buff trip finish, and give the yields a
        // chance to run before the next pull.
        if (Plugin.Chain.IsRunning || Plugin.Chain.QueueCount > 0)
        {
            return null;
        }

        var config = Module.Config;
        if (MobFarmerYieldPolicy.ShouldCastTreasureSightWhileWaiting(
                config.CastTreasureSightAtFarm,
                config.TreasureSightIntervalMinutes,
                Module.Farmer.LastTreasureSightAt,
                DateTimeOffset.UtcNow))
        {
            return FarmerPhase.TreasureFinding;
        }

        var mobs = MobFarmerPullPolicy.CountTowardMinimum(
            Module.Scanner.Mobs.Select(o => o.NameId),
            config.CountSpecialMobsTowardMinimum);

        return mobs >= config.MinimumMobsToStartLoop ? FarmerPhase.Buffing : null;
    }
}
