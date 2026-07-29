using BOCCHI;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Pathfinding;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;

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

var fate2075 = EventData.Fates[2075];
var wispLanding = Aethernet.WillOWispVillage.GetData().Destination;
Assert(NorthHornSouthCrossingRoute.TryCreate(fate2075, wispLanding, out var southCrossingRoute),
    "FATE 2075 must use the South Crossing transit profile from Will-o'-the-Wisp Village.");
Assert(southCrossingRoute.Count == 92,
    "FATE 2075 South Crossing route must retain every verified land waypoint.");
Assert(Vector3.Distance(southCrossingRoute[^1], new Vector3(510f, 15.65f, -30f)) < 0.01f,
    "FATE 2075 South Crossing route must end on the east-bank approach.");
Assert(Vector3.Distance(southCrossingRoute[^1], fate2075.StartPosition!.Value) <= NorthHornSouthCrossingRoute.ArrivalDistance,
    "FATE 2075 South Crossing route must finish inside the event-arrival radius without a fallback pathfind.");
Assert(Vector3.Distance(wispLanding, southCrossingRoute[0]) <= 15.01f
       && southCrossingRoute.Zip(southCrossingRoute.Skip(1), Vector3.Distance).All(distance => distance <= 15.01f),
    "Every FATE 2075 South Crossing leg must remain inside the verified direct-follow radius.");
Assert(southCrossingRoute.All(point => point.Y > 1f),
    "FATE 2075 South Crossing route must not include the river-water shortcut.");
Assert(southCrossingRoute.Any(point => point.X is > 175f and < 256f && point.Z is > -470f and < -420f),
    "FATE 2075 South Crossing route is missing the verified southern lowland transit segment.");
Assert(!NorthHornSouthCrossingRoute.ShouldUse(fate2075, fate2075.StartPosition!.Value),
    "The FATE 2075 South Crossing profile must not override an already-east-bank route.");
Assert(!NorthHornSouthCrossingRoute.ShouldUse(EventData.Fates[2076], wispLanding),
    "The South Crossing profile must not affect another North Horn FATE.");

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

var northTowerDefinitions = TowerHelper.GetDefinitionsForTerritory(ZoneData.NORTHHORN);
Assert(northTowerDefinitions.Count == 2 &&
       northTowerDefinitions.Select(definition => definition.DynamicEventId).SequenceEqual(new uint[] { 64, 65 }),
    "North Horn tower definitions must independently map dynamic events 64 and 65.");
Assert(northTowerDefinitions.All(definition => !definition.HasPlatformGeometry),
    "North Horn platform geometry must remain unset until captured from the live client.");

var towerClock = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);
var magicTowerCycle = new TowerCycleState(towerClock);
var grandMagicTowerCycle = new TowerCycleState(towerClock);
magicTowerCycle.RecordFate();
magicTowerCycle.RecordCriticalEncounter();
Assert(magicTowerCycle.FatesCompleted == 1 && magicTowerCycle.CriticalEncountersCompleted == 1,
    "Magic Tower cycle did not record its modifiers.");
Assert(grandMagicTowerCycle.FatesCompleted == 0 && grandMagicTowerCycle.CriticalEncountersCompleted == 0,
    "North Horn tower cycle counters must not share mutable state.");
magicTowerCycle.MarkEnded(towerClock.AddMinutes(10));
Assert(magicTowerCycle.FatesCompleted == 0 && magicTowerCycle.CriticalEncountersCompleted == 0 &&
       grandMagicTowerCycle.LastTowerEnd == towerClock,
    "Ending one North Horn tower must reset only that tower cycle.");

var northAethernets = ZoneData.GetAethernets(ZoneData.NORTHHORN);
Assert(northAethernets.Count == 6, $"Expected 6 North Horn aethernet entries, got {northAethernets.Count}.");
Assert(northAethernets.Distinct().Count() == northAethernets.Count, "North Horn aethernet IDs must be unique.");
Assert(Aethernet.NorthBaseCamp.GetData().BaseId == 2015429, "North Horn base-camp EObj ID is incorrect.");
Assert(Aethernet.KarnakCitadel.GetData().BaseId == 2015434, "Karnak Citadel EObj ID is incorrect.");

Assert((int)JobId.Ninja == 16 && (int)JobId.Necromancer == 23, "North Horn support-job IDs are incorrect.");
Assert((uint)PlayerStatus.PhantomNinja == 5328 && (uint)PlayerStatus.PhantomNecromancer == 5335,
    "North Horn support-job status IDs are incorrect.");
