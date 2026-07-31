using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.AggroRange;

/// <summary>
/// Learns only clean passive pulls. Active pulls are deliberately rejected so
/// a ranged attack cannot be recorded as a monster's detection distance.
/// </summary>
internal sealed class AggroRangeObserver
{
    private readonly HashSet<uint> previousHaters = [];
    private bool initialized;
    private bool previousPlayerInCombat;

    public unsafe bool Update(AggroRangeConfig config)
    {
        if (!global::BOCCHI.Data.ZoneData.IsInNorthHorn())
        {
            Reset();
            return false;
        }

        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            Reset();
            return false;
        }

        var uiState = UIState.Instance();
        if (uiState == null)
        {
            return false;
        }

        var currentHaters = new HashSet<uint>();
        for (var i = 0; i < uiState->Hater.HaterCount; ++i)
        {
            currentHaters.Add(uiState->Hater.Haters[i].EntityId);
        }

        var playerInCombat = Svc.Condition[ConditionFlag.InCombat];
        if (!initialized)
        {
            ReplaceHaters(currentHaters);
            previousPlayerInCombat = playerInCombat;
            initialized = true;
            return false;
        }

        var changed = false;
        if (config.AutoCalibrate && !previousPlayerInCombat)
        {
            foreach (var entityId in currentHaters.Where(id => !previousHaters.Contains(id)))
            {
                var mob = Svc.Objects.OfType<IBattleNpc>()
                    .FirstOrDefault(candidate => candidate.EntityId == entityId);
                if (mob == null
                    || mob.Address == IntPtr.Zero
                    || mob.IsDead
                    || !mob.IsTargetable
                    || !mob.IsHostile()
                    || mob.TargetObjectId != player.GameObjectId
                    || player.TargetObjectId == mob.GameObjectId
                    || !CommonMobCatalog.TryGet(mob.NameId, out var profile))
                {
                    continue;
                }

                var battleChara = (BattleChara*)mob.Address;
                if (battleChara->FateId != 0)
                {
                    continue;
                }

                var centerDistance = AggroRangeGeometry.HorizontalDistance(player.Position, mob.Position);
                var edgeRange = centerDistance - Math.Max(0f, mob.HitboxRadius);
                // Reject implausible transitions as well as active long-range
                // attacks that slipped through target-state filtering.
                if (edgeRange > profile.FallbackEdgeRange + 8f)
                {
                    continue;
                }

                config.Calibrations ??= [];
                if (!config.Calibrations.TryGetValue(mob.NameId, out var calibration)
                    || calibration == null)
                {
                    calibration = new AggroRangeCalibration();
                    config.Calibrations[mob.NameId] = calibration;
                }

                if (calibration.AddSample(edgeRange))
                {
                    changed = true;
                    Svc.Log.Information(
                        $"Aggro range calibrated: NameId={mob.NameId}, edge={edgeRange:F1}y, "
                        + $"resolved={calibration.ResolveEdgeRange(profile.FallbackEdgeRange):F1}y");
                }
            }
        }

        ReplaceHaters(currentHaters);
        previousPlayerInCombat = playerInCombat;
        return changed;
    }

    public void Reset()
    {
        previousHaters.Clear();
        initialized = false;
        previousPlayerInCombat = false;
    }

    private void ReplaceHaters(HashSet<uint> current)
    {
        previousHaters.Clear();
        previousHaters.UnionWith(current);
    }
}
