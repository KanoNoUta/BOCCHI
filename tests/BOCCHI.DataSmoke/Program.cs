using BOCCHI;
using BOCCHI.Data;
using BOCCHI.Enums;
using System.Reflection;
using System.Text.RegularExpressions;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var southFates = EventData.GetFatesForTerritory(ZoneData.SOUTHHORN).ToList();
var northFates = EventData.GetFatesForTerritory(ZoneData.NORTHHORN).ToList();
var southCriticalEncounters = EventData.GetCriticalEncountersForTerritory(ZoneData.SOUTHHORN).ToList();
var northCriticalEncounters = EventData.GetCriticalEncountersForTerritory(ZoneData.NORTHHORN).ToList();

Assert(southFates.Count == 13, $"Expected 13 South Horn FATEs, got {southFates.Count}.");
Assert(northFates.Count == 13, $"Expected 13 North Horn FATEs, got {northFates.Count}.");
Assert(southCriticalEncounters.Count == 16, $"Expected 16 South Horn dynamic events, got {southCriticalEncounters.Count}.");
Assert(northCriticalEncounters.Count == 17, $"Expected 17 North Horn dynamic events, got {northCriticalEncounters.Count}.");
Assert(northFates.Count(fate => fate.IsPot) == 2, "North Horn must contain exactly two pot FATEs.");
Assert(northCriticalEncounters.Count(encounter => encounter.Id is >= 49 and <= 63) == 15,
    "North Horn must contain CE IDs 49 through 63.");
Assert(northCriticalEncounters.Any(encounter => encounter.Id == 64 && encounter.InternalName == "两岐塔 魔之塔"),
    "North Horn normal tower event 64 is missing.");
Assert(northCriticalEncounters.Any(encounter => encounter.Id == 65 && encounter.InternalName == "两歧塔 超魔之塔"),
    "North Horn high-difficulty tower event 65 is missing.");

var northAethernets = ZoneData.GetAethernets(ZoneData.NORTHHORN);
Assert(northAethernets.Count == 6, $"Expected 6 North Horn aethernet entries, got {northAethernets.Count}.");
Assert(northAethernets.Distinct().Count() == northAethernets.Count, "North Horn aethernet IDs must be unique.");
Assert(Aethernet.NorthBaseCamp.GetData().BaseId == 2015429, "North Horn base-camp EObj ID is incorrect.");
Assert(Aethernet.KarnakCitadel.GetData().BaseId == 2015434, "Karnak Citadel EObj ID is incorrect.");

Assert((int)JobId.Ninja == 16 && (int)JobId.Necromancer == 23, "North Horn support-job IDs are incorrect.");
Assert((uint)PlayerStatus.PhantomNinja == 5328 && (uint)PlayerStatus.PhantomNecromancer == 5335,
    "North Horn support-job status IDs are incorrect.");
Assert((uint)MonsterNote.AncientGrimoire == 51979 && (uint)MonsterNote.CalofisteriDoppelganger == 51988,
    "North Horn survey-record item IDs are incorrect.");

var northHornMobs = Enum.GetValues<Mob>()
    .Where(mob => (uint)mob is >= 14857 and <= 14923)
    .ToArray();
Assert(northHornMobs.Length == 67, $"Expected 67 North Horn BNpcName rows, got {northHornMobs.Length}.");
Assert((uint)Mob.CrescentCliffkite == 14857 && (uint)Mob.CrescentFlame == 14923,
    "North Horn field-monster ID range is incorrect.");
Assert((uint)Mob.CrescentBibliotaph == 14860 && (uint)Mob.CrescentOiseauRare == 14910,
    "North Horn field-monster semantic names are not aligned with the 7.55 BNpcName sheet.");

var treasurePattern = LogMessageHelper.BuildPattern(
    "在当前区域中感知到了<num(lnum1)>个银宝箱、<num(lnum2)>个铜宝箱……！");
var treasureMatch = Regex.Match("在当前区域中感知到了3个银宝箱、17个铜宝箱……！", treasurePattern);
Assert(treasureMatch.Success, "CN treasure-count LogMessage pattern did not match rendered text.");
Assert(treasureMatch.Groups["lnum1"].Value == "3" && treasureMatch.Groups["lnum2"].Value == "17",
    "CN treasure-count LogMessage captures are incorrect.");

var retainedFateHandles = typeof(BOCCHI.Modules.Fates.Fate)
    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    .Where(field => field.FieldType.FullName == "Dalamud.Game.ClientState.Fates.IFate")
    .ToArray();
Assert(retainedFateHandles.Length == 0,
    "Fate snapshots must not retain an IFate backed by game memory after despawn.");

Console.WriteLine("BOCCHI 7.55 North Horn data and FATE lifecycle smoke tests passed.");