Assert((uint)PlayerStatus.MagicEvasion == 5316 &&
       (uint)PlayerStatus.DragonSword == 5319 &&
       (uint)PlayerStatus.EarthenWall == 5320 &&
       (uint)PlayerStatus.MagicMightyGuard == 5321 &&
       (uint)PlayerStatus.SmokeBomb == 5327,
    "North Horn support-action status IDs are not aligned with the CN 7.55 Status sheet.");
var northSupportActions = NorthHornSupportActionData.All.OrderBy(set => set.SupportJobRowId).ToArray();
Assert(northSupportActions.Length == 8 &&
       northSupportActions.Select(set => set.SupportJobRowId).SequenceEqual(Enumerable.Range(16, 8).Select(id => (uint)id)),
    "North Horn MKDSupportJob rows 16-23 are incomplete.");
Assert(northSupportActions.All(set => set.Actions.Select(action => action.GeneralActionId)
        .SequenceEqual(Enumerable.Range(31, set.Actions.Count).Select(id => (uint)id))),
    "North Horn support-job GeneralAction slots must remain contiguous from slot 31.");
Assert(NorthHornSupportActionData.Get(JobId.BlueMage).Actions.Last().ActionRowId == 49090 &&
       NorthHornSupportActionData.Get(JobId.RedMage).Actions.First().ActionRowId == 49092 &&
       NorthHornSupportActionData.Get(JobId.Necromancer).Actions.Last().ActionRowId == 49101,
    "North Horn Action-sheet rows are not aligned with the CN 7.55 MKDSupportJob sheet.");
Assert((uint)MonsterNote.AncientGrimoire == 51979 && (uint)MonsterNote.CalofisteriDoppelganger == 51988,
    "North Horn survey-record item IDs are incorrect.");
Assert(Enum.GetValues<SoulShard>()
        .Where(soulShard => (uint)soulShard is >= 51967 and <= 51974)
        .Select(soulShard => (uint)soulShard)
        .SequenceEqual(Enumerable.Range(51967, 8).Select(id => (uint)id)),
    "North Horn Soul Shard item IDs 51967-51974 are incomplete or out of order.");

var confirmedNorthHornNoteDrops = new Dictionary<uint, MonsterNote>
{
    [50] = MonsterNote.CalofisteriDoppelganger,
    [51] = MonsterNote.AlabasterBlade,
    [52] = MonsterNote.AncientGrimoire,
    [53] = MonsterNote.RedDragon,
    [54] = MonsterNote.Algol,
    [57] = MonsterNote.PhantomNecromancer,
    [59] = MonsterNote.PallidDemon,
    [60] = MonsterNote.LittleMage,
    [61] = MonsterNote.KidnapperDemon,
    [63] = MonsterNote.MorphingMage,
};
var actualNorthHornNoteDrops = northCriticalEncounters
    .Where(encounter => encounter.Id is >= 49 and <= 63 && encounter.Note is not null)
    .ToDictionary(encounter => encounter.Id, encounter => encounter.Note!.Value);
Assert(actualNorthHornNoteDrops.Count == confirmedNorthHornNoteDrops.Count,
    $"Expected {confirmedNorthHornNoteDrops.Count} confirmed North Horn CE record drops, got {actualNorthHornNoteDrops.Count}.");
foreach (var (eventId, expectedNote) in confirmedNorthHornNoteDrops)
{
    Assert(actualNorthHornNoteDrops.TryGetValue(eventId, out var actualNote) && actualNote == expectedNote,
        $"North Horn CE {eventId} has an incorrect Investigation Record mapping.");
}

var towerOnlyRecords = new[]
{
    MonsterNote.Amphiptere,
    MonsterNote.SwordDancer,
    MonsterNote.DeathDefier,
    MonsterNote.Catalog,
};
Assert(!northCriticalEncounters.Any(encounter =>
        encounter.Id is >= 49 and <= 63 &&
        encounter.Note is { } note &&
        towerOnlyRecords.Contains(note)),
    "Tower-only Investigation Records 51989-51992 must not be assigned to an ordinary CE.");

var recordAlertConfig = new CriticalEncountersConfig { AlertInvestigationRecords = true };
Assert(confirmedNorthHornNoteDrops.Keys.All(eventId =>
        recordAlertConfig.ShouldAlertForRewards(EventData.CriticalEncounters[eventId])),
    "The Investigation Record alert option must cover every confirmed North Horn CE record drop.");

