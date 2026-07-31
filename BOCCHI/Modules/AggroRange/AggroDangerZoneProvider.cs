using BOCCHI.Data;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.AggroRange;

public static class AggroDangerZoneProvider
{
    private const float MaximumRelevantDistance = 120f;

    /// <summary>
    /// Captures game objects on the framework thread. The returned value is
    /// detached from Dalamud and is safe to pass to the background planner.
    /// </summary>
    public static unsafe List<AggroDangerZone> Capture(AggroRangeConfig config)
    {
        var zones = new List<AggroDangerZone>();
        if (!ZoneData.IsInNorthHorn() || Svc.Objects.LocalPlayer is not { } player)
        {
            return zones;
        }

        if (player.Address == IntPtr.Zero)
        {
            return zones;
        }

        var playerLevel = ((BattleChara*)player.Address)->ForayInfo.Level;
        var maximumDistanceSquared = MaximumRelevantDistance * MaximumRelevantDistance;
        foreach (var mob in Svc.Objects.OfType<IBattleNpc>())
        {
            if (mob is not { IsDead: false, IsTargetable: true }
                || !mob.IsHostile()
                || !CommonMobCatalog.TryGet(mob.NameId, out var profile)
                || mob.Address == IntPtr.Zero
                || Vector3DistanceSquaredXZ(player.Position, mob.Position) > maximumDistanceSquared)
            {
                continue;
            }

            var battleChara = (BattleChara*)mob.Address;
            if (battleChara->FateId != 0
                || config.HideEngagedMobs && mob.HasTarget()
                || !AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel, battleChara->ForayInfo.Level))
            {
                continue;
            }

            zones.Add(new AggroDangerZone(
                mob.NameId,
                mob.Position,
                AggroRangeResolver.ResolveTriggerRadius(profile, mob.HitboxRadius, config)
                + config.AvoidanceSafetyMargin));
        }

        return zones;
    }

    private static float Vector3DistanceSquaredXZ(System.Numerics.Vector3 left, System.Numerics.Vector3 right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return x * x + z * z;
    }
}
