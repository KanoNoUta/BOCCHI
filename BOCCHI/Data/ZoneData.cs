using BOCCHI.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Data;

public static class ZoneData
{
    public const uint SOUTHHORN = 1252;

    public const uint NORTHHORN = 1346;

    public readonly static HashSet<uint> OccultCrescentTerritoryIds =
    [
        SOUTHHORN,
        NORTHHORN,
    ];

    // This can and should be filled using layout files or excel data
    public readonly static Dictionary<uint, Vector3> Aetherytes = new()
    {
        { SOUTHHORN, new Vector3(830.75f, 72.98f, -695.98f) },
        { NORTHHORN, new Vector3(880.0015f, 259.7396f, 880.0587f) },
    };

    public readonly static Dictionary<uint, Vector3> StartingLocations = new()
    {
        { SOUTHHORN, new Vector3(850.33f, 72.99f, -704.07f) },
        // Center of North Horn's base-camp PC PopRange.  The actual Return
        // landing point may vary slightly within this range.
        { NORTHHORN, new Vector3(888.4536f, 258.5f, 882.024f) },
    };

    private readonly static Dictionary<uint, Aethernet[]> Aethernets = new()
    {
        {
            SOUTHHORN,
            [
                Aethernet.BaseCamp,
                Aethernet.TheWanderersHaven,
                Aethernet.CrystallizedCaverns,
                Aethernet.Eldergrowth,
                Aethernet.Stonemarsh,
            ]
        },
        {
            NORTHHORN,
            [
                Aethernet.NorthBaseCamp,
                Aethernet.SunkenTempleFront,
                Aethernet.FloatingRuins,
                Aethernet.RuinedStreetsFront,
                Aethernet.WillOWispVillage,
                Aethernet.KarnakCitadel,
            ]
        },
    };

    // Zone functions
    public static bool IsInSouthHorn()
    {
        return Svc.ClientState.TerritoryType == SOUTHHORN;
    }

    public static bool IsInNorthHorn()
    {
        return Svc.ClientState.TerritoryType == NORTHHORN;
    }

    public static bool IsOccultCrescentTerritory(uint territoryId)
    {
        return OccultCrescentTerritoryIds.Contains(territoryId);
    }

    public static bool IsInOccultCrescent()
    {
        return Svc.Objects.LocalPlayer != null && IsOccultCrescentTerritory(Svc.ClientState.TerritoryType);
    }

    // Tower functions
    private static bool HasForkedTowerStatus()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            return false;
        }

        return player.StatusList.HasAny(
            PlayerStatus.DutiesAsAssigned,
            PlayerStatus.ResurrectionDenied,
            PlayerStatus.ResurrectionRestricted
        ) && IsOccultCrescentTerritory(Svc.ClientState.TerritoryType);
    }

    public static unsafe uint GetCurrentForkedTowerEventId()
    {
        var events = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEventContainer.GetInstance();
        if (events == null)
        {
            return 0;
        }

        var eventId = (uint)events->CurrentEventId;
        return TowerHelper.TryGetDefinitionByEventId(eventId, out var definition)
               && definition.TerritoryId == Svc.ClientState.TerritoryType
            ? eventId
            : 0;
    }

    public static unsafe bool IsInForkedTower()
    {
        if (GetCurrentForkedTowerEventId() != 0)
        {
            return true;
        }

        return HasForkedTowerStatus();
    }

    private static string GetCurrentZoneName()
    {
        if (IsInSouthHorn())
        {
            return "South Horn";
        }

        if (IsInNorthHorn())
        {
            return "North Horn";
        }

        throw new Exception("Unknown Zone");
    }

    public static string GetCurrentZoneDataDirectory()
    {
        var directory = GetCurrentZoneDataDirectoryPath();
        Directory.CreateDirectory(directory);

        return directory;
    }

    private static string GetCurrentZoneDataDirectoryPath()
    {
        return Path.Join(Svc.PluginInterface.AssemblyLocation.DirectoryName, "Data", GetCurrentZoneName().Replace(" ", ""));
    }

    public static bool HasCurrentZoneDataFile(string filename)
    {
        return IsOccultCrescentTerritory(Svc.ClientState.TerritoryType)
               && File.Exists(Path.Join(GetCurrentZoneDataDirectoryPath(), filename));
    }

    public static Aethernet GetClosestAethernetShard(Vector3 position)
    {
        return AethernetData.All().OrderBy((data) => Vector3.Distance(position, data.Position)).First().Aethernet;
    }

    public static IReadOnlyList<Aethernet> GetAethernets(uint territoryId)
    {
        return Aethernets.TryGetValue(territoryId, out var aethernet) ? aethernet : [];
    }

    public static IReadOnlyList<Aethernet> GetCurrentAethernets()
    {
        return GetAethernets(Svc.ClientState.TerritoryType);
    }

    public static Aethernet GetBaseCampAethernet()
    {
        return Svc.ClientState.TerritoryType switch
        {
            SOUTHHORN => Aethernet.BaseCamp,
            NORTHHORN => Aethernet.NorthBaseCamp,
            _ => throw new InvalidOperationException("The current territory is not an Occult Crescent zone."),
        };
    }

    public static IList<IGameObject> GetNearbyAethernetShards(float range = 4.3f)
    {
        var playerPos = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;

        var baseIds = AethernetData.All().Select(datum => datum.BaseId).ToHashSet();

        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => baseIds.Contains(o.BaseId))
            .Where(o => Vector3.Distance(o.Position, playerPos) <= range)
            .ToList();
    }

    public static bool IsNearAethernetShard(Aethernet aethernet, float range = 4.3f)
    {
        var data = aethernet.GetData();
        if (GetNearbyAethernetShards(range).Any(o => o.BaseId == data.BaseId))
        {
            return true;
        }

        // North Horn's custom aethernet EventObj can be absent from the
        // Dalamud object table (or expose a different runtime BaseId) even
        // while the player is standing at the shard. The maintained shard
        // coordinates remain a reliable fallback for initiating Lifestream.
        var player = Svc.Objects.LocalPlayer;
        return player != null && IsWithinKnownAethernetRange(player.Position, data.Position, range);
    }

    public static bool IsNearAnyAethernetShard(float range = 4.3f)
    {
        if (GetNearbyAethernetShards(range).Any())
        {
            return true;
        }

        var player = Svc.Objects.LocalPlayer;
        return player != null
               && AethernetData.All().Any(data =>
                   IsWithinKnownAethernetRange(player.Position, data.Position, range));
    }

    public static bool IsWithinKnownAethernetRange(Vector3 playerPosition, Vector3 shardPosition, float range)
    {
        return float.IsFinite(range)
               && range >= 0f
               && Vector3.DistanceSquared(playerPosition, shardPosition) <= range * range;
    }

    public static IList<IGameObject> GetNearbyKnowledgeCrystal(float range = 4.5f)
    {
        var playerPos = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;

        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => o.BaseId == (uint)OccultObjectType.KnowledgeCrystal)
            .Where(o => Vector3.Distance(o.Position, playerPos) <= range)
            .ToList();
    }

    public static bool IsNearKnowledgeCrystal(float range = 4.5f)
    {
        return GetNearbyKnowledgeCrystal(range).Any();
    }
}