foreach (var soulShard in Enum.GetValues<SoulShard>())
{
    var soulShardAlertConfig = new CriticalEncountersConfig();
    var property = typeof(CriticalEncountersConfig).GetProperty($"Alert{soulShard}");
    Assert(property is { CanWrite: true, PropertyType: { } propertyType } && propertyType == typeof(bool),
        $"Soul Shard alert configuration is missing the Alert{soulShard} switch.");
    property!.SetValue(soulShardAlertConfig, true);

    Assert(soulShardAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = soulShard }),
        $"The dedicated Soul Shard switch does not enable {soulShard} ({(uint)soulShard}).");
    Assert(Enum.GetValues<SoulShard>()
            .Where(other => other != soulShard)
            .All(other => !soulShardAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = other })),
        $"The Alert{soulShard} switch incorrectly enables another Soul Shard reward.");
}
Assert(!northCriticalEncounters.Any(encounter => encounter.Soulshard is not null),
    "North Horn Soul Shard sources are unconfirmed and must not be guessed in CE reward data.");
Assert(!recordAlertConfig.ShouldAlertForRewards(EventData.CriticalEncounters[49]),
    "A North Horn CE without a confirmed Investigation Record must not trigger the record alert.");

var legacyAlertPropertyNames = new[] { "AlertOracle", "AlertBerserker", "AlertRanger" };
Assert(legacyAlertPropertyNames.All(name =>
        typeof(CriticalEncountersConfig).GetProperty(name) is { CanRead: true, CanWrite: true, PropertyType: { } type } &&
        type == typeof(bool)),
    "Legacy Oracle/Berserker/Ranger configuration properties must remain public writable booleans.");
var legacyAlertConfig = new CriticalEncountersConfig
{
    AlertOracle = true,
    AlertBerserker = true,
    AlertRanger = true,
};
Assert(legacyAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = SoulShard.Oracle }) &&
       legacyAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = SoulShard.Berserker }) &&
       legacyAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = SoulShard.Ranger }),
    "Legacy Oracle/Berserker/Ranger alert properties must remain configuration-compatible.");

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
var retainedDynamicEvents = typeof(CriticalEncounterTracker)
    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    .Where(field => field.FieldType.FullName ==
                    "FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent")
    .ToArray();
Assert(retainedDynamicEvents.Length == 0,
    "Critical Encounter tracking must not retain a native DynamicEvent across frames.");
Assert(typeof(CriticalEncounterSnapshot).GetProperties().All(property =>
        property.PropertyType.FullName is not
            "FFXIVClientStructs.FFXIV.Client.System.String.Utf8String" and not
            "FFXIVClientStructs.FFXIV.Client.Game.UI.MapMarkerData"),
    "Critical Encounter snapshots must copy native strings and map markers into managed values.");
var clientStructsAssembly = Assembly.Load(
    typeof(CriticalEncounterTracker).Assembly.GetReferencedAssemblies()
        .Single(reference => reference.Name == "FFXIVClientStructs"));
var dynamicEventType = clientStructsAssembly.GetType(
    "FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent",
    throwOnError: true)!;
Assert(Marshal.OffsetOf(dynamicEventType, "SecondsRegistrationTime").ToInt64() == 0x6C
       && Marshal.OffsetOf(dynamicEventType, "SecondsWarmupTime").ToInt64() == 0x70,
    "CN 7.55 DynamicEvent timing offsets changed; update the managed snapshot before release.");

var automatorConfig = new AutomatorConfig();
var northFateIds = Enumerable.Range(2072, 13).Select(id => (uint)id).ToArray();
var northCriticalEncounterIds = Enumerable.Range(49, 15).Select(id => (uint)id).ToArray();

Assert(automatorConfig.FatesMap.Keys.Where(northFateIds.Contains).Order().SequenceEqual(northFateIds),
    "Automator must expose one mapping for every North Horn FATE ID 2072 through 2084.");
Assert(automatorConfig.CriticalEncountersMap.Keys.Where(northCriticalEncounterIds.Contains).Order()
        .SequenceEqual(northCriticalEncounterIds),
    "Automator must expose one mapping for every ordinary North Horn CE ID 49 through 63.");
