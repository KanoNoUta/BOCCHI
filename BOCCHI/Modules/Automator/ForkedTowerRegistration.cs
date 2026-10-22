using BOCCHI.Data;
using BOCCHI.Modules.CriticalEncounters;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using System;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

/// <summary>
/// Decides when Illegal Mode should walk to the Forked Tower pad and wait to
/// be pulled in. Ported from upstream BOCCHI 4.2's ForkedTowerRegistration.
/// </summary>
public static class ForkedTowerRegistration
{
    public readonly record struct Pad(
        uint EventId,
        uint TerritoryId,
        Vector3 Position,
        int RequiredKnowledgeLevel,
        bool NeedsCiphers);

    public const float ArrivalRadius = 6f;

    public const float ArrivalHeight = 10f;

    // Generous so walking over to a reliquary to offer ciphers does not drag
    // the player back.
    public const float LeashRadius = 25f;

    // Boss arenas sit 500+ yalms from the pad, while the tower event can
    // already be current on the pad itself.
    public const float InsideTowerMinDistanceFromPad = 150f;

    private static readonly Pad[] Pads =
    [
        // Blood: ciphers are offered at the four reliquaries by hand.
        new(48, ZoneData.SOUTHHORN, new Vector3(63f, 126.5f, 4f), 0, true),
        // Magic turns away anyone below Knowledge 40.
        new(64, ZoneData.NORTHHORN, new Vector3(-320f, 11f, 422f), 40, false),
    ];

    public static bool TryGetPad(uint territoryId, out Pad pad)
    {
        foreach (var candidate in Pads)
        {
            if (candidate.TerritoryId == territoryId)
            {
                pad = candidate;
                return true;
            }
        }

        pad = default;
        return false;
    }

    public static float Distance2D(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
    }

    public static bool IsOnPad(Vector3 position, Vector3 pad)
    {
        return Distance2D(position, pad) <= ArrivalRadius && MathF.Abs(position.Y - pad.Y) <= ArrivalHeight;
    }

    public static bool IsInsideTower(bool inForkedTower, Vector3 position, Vector3 pad)
    {
        return inForkedTower && Distance2D(position, pad) > InsideTowerMinDistanceFromPad;
    }

    public static bool IsPreparing(DynamicEventState state)
    {
        return state is DynamicEventState.Register or DynamicEventState.Warmup;
    }

    /// <summary>An unknown level never blocks registration.</summary>
    public static bool MeetsKnowledgeRequirement(int required, int? level)
    {
        return required <= 0 || level is not { } known || known >= required;
    }

    public static unsafe int? GetPlayerKnowledgeLevel()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null || player.Address == nint.Zero)
        {
            return null;
        }

        var level = ((BattleChara*)player.Address)->ForayInfo.Level;
        return level == 0 ? null : level;
    }

    public static bool IsInsideTowerNow()
    {
        var player = Svc.Objects.LocalPlayer;
        return player != null
               && TryGetPad(Svc.ClientState.TerritoryType, out var pad)
               && IsInsideTower(ZoneData.IsInForkedTower(), player.Position, pad.Position);
    }

    public static bool TryFindRegistrable(AutomatorModule module, out Pad pad)
    {
        pad = default;
        if (!module.Config.AutoRegisterForForkedTower
            || !TryGetPad(Svc.ClientState.TerritoryType, out var found)
            || IsInsideTowerNow()
            || !module.TryGetModule<CriticalEncountersModule>(out var critical)
            || critical == null
            || !critical.CriticalEncounters.TryGetValue(found.EventId, out var tower)
            || !IsPreparing(tower.State)
            || !MeetsKnowledgeRequirement(found.RequiredKnowledgeLevel, GetPlayerKnowledgeLevel()))
        {
            return false;
        }

        pad = found;
        return true;
    }
}
