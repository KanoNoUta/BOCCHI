using BOCCHI.Data;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Enums;

public enum Aethernet : uint
{
    Unknown = 0,

    // South Horn
    BaseCamp = 4944,
    TheWanderersHaven = 4936,
    CrystallizedCaverns = 4929,
    Eldergrowth = 4930,
    Stonemarsh = 4942,

    // North Horn (7.55)
    NorthBaseCamp = 5571,
    SunkenTempleFront = 5572,
    FloatingRuins = 5573,
    RuinedStreetsFront = 5574,
    WillOWispVillage = 5575,
    KarnakCitadel = 5576,
}

public class AethernetData
{
    public readonly static float DISTANCE = 4.2f;

    public Aethernet Aethernet;

    public uint BaseId;

    public Vector3 Position;


    public Vector3 Destination; // Where you end up after teleporting to this shard

    public static IEnumerable<AethernetData> All()
    {
        return ZoneData.GetCurrentAethernets().Select(a => a.GetData());
    }

    public static IOrderedEnumerable<AethernetData> AllByDistance()
    {
        return AllByDistance(Player.Position);
    }

    public static IOrderedEnumerable<AethernetData> AllByDistance(Vector3 position)
    {
        return All().OrderBy(a => Vector3.Distance(a.Position, position));
    }

    public static AethernetData GetClosestTo(Vector3 to)
    {
        return All().OrderBy(data => Vector3.Distance(to, data.Position)).First();
    }

    public static AethernetData GetClosestToPlayer()
    {
        return GetClosestTo(Player.Position);
    }

    public float DistanceTo(Vector3 to)
    {
        return Vector3.Distance(to, Position);
    }

    public float DistanceToPlayer()
    {
        return DistanceTo(Player.Position);
    }
}

public static class AethernetExtensions
{
    public static string ToFriendlyString(this Aethernet aethernet)
    {
        return Svc.Data.GetExcelSheet<PlaceName>().FirstOrDefault(p => p.RowId == (uint)aethernet).Name.ToString();
    }

    public static AethernetData GetData(this Aethernet aethernet)
    {
        switch (aethernet)
        {
            case Aethernet.BaseCamp:
                return new AethernetData
                {
                    Aethernet = Aethernet.BaseCamp,
                    BaseId = 2014664,
                    Position = ZoneData.Aetherytes[ZoneData.SOUTHHORN],
                    Destination = new Vector3(835.3f, 73f, -695.9f),
                };
            case Aethernet.TheWanderersHaven:
                return new AethernetData
                {
                    Aethernet = Aethernet.TheWanderersHaven,
                    BaseId = 2014665,
                    Position = new Vector3(-173.02f, 8.19f, -611.14f),
                    Destination = new Vector3(-169.1f, 6.5f, -609.4f),
                };
            case Aethernet.CrystallizedCaverns:
                return new AethernetData
                {
                    Aethernet = Aethernet.CrystallizedCaverns,
                    BaseId = 2014666,
                    Position = new Vector3(-358.14f, 101.98f, -120.96f),
                    Destination = new Vector3(-354.6f, 100f, -120.7f),
                };
            case Aethernet.Eldergrowth:
                return new AethernetData
                {
                    Aethernet = Aethernet.Eldergrowth,
                    BaseId = 2014667,
                    Position = new Vector3(306.94f, 105.18f, 305.65f),
                    Destination = new Vector3(306.94f, 103f, 306f),
                };
            case Aethernet.Stonemarsh:
                return new AethernetData
                {
                    Aethernet = Aethernet.Stonemarsh,
                    BaseId = 2014744,
                    Position = new Vector3(-384.12f, 99.20f, 281.42f),
                    Destination = new Vector3(-384f, 97.2f, 278.1f),
                };
            case Aethernet.NorthBaseCamp:
                return new AethernetData
                {
                    Aethernet = Aethernet.NorthBaseCamp,
                    BaseId = 2015429,
                    Position = ZoneData.Aetherytes[ZoneData.NORTHHORN],
                    Destination = new Vector3(881.9951f, 258.5f, 881.9271f),
                };
            case Aethernet.SunkenTempleFront:
                return new AethernetData
                {
                    Aethernet = Aethernet.SunkenTempleFront,
                    BaseId = 2015430,
                    Position = new Vector3(357.6689f, 45.76582f, -554.3083f),
                    Destination = new Vector3(358.2281f, 45.13132f, -557.2622f),
                };
            case Aethernet.FloatingRuins:
                return new AethernetData
                {
                    Aethernet = Aethernet.FloatingRuins,
                    BaseId = 2015431,
                    Position = new Vector3(-547.2471f, 67.99808f, 594.4042f),
                    Destination = new Vector3(-549.5803f, 67.20449f, 596.822f),
                };
            case Aethernet.RuinedStreetsFront:
                return new AethernetData
                {
                    Aethernet = Aethernet.RuinedStreetsFront,
                    BaseId = 2015432,
                    Position = new Vector3(-388.5732f, 41.22126f, -440.5197f),
                    Destination = new Vector3(-387.1999f, 39.27517f, -437.6127f),
                };
            case Aethernet.WillOWispVillage:
                return new AethernetData
                {
                    Aethernet = Aethernet.WillOWispVillage,
                    BaseId = 2015433,
                    Position = new Vector3(-13.3638f, 3.144829f, -40.51193f),
                    Destination = new Vector3(-15.021f, 2.038227f, -43.83533f),
                };
            case Aethernet.KarnakCitadel:
                return new AethernetData
                {
                    Aethernet = Aethernet.KarnakCitadel,
                    BaseId = 2015434,
                    Position = new Vector3(451.6841f, 70.9269f, 528.8388f),
                    Destination = new Vector3(454.3429f, 69.99997f, 530.9988f),
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(aethernet), aethernet, "Unknown Occult Crescent aethernet");
        }
    }
}