Assert(!automatorConfig.CriticalEncountersMap.ContainsKey(64)
       && !automatorConfig.CriticalEncountersMap.ContainsKey(65),
    "Forked Tower events 64/65 must remain under the tower module, not Automator participation.");
Assert(!automatorConfig.FatesMap[2072] && !automatorConfig.FatesMap[2073],
    "North Horn pot FATE replacements 2072/2073 must preserve the legacy opt-in default.");
Assert(northFateIds.Where(id => id >= 2074).All(id => automatorConfig.FatesMap[id]),
    "Ordinary North Horn Automator FATE switches must be enabled by default.");
Assert(northCriticalEncounterIds.All(id => automatorConfig.CriticalEncountersMap[id]),
    "North Horn Automator CE switches must retain the previous enabled-by-default behavior.");

foreach (var id in northFateIds)
{
    var isolatedConfig = new AutomatorConfig();
    var property = typeof(AutomatorConfig).GetProperty($"DoNorthHornFate{id}");
    Assert(property != null, $"North Horn FATE {id} is missing its dedicated Automator setting.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "CheckboxAttribute"),
        $"North Horn FATE {id} setting is not rendered as a checkbox.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "DependsOnAttribute"),
        $"North Horn FATE {id} setting is not tied to the master FATE switch.");
    property!.SetValue(isolatedConfig, true);
    Assert(isolatedConfig.FatesMap[id], $"North Horn FATE {id} switch is not bound to its map entry.");
    property!.SetValue(isolatedConfig, false);
    Assert(!isolatedConfig.FatesMap[id], $"Disabling North Horn FATE {id} does not disable its map entry.");
}

foreach (var id in northCriticalEncounterIds)
{
    var isolatedConfig = new AutomatorConfig();
    var property = typeof(AutomatorConfig).GetProperty($"DoNorthHornCe{id}");
    Assert(property != null, $"North Horn CE {id} is missing its dedicated Automator setting.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "CheckboxAttribute"),
        $"North Horn CE {id} setting is not rendered as a checkbox.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "DependsOnAttribute"),
        $"North Horn CE {id} setting is not tied to the master CE switch.");
    property!.SetValue(isolatedConfig, false);
    Assert(!isolatedConfig.CriticalEncountersMap[id],
        $"Disabling North Horn CE {id} does not disable its map entry.");
    Assert(northCriticalEncounterIds.Where(other => other != id)
            .All(other => isolatedConfig.CriticalEncountersMap[other]),
        $"Disabling North Horn CE {id} incorrectly changed another CE switch.");
}

Assert(typeof(AutomatorConfig).GetProperty("DoNorthHornCe64") == null
       && typeof(AutomatorConfig).GetProperty("DoNorthHornCe65") == null,
    "Forked Tower Automator checkboxes must not be rendered as dead controls.");

var legacyConfig = new Config
{
    Version = 1,
    AutomatorConfig = new AutomatorConfig
    {
        DoPersistentPots = true,
        DoPleadingPots = false,
    },
};
Assert(legacyConfig.Migrate(), "Legacy configuration version 1 was not migrated.");
Assert(legacyConfig.Version == Config.CurrentVersion
       && legacyConfig.AutomatorConfig.DoNorthHornFate2072
       && !legacyConfig.AutomatorConfig.DoNorthHornFate2073,
    "Legacy pot FATE selections were not copied to North Horn FATE 2072/2073.");
Assert(!legacyConfig.Migrate(), "Configuration migration must be idempotent.");

automatorConfig.DoFates = false;
automatorConfig.DoCriticalEncounters = false;
Assert(northFateIds.All(id => !automatorConfig.FatesMap[id]),
    "The master FATE switch must suppress all North Horn FATE settings.");
Assert(northCriticalEncounterIds.All(id => !automatorConfig.CriticalEncountersMap[id]),
    "The master CE switch must suppress all North Horn CE settings.");

var translationRoot = Path.Combine(Directory.GetCurrentDirectory(), "Translations");
Assert(Directory.Exists(translationRoot), $"Translation directory was not found: {translationRoot}");
var translationFiles = Directory.EnumerateFiles(translationRoot, "*.json", SearchOption.AllDirectories).ToArray();
Assert(translationFiles.Length >= 80, $"Expected the full translation tree, found only {translationFiles.Length} JSON files.");
foreach (var file in translationFiles)
{
    using var _ = JsonDocument.Parse(File.ReadAllText(file));
}

