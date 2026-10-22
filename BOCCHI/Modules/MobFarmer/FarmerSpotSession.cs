using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer;

/// <summary>
/// The farm spot the farmer works this session, and the spots other players
/// have claimed. Ported from upstream BOCCHI 4.2's FarmerSpotSession.
/// </summary>
public sealed class FarmerSpotSession
{
    private readonly HashSet<FarmSpot> claimed = [];

    private readonly FarmSpotClaimTimer claimTimer = new();

    public FarmSpot? Current { get; private set; }

    public Vector3? StackPoint
    {
        get => Current?.GetStackPoint();
    }

    public string? Name
    {
        get => Current?.Name;
    }

    public int EffectiveMinimumMobsToStartFight(MobFarmerConfig config)
    {
        return FarmSpotPolicy.EffectiveMinimumMobsToStartFight(Current, config.MinimumMobsToStartFight);
    }

    public void Begin(MobFarmerConfig config, Vector3 from)
    {
        claimed.Clear();
        claimTimer.Reset();
        Current = FarmSpotPolicy.SelectBest(config.Spots, Svc.ClientState.TerritoryType, claimed, from);
        if (Current != null)
        {
            Svc.Log.Info($"[MobFarmer] Farming at spot \"{Current.Name}\".");
        }
    }

    public void Reset()
    {
        claimed.Clear();
        claimTimer.Reset();
        Current = null;
    }

    /// <summary>
    /// Notices a spot another player has taken and picks the next one.
    /// Returns true when the farmer should walk to a new spot.
    /// </summary>
    public bool TickClaimed(MobFarmerModule module, Vector3 from)
    {
        var config = module.Config;
        if (Current == null || config.Spots.Count == 0)
        {
            claimTimer.Reset();
            return false;
        }

        var scanner = module.Scanner;
        var claimedNow = FarmSpotPolicy.IsClaimedNow(
            scanner.Contested.Any(),
            scanner.NotInCombat.Any(),
            IsOtherPlayerNear(StackPoint ?? Current.GetOrigin(), config.ClaimedPlayerRadius));
        if (!claimTimer.Tick(claimedNow, DateTimeOffset.UtcNow, config.ClaimedSpotSeconds))
        {
            return false;
        }

        claimed.Add(Current);
        var next = FarmSpotPolicy.SelectBest(config.Spots, Svc.ClientState.TerritoryType, claimed, from);
        if (next == null || ReferenceEquals(next, Current))
        {
            Svc.Log.Info($"[MobFarmer] Spot \"{Current.Name}\" looks claimed, but there is no other spot to go to.");
            return false;
        }

        Svc.Log.Info($"[MobFarmer] Spot \"{Current.Name}\" is claimed by another player; moving to \"{next.Name}\".");
        Current = next;
        return true;
    }

    private static bool IsOtherPlayerNear(Vector3 point, float radius)
    {
        var localId = Svc.Objects.LocalPlayer?.GameObjectId ?? 0;
        return Svc.Objects
            .OfType<IPlayerCharacter>()
            .Any(player => player.GameObjectId != localId
                           && FarmSpotPolicy.Distance2D(player.Position, point) <= radius);
    }
}
