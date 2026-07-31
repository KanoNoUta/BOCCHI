using BOCCHI.Data;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer;

public class Scanner(MobFarmerModule module)
{
    public IEnumerable<IBattleNpc> Mobs { get; private set; } = [];

    public IEnumerable<IBattleNpc> InCombat
    {
        get => Mobs.Where(o => o.IsTargetingPlayer());
    }

    public IEnumerable<IBattleNpc> NotInCombat
    {
        get => Mobs.Where(o => !o.HasTarget());
    }

    public unsafe void Tick(IFramework _)
    {
        Mobs = TargetHelper.Enemies.Where(o =>
        {
            if (o.IsTargetingPlayer())
            {
                return true;
            }

            if (Player.DistanceTo(o) > module.Config.MaxEuclideanDistance)
            {
                return false;
            }

            var chara = (BattleChara*)o.Address;

            if (module.Config.IsSelectedForTerritory((Mob)o.NameId, Svc.ClientState.TerritoryType))
            {
                return chara->ForayInfo.Level <= module.Config.MaxMobLevel;
            }

            if (!module.Config.ConsiderSpecialMobs)
            {
                return false;
            }

            if (MobData.MobsWithSpawnCondition.Contains((Mob)o.NameId))
            {
                return chara->ForayInfo.Level <= module.Config.MaxMobLevel;
            }

            return false;
        });
    }
}