foreach (var language in new[] { "en", "fr", "jp", "zh" })
{
    var file = Path.Combine(translationRoot, language, "modules.automator.json");
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    var configKeys = document.RootElement
        .GetProperty("modules")
        .GetProperty("automator")
        .GetProperty("config");
    Assert(northFateIds.All(id => configKeys.TryGetProperty($"do_north_horn_fate{id}", out _)),
        $"{language} Automator translation is missing a North Horn FATE key.");
    Assert(northCriticalEncounterIds.All(id => configKeys.TryGetProperty($"do_north_horn_ce{id}", out _)),
        $"{language} Automator translation is missing a North Horn CE key.");
    Assert(!configKeys.TryGetProperty("do_north_horn_ce64", out _)
           && !configKeys.TryGetProperty("do_north_horn_ce65", out _),
        $"{language} Automator translation still exposes dead Forked Tower controls.");

    var criticalFile = Path.Combine(translationRoot, language, "modules.critical_encounters.json");
    using var criticalDocument = JsonDocument.Parse(File.ReadAllText(criticalFile));
    var criticalConfigKeys = criticalDocument.RootElement
        .GetProperty("modules")
        .GetProperty("critical_encounters")
        .GetProperty("config");
    foreach (var key in new[]
             {
                 "alert_ninja", "alert_black_mage", "alert_white_mage", "alert_dragoon",
                 "alert_summoner", "alert_blue_mage", "alert_red_mage", "alert_necromancer",
             })
    {
        Assert(criticalConfigKeys.TryGetProperty(key, out var entry)
               && entry.GetProperty("label").ValueKind == JsonValueKind.String
               && entry.GetProperty("tooltip").ValueKind == JsonValueKind.String,
            $"{language} {key} must explicitly state that its event source is pending confirmation.");
    }
}

var fallbackPositions = new Dictionary<uint, Vector3>
{
    [1] = new Vector3(1, 0, 0),
    [2] = new Vector3(10, 0, 0),
    [3] = new Vector3(5, 0, 0),
};
Assert(FallbackRoutePlanner.OrderByEuclideanCost(Vector3.Zero, new uint[] { 2, 99, 3, 1 }, fallbackPositions)
        .SequenceEqual(new uint[] { 1, 3, 2 }),
    "Euclidean fallback ordering must be deterministic and skip nodes without trusted positions.");

var partialGraph = new Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>>
{
    [1] = new() { [2] = (1f, [PathfinderStep.WalkToDestination(2)]) },
    [2] = new() { [3] = (1f, [PathfinderStep.WalkToDestination(3)]) },
    [3] = new(),
};
Assert(!HybridRoutePlanner.HasCompleteFiniteGraph(new uint[] { 1, 2, 3 }, partialGraph),
    "A partial hunt graph must not be classified as complete.");
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, new uint[] { 1, 2, 3 }, partialGraph)
        .SequenceEqual(new uint[] { 1, 2, 3 }),
    "Hybrid routing must consume every verified reachable edge before fallback.");
var deadEndGraph = new Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>>
{
    [1] = new()
    {
        [2] = (1f, [PathfinderStep.WalkToDestination(2)]),
        [3] = (2f, [PathfinderStep.WalkToDestination(3)]),
        [5] = (2f, [PathfinderStep.WalkToDestination(5)]),
    },
    [2] = new(),
    [3] = new() { [4] = (1f, [PathfinderStep.WalkToDestination(4)]) },
    [4] = new(),
    [5] = new() { [4] = (1f, [PathfinderStep.WalkToDestination(4)]) },
};
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, new uint[] { 5, 4, 3, 2, 1 }, deadEndGraph)
        .SequenceEqual(new uint[] { 1, 3, 4 }),
    "Hybrid routing must maximize verified-node coverage before cost and use node IDs as a deterministic tie-break.");
var unreachableGraph = new Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>>
{
    [1] = new() { [2] = (float.MaxValue, []) },
    [2] = new(),
};
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, new uint[] { 1, 2 }, unreachableGraph)
        .SequenceEqual(new uint[] { 1 }),
    "Hybrid routing must stop before an unreachable edge so fallback can append that node.");
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, Array.Empty<uint>(), partialGraph).Count == 0,
    "Hybrid routing must return an empty prefix for an empty node set.");

