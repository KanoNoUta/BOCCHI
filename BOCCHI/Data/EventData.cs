using BOCCHI.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Data;

public struct EventData
{
    public uint Id;

    // Existing South Horn rows predate territory-aware data and therefore use
    // zero here.  EffectiveTerritoryId keeps those rows backwards compatible.
    public uint TerritoryId;

    public EventType Type;

    public string InternalName;

    public Demiatma? Demiatma;

    public SoulShard? Soulshard;

    public MonsterNote? Note;

    public bool IsPot;

    public Aethernet? Aethernet;

    public Vector3? StartPosition;

    public Vector3? NavigationPositionOverride;

    public float? Radius;

    public readonly uint EffectiveTerritoryId
    {
        get => TerritoryId == 0 ? ZoneData.SOUTHHORN : TerritoryId;
    }

    public static EventData GetFate(uint id, uint territoryId)
    {
        return Fates.TryGetValue(id, out var data)
            ? data
            : new EventData
            {
                Id = id,
                TerritoryId = territoryId,
                Type = EventType.Fate,
                InternalName = $"FATE {id}",
            };
    }

    public static EventData GetCriticalEncounter(uint id, uint territoryId)
    {
        return CriticalEncounters.TryGetValue(id, out var data)
            ? data
            : new EventData
            {
                Id = id,
                TerritoryId = territoryId,
                Type = EventType.CriticalEncounter,
                InternalName = $"Critical Encounter {id}",
            };
    }

    public static IEnumerable<EventData> GetFatesForTerritory(uint territoryId)
    {
        return Fates.Values.Where(data => data.EffectiveTerritoryId == territoryId);
    }

    public static IEnumerable<EventData> GetCriticalEncountersForTerritory(uint territoryId)
    {
        return CriticalEncounters.Values.Where(data => data.EffectiveTerritoryId == territoryId);
    }

