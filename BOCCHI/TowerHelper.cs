using BOCCHI.Data;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI;

public static class TowerHelper
{
    public enum TowerType
    {
        Blood,
        Magic,
        GrandMagic,
    }

    public sealed record TowerDefinition(
        TowerType Type,
        uint DynamicEventId,
        uint TerritoryId,
        string DisplayName,
        Vector3? PlatformCenter = null,
        float? PlatformRadius = null)
    {
        public bool HasPlatformGeometry => PlatformCenter.HasValue && PlatformRadius is > 0f;
    }

    private static readonly TowerDefinition[] Definitions =
    [
        new(
            TowerType.Blood,
            48,
            ZoneData.SOUTHHORN,
            "The Forked Tower: Blood",
            new Vector3(63f, 126.5f, 4f),
            20f),
        // North Horn platform coordinates and radii are deliberately left
        // unset until they are captured from the live CN 7.55 client.  The
        // event IDs and territory are verified; reusing Blood Tower geometry
        // here would produce incorrect player counts.
        new(TowerType.Magic, 64, ZoneData.NORTHHORN, "两岐塔 魔之塔"),
        new(TowerType.GrandMagic, 65, ZoneData.NORTHHORN, "两歧塔 超魔之塔"),
    ];

    private static readonly IReadOnlyDictionary<TowerType, TowerDefinition> DefinitionsByType =
        Definitions.ToDictionary(definition => definition.Type);

    private static readonly IReadOnlyDictionary<uint, TowerDefinition> DefinitionsByEventId =
        Definitions.ToDictionary(definition => definition.DynamicEventId);

    public static IReadOnlyList<TowerDefinition> All => Definitions;

    public static bool TryGetDefinition(TowerType type, out TowerDefinition definition)
    {
        return DefinitionsByType.TryGetValue(type, out definition!);
    }

    public static bool TryGetDefinitionByEventId(uint dynamicEventId, out TowerDefinition definition)
    {
        return DefinitionsByEventId.TryGetValue(dynamicEventId, out definition!);
    }

    public static bool TryGetTowerType(uint dynamicEventId, out TowerType type)
    {
        if (TryGetDefinitionByEventId(dynamicEventId, out var definition))
        {
            type = definition.Type;
            return true;
        }

        type = default;
        return false;
    }

    public static IReadOnlyList<TowerDefinition> GetDefinitionsForTerritory(uint territoryId)
    {
        return Definitions.Where(definition => definition.TerritoryId == territoryId).ToArray();
    }

    public static bool IsInTowerZone(TowerType type, Vector3 position)
    {
        return TryGetPlatformGeometry(type, out var center, out var radius)
               && Vector3.Distance(center, position) <= radius;
    }

    public static bool IsNearTowerZone(TowerType type, Vector3 position)
    {
        if (!TryGetPlatformGeometry(type, out var center, out var radius))
        {
            return false;
        }

        var distance = Vector3.Distance(center, position);
        return distance > radius && distance <= radius * 4;
    }

    public static bool IsPlayerNearTower(TowerType type)
    {
        return IsNearTowerZone(type, Player.Position) || IsInTowerZone(type, Player.Position);
    }

    public static int GetPlayersInTowerZone(TowerType type)
    {
        if (!TryGetPlatformGeometry(type, out _, out _) || !IsPlayerNearTower(type))
        {
            return -1;
        }

        return Svc.Objects.Count(gameObject =>
            gameObject.ObjectKind == ObjectKind.Pc && IsInTowerZone(type, gameObject.Position));
    }

    public static int GetPlayersNearTowerZone(TowerType type)
    {
        if (!TryGetPlatformGeometry(type, out _, out _) || !IsPlayerNearTower(type))
        {
            return -1;
        }

        return Svc.Objects.Count(gameObject =>
            gameObject.ObjectKind == ObjectKind.Pc && IsNearTowerZone(type, gameObject.Position));
    }

    public static bool TryGetPlatformGeometry(TowerType type, out Vector3 center, out float radius)
    {
        if (TryGetDefinition(type, out var definition)
            && definition.PlatformCenter is { } platformCenter
            && definition.PlatformRadius is { } platformRadius
            && platformRadius > 0f)
        {
            center = platformCenter;
            radius = platformRadius;
            return true;
        }

        center = default;
        radius = default;
        return false;
    }
}