var precomputeNodes = new[]
{
    new HuntNode(1, new Vector3(1, 2, 3)),
    new HuntNode(2, new Vector3(4, 5, 6)),
};
var precomputeAethernets = new[]
{
    new HuntAethernet(Aethernet.BaseCamp, new Vector3(10, 0, 10), new Vector3(11, 0, 11)),
};
var completedSegments = 0;
var computedData = await NodeDataPrecomputer.ComputeAsync(
    precomputeNodes,
    precomputeAethernets,
    (start, destination) => Task.FromResult(new List<Vector3> { start, destination }),
    _ => completedSegments++,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(completedSegments == NodeDataPrecomputer.GetTaskCount(2, 1),
    "Node precomputation progress did not count every route segment.");
Assert(computedData.NodeToNodeDistances.Values.Sum(routes => routes.Count) == 2
       && computedData.NodeToAethernetDistances.Values.Sum(routes => routes.Count) == 2
       && computedData.AethernetToNodeDistances.Values.Sum(routes => routes.Count) == 2,
    "Node precomputation did not persist every successful route direction.");
Assert(computedData.NodeToAethernetDistances[1].Single().Path.First().X == precomputeNodes[0].Position.X
       && computedData.NodeToAethernetDistances[1].Single().Path.Last().X == precomputeAethernets[0].ShardPosition.X,
    "Node-to-aethernet precomputation direction is reversed.");
Assert(computedData.AethernetToNodeDistances[Aethernet.BaseCamp].Single(route => route.Id == 1).Path.First().X
       == precomputeAethernets[0].ArrivalPosition.X,
    "Aethernet-to-node precomputation must start at the arrival position.");

var timedOutSegments = 0;
var timeoutFailures = 0;
var neverCompletes = new TaskCompletionSource<List<Vector3>>(TaskCreationOptions.RunContinuationsAsynchronously);
var timedOutData = await NodeDataPrecomputer.ComputeAsync(
    precomputeNodes.Take(1).ToArray(),
    precomputeAethernets,
    (_, _) => neverCompletes.Task,
    _ => timedOutSegments++,
    _ => timeoutFailures++,
    TimeSpan.FromMilliseconds(20));
Assert(timedOutSegments == 2 && timeoutFailures == 2
       && timedOutData.NodeToAethernetDistances[1].Count == 0
       && timedOutData.AethernetToNodeDistances[Aethernet.BaseCamp].Count == 0,
    "Timed-out precomputation segments must be counted, reported, and omitted.");

using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
{
    var cancelled = false;
    try
    {
        await NodeDataPrecomputer.ComputeAsync(
            precomputeNodes.Take(1).ToArray(),
            precomputeAethernets,
            (_, _) => neverCompletes.Task,
            segmentTimeout: TimeSpan.FromSeconds(1),
            cancellationToken: cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }

    Assert(cancelled, "Node precomputation did not honor cancellation while an IPC segment was pending.");
}

var atomicDirectory = Path.Combine(Path.GetTempPath(), $"BOCCHI.DataSmoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(atomicDirectory);
try
{
    var atomicFile = Path.Combine(atomicDirectory, "nodes.json");
    await NodeDataPrecomputer.WriteAtomicAsync(atomicFile, computedData);
    var roundTrip = JsonSerializer.Deserialize<NodeDataSchema>(await File.ReadAllTextAsync(atomicFile));
    Assert(roundTrip.NodePositions.Count == computedData.NodePositions.Count,
        "Atomic node-data output did not deserialize with the expected positions.");
    Assert(!Directory.EnumerateFiles(atomicDirectory, "*.tmp").Any(),
        "Atomic node-data output left a temporary file after success.");

    await File.WriteAllTextAsync(atomicFile, "preserve-existing-output");
    using var cancelledWrite = new CancellationTokenSource();
    cancelledWrite.Cancel();
    var writeCancelled = false;
    try
    {
        await NodeDataPrecomputer.WriteAtomicAsync(atomicFile, computedData, cancelledWrite.Token);
    }
    catch (OperationCanceledException)
    {
        writeCancelled = true;
    }

    Assert(writeCancelled && await File.ReadAllTextAsync(atomicFile) == "preserve-existing-output",
        "Cancelled atomic output must leave the previous file untouched.");
    Assert(!Directory.EnumerateFiles(atomicDirectory, "*.tmp").Any(),
        "Cancelled atomic node-data output left a temporary file.");
}
finally
{
    Directory.Delete(atomicDirectory, true);
}

Console.WriteLine("BOCCHI 7.55 North Horn data, lifecycle, config, translation, routing, and precompute smoke tests passed.");