    public readonly static Dictionary<uint, EventData> Fates = new()
    {
        {
            1962,
            new EventData
            {
                Id = 1962,
                Type = EventType.Fate,
                InternalName = "Rough Waters",
                Demiatma = Enums.Demiatma.Azurite,
                StartPosition = new Vector3(162.00f, 56.00f, 676.00f),
            }
        },
        {
            1963,
            new EventData
            {
                Id = 1963,
                Type = EventType.Fate,
                InternalName = "The Golden Guardian",
                Demiatma = Enums.Demiatma.Azurite,
                StartPosition = new Vector3(373.20f, 70.00f, 486.00f),
            }
        },
        {
            1964,
            new EventData
            {
                Id = 1964,
                Type = EventType.Fate,
                InternalName = "King of the Crescent",
                Demiatma = Enums.Demiatma.Orpiment,
                StartPosition = new Vector3(-226.10f, 116.38f, 254.00f),
            }
        },
        {
            1965,
            new EventData
            {
                Id = 1965,
                Type = EventType.Fate,
                InternalName = "The Winged Terror",
                Demiatma = Enums.Demiatma.Realgar,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
                StartPosition = new Vector3(-548.50f, 3.00f, -595.00f),
            }
        },
        {
            1966,
            new EventData
            {
                Id = 1966,
                Type = EventType.Fate,
                InternalName = "An Unending Duty",
                Demiatma = Enums.Demiatma.Malachite,
                StartPosition = new Vector3(-223.10f, 107.00f, 36.00f),
            }
        },
        {
            1967,
            new EventData
            {
                Id = 1967,
                Type = EventType.Fate,
                InternalName = "Brain Drain",
                Demiatma = Enums.Demiatma.Realgar,
                Aethernet = Enums.Aethernet.CrystallizedCaverns,
                StartPosition = new Vector3(-48.10f, 111.76f, -320.00f),
            }
        },
        {
            1968,
            new EventData
            {
                Id = 1968,
                Type = EventType.Fate,
                InternalName = "A Delicate Balance",
                Demiatma = Enums.Demiatma.Verdigris,
                StartPosition = new Vector3(-370.00f, 75.00f, 650.00f),
            }
        },
        {
            1969,
            new EventData
            {
                Id = 1969,
                Type = EventType.Fate,
                InternalName = "Sworn to Soil",
                Demiatma = Enums.Demiatma.Verdigris,
                StartPosition = new Vector3(-589.10f, 96.50f, 333.00f),
            }
        },
        {
            1970,
            new EventData
            {
                Id = 1970,
                Type = EventType.Fate,
                InternalName = "A Prying Eye",
                Demiatma = Enums.Demiatma.Azurite,
                StartPosition = new Vector3(-71.00f, 71.31f, 557.00f),
            }
        },
        {
            1971,
            new EventData
            {
                Id = 1971,
                Type = EventType.Fate,
                InternalName = "Fatal Allure",
                Demiatma = Enums.Demiatma.Orpiment,
                StartPosition = new Vector3(79.00f, 97.86f, 278.00f),
            }
        },
        {
            1972,
            new EventData
            {
                Id = 1972,
                Type = EventType.Fate,
                InternalName = "Serving Darkness",
                Demiatma = Enums.Demiatma.CaputMortuum,
                StartPosition = new Vector3(413.00f, 96.00f, -13.00f),
            }
        },
        {
            1976,
            new EventData
            {
                Id = 1976,
                Type = EventType.Fate,
                InternalName = "Persistent Pots",
                Demiatma = Enums.Demiatma.Orpiment,
                Note = MonsterNote.PersistentPots,
                StartPosition = new Vector3(200.00f, 111.73f, -215.00f),
            }
        },
        {
            1977,
            new EventData
            {
                Id = 1977,
                Type = EventType.Fate,
                InternalName = "Pleading Pots",
                Demiatma = Enums.Demiatma.Verdigris,
                Note = MonsterNote.PersistentPots,
                StartPosition = new Vector3(-481.00f, 75.00f, 528.00f),
            }
        },
        {
            2072,
            new EventData
            {
                Id = 2072,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "被欺负的魔法罐",
                IsPot = true,
                StartPosition = new Vector3(233f, 7.729229f, -470f),
            }
        },
        {
            2073,
            new EventData
            {
                Id = 2073,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "被吹飞的魔法罐",
                IsPot = true,
                StartPosition = new Vector3(-505.2822f, 53.14409f, 244.041f),
            }
        },
        {
            2074,
            new EventData
            {
                Id = 2074,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "暴力牛魔——好战弥诺陶洛斯",
                StartPosition = new Vector3(724f, 70f, 220f),
            }
        },
        {
            2075,
            new EventData
            {
                Id = 2075,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "诅咒宝珠——邪瞳",
                Aethernet = Enums.Aethernet.SunkenTempleFront,
                StartPosition = new Vector3(510f, 16.76658f, -30f),
            }
        },
        {
            2076,
            new EventData
            {
                Id = 2076,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "水边暴君——统领奇美拉",
                StartPosition = new Vector3(95f, 10f, 470f),
            }
        },
        {
            2077,
            new EventData
            {
                Id = 2077,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "历战水马——凯尔派总领",
                StartPosition = new Vector3(330f, 0f, -250f),
            }
        },
        {
            2078,
            new EventData
            {
                Id = 2078,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "魔界的叹息——妖艳魔花珊迪",
                StartPosition = new Vector3(-402.0002f, 29.76808f, -252.9997f),
            }
        },
        {
            2079,
            new EventData
            {
                Id = 2079,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "自怨自艾的歌手——伊阿姆柏",
                StartPosition = new Vector3(-170f, 30f, -500f),
            }
        },
        {
            2080,
            new EventData
            {
                Id = 2080,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "狼占狗窝——遗迹冰狼",
                StartPosition = new Vector3(-90f, 67.47852f, 865.9999f),
            }
        },
        {
            2081,
            new EventData
            {
                Id = 2081,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "腐坏街道的守护者——忍耐基路伯",
                StartPosition = new Vector3(-440f, 47.02659f, -790f),
            }
        },
        {
            2082,
            new EventData
            {
                Id = 2082,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "驾驭自然的巨兽——呼风狮鹫",
                StartPosition = new Vector3(-855.7433f, 70.67716f, 482.1518f),
            }
        },
        {
            2083,
            new EventData
            {
                Id = 2083,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "仿制的蛇人偶——半灵美杜莎",
                StartPosition = new Vector3(-661.0049f, 87f, -54.00021f),
            }
        },
        {
            2084,
            new EventData
            {
                Id = 2084,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.Fate,
                InternalName = "高傲的雷兽——新月女王",
                // 官方 planmap.lgb FATE11_base 布局位置即 (140,37,-708)，
                // 与旧硬编码一致。留空让 Fate.Refresh 回落 IFate.Position 实时坐标，
                // 位置始终跟随服务器数据，避免后续布局调整时再次偏移。
            }
        },
    };

    public readonly static Dictionary<uint, EventData> CriticalEncounters = new()
    {
        {
            48,
            new EventData
            {
                Id = 48,
                Type = EventType.CriticalEncounter,
                InternalName = "The Forked Tower: Blood",
            }
        },
        {
            33,
            new EventData
            {
                Id = 33,
                Type = EventType.CriticalEncounter,
                InternalName = "Scourge of the Mind",
                Demiatma = Enums.Demiatma.Azurite,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            34,
            new EventData
            {
                Id = 34,
                Type = EventType.CriticalEncounter,
                InternalName = "The Black Regiment",
                Demiatma = Enums.Demiatma.Orpiment,
                Soulshard = SoulShard.Ranger,
                Note = MonsterNote.BlackChocobos,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            35,
            new EventData
            {
                Id = 35,
                Type = EventType.CriticalEncounter,
                InternalName = "The Unbridled",
                Demiatma = Enums.Demiatma.Azurite,
                Soulshard = SoulShard.Berserker,
                Note = MonsterNote.CrescentBerserker,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            36,
            new EventData
            {
                Id = 36,
                Type = EventType.CriticalEncounter,
                InternalName = "Crawling Death",
                Demiatma = Enums.Demiatma.Azurite,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            37,
            new EventData
            {
                Id = 37,
                Type = EventType.CriticalEncounter,
                InternalName = "Calamity Bound",
                Demiatma = Enums.Demiatma.Verdigris,
                Note = MonsterNote.CloisterDemon,
                Aethernet = Enums.Aethernet.Stonemarsh,
            }
        },
        {
            38,
            new EventData
            {
                Id = 38,
                Type = EventType.CriticalEncounter,
                InternalName = "Trial by Claw",
                Demiatma = Enums.Demiatma.Malachite,
                Aethernet = Enums.Aethernet.CrystallizedCaverns,
            }
        },
        {
            39,
            new EventData
            {
                Id = 39,
                Type = EventType.CriticalEncounter,
                InternalName = "From Times Bygone",
                Demiatma = Enums.Demiatma.Malachite,
                Note = MonsterNote.MythicIdol,
                Aethernet = Enums.Aethernet.Stonemarsh,
            }
        },
        {
            40,
            new EventData
            {
                Id = 40,
                Type = EventType.CriticalEncounter,
                InternalName = "Company of Stone",
                Demiatma = Enums.Demiatma.CaputMortuum,
                Aethernet = Enums.Aethernet.BaseCamp,
            }
        },
        {
            41,
            new EventData
            {
                Id = 41,
                Type = EventType.CriticalEncounter,
                InternalName = "Shark Attack",
                Demiatma = Enums.Demiatma.Realgar,
                Note = MonsterNote.NymianPotaladus,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
            }
        },
        {
            42,
            new EventData
            {
                Id = 42,
                Type = EventType.CriticalEncounter,
                InternalName = "On the Hunt",
                Demiatma = Enums.Demiatma.CaputMortuum,
                Soulshard = SoulShard.Oracle,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            43,
            new EventData
            {
                Id = 43,
                Type = EventType.CriticalEncounter,
                InternalName = "With Extreme Prejudice",
                Demiatma = Enums.Demiatma.Realgar,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
            }
        },
        {
            44,
            new EventData
            {
                Id = 44,
                Type = EventType.CriticalEncounter,
                InternalName = "Noise Complaint",
                Demiatma = Enums.Demiatma.Orpiment,
                Aethernet = Enums.Aethernet.BaseCamp,
            }
        },
        {
            45,
            new EventData
            {
                Id = 45,
                Type = EventType.CriticalEncounter,
                InternalName = "Cursed Concern",
                Demiatma = Enums.Demiatma.Realgar,
                Note = MonsterNote.TradeTortoise,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
            }
        },
        {
            46,
            new EventData
            {
                Id = 46,
                Type = EventType.CriticalEncounter,
                InternalName = "Eternal Watch",
                Demiatma = Enums.Demiatma.CaputMortuum,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            47,
            new EventData
            {
                Id = 47,
                Type = EventType.CriticalEncounter,
                InternalName = "Flame of Dusk",
                Demiatma = Enums.Demiatma.Malachite,
                Aethernet = Enums.Aethernet.CrystallizedCaverns,
            }
        },
        {
            49,
            new EventData
            {
                Id = 49,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "四颚斧花——提蔛",
                StartPosition = new Vector3(-870f, 20f, -560f),
            }
        },
        {
            50,
            new EventData
            {
                Id = 50,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "魔女复制体——卡洛菲斯提莉二重身",
                Note = MonsterNote.CalofisteriDoppelganger,
                StartPosition = new Vector3(-215f, 18f, -65f),
            }
        },
        {
            51,
            new EventData
            {
                Id = 51,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "纯白守护者——雪石膏之剑",
                Note = MonsterNote.AlabasterBlade,
                StartPosition = new Vector3(-519f, 48f, -641f),
            }
        },
        {
            52,
            new EventData
            {
                Id = 52,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "禁书化形——古术魔典",
                Note = MonsterNote.AncientGrimoire,
                StartPosition = new Vector3(659f, 132f, 659f),
            }
        },
        {
            53,
            new EventData
            {
                Id = 53,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "暗红尸骸——赤龙",
                Note = MonsterNote.RedDragon,
                StartPosition = new Vector3(-688f, 90f, 150f),
            }
        },
        {
            54,
            new EventData
            {
                Id = 54,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "暴食咒鬼——阿尔戈尔",
                Note = MonsterNote.Algol,
                StartPosition = new Vector3(765f, 70f, 0f),
            }
        },
        {
            55,
            new EventData
            {
                Id = 55,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "残暴的母蜘蛛——新月阿剌克涅",
                StartPosition = new Vector3(170f, 4f, -136f),
            }
        },
        {
            56,
            new EventData
            {
                Id = 56,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "叛逆使魔——负隅宝石兽",
                StartPosition = new Vector3(238f, 15f, 367f),
                NavigationPositionOverride = new Vector3(238f, 15f, 352f),
            }
        },
        {
            57,
            new EventData
            {
                Id = 57,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "天道好轮回——魔亡灵法师",
                Note = MonsterNote.PhantomNecromancer,
                StartPosition = new Vector3(224f, 52f, -860f),
            }
        },
        {
            58,
            new EventData
            {
                Id = 58,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "求道的人造人——神木巨人",
                StartPosition = new Vector3(-390f, 68f, 700f),
            }
        },
        {
            59,
            new EventData
            {
                Id = 59,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "诅咒的继承者——惨白魔人",
                Note = MonsterNote.PallidDemon,
                StartPosition = new Vector3(807f, 61f, -562f),
            }
        },
        {
            60,
            new EventData
            {
                Id = 60,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "魔法军团——小小法师",
                Note = MonsterNote.LittleMage,
                StartPosition = new Vector3(152f, 70f, 716f),
            }
        },
        {
            61,
            new EventData
            {
                Id = 61,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "孤岛的绑架犯——诱拐魔",
                Note = MonsterNote.KidnapperDemon,
                StartPosition = new Vector3(-150f, 70f, -860f),
            }
        },
        {
            62,
            new EventData
            {
                Id = 62,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "苏醒的多头龙——魔许德拉",
                StartPosition = new Vector3(-82f, 12f, 485f),
            }
        },
        {
            63,
            new EventData
            {
                Id = 63,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "拟态使魔——变形法师",
                Note = MonsterNote.MorphingMage,
                Aethernet = Enums.Aethernet.SunkenTempleFront,
                StartPosition = new Vector3(500f, 56f, -310f),
            }
        },
        {
            64,
            new EventData
            {
                Id = 64,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "两岐塔 魔之塔",
            }
        },
        {
            65,
            new EventData
            {
                Id = 65,
                TerritoryId = ZoneData.NORTHHORN,
                Type = EventType.CriticalEncounter,
                InternalName = "两歧塔 超魔之塔",
            }
        },
    };
}
