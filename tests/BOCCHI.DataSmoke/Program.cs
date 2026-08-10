using BOCCHI;
using BOCCHI.Chains;
using BOCCHI.Commands;
using BOCCHI.Data;
using BOCCHI.Data.Traps;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.AggroRange;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.CeCrowdsource;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Currency;
using BOCCHI.Modules.StateManager;
using BOCCHI.Modules.Treasure;
using BOCCHI.Pathfinding;
using BOCCHI.Ui;
using BOCCHI.Ui.Lumin;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void RunCeCrowdsourceTests()
{
    Assert(CeCrowdsourceDisplayPolicy.ResolveState("Battle", null, isActive: false) == "Inactive"
           && CeCrowdsourceDisplayPolicy.ResolveState("Battle", "Inactive", isActive: true) == "Inactive"
           && CeCrowdsourceDisplayPolicy.ResolveState("Battle", null, isActive: true) == "Battle"
           && CeCrowdsourceDisplayPolicy.ResolveState("Register", null, isActive: false) == "Inactive"
           && CeCrowdsourceDisplayPolicy.ResolveState("Register", null, isActive: true) == "Register",
        "Crowdsource CE history must remain visible as ended, while live CE observations keep their active state.");

    var legacyConfig = new Config
    {
        Version = 3,
        CeCrowdsourceConfig = new CeCrowdsourceConfig { ShowOnlyActive = true },
    };
    Assert(legacyConfig.Migrate()
           && legacyConfig.Version == Config.CurrentVersion
           && !legacyConfig.CeCrowdsourceConfig.ShowOnlyActive,
        "The CE history default must migrate existing configurations away from active-only filtering.");

    var crowdsourceSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "CeCrowdsource", "CeCrowdsourceModule.cs"));
    Assert(crowdsourceSource.Contains("public override void OnTerritoryChanged(uint id)", StringComparison.Ordinal)
           && crowdsourceSource.Contains("Interlocked.Increment(ref presenceRevision)", StringComparison.Ordinal)
           && crowdsourceSource.Contains("CacheHeartbeatIdentity()", StringComparison.Ordinal)
           && crowdsourceSource.Contains("CeCrowdsourcePresencePolicy.CanPublishIslandPresence", StringComparison.Ordinal)
           && !crowdsourceSource.Contains("presence.IsIsland && presence.InstanceId == 0", StringComparison.Ordinal),
        "Island companion presence must clear on territory changes, preserve player identity during loading, and reject stale island responses.");

    Assert(CeCrowdsourcePresencePolicy.CanPublishIslandPresence(isIsland: true, zoneServerId: 3538944)
           && !CeCrowdsourcePresencePolicy.CanPublishIslandPresence(isIsland: true, zoneServerId: 0)
           && !CeCrowdsourcePresencePolicy.CanPublishIslandPresence(isIsland: false, zoneServerId: 3538944),
        "Island presence must use the zone server ID and must not require PublicInstance.");

    var islandRecord = new CeRecord("island", 101, 3538944, ZoneData.NORTHHORN, "CE", 56,
        null, 0, "Battle", 1, "upstream", 0, "", true);
    var bozjaRecord = islandRecord with { TerritoryID = 920 };
    Assert(CeCrowdsourceDisplayPolicy.ShouldDisplayRecord(islandRecord)
           && !CeCrowdsourceDisplayPolicy.ShouldDisplayRecord(bozjaRecord),
        "CE history must exclude legacy non-Occult Crescent territories.");
}

static void RunCurrencyTrackerTests()
{
    var start = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
    var now = start;
    int? gold = null;
    int? silver = null;
    var tracker = new CurrencyTracker(() => gold, () => silver, () => now);

    tracker.Tick();
    now = start.AddMinutes(1);
    gold = 2070;
    silver = 94917;
    tracker.Tick();
    Assert(tracker.GetGoldPerHour() == 0 && tracker.GetSilverPerHour() == 0,
        "The first valid currency sample must establish a baseline instead of counting the existing balance as income.");

    now = start.AddMinutes(31);
    gold = 2080;
    tracker.Tick();
    Assert(Math.Abs(tracker.GetGoldPerHour() - 20f) < 0.001f,
        "Only positive balance changes after the baseline must contribute to the hourly rate.");

    now = start.AddMinutes(32);
    gold = null;
    tracker.Tick();
    now = start.AddMinutes(33);
    gold = 2080;
    tracker.Tick();
    Assert(Math.Abs(tracker.GetGoldPerHour() - 18.75f) < 0.001f,
        "A temporarily unavailable inventory read must not replace the last valid balance with zero.");

    now = start.AddMinutes(40);
    tracker.ResetGold();
    now = start.AddMinutes(41);
    tracker.Tick();
    Assert(tracker.GetGoldPerHour() == 0,
        "Reset must wait for a fresh baseline and must not count the current balance as new income.");
}

static IEnumerable<string> GetJsonLeafPaths(JsonElement element, string prefix = "")
{
    foreach (var property in element.EnumerateObject())
    {
        var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
        if (property.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in GetJsonLeafPaths(property.Value, path))
            {
                yield return child;
            }
            continue;
        }

        yield return path;
    }
}

static string? GetJsonStringAtPath(JsonElement root, string path)
{
    var current = root;
    foreach (var segment in path.Split('.'))
    {
        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
        {
            return null;
        }
    }

    return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
}

static HashSet<string> GetFormatArguments(string? value)
{
    return Regex.Matches(value ?? string.Empty, @"\{(\d+)(?:[^}]*)\}")
        .Select(match => match.Groups[1].Value)
        .ToHashSet();
}

static void RunUiShellTests()
{
    Assert(Math.Abs(LuminTheme.CalculateScale(17f) - 1f) < 0.001f
           && Math.Abs(LuminTheme.CalculateScale(25.5f) - 1.5f) < 0.001f,
        "Lumin UI scaling must follow the already-scaled ImGui font without applying a second DPI multiplier.");

    Assert(BocchiUiPolicy.UseSidebar(900f)
           && !BocchiUiPolicy.UseSidebar(BocchiUiPolicy.SidebarBreakpoint - 1f),
        "The main shell must switch to compact navigation below its sidebar breakpoint.");
    Assert(BocchiUiPolicy.GetWorkspaceColumns(700f) == 2
           && BocchiUiPolicy.GetWorkspaceColumns(500f) == 1,
        "Workspace grids must collapse to one column when horizontal space is limited.");

    var outsidePages = BocchiUiPolicy.GetVisiblePages(false, false, false);
    Assert(outsidePages.SequenceEqual(new[]
           {
               MainWindowPage.Overview,
               MainWindowPage.Events,
               MainWindowPage.Explore,
               MainWindowPage.Farming,
               MainWindowPage.Statistics,
           }),
        "The redesigned shell must keep its primary navigation stable outside the island.");

    var northPages = BocchiUiPolicy.GetVisiblePages(true, false, true);
    Assert(northPages.Contains(MainWindowPage.Events)
           && northPages.Contains(MainWindowPage.Explore)
           && northPages.Contains(MainWindowPage.Farming)
           && northPages.Contains(MainWindowPage.AggroRange)
           && !northPages.Contains(MainWindowPage.Tower),
        "North Horn must expose its operational pages and contextual aggro page.");
    Assert(BocchiUiPolicy.GetVisiblePages(true, true, false).Contains(MainWindowPage.Tower),
        "The Forked Tower page must appear only when its context is active.");

    Assert(BocchiUiPolicy.GetSettingsGroup("AutomatorModule") == BocchiSettingsGroup.Automation
           && BocchiUiPolicy.GetSettingsGroup("CriticalEncountersModule") == BocchiSettingsGroup.Events
           && BocchiUiPolicy.GetSettingsGroup("TreasureModule") == BocchiSettingsGroup.Explore
           && BocchiUiPolicy.GetSettingsGroup("MobFarmerModule") == BocchiSettingsGroup.Farming
           && BocchiUiPolicy.GetSettingsGroup("WindowManagerModule") == BocchiSettingsGroup.DisplayAndNotifications
           && BocchiUiPolicy.GetSettingsGroup("DataModule") == BocchiSettingsGroup.Advanced,
        "Settings navigation must group modules by user task instead of module load order.");
    Assert(BocchiUiPolicy.MatchesSettingsSearch("Treasure Hunt", BocchiSettingsGroup.Explore, "treasure")
           && BocchiUiPolicy.MatchesSettingsSearch("宝箱猎人", BocchiSettingsGroup.Explore, "探索")
           && BocchiUiPolicy.MatchesSettingsSearch("Chasse", BocchiSettingsGroup.Automation, "Automatisation", "Automatisation")
           && BocchiUiPolicy.MatchesSettingsSearch("宝箱", BocchiSettingsGroup.Farming, "モブ狩り", "モブ狩り")
           && !BocchiUiPolicy.MatchesSettingsSearch("宝箱猎人", BocchiSettingsGroup.Explore, "FATE"),
        "Settings search must match localized module or group labels without leaking unrelated entries.");

    var stopped = BocchiOperationPolicy.Create(new BocchiOperationInput(
        BocchiOperationState.Stopped,
        null,
        null,
        false,
        false,
        false));
    Assert(stopped.State == BocchiOperationState.Stopped && !stopped.CanStopAll,
        "An idle aggregate snapshot must not offer a meaningless stop action.");

    var manualTreasure = BocchiOperationPolicy.Create(new BocchiOperationInput(
        BocchiOperationState.Stopped,
        "Previous startup failure",
        null,
        true,
        false,
        false));
    Assert(manualTreasure.State == BocchiOperationState.Running
           && manualTreasure.Operation == "Treasure hunt"
           && manualTreasure.Source == BocchiOperationSource.Manual
           && manualTreasure.CanStopAll,
        "Manual treasure hunting must override stale Automator failure text in the global runtime truth.");

    var automatic = BocchiOperationPolicy.Create(new BocchiOperationInput(
        BocchiOperationState.Running,
        null,
        "Critical Encounter",
        true,
        false,
        false));
    Assert(automatic.Operation == "Critical Encounter"
           && automatic.Source == BocchiOperationSource.Automatic,
        "An active Automator operation must remain the primary aggregate task.");

    var stopAllState = new AutomatorRunStateMachine();
    Assert(stopAllState.RequestStopAll() == AutomatorRunAction.BeginStop
           && stopAllState.State == AutomatorRunState.Stopping,
        "Stop all must enter the stop drain even when Automator itself was already stopped.");
    Assert(stopAllState.RequestStopAll() == AutomatorRunAction.None,
        "Repeated stop-all requests must remain idempotent while the stop drain is active.");
    stopAllState.CompleteStop();
    Assert(stopAllState.State == AutomatorRunState.Stopped,
        "The forced stop-all drain must settle back to Stopped.");

    var mainWindowSource = File.ReadAllText(Path.Combine("BOCCHI", "Windows", "MainWindow.cs"));
    Assert(!mainWindowSource.Contains("CollapsingHeader", StringComparison.Ordinal),
        "The redesigned main shell must not use collapsing headers as primary navigation.");
    Assert(!Regex.IsMatch(mainWindowSource, @"Checkbox\([^\r\n]*CompactMainWindow"),
        "Compact mode must be a button control, not a checkbox.");
    Assert(mainWindowSource.Contains("ImGuiWindowFlags.NoCollapse", StringComparison.Ordinal)
           && mainWindowSource.Contains("compactTitleBarButton", StringComparison.Ordinal)
           && mainWindowSource.Contains("SetCompactMode(!config.CompactMainWindow)", StringComparison.Ordinal),
        "Title-bar and command-bar compact controls must share one synchronized state.");
    Assert(mainWindowSource.Contains("ImGuiStyleVar.WindowTitleAlign", StringComparison.Ordinal)
           && mainWindowSource.Contains("new Vector2(0f, 0.5f)", StringComparison.Ordinal)
           && !mainWindowSource.Contains("TitleBarButtons.Remove(", StringComparison.Ordinal),
        "Compact mode must left-align its title without removing title-bar controls.");
    Assert(mainWindowSource.Contains("RequestStopAll", StringComparison.Ordinal),
        "The global command bar must call an explicit stop-all operation.");

    var configWindowSource = File.ReadAllText(Path.Combine("BOCCHI", "Windows", "ConfigWindow.cs"));
    Assert(configWindowSource.Contains("LuminWidgets.Checkbox(label, description, ref value)", StringComparison.Ordinal),
        "Boolean settings must use the compact Lumin switch instead of an oversized check box.");
    Assert(configWindowSource.Contains("SettingsSearch", StringComparison.Ordinal)
           && configWindowSource.Contains("GetSettingsGroup", StringComparison.Ordinal),
        "The settings shell must expose grouped search navigation.");
    Assert(configWindowSource.Contains("ConfirmDisableAll", StringComparison.Ordinal)
           && configWindowSource.Contains("ConfirmClearMobs", StringComparison.Ordinal),
        "Destructive settings actions must require an explicit confirmation step.");

    var towerPanelSource = File.ReadAllText(Path.Combine("BOCCHI", "Modules", "ForkedTower", "Panel.cs"));
    var criticalPanelSource = File.ReadAllText(Path.Combine("BOCCHI", "Modules", "CriticalEncounters", "Panel.cs"));
    Assert(towerPanelSource.Contains("ShowAdvancedUi", StringComparison.Ordinal)
           && criticalPanelSource.Contains("ShowAdvancedUi", StringComparison.Ordinal),
        "Raw tower IDs, coordinates, and capture tools must stay behind the Advanced UI preference.");

    var towerRunSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "ForkedTower", "TowerRun.cs"));
    Assert(!towerRunSource.Contains("ThunderZone.Render", StringComparison.Ordinal),
        "The opaque ice/fire/thunder final-boss overlay must remain disabled until its phase-gated redesign.");

    var responsivePanelSources = new[]
    {
        mainWindowSource,
        configWindowSource,
        File.ReadAllText(Path.Combine("BOCCHI", "Modules", "AggroRange", "Panel.cs")),
        File.ReadAllText(Path.Combine("BOCCHI", "Modules", "Treasure", "Panel.cs")),
        File.ReadAllText(Path.Combine("BOCCHI", "Modules", "MobFarmer", "Panel.cs")),
    };
    Assert(responsivePanelSources.All(source => source.Contains("GetWorkspaceColumns", StringComparison.Ordinal)),
        "Every event or metric grid named by the UI spec must use the responsive column policy.");
    Assert(mainWindowSource.Contains("windows.main.", StringComparison.Ordinal)
           && configWindowSource.Contains("windows.config.", StringComparison.Ordinal),
        "The redesigned shells must route visible copy through their translation namespaces.");

    HashSet<string>? expectedMainKeys = null;
    HashSet<string>? expectedConfigKeys = null;
    foreach (var language in new[] { "en", "fr", "jp", "zh" })
    {
        using var mainDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine("Translations", language, "windows.main.json")));
        using var configDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine("Translations", language, "windows.config.json")));
        var mainKeys = GetJsonLeafPaths(mainDocument.RootElement.GetProperty("windows").GetProperty("main")).ToHashSet();
        var configKeys = GetJsonLeafPaths(configDocument.RootElement.GetProperty("windows").GetProperty("config")).ToHashSet();
        expectedMainKeys ??= mainKeys;
        expectedConfigKeys ??= configKeys;
        Assert(expectedMainKeys.SetEquals(mainKeys),
            $"{language} main-window shell keys must match the English shell contract.");
        Assert(expectedConfigKeys.SetEquals(configKeys),
            $"{language} config-window shell keys must match the English shell contract.");
    }

    var automatorWindowSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "Automator", "AutomatorWindow.cs"));
    var mainReferences = Regex.Matches(
            mainWindowSource + automatorWindowSource,
            @"(?<!\.)\bT\(""([^""]+)""\)")
        .Select(match => match.Groups[1].Value)
        .ToHashSet();
    var configReferences = Regex.Matches(
            configWindowSource,
            @"(?<!\.)\bT\(""([^""]+)""\)")
        .Select(match => match.Groups[1].Value)
        .ToHashSet();
    Assert(expectedMainKeys != null && mainReferences.All(expectedMainKeys.Contains),
        "Every literal windows.main translation referenced by the shell must exist in the key contract.");
    Assert(expectedConfigKeys != null && configReferences.All(expectedConfigKeys.Contains),
        "Every literal windows.config translation referenced by the shell must exist in the key contract.");
    Assert(Enum.GetValues<BocchiSettingsGroup>().All(group =>
            expectedConfigKeys!.Contains($"groups.{BocchiUiPolicy.GetSettingsGroupKey(group)}")),
        "Every dynamic localized settings group must exist in the config-window key contract.");

    var moduleUiSourcePaths = new Dictionary<string, string[]>
    {
        ["automator"] =
        [
            Path.Combine("BOCCHI", "Modules", "Automator", "AutomatorModule.cs"),
            Path.Combine("BOCCHI", "Modules", "Automator", "Panel.cs"),
        ],
        ["aggro_range"] = [Path.Combine("BOCCHI", "Modules", "AggroRange", "Panel.cs")],
        ["buff"] = [Path.Combine("BOCCHI", "Modules", "Buff", "Panel.cs")],
        ["carrots"] = [Path.Combine("BOCCHI", "Modules", "Carrots", "Panel.cs")],
        ["critical_encounters"] = [Path.Combine("BOCCHI", "Modules", "CriticalEncounters", "Panel.cs")],
        ["currency"] = [Path.Combine("BOCCHI", "Modules", "Currency", "Panel.cs")],
        ["exp"] = [Path.Combine("BOCCHI", "Modules", "Exp", "Panel.cs")],
        ["fates"] = [Path.Combine("BOCCHI", "Modules", "Fates", "Panel.cs")],
        ["mob_farmer"] = [Path.Combine("BOCCHI", "Modules", "MobFarmer", "Panel.cs")],
        ["treasure"] = [Path.Combine("BOCCHI", "Modules", "Treasure", "Panel.cs")],
    };
    var criticalPanelPath = Path.Combine("BOCCHI", "Modules", "CriticalEncounters", "Panel.cs");
    foreach (var sourcePath in moduleUiSourcePaths.Values.SelectMany(paths => paths).Distinct())
    {
        if (sourcePath == criticalPanelPath)
        {
            continue;
        }

        Assert(!Regex.IsMatch(File.ReadAllText(sourcePath), @"[\u4E00-\u9FFF]"),
            $"Normal workspace UI must not hard-code Chinese copy: {sourcePath}");
    }

    var towerHandlerIndex = criticalPanelSource.IndexOf("private void HandleTower", StringComparison.Ordinal);
    Assert(towerHandlerIndex > 0
           && !Regex.IsMatch(criticalPanelSource[..towerHandlerIndex], @"[\u4E00-\u9FFF]"),
        "The normal Critical Encounter workspace must not hard-code Chinese copy; advanced tower diagnostics are separate.");

    var translationReferencePattern = new Regex(
        @"(?:(?<![\w.])T|module\.T)\(""([^""]+)""\)",
        RegexOptions.CultureInvariant);
    var moduleUiTranslationContracts = moduleUiSourcePaths.ToDictionary(
        pair => pair.Key,
        pair => pair.Value
            .SelectMany(path => translationReferencePattern.Matches(File.ReadAllText(path)))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray());
    var expectedFormatArguments = new Dictionary<(string Module, string Key), HashSet<string>>();
    foreach (var language in new[] { "en", "fr", "jp", "zh" })
    {
        foreach (var (module, requiredKeys) in moduleUiTranslationContracts)
        {
            var path = Path.Combine("Translations", language, $"modules.{module}.json");
            Assert(File.Exists(path), $"{language} must provide the {module} UI translation file.");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var moduleRoot = document.RootElement.GetProperty("modules").GetProperty(module);
            var moduleKeys = GetJsonLeafPaths(moduleRoot).ToHashSet();
            Assert(requiredKeys.All(moduleKeys.Contains),
                $"{language} {module} UI translations must contain every literal source reference.");

            foreach (var key in requiredKeys)
            {
                var formatArguments = GetFormatArguments(GetJsonStringAtPath(moduleRoot, key));
                var contractKey = (module, key);
                if (language == "en")
                {
                    expectedFormatArguments[contractKey] = formatArguments;
                    continue;
                }

                Assert(expectedFormatArguments[contractKey].SetEquals(formatArguments),
                    $"{language} {module}.{key} must preserve the English format placeholders.");
            }
        }
    }

    foreach (var file in Directory.EnumerateFiles("Translations", "*.json", SearchOption.AllDirectories))
    {
        using var _ = JsonDocument.Parse(File.ReadAllText(file));
    }
}

static void RunSpiritPotPredictionTests()
{
    var directionNames = new[] { "正北", "东北", "正东", "东南", "正南", "西南", "正西", "西北" };
    for (var index = 0; index < directionNames.Length; index++)
    {
        Assert(SpiritPotTreasurePredictor.TryParseHint(
                   $"财宝好像是在{directionNames[index]}方向很近的地方！",
                   Vector3.Zero,
                   out var parsed)
               && parsed.Direction == (SpiritPotDirection)index,
            $"Spirit-pot direction {directionNames[index]} must map to the correct world X/Z sector.");
    }

    var distanceCases = new[]
    {
        (Text: "很近", Band: SpiritPotDistanceBand.VeryNear, Min: 0f, Max: 20f),
        (Text: "不远", Band: SpiritPotDistanceBand.Near, Min: 20f, Max: 100f),
        (Text: "稍远", Band: SpiritPotDistanceBand.Far, Min: 100f, Max: 200f),
        (Text: "很远", Band: SpiritPotDistanceBand.VeryFar, Min: 200f, Max: float.PositiveInfinity),
    };
    foreach (var distanceCase in distanceCases)
    {
        Assert(SpiritPotTreasurePredictor.TryParseHint(
                   $"财宝好像是在正北方向{distanceCase.Text}的地方！",
                   Vector3.Zero,
                   out var parsed)
               && parsed.DistanceBand == distanceCase.Band
               && parsed.MinimumDistance == distanceCase.Min
               && parsed.MaximumDistance == distanceCase.Max,
            $"Spirit-pot distance band {distanceCase.Text} must preserve its calibrated bounds.");
    }

    var predictor = new SpiritPotTreasurePredictor();
    var northCandidate = new Vector3(0f, 12f, -50f);
    var candidates = new[]
    {
        northCandidate,
        new Vector3(50f, 12f, 0f),
        new Vector3(0f, 12f, -150f),
        new Vector3(0f, 12f, -250f),
    };
    Assert(predictor.TryApplyHint(
               "财宝好像是在正北方向不远的地方！",
               Vector3.Zero,
               candidates)
           && predictor.Candidates.SequenceEqual(new[] { northCandidate }),
        "A spirit-pot hint must filter the 68-point universe by direction and distance.");
    Assert(predictor.TryApplyHint(
               "财宝好像是在正西方向不远的地方！",
               new Vector3(50f, 0f, -50f),
               candidates)
           && predictor.Candidates.SequenceEqual(new[] { northCandidate })
           && predictor.Hints.Count == 2,
        "Multiple spirit-pot hints must intersect instead of replacing the earlier search area.");
    Assert(predictor.TryApplyHint(
               "财宝好像是在正南方向很近的地方！",
               Vector3.Zero,
               candidates)
           && predictor.HasConflict
           && predictor.Candidates.SequenceEqual(new[] { northCandidate })
           && predictor.Hints.Count == 2,
        "A conflicting hint must retain the last valid candidate set.");
    Assert(SpiritPotTreasurePredictor.ShouldResetForMessage("发现了财宝！！")
           && SpiritPotTreasurePredictor.ShouldResetForMessage("时间过了太久，已经发现的财宝消失了。")
           && SpiritPotTreasurePredictor.ShouldResetForMessage("似乎能够告知第二处财宝所在地！")
           && !SpiritPotTreasurePredictor.ShouldResetForMessage("很想要圣灵药。"),
        "Spirit-pot prediction lifecycle messages must clear stale areas without treating a potion request as completion.");
}

static void RunTreasureRoutePolicyTests()
{
    var route = NorthHornTreasureRoute.NodeIds;
    Assert(route.Count == 68
           && route.Distinct().Count() == 68
           && route[0] == NorthHornTreasureRoute.WaypointIdBase + 1
           && route[^1] == NorthHornTreasureRoute.WaypointIdBase + 68
           && route.All(NorthHornTreasureRoute.IsWaypointId),
        "The numbered North Horn loop must use 68 isolated synthetic waypoint IDs.");
    Assert(NorthHornTreasureRoute.MapPoints.Count == NorthHornTreasureRoute.RouteCount
           && NorthHornTreasureRoute.MapPoints.All(point =>
               point.X >= 0f
               && point.X <= NorthHornTreasureRoute.MapImageSize
               && point.Y >= 0f
               && point.Y <= NorthHornTreasureRoute.MapImageSize),
        "Every numbered route point must remain inside the embedded map image.");
    var routePointSnapshot = string.Join(
        ";",
        NorthHornTreasureRoute.MapPoints.Select(point => $"{point.X:0},{point.Y:0}"));
    Assert(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(routePointSnapshot)))
           == "581C7ABBA05C632FE064B8F50E223A57B6CD446A1F480463CCD901C90F50F9A6",
        "The 1-68 image checkpoint order must match the reviewed route snapshot.");

    var packagedPositions = Enumerable.Range(0, NorthHornTreasureRoute.RouteCount)
        .Select(index => new Vector3(index * 23f - 700f, index % 9, index * 17f - 500f))
        .ToArray();
    var allPositions = NorthHornTreasureRoute.BuildWaypointPositions(packagedPositions);
    Assert(allPositions.Count == NorthHornTreasureRoute.RouteCount
           && allPositions.Values.Distinct().Count() == NorthHornTreasureRoute.RouteCount
           && allPositions.Values.All(packagedPositions.Contains),
        "Every route checkpoint must own one unique real packaged treasure position.");
    var incompletePositions = NorthHornTreasureRoute.BuildWaypointPositions(packagedPositions.Take(2));
    Assert(incompletePositions.Count == 2
           && incompletePositions.Values.Distinct().Count() == 2,
        "An incomplete layout must leave checkpoints missing instead of reusing or fabricating coordinates.");

    var northBaseMapPoint = NorthHornTreasureRoute.WorldToMapPoint(
        ZoneData.Aetherytes[ZoneData.NORTHHORN]);
    Assert(northBaseMapPoint.X > 900f && northBaseMapPoint.Y > 900f,
        "North Horn base camp must map to the lower-right corner of the supplied route image.");

    var manual = NorthHornTreasureRoute.OrderNodes(
        Vector3.Zero,
        route,
        allPositions,
        TreasureRouteStartMode.Manual,
        68);
    Assert(manual.Count == 68 && manual[0] == route[67] && manual[1] == route[0],
        "Manual route 68 must wrap to route 1 without losing a node.");
    Assert(NorthHornTreasureRoute.OrderNodes(
               Vector3.Zero,
               route,
               allPositions,
               TreasureRouteStartMode.Manual,
               5)
           .Take(4)
           .SequenceEqual(route.Skip(4).Take(4))
           && NorthHornTreasureRoute.OrderNodes(
                   Vector3.Zero,
                   route,
                   allPositions,
                   TreasureRouteStartMode.Manual,
                   54)
               .Take(3)
               .SequenceEqual(route.Skip(53).Take(3)),
        "North Horn numbered travel must preserve 5-6-7-8 and 54-55-56 without future-node promotion.");

    var sparse = new[] { route[0], route[2], route[4] };
    Assert(NorthHornTreasureRoute.OrderNodes(
               Vector3.Zero,
               sparse,
               allPositions,
               TreasureRouteStartMode.Manual,
               2)
           .SequenceEqual(new[] { route[2], route[4], route[0] }),
        "A filtered manual start must advance to the next valid numbered node.");

    allPositions[route[10]] = new Vector3(2, 0, 0);
    allPositions[route[40]] = new Vector3(20, 0, 0);
    Assert(NorthHornTreasureRoute.OrderNodes(
               Vector3.Zero,
               route,
               allPositions,
               TreasureRouteStartMode.Nearest,
               1)[0] == route[10],
        "Nearest start mode must rotate the loop at the closest valid node.");

    Assert(NorthHornCurrentTreasurePolicy.IsMatch(
               Vector3.Zero,
               new Vector3(12f, 0f, 0f),
               12f)
           && !NorthHornCurrentTreasurePolicy.IsMatch(
               Vector3.Zero,
               new Vector3(12.01f, 0f, 0f),
               12f),
        "Real-time North Horn detection must only satisfy the current checkpoint within 12 yalms.");
    Assert(TreasureLevelPolicy.IsEligible(
               isNorthHorn: true,
               verifiedLevel: null,
               maximumLevel: 10)
           && !TreasureLevelPolicy.IsEligible(
               isNorthHorn: false,
               verifiedLevel: null,
               maximumLevel: 10)
           && !TreasureLevelPolicy.IsEligible(
               isNorthHorn: true,
               verifiedLevel: 11,
               maximumLevel: 10),
        "North Horn live coffers without verified level rows must remain detectable without bypassing known level limits.");
    Assert(LiveTreasureObjectPolicy.ShouldTrack(
               isValid: true,
               isTargetable: true,
               isOpened: false)
           && !LiveTreasureObjectPolicy.ShouldTrack(
               isValid: true,
               isTargetable: true,
               isOpened: true),
        "An opened coffer must never be promoted into the hunt route again while its object remains loaded.");
    Assert(NorthHornRouteTransitPolicy.AllowsInitialTransit(
               isNorthHorn: true,
               stepIndex: 0)
           && !NorthHornRouteTransitPolicy.AllowsInitialTransit(
               isNorthHorn: true,
               stepIndex: 1)
           && !NorthHornRouteTransitPolicy.AllowsInitialTransit(
               isNorthHorn: false,
               stepIndex: 0),
        "Only the first North Horn route node may compare walk, Return, and aethernet startup costs.");
    Assert(!NorthHornRouteTransitPolicy.AllowsForcedRecovery(isNorthHorn: true)
           && NorthHornRouteTransitPolicy.AllowsForcedRecovery(isNorthHorn: false),
        "North Horn patrol and live-coffer nodes must never trigger forced Return recovery after startup.");
    Assert(NorthHornRouteRejoinPolicy.PreservePlannedOrder(
               new[] { route[6], route[7], route[53], route[54] })
           .SequenceEqual(new[] { route[6], route[7], route[53], route[54] }),
        "Opening a live treasure must resume at the interrupted route number without rotating to the nearest remaining point.");

    Assert(!TreasureObjectMatchPolicy.IsMatch(
               isNorthHorn: false,
               nodeId: 1789,
               candidateBaseId: 1790,
               distanceSquared: 1f,
               matchRadius: 12f),
        "South Horn must never fall back to a different treasure BaseId.");
    Assert(!TreasureObjectMatchPolicy.IsMatch(
               isNorthHorn: true,
               nodeId: route[0],
               candidateBaseId: 1789,
               distanceSquared: 1f,
               matchRadius: 12f),
        "A synthetic North Horn patrol waypoint must never resolve to a treasure object.");
    Assert(TreasureObjectMatchPolicy.IsMatch(
               isNorthHorn: true,
               nodeId: 1789,
               candidateBaseId: 1790,
               distanceSquared: 1f,
               matchRadius: 12f),
        "A real North Horn diversion may use bounded position fallback.");
    Assert(!TreasureObjectMatchPolicy.IsMatch(
               isNorthHorn: true,
               nodeId: 1789,
               candidateBaseId: 1790,
               distanceSquared: 1f,
               matchRadius: 12f,
               allowPositionFallback: false),
        "North Horn position fallback must not bypass MaxLevel eligibility.");

    Assert(typeof(TreasureConfig).GetProperty("NorthHornRouteStartMode") != null
           && typeof(TreasureConfig).GetProperty("SouthHornRouteStartMode") == null
           && typeof(TreasureConfig).GetProperty("ShowSpiritPotPrediction") != null,
        "The numbered route controls must be persisted as North Horn settings.");
    var treasureHuntSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "Treasure", "TreasureHunt.cs"));
    Assert(treasureHuntSource.Contains("ZoneData.IsInNorthHorn()", StringComparison.Ordinal)
           && !treasureHuntSource.Contains("ZoneData.IsInSouthHorn()", StringComparison.Ordinal)
           && treasureHuntSource.Contains("treasure-route-north-horn.png", StringComparison.Ordinal),
        "The route UI and runtime must be wired to North Horn and its correctly named asset.");
    Assert(treasureHuntSource.Contains(
               "NorthHornRouteTransitPolicy.AllowsInitialTransit(ZoneData.IsInNorthHorn(), stepIndex)",
               StringComparison.Ordinal)
           && treasureHuntSource.Contains(
               "NorthHornRouteTransitPolicy.AllowsForcedRecovery(ZoneData.IsInNorthHorn())",
               StringComparison.Ordinal),
        "Treasure hunt must separate one-time startup transit from forced runtime recovery.");
    Assert(treasureHuntSource.Contains(
               "NorthHornRouteRejoinPolicy.PreservePlannedOrder(remainingNodeIds)",
               StringComparison.Ordinal)
           && !treasureHuntSource.Contains(
               "LiveTreasurePriorityPolicy.Select",
               StringComparison.Ordinal),
        "Treasure hunt must preserve numbered order and never promote a future live coffer.");

    RunSpiritPotPredictionTests();

    var hunterSource = File.ReadAllText(Path.Combine("BOCCHI", "Pathfinding", "Hunter.cs"));
    var resetHunterStart = hunterSource.IndexOf(
        "protected void ResetHunter(bool keepRunning)",
        StringComparison.Ordinal);
    var teardownStart = hunterSource.IndexOf(
        "protected virtual void Teardown()",
        resetHunterStart,
        StringComparison.Ordinal);
    Assert(resetHunterStart >= 0 && teardownStart > resetHunterStart,
        "Hunter reset lifecycle must remain discoverable by the stop regression test.");
    var resetHunterSource = hunterSource[resetHunterStart..teardownStart];
    Assert(resetHunterSource.Contains("AggroAvoidanceNavigation.Stop();", StringComparison.Ordinal)
           && !resetHunterSource.Contains("navigation.IsReady()", StringComparison.Ordinal),
        "The independent hunt Stop button must cancel pending navigation even while vnavmesh is not ready.");
    Assert(hunterSource.Contains("ShouldUseInitialTransit(nodeId)", StringComparison.Ordinal)
           && hunterSource.Contains("ShouldUseForcedRecovery(nodeId)", StringComparison.Ordinal),
        "Proactive transit and forced recovery must use independent hunt policies.");
    Assert(hunterSource.Contains("includeWalkTeleportCandidate: true", StringComparison.Ordinal),
        "Initial hunt routing must compare walking to a nearby shard before teleporting to the route start.");
    var promotePriorityStart = hunterSource.IndexOf(
        "private bool TryPromotePriorityNode()",
        StringComparison.Ordinal);
    var rejoinRouteStart = hunterSource.IndexOf(
        "private void RejoinRemainingRoute()",
        promotePriorityStart,
        StringComparison.Ordinal);
    Assert(promotePriorityStart >= 0 && rejoinRouteStart > promotePriorityStart,
        "Priority diversion navigation must remain discoverable by the treasure interaction regression test.");
    var promotePrioritySource = hunterSource[promotePriorityStart..rejoinRouteStart];
    var stopPreviousNavigation = promotePrioritySource.IndexOf(
        "AggroAvoidanceNavigation.Stop(vnav);",
        StringComparison.Ordinal);
    var resetPromotedNode = promotePrioritySource.IndexOf(
        "ResetNodeNavigation();",
        StringComparison.Ordinal);
    Assert(stopPreviousNavigation >= 0
           && resetPromotedNode > stopPreviousNavigation,
        "Promoting a live treasure must stop the old route movement before resetting navigation for the coffer.");
}

if (args.Contains("--ce-crowdsource", StringComparer.OrdinalIgnoreCase))
{
    RunCeCrowdsourceTests();
    Console.WriteLine("BOCCHI CE crowdsource display tests passed.");
    return;
}

if (args.Contains("--currency-tracker", StringComparer.OrdinalIgnoreCase))
{
    RunCurrencyTrackerTests();
    Console.WriteLine("BOCCHI currency baseline and hourly-rate tests passed.");
    return;
}

if (args.Contains("--ui-shell", StringComparer.OrdinalIgnoreCase))
{
    RunUiShellTests();
    Console.WriteLine("BOCCHI responsive UI shell and aggregate operation policy tests passed.");
    return;
}

if (args.Contains("--treasure-route", StringComparer.OrdinalIgnoreCase))
{
    RunTreasureRoutePolicyTests();
    Console.WriteLine("BOCCHI North Horn strict numbered route and current-treasure matching tests passed.");
    return;
}

if (args.Contains("--automator-run-state", StringComparer.OrdinalIgnoreCase))
{
    Assert(PostActivityReturnPolicy.ShouldQueue(EventType.Fate, independentNavigationRunning: false)
           && !PostActivityReturnPolicy.ShouldQueue(EventType.Fate, independentNavigationRunning: true),
        "Independent navigation must suppress the automatic post-FATE return without changing normal FATE behavior.");

    var runState = new AutomatorRunStateMachine();
    Assert(runState.State == AutomatorRunState.Stopped,
        "Automation must initialize stopped.");
    Assert(runState.RequestEnabled(true) == AutomatorRunAction.BeginStart
           && runState.State == AutomatorRunState.Starting
           && runState.TargetEnabled,
        "An enable request must publish Starting immediately.");
    Assert(runState.RequestEnabled(true) == AutomatorRunAction.None,
        "Repeated enable requests must be idempotent.");
    runState.SetStartingDetail("正在等待依赖");
    Assert(runState.Detail == "正在等待依赖",
        "Starting detail must expose dependency progress without changing state.");
    runState.CompleteStart();
    Assert(runState.State == AutomatorRunState.Running
           && runState.CanRunWork
           && runState.Detail == null,
        "A ready start must become Running and clear transitional detail.");
    Assert(runState.RequestEnabled(false) == AutomatorRunAction.BeginStop
           && runState.State == AutomatorRunState.Stopping
           && !runState.TargetEnabled
           && !runState.CanRunWork,
        "A stop request must block new work immediately.");
    Assert(runState.RequestEnabled(false) == AutomatorRunAction.None,
        "Repeated stop requests must be idempotent.");
    runState.CompleteStop();
    Assert(runState.State == AutomatorRunState.Stopped,
        "Cleanup completion must publish Stopped.");

    var failedStart = new AutomatorRunStateMachine();
    failedStart.RequestEnabled(true);
    failedStart.FailStart("vnavmesh 未加载");
    Assert(failedStart.State == AutomatorRunState.Stopped
           && failedStart.Detail == "vnavmesh 未加载"
           && !failedStart.TargetEnabled,
        "Rejected starts must turn off and preserve a user-readable reason.");

    var cancelledStart = new AutomatorRunStateMachine();
    cancelledStart.RequestEnabled(true);
    Assert(cancelledStart.RequestEnabled(false) == AutomatorRunAction.BeginStop
           && !cancelledStart.TargetEnabled,
        "Disable during startup must prevent a later running transition.");
    cancelledStart.CompleteStart();
    Assert(cancelledStart.State == AutomatorRunState.Stopping,
        "A stale completion callback must not revive a cancelled start.");
    cancelledStart.CompleteStop();

    Assert(AutomatorStopPolicy.ShouldRetry(providersStopped: false, completedAttempts: 1)
           && !AutomatorStopPolicy.ShouldRetry(providersStopped: true, completedAttempts: 1)
           && !AutomatorStopPolicy.ShouldRetry(
               providersStopped: false,
               completedAttempts: AutomatorStopPolicy.MaxAttempts),
        "Stop draining must retry transient IPC gaps without remaining stuck forever.");

    Assert(AutomatorStartPolicy.Evaluate(vnavmeshAvailable: true, lifestreamLoaded: true)
           == AutomatorStartReadiness.Ready,
        "Loaded navigation dependencies must permit startup.");
    Assert(AutomatorStartPolicy.Evaluate(vnavmeshAvailable: false, lifestreamLoaded: true)
           == AutomatorStartReadiness.VnavmeshUnavailable,
        "Missing vnavmesh must reject startup immediately.");
    Assert(AutomatorStartPolicy.Evaluate(vnavmeshAvailable: true, lifestreamLoaded: false)
           == AutomatorStartReadiness.LifestreamUnavailable,
        "Missing Lifestream must reject startup immediately.");

    var mainWindowSource = File.ReadAllText(Path.Combine("BOCCHI", "Windows", "MainWindow.cs"));
    Assert(!mainWindowSource.Contains("EnsureDailyRoutinesCommandModules", StringComparison.Ordinal),
        "Rendering the compact window must never enable DailyRoutines modules.");
    var postInitializeStart = mainWindowSource.IndexOf("public override void PostInitialize()", StringComparison.Ordinal);
    var preDrawStart = mainWindowSource.IndexOf("public override void PreDraw()", postInitializeStart, StringComparison.Ordinal);
    var postInitializeSource = mainWindowSource[postInitializeStart..preDrawStart];
    Assert(!postInitializeSource.Contains("RequestEnabled(", StringComparison.Ordinal)
           && !postInitializeSource.Contains("DrawAutomatorButton(", StringComparison.Ordinal),
        "The title bar may control compact layout, emergency stop and illegal mode, but must not duplicate the automation switch.");
    Assert(Regex.Matches(mainWindowSource, "DrawAutomatorButton\\(").Count == 3,
        "Wide and narrow layouts must share exactly one run-toggle renderer without duplicating it in compact mode.");
    Assert(!mainWindowSource.Contains("ImGui.Checkbox($\"自动运行##AutomatorRun-", StringComparison.Ordinal)
           && postInitializeSource.Contains("SetCompactMode(!config.CompactMainWindow)", StringComparison.Ordinal)
           && mainWindowSource.Contains("LuminWidgets.PrimaryButton(", StringComparison.Ordinal),
        "The single automation control must be a button, not a checkbox.");

    var automatorModuleSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "Automator", "AutomatorModule.cs"));
    Assert(automatorModuleSource.Contains("public bool IsIndependentNavigationRunning", StringComparison.Ordinal)
           && automatorModuleSource.Contains(
               "automator.SuspendForIndependentNavigation(\"independent navigation\")",
               StringComparison.Ordinal),
        "The automator update loop must yield navigation and clear stale FATE state while an independent hunt is running.");
    var beginStopStart = automatorModuleSource.IndexOf(
        "private void BeginStopRequest()", StringComparison.Ordinal);
    var completeStopStart = automatorModuleSource.IndexOf(
        "private void CompleteStopRequest()", StringComparison.Ordinal);
    Assert(beginStopStart >= 0 && completeStopStart > beginStopStart,
        "Automator stop lifecycle methods must remain discoverable by the regression test.");
    var beginStopSource = automatorModuleSource[beginStopStart..completeStopStart];
    Assert(beginStopSource.Contains("StopLocalAutomation();", StringComparison.Ordinal)
           && beginStopSource.IndexOf("StopLocalAutomation();", StringComparison.Ordinal)
           < beginStopSource.IndexOf("RunOnTick", StringComparison.Ordinal),
        "Pause must cancel local chains before deferred cleanup is scheduled.");
    var completeStopEnd = automatorModuleSource.IndexOf(
        "private static void TryStopStep", completeStopStart, StringComparison.Ordinal);
    var completeStopSource = automatorModuleSource[completeStopStart..completeStopEnd];
    Assert(!completeStopSource.Contains("navigation.IsReady()", StringComparison.Ordinal)
           && !completeStopSource.Contains("lifestream.IsReady()", StringComparison.Ordinal),
        "Pause must attempt vnavmesh and Lifestream cancellation during transient not-ready windows.");

    var returnChainSource = File.ReadAllText(Path.Combine("BOCCHI", "Chains", "ReturnChain.cs"));
    var approachWaitStart = returnChainSource.IndexOf(
        "chain.Then(new TaskManagerTask(() =>", StringComparison.Ordinal);
    var outsideTerritoryGuard = returnChainSource.IndexOf(
        "if (!ZoneData.IsInOccultCrescent())", approachWaitStart, StringComparison.Ordinal);
    var firstApproachRangeCheck = returnChainSource.IndexOf(
        "ZoneData.IsNearAethernetShard", approachWaitStart, StringComparison.Ordinal);
    Assert(approachWaitStart >= 0
           && outsideTerritoryGuard > approachWaitStart
           && outsideTerritoryGuard < firstApproachRangeCheck,
        "The return approach must stop before reading island-only data after a territory change.");

    var configWindowSource = File.ReadAllText(Path.Combine("BOCCHI", "Windows", "ConfigWindow.cs"));
    Assert(!configWindowSource.Contains("##AutomatorEnabled", StringComparison.Ordinal),
        "Runtime start/stop must not be duplicated in settings.");
    Assert(configWindowSource.Contains("TryStartFromOutside", StringComparison.Ordinal)
           && configWindowSource.Contains("test_instance_entry_help", StringComparison.Ordinal),
        "Instance rotation settings must expose a direct outside-NPC entry test and its DailyRoutines prerequisites.");

    var automatorWindowSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "Automator", "AutomatorWindow.cs"));
    Assert(!automatorWindowSource.Contains("ToggleIllegalMode", StringComparison.Ordinal),
        "The Automator lens title bar must not provide a second run control.");

    var treasureModuleSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "Treasure", "TreasureModule.cs"));
    var prepareIndependentNavigation = treasureModuleSource.IndexOf(
        "PrepareForIndependentNavigation(\"treasure hunt\")",
        StringComparison.Ordinal);
    var startTreasureHunt = treasureModuleSource.IndexOf(
        "hunter.Start();",
        StringComparison.Ordinal);
    Assert(prepareIndependentNavigation >= 0
           && startTreasureHunt > prepareIndependentNavigation,
        "Treasure hunt startup must cancel an in-flight FATE return before submitting its own route chains.");

    var teleporterSource = File.ReadAllText(Path.Combine(
        "BOCCHI", "Modules", "Teleporter", "Teleporter.cs"));
    var fateEndStart = teleporterSource.IndexOf("public void OnFateEnd", StringComparison.Ordinal);
    var criticalEndStart = teleporterSource.IndexOf("public void OnCriticalEncounterEnd", StringComparison.Ordinal);
    var fateEndSource = teleporterSource[fateEndStart..criticalEndStart];
    Assert(fateEndSource.Contains("automator.IsIndependentNavigationRunning", StringComparison.Ordinal)
           && fateEndSource.IndexOf("automator.IsIndependentNavigationRunning", StringComparison.Ordinal)
           < fateEndSource.IndexOf("automator.IsEnabled", StringComparison.Ordinal)
           && fateEndSource.IndexOf("automator.IsIndependentNavigationRunning", StringComparison.Ordinal)
           < fateEndSource.IndexOf("Return();", StringComparison.Ordinal),
        "FATE exit handling must suppress both automatic and manual returns while treasure navigation owns movement.");

    Console.WriteLine("BOCCHI automator run-state smoke tests passed.");
    return;
}

static AethernetData CreateShard(Aethernet aethernet, Vector3 position, Vector3 destination)
{
    return new AethernetData
    {
        Aethernet = aethernet,
        Position = position,
        Destination = destination,
    };
}

static Task<List<Vector3>> StraightPath(Vector3 start, Vector3 destination, CancellationToken _)
{
    return Task.FromResult(new List<Vector3> { start, destination });
}

Assert(TreasureHuntCommand.ShortAlias == "/ochth",
    "The compact treasure-hunter command alias must remain stable for macros.");
Assert(typeof(Hunter).GetMethod(nameof(Hunter.Start), BindingFlags.Instance | BindingFlags.Public) != null,
    "Treasure commands and the UI must share Hunter's public start lifecycle.");

var aggroZone = new AggroDangerZone(14857, Vector3.Zero, 5f);
Assert(!AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel: 10, mobLevel: 9)
       && AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel: 10, mobLevel: 10)
       && AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel: 10, mobLevel: 11),
    "Aggro avoidance must ignore confirmed lower-level mobs while retaining equal- and higher-level threats.");
Assert(AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel: 0, mobLevel: 9)
       && AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel: 10, mobLevel: 0),
    "Transient unknown Occult Crescent levels must remain conservative during object loading.");
var terminalAggroZones = AggroAvoidancePlanner.GetRelevantZones(
    [aggroZone, new AggroDangerZone(14858, new Vector3(20f, 0f, 0f), 5f)],
    new Vector3(1f, 0f, 0f));
Assert(terminalAggroZones.Length == 1 && terminalAggroZones[0].NameId == 14858,
    "A danger circle containing the requested destination must be excluded without hiding unrelated obstacles.");
var straightAggroPath = new List<Vector3>
{
    new(-10f, 0f, 0f),
    new(10f, 0f, 0f),
};
Assert(AggroAvoidancePlanner.TryAvoid(
           straightAggroPath,
           [aggroZone],
           verticalTolerance: 6f,
           point => point,
           out var avoidedAggroPath)
       && avoidedAggroPath.Count > straightAggroPath.Count
       && AggroAvoidancePlanner.IsPathClear(avoidedAggroPath, [aggroZone], 6f),
    "Aggro avoidance must insert a clear tangent/arc detour around a crossed ordinary-mob circle.");
Assert(AggroAvoidancePlanner.TryAvoid(
           [new Vector3(-10f, 0f, 8f), new Vector3(10f, 0f, 8f)],
           [aggroZone],
           6f,
           point => point,
           out var alreadyClearAggroPath)
       && alreadyClearAggroPath.Count == 2,
    "A clear route must remain unchanged instead of gaining unnecessary avoidance waypoints.");
Assert(!AggroAvoidancePlanner.SegmentEntersZone(
           new Vector3(-10f, 20f, 0f),
           new Vector3(10f, 20f, 0f),
           aggroZone,
           verticalTolerance: 6f),
    "Aggro avoidance must ignore circles on a different vertical floor.");
Assert(!AggroAvoidancePlanner.SegmentEntersZone(
           new Vector3(1f, 0f, 0f),
           new Vector3(10f, 0f, 0f),
           aggroZone,
           verticalTolerance: 6f),
    "A player already inside a circle must be allowed to leave directly without deadlocking navigation.");
Assert(AggroAvoidancePlanner.TryAvoid(
           [new Vector3(1f, 0f, 0f), new Vector3(-10f, 0f, 0f)],
           [aggroZone],
           6f,
           point => point,
           out var insideEscapePath)
       && insideEscapePath.Count > 2
       && AggroAvoidancePlanner.IsPathClear(insideEscapePath, [aggroZone], 6f),
    "A route starting inside and initially travelling deeper must first escape outward, then detour without deadlock.");

Assert(PostActivityReturnPolicy.ShouldQueue(EventType.Fate, independentNavigationRunning: false)
       && !PostActivityReturnPolicy.ShouldQueue(EventType.CriticalEncounter, independentNavigationRunning: false),
    "Completed FATEs must lock the automator into a base-camp return before selecting another activity.");
Assert(!PostActivityReturnPolicy.ShouldQueue(EventType.Fate, independentNavigationRunning: true),
    "An independent treasure hunt must own navigation and suppress the post-FATE base-camp return.");
Assert(ActivitySelectionPolicy.GetOrder(preferFate: false)
           .SequenceEqual([EventType.CriticalEncounter, EventType.Fate])
       && ActivitySelectionPolicy.GetOrder(preferFate: true)
           .SequenceEqual([EventType.Fate, EventType.CriticalEncounter]),
    "Activity selection must give one pending FATE preference priority over an available CE.");
var preferFateAfterCe = ActivitySelectionPolicy.AfterActivityEnded(
    preferFate: false,
    EventType.CriticalEncounter);
Assert(preferFateAfterCe
       && ActivitySelectionPolicy.AfterActivitySelected(preferFateAfterCe, EventType.CriticalEncounter)
       && !ActivitySelectionPolicy.AfterActivitySelected(preferFateAfterCe, EventType.Fate)
       && !ActivitySelectionPolicy.AfterActivityEnded(preferFate: false, EventType.Fate),
    "CE completion must retain FATE preference until a FATE is actually selected.");
Assert(CombatAutomationPolicy.ShouldAcquireTarget(false, false)
       && CombatAutomationPolicy.ShouldAcquireTarget(true, true)
       && !CombatAutomationPolicy.ShouldAcquireTarget(false, true),
    "FATE arrival must acquire an initial target even when continuous force-target is disabled.");
Assert(CombatAutomationPolicy.ShouldRetryPromeRotation(true, false)
       && !CombatAutomationPolicy.ShouldRetryPromeRotation(false, false)
       && !CombatAutomationPolicy.ShouldRetryPromeRotation(true, true),
    "FATE combat maintenance must retry a loaded but stopped PromeRotation instance only.");
Assert(TreasureInteractionPolicy.CanAttempt(true, false, false)
       && TreasureInteractionPolicy.CanAttempt(false, false, false)
       && !TreasureInteractionPolicy.CanAttempt(true, true, false)
       && !TreasureInteractionPolicy.CanAttempt(true, false, true),
    "Treasure interaction must remain available while mounted and only wait for combat/casting to clear.");
Assert(TreasureSightRefreshPolicy.ShouldCast(true, false)
       && !TreasureSightRefreshPolicy.ShouldCast(true, true)
       && !TreasureSightRefreshPolicy.ShouldCast(false, false),
    "Treasuresight must run only when requested and the current territory count has not been initialized.");
Assert(TreasureHuntDataPolicy.ShouldReload(null, 1252, 60)
       && TreasureHuntDataPolicy.ShouldReload(1252, 1253, 60)
       && TreasureHuntDataPolicy.ShouldReload(1252, 1252, 0)
       && !TreasureHuntDataPolicy.ShouldReload(1252, 1252, 60),
    "Treasure layout data must be cached within one territory and reloaded only after a map change or empty load.");

var legacyMobConfig = new Config
{
    Version = 2,
    MobFarmerConfig = new BOCCHI.Modules.MobFarmer.MobFarmerConfig
    {
        Mobs = [Mob.Goobbue, Mob.CrescentCliffkite],
    },
};
Assert(legacyMobConfig.Migrate()
       && legacyMobConfig.Version == Config.CurrentVersion
       && legacyMobConfig.MobFarmerConfig.SouthHornMobs.SequenceEqual([Mob.Goobbue])
       && legacyMobConfig.MobFarmerConfig.NorthHornMobs.SequenceEqual([Mob.CrescentCliffkite])
       && legacyMobConfig.MobFarmerConfig.Mobs.Count == 0,
    "Legacy mixed monster selections must migrate into independent South/North Horn lists.");
var mainWindowConfig = new Config();
Assert(!mainWindowConfig.CompactMainWindow,
    "The compact main-window layout must remain opt-in for existing users.");
mainWindowConfig.CompactMainWindow = true;
Assert(mainWindowConfig.CompactMainWindow,
    "The compact main-window layout selection must be persistable in plugin configuration.");

var navigationEvent = new EventData
{
    Id = 9000,
    Type = EventType.Fate,
    InternalName = "Smart navigation smoke test",
};
var navigationPlayer = Vector3.Zero;
var navigationDestination = new Vector3(100f, 0f, 0f);
var navigationBaseCamp = CreateShard(
    Aethernet.NorthBaseCamp,
    new Vector3(1000f, 0f, 0f),
    new Vector3(1000f, 0f, 0f));
var navigationSource = CreateShard(
    Aethernet.WillOWispVillage,
    new Vector3(45f, 0f, 0f),
    new Vector3(1000f, 0f, 0f));
var navigationTarget = CreateShard(
    Aethernet.SunkenTempleFront,
    new Vector3(900f, 0f, 0f),
    new Vector3(60f, 0f, 0f));
var navigationShards = new[] { navigationBaseCamp, navigationSource, navigationTarget };

var detourPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    (start, destination, _) => Task.FromResult(
        start == navigationPlayer && destination == navigationDestination
            ? new List<Vector3> { start, new(0f, 0f, 300f), destination }
            : new List<Vector3> { start, destination }),
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
var straightFallbackPlan = SmartNavigation.DecideFallback(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    returnCost: 300f,
    teleportCost: 20f,
    "test comparison");
Assert(straightFallbackPlan.Type == NavigationType.Walk
       && detourPlan.Type == NavigationType.WalkTeleportWalk
       && detourPlan.SourceAethernet == navigationSource.Aethernet
       && detourPlan.DestinationAethernet == navigationTarget.Aethernet,
    "Measured vnavmesh detours must be able to change the route selected by straight-line costs.");

var huntFallbackPlan = SmartNavigation.DecideFallback(
    navigationPlayer,
    navigationDestination,
    (Aethernet?)null,
    navigationShards,
    navigationBaseCamp,
    returnCost: 300f,
    teleportCost: 5f,
    "North Horn hunt smoke test");
var huntTransitSteps = HuntNavigationPlanner.BuildTransitSteps(huntFallbackPlan);
Assert(huntFallbackPlan.Type == NavigationType.WalkTeleportWalk
       && huntTransitSteps.Count == 2
       && huntTransitSteps[0].Type == PathfinderStepType.WalkToAethernet
       && huntTransitSteps[0].Aethernet == navigationSource.Aethernet
       && huntTransitSteps[1].Type == PathfinderStepType.TeleportToAethernet
       && huntTransitSteps[1].Aethernet == navigationTarget.Aethernet,
    "Generic fallback routing must retain its source-shard/teleport option for callers that explicitly allow it.");
var safeHuntFallbackPlan = SmartNavigation.DecideFallback(
    navigationPlayer,
    navigationDestination,
    (Aethernet?)null,
    navigationShards,
    navigationBaseCamp,
    returnCost: 300f,
    teleportCost: 5f,
    "North Horn safe hunt fallback",
    includeWalkTeleportCandidate: false);
Assert(safeHuntFallbackPlan.Type != NavigationType.WalkTeleportWalk
       && safeHuntFallbackPlan.Candidates.All(candidate =>
           candidate.Type != NavigationType.WalkTeleportWalk),
    "An unmeasured hunt fallback must not walk toward an unverified source shard.");
var forcedHuntRecovery = HuntNavigationPlanner.BuildForcedRecoverySteps(
    navigationTarget.Aethernet,
    navigationBaseCamp.Aethernet);
Assert(forcedHuntRecovery.Count == 2
       && forcedHuntRecovery[0].Type == PathfinderStepType.ReturnToBaseCamp
       && forcedHuntRecovery[1].Type == PathfinderStepType.TeleportToAethernet
       && forcedHuntRecovery.All(step => step.Type != PathfinderStepType.WalkToAethernet),
    "An unreachable North Horn hunt node must recover through return and its closest aethernet.");
var sameCampRecovery = HuntNavigationPlanner.BuildForcedRecoverySteps(
    navigationBaseCamp.Aethernet,
    navigationBaseCamp.Aethernet);
Assert(sameCampRecovery.Count == 1
       && sameCampRecovery[0].Type == PathfinderStepType.ReturnToBaseCamp,
    "Forced recovery to base camp must not add a redundant teleport.");
Assert(HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, navigationDestination],
           navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, new Vector3(50f, 0f, 0f)],
           navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination([], navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination([navigationDestination], navigationDestination)
       && HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, navigationDestination + new Vector3(5f, 0f, 0f)],
           navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, navigationDestination + new Vector3(5.01f, 0f, 0f)],
           navigationDestination),
    "Hunt routing must reject partial vnavmesh paths before following them across an unreachable boundary.");

var routeWithCurrentNode = new List<PathfinderStep>
{
    PathfinderStep.WalkToDestination(1),
    PathfinderStep.WalkToDestination(2),
    PathfinderStep.WalkToDestination(3),
};
HuntNavigationPlanner.InsertBeforeCurrentNode(routeWithCurrentNode, 1, forcedHuntRecovery);
Assert(routeWithCurrentNode.Count == 5
       && routeWithCurrentNode[0].NodeId == 1
       && routeWithCurrentNode[1].Type == PathfinderStepType.ReturnToBaseCamp
       && routeWithCurrentNode[2].Type == PathfinderStepType.TeleportToAethernet
       && routeWithCurrentNode[3].Type == PathfinderStepType.WalkToNode
       && routeWithCurrentNode[3].NodeId == 2
       && routeWithCurrentNode[4].NodeId == 3,
    "Transit insertion must preserve the current hunt node behind return/teleport without pre-advancing it.");

var partialSourcePlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    (start, destination, _) => Task.FromResult(
        start == navigationPlayer && destination == navigationSource.Position
            ? new List<Vector3> { start, start + Vector3.UnitX }
            : new List<Vector3> { start, destination }),
    returnCost: 300f,
    teleportCost: 5f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(partialSourcePlan.Candidates.All(candidate =>
        candidate.Type != NavigationType.WalkTeleportWalk
        || candidate.SourceAethernet != navigationSource.Aethernet),
    "A partial route to the source shard must never produce a WalkTeleportWalk candidate.");

var cheapTeleportPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    StraightPath,
    returnCost: 300f,
    teleportCost: 5f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
var expensiveTeleportPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    StraightPath,
    returnCost: 300f,
    teleportCost: 200f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(cheapTeleportPlan.Type == NavigationType.WalkTeleportWalk
       && expensiveTeleportPlan.Type == NavigationType.Walk,
    "TeleportCost must participate in smart-navigation route selection.");
var delayedCePlan = SmartNavigation.PreferBaseCampReturn(cheapTeleportPlan, navigationBaseCamp);
Assert(delayedCePlan.Type == NavigationType.ReturnTeleportWalk
       && delayedCePlan.SourceAethernet == navigationBaseCamp.Aethernet
       && delayedCePlan.DestinationAethernet == cheapTeleportPlan.DestinationAethernet,
    "A delayed CE must use Return from base camp instead of walking to an arbitrary source shard.");
Assert(SmartNavigation.PreferBaseCampReturn(expensiveTeleportPlan, navigationBaseCamp)
           == expensiveTeleportPlan,
    "Delayed-CE return preference must not replace a direct walking route.");

var preferredNavigationEvent = navigationEvent;
preferredNavigationEvent.Aethernet = navigationTarget.Aethernet;
var unreachableTargetPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    preferredNavigationEvent,
    navigationShards,
    navigationBaseCamp,
    (start, destination, _) => Task.FromResult(
        start == navigationTarget.Destination && destination == navigationDestination
            ? new List<Vector3> { start, start + Vector3.UnitX }
            : new List<Vector3> { start, destination }),
    returnCost: 300f,
    teleportCost: 5f,
    destinationCandidateCount: 1,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(unreachableTargetPlan.Candidates.All(candidate =>
        candidate.DestinationAethernet != navigationTarget.Aethernet),
    "Candidates with an unreachable aethernet-to-event segment must be skipped.");

var fallbackPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    (_, _, _) => Task.FromResult(new List<Vector3>()),
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(fallbackPlan.UsedFallback && fallbackPlan.FallbackReason != null,
    "Smart navigation must fall back to straight-line costs when every vnavmesh segment fails.");

var preferredPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    preferredNavigationEvent,
    navigationShards,
    navigationBaseCamp,
    StraightPath,
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 1,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(preferredPlan.Candidates.Any(candidate =>
        candidate.DestinationAethernet == navigationTarget.Aethernet),
    "An event's preferred aethernet must survive destination-candidate prefiltering.");

var activePathfindRequests = 0;
var maximumConcurrentPathfindRequests = 0;
var pathfindConcurrencyLock = new object();
await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    async (start, destination, token) =>
    {
        lock (pathfindConcurrencyLock)
        {
            activePathfindRequests++;
            maximumConcurrentPathfindRequests = Math.Max(
                maximumConcurrentPathfindRequests,
                activePathfindRequests);
        }

        try
        {
            await Task.Delay(5, token);
            return new List<Vector3> { start, destination };
        }
        finally
        {
            lock (pathfindConcurrencyLock)
            {
                activePathfindRequests--;
            }
        }
    },
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(maximumConcurrentPathfindRequests == 1,
    "Smart navigation must serialize candidate probes because vnavmesh supports only one pathfinding task.");

Assert(FateTravelTargetPolicy.ShouldPursue(2075, 2075),
    "FATE travel must allow targets that belong to the selected FATE.");
Assert(!FateTravelTargetPolicy.ShouldPursue(2075, 0)
       && !FateTravelTargetPolicy.ShouldPursue(2075, 2076),
    "Roadside enemies and enemies from another FATE must not override the selected activity route.");
Assert(!FateNavigationPolicy.ShouldRepath(navigationActive: true, targetMovement: 100f),
    "A moving FATE target must not replace a vnavmesh route that is still active.");
Assert(FateNavigationPolicy.ShouldRepath(navigationActive: false, targetMovement: 6f)
       && !FateNavigationPolicy.ShouldRepath(navigationActive: false, targetMovement: 5f),
    "FATE target repathing must wait for navigation to stop and enforce its movement threshold.");
Assert(FateNavigationPolicy.ShouldTakeOverInitialTargetRoute(hasSubmittedTargetRoute: false)
       && !FateNavigationPolicy.ShouldTakeOverInitialTargetRoute(hasSubmittedTargetRoute: true),
    "The first visible FATE enemy must take over the center route exactly once.");
Assert(FateNavigationPolicy.IsTargetlessArrival(distanceToCenter: 4.9f, fateRadius: 100f)
       && !FateNavigationPolicy.IsTargetlessArrival(distanceToCenter: 5.1f, fateRadius: 100f),
    "A broad FATE radius must not end travel until the player reaches the center approach.");
Assert(FateNavigationPolicy.IsTargetInEngagementRange(centerDistance: 25f, hitboxRadius: 20f, engagementRange: 5f)
       && !FateNavigationPolicy.IsTargetInEngagementRange(centerDistance: 100f, hitboxRadius: 500f, engagementRange: 5f),
    "FATE arrival must honor normal hitboxes without trusting corrupt or oversized radii.");
Assert(FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation: false, elapsedMs: 1000)
       && !FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation: true, elapsedMs: 1000)
       && !FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation: false, elapsedMs: 2000),
    "FATE travel must tolerate only the initial vnavmesh SimpleMove startup gap.");
var fateWatcherStartedAt = FateNavigationPolicy.StartAtFirstTick(null, 100000);
Assert(fateWatcherStartedAt == 100000
       && FateNavigationPolicy.StartAtFirstTick(fateWatcherStartedAt, 101999) == 100000,
    "FATE startup grace must begin when its watcher first executes, not while an earlier return/teleport chain is running.");

var ceFinalTarget = new Vector3(20f, 0f, 0f);
Assert(CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(false, 1999)
       && !CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(true, 1999)
       && !CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(false, 2000),
    "CE initial travel must tolerate only the vnavmesh SimpleMove startup gap.");
var ceWatcherStartedAt = CriticalEncounterNavigationPolicy.StartAtFirstTick(null, 200000);
Assert(ceWatcherStartedAt == 200000
       && CriticalEncounterNavigationPolicy.StartAtFirstTick(ceWatcherStartedAt, 201999) == 200000,
    "CE startup grace must begin when its watcher first executes, not while an earlier return/teleport chain is running.");
Assert(CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, false, false, 1999) == FinalApproachDecision.Waiting
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, true, false, 2000) == FinalApproachDecision.Waiting
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, false, false, 2000) == FinalApproachDecision.StoppedBeforeArrival
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, false, true, 1) == FinalApproachDecision.StoppedBeforeArrival
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           new Vector3(15f, 0f, 0f), ceFinalTarget, false, true, 2000) == FinalApproachDecision.Arrived
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           new Vector3(14.99f, 0f, 0f), ceFinalTarget, false, true, 2000) == FinalApproachDecision.StoppedBeforeArrival,
    "CE final approach must survive vnavmesh's startup gap and confirm the saved random target within five yalms.");
Assert(CriticalEncounterNavigationPolicy.CanSubmitApproach(registrationOpen: true, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitApproach(registrationOpen: false, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitApproach(registrationOpen: true, playerInEncounter: true),
    "CE approach navigation must stop as soon as registration closes or participation begins.");
Assert(CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
           registrationOpen: true,
           finalDestinationSubmitted: false,
           isCloseToZone: true,
           pathfindingInProgress: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
           registrationOpen: false,
           finalDestinationSubmitted: false,
           isCloseToZone: true,
           pathfindingInProgress: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
           registrationOpen: true,
           finalDestinationSubmitted: true,
           isCloseToZone: true,
           pathfindingInProgress: false),
    "CE final random approach must be submitted exactly once while registration remains open.");
var ceCenter = new Vector3(100f, 20f, 200f);
var ceMinFinalTarget = CriticalEncounterNavigationPolicy.CreateFinalTarget(ceCenter, 0f, 0f);
var ceMaxFinalTarget = CriticalEncounterNavigationPolicy.CreateFinalTarget(ceCenter, MathF.PI, 100f);
Assert(MathF.Abs(Vector3.Distance(ceCenter, ceMinFinalTarget)
                 - CriticalEncounterNavigationPolicy.MinFinalOffset) < 0.001f
       && MathF.Abs(Vector3.Distance(ceCenter, ceMaxFinalTarget)
                    - CriticalEncounterNavigationPolicy.MaxFinalOffset) < 0.001f,
    "CE final random targets must remain in the configured inner annulus instead of landing at the center or edge.");
Assert(CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: false, isInZone: false, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: true, isInZone: false, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: false, isInZone: true, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: false, isInZone: false, playerInEncounter: true),
    "A started CE must be abandoned only while the player remains outside and has not joined it.");

Assert(CriticalEncounterSelectionPolicy.HasEnoughRegistrationTime(
           TimeSpan.FromSeconds(60),
           delayEnabled: true,
           maximumDelaySeconds: 15f,
           transitReserveSeconds: 45d)
       && !CriticalEncounterSelectionPolicy.HasEnoughRegistrationTime(
           TimeSpan.FromSeconds(59.99),
           delayEnabled: true,
           maximumDelaySeconds: 15f,
           transitReserveSeconds: 45d)
       && CriticalEncounterSelectionPolicy.HasEnoughRegistrationTime(
           TimeSpan.FromSeconds(45),
           delayEnabled: false,
           maximumDelaySeconds: 15f,
           transitReserveSeconds: 45d)
       && !CriticalEncounterSelectionPolicy.HasEnoughRegistrationTime(
           TimeSpan.Zero,
           delayEnabled: false,
           maximumDelaySeconds: 0f,
           transitReserveSeconds: 0d),
    "CE selection must reserve enough registration time for delay and crystal transit.");
Assert(CriticalEncounterSelectionPolicy.ShouldPreferBaseCampReturn(
           delayEnabled: true,
           isAtBaseCampAethernet: false,
           isNearDestinationAethernet: false)
       && !CriticalEncounterSelectionPolicy.ShouldPreferBaseCampReturn(
           delayEnabled: true,
           isAtBaseCampAethernet: true,
           isNearDestinationAethernet: false)
       && !CriticalEncounterSelectionPolicy.ShouldPreferBaseCampReturn(
           delayEnabled: false,
           isAtBaseCampAethernet: false,
           isNearDestinationAethernet: false)
       && !CriticalEncounterSelectionPolicy.ShouldPreferBaseCampReturn(
           delayEnabled: true,
           isAtBaseCampAethernet: false,
           isNearDestinationAethernet: true),
    "Delayed CE navigation must not Return again while already at the base-camp crystal or the destination shard.");

Assert(DepartureDelayPolicy.GetDelayMilliseconds(
           enabled: false,
           minimumSeconds: 5f,
           maximumSeconds: 15f,
           randomSample: 0.5d) == 0,
    "Disabled CE departure delay must not enqueue a wait.");
Assert(DepartureDelayPolicy.GetDelayMilliseconds(
           enabled: true,
           minimumSeconds: 5f,
           maximumSeconds: 15f,
           randomSample: 0d) == 5000
       && DepartureDelayPolicy.GetDelayMilliseconds(
           enabled: true,
           minimumSeconds: 5f,
           maximumSeconds: 15f,
           randomSample: 0.999999d) is >= 5000 and <= 15000,
    "Enabled CE departure delay must stay inside the configured millisecond range.");
Assert(DepartureDelayPolicy.GetDelayMilliseconds(
           enabled: true,
           minimumSeconds: 0.9f,
           maximumSeconds: 1.1f,
           randomSample: 0.5d) == 1000,
    "Fractional departure-delay seconds must be converted before rounding to milliseconds.");
Assert(DepartureDelayPolicy.GetDelayMilliseconds(
           enabled: true,
           minimumSeconds: 15f,
           maximumSeconds: 5f,
           randomSample: 0.5d) == 10000
       && DepartureDelayPolicy.GetDelayMilliseconds(
           enabled: true,
           minimumSeconds: float.NaN,
           maximumSeconds: float.PositiveInfinity,
           randomSample: 0.5d) == 0,
    "Reversed or invalid persisted delay bounds must be normalized without throwing.");

Assert(TransitCompletionPolicy.HasVerifiedArrival(true, true)
       && !TransitCompletionPolicy.HasVerifiedArrival(true, false)
       && !TransitCompletionPolicy.HasVerifiedArrival(false, true)
       && !TransitCompletionPolicy.HasVerifiedArrival(false, false)
       && TransitCompletionPolicy.CanContinueAfterReturn(true)
       && !TransitCompletionPolicy.CanContinueAfterReturn(false),
    "Return/teleport chains must not advance until both child success and destination validation are true.");

var observedNorthReturnLanding = new Vector3(906.7626f, 259.99268f, 907.2258f);
var karnakLanding = new Vector3(454.3429f, 69.99997f, 530.9988f);
Assert(TransitCompletionPolicy.IsVerifiedReturnLanding(
           observedNorthReturnLanding,
           ZoneData.StartingLocations[ZoneData.NORTHHORN],
           Aethernet.NorthBaseCamp,
           Aethernet.NorthBaseCamp)
       && !TransitCompletionPolicy.IsVerifiedReturnLanding(
           karnakLanding,
           ZoneData.StartingLocations[ZoneData.NORTHHORN],
           Aethernet.KarnakCitadel,
           Aethernet.NorthBaseCamp),
    "Return must verify the base-camp landing instead of accepting another aethernet's BetweenAreas cycle.");

long? inactiveSince = null;
inactiveSince = NavigationStopPolicy.UpdateInactiveSince(false, 100, inactiveSince);
Assert(!NavigationStopPolicy.HasStopped(100, inactiveSince, 900),
    "Navigation must retain its startup grace before reporting a stop.");
inactiveSince = NavigationStopPolicy.UpdateInactiveSince(true, 1000, inactiveSince);
inactiveSince = NavigationStopPolicy.UpdateInactiveSince(false, 1100, inactiveSince);
Assert(!NavigationStopPolicy.HasStopped(100, inactiveSince, 2500)
       && NavigationStopPolicy.HasStopped(100, inactiveSince, 2600),
    "A transient vnavmesh status gap must not be treated as a stable stop.");

Assert(ActivityParticipationState.GetCombatStartupDecision(false, 0) == CombatStartupDecision.Ready
       && ActivityParticipationState.GetCombatStartupDecision(true, 0) == CombatStartupDecision.WaitingForUnmount
       && ActivityParticipationState.GetCombatStartupDecision(true, 7999) == CombatStartupDecision.WaitingForUnmount
       && ActivityParticipationState.GetCombatStartupDecision(true, 8000) == CombatStartupDecision.TimedOut,
    "Combat automation must wait for a confirmed dismount and fail closed after the bounded retry window.");

var entryCommandDispatchedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
Assert(InstanceEntryConfirmationPolicy.CanAttempt(
           InstanceRotationState.WaitingForEntry,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt)
       && !InstanceEntryConfirmationPolicy.CanAttempt(
           InstanceRotationState.Idle,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt)
       && !InstanceEntryConfirmationPolicy.CanAttempt(
           InstanceRotationState.WaitingForEntry,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt.AddSeconds(1),
           entryCommandDispatchedAt)
       && !InstanceEntryConfirmationPolicy.CanAttempt(
           InstanceRotationState.WaitingForEntry,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt,
           entryCommandDispatchedAt + InstanceRotationStateMachine.EntryTimeout + TimeSpan.FromMilliseconds(1)),
    "Instance entry confirmation must be armed only by a recent command, while waiting for entry, and respect retry throttling.");

Assert(!SupportJobLevelingPolicy.ShouldKeepCurrent(JobId.Freelancer, 1, 24)
       && SupportJobLevelingPolicy.ShouldKeepCurrent(JobId.Ninja, 1, 10)
       && !SupportJobLevelingPolicy.ShouldKeepCurrent(JobId.Ninja, 10, 10),
    "Freelancer must be excluded from automatic low-level job retention.");
var lowestSupportJob = SupportJobLevelingPolicy.SelectLowestIncomplete([
    new SupportJobLevelCandidate((byte)JobId.Freelancer, 5, 24),
    new SupportJobLevelCandidate((byte)JobId.Ninja, 3, 10),
    new SupportJobLevelCandidate((byte)JobId.WhiteMage, 2, 10),
    new SupportJobLevelCandidate((byte)JobId.BlackMage, 0, 10),
    new SupportJobLevelCandidate((byte)JobId.Dragoon, 10, 10),
]);
Assert(lowestSupportJob == (byte)JobId.WhiteMage,
    "Automatic leveling must compare actual levels instead of treating row-zero Freelancer as automatically lowest.");
var unfinishedFreelancer = SupportJobLevelingPolicy.SelectLowestIncomplete([
    new SupportJobLevelCandidate((byte)JobId.Freelancer, 1, 24),
    new SupportJobLevelCandidate((byte)JobId.WhiteMage, 2, 10),
]);
Assert(unfinishedFreelancer == (byte)JobId.WhiteMage,
    "Freelancer must be filtered out of automatic low-level job selection.");
Assert(SupportJobLevelingPolicy.SelectLowestIncomplete([
           new SupportJobLevelCandidate((byte)JobId.Freelancer, 1, 24),
       ]) == null,
    "Freelancer must not be selected when it is the only incomplete support job.");
var northCampShard = Aethernet.NorthBaseCamp.GetData();
Assert(ZoneData.IsWithinKnownAethernetRange(
           new Vector3(881.836f, 258.5f, 881.894f),
           northCampShard.Position,
           AethernetData.DISTANCE + 1f)
       && !ZoneData.IsWithinKnownAethernetRange(
           new Vector3(900f, 258.5f, 900f),
           northCampShard.Position,
           AethernetData.DISTANCE + 1f),
    "North Horn teleport validation must fall back to maintained shard coordinates when EventObj lookup is unavailable.");
var ruinedStreetsShard = Aethernet.RuinedStreetsFront.GetData();
var failedRuinedStreetsApproach = new Vector3(-384.575f, 39.15826f, -439.2537f);
Assert(!ZoneData.IsWithinKnownAethernetRange(
           failedRuinedStreetsApproach,
           ruinedStreetsShard.Position,
           AethernetData.DISTANCE)
       && ZoneData.IsWithinKnownAethernetRange(
           failedRuinedStreetsApproach,
           ruinedStreetsShard.Position,
           AethernetData.DISTANCE + 1f),
    "Source aethernet approach must reject the former 5.2-yalm allowance so Lifestream can actually interact.");
var maintainedAethernets = ZoneData.GetAethernets(ZoneData.SOUTHHORN)
    .Concat(ZoneData.GetAethernets(ZoneData.NORTHHORN))
    .Select(aethernet => aethernet.GetData())
    .ToArray();
Assert(maintainedAethernets.All(shard =>
           shard.IsWithinLandingRange(shard.Destination, AethernetData.DISTANCE))
       && maintainedAethernets.All(shard =>
           !shard.IsWithinLandingRange(
               shard.Destination + new Vector3(AethernetData.DISTANCE + 0.01f, 0f, 0f),
               AethernetData.DISTANCE)),
    "Post-teleport validation must use the maintained landing point and reject positions outside 4.2 yalms.");
var northHornLayerTargets = ZoneData.GetAethernets(ZoneData.NORTHHORN)
    .Select(aethernet => aethernet.GetData())
    .ToArray();
Assert(northHornLayerTargets.All(shard => shard.NavigationPositionOverride.HasValue)
       && northHornLayerTargets.All(shard =>
           Vector3.Distance(shard.NavigationPosition, shard.Position) <= AethernetData.DISTANCE)
       && northHornLayerTargets.All(shard => shard.NavigationPosition != shard.Position),
    "North Horn LAYERS navigation targets must remain on the reachable mesh and inside aethernet interaction range.");
Assert(Aethernet.BaseCamp.GetData().NavigationPosition == Aethernet.BaseCamp.GetData().Position,
    "Territories without a mesh-specific override must keep using the interaction coordinate for navigation.");
Assert(!AutomatorChainPolicy.IsActive([])
       && !AutomatorChainPolicy.IsActive([(false, 0), (false, 0)])
       && AutomatorChainPolicy.IsActive([(true, 0)])
       && AutomatorChainPolicy.IsActive([(false, 1)]),
    "Only running or queued chains may block the automator; retained empty queues must not stall navigation.");
Assert(typeof(CarrotHunt).GetMethod(
           "Teardown",
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null,
    "Repeating carrot routes must use Hunter's centralized teardown so route-generation and interaction state are reset.");
Assert(typeof(CarrotHunt).GetMethod(
           "ShouldStopEarly",
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null,
    "Carrot hunting must stop immediately when Fortune Carrots run out instead of repeating an empty route.");

var southFates = EventData.GetFatesForTerritory(ZoneData.SOUTHHORN).ToList();
var northFates = EventData.GetFatesForTerritory(ZoneData.NORTHHORN).ToList();
var southCriticalEncounters = EventData.GetCriticalEncountersForTerritory(ZoneData.SOUTHHORN).ToList();
var northCriticalEncounters = EventData.GetCriticalEncountersForTerritory(ZoneData.NORTHHORN).ToList();

var fate2075 = EventData.Fates[2075];
var wispLanding = Aethernet.WillOWispVillage.GetData().Destination;
Assert(fate2075.Aethernet == Aethernet.SunkenTempleFront,
    "FATE 2075 must teleport to the north-shore aethernet instead of Will-o'-the-Wisp Village.");
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

var southCrossingPathfindCalls = new List<(Vector3 Start, Vector3 Destination)>();
var southCrossingPlan = await SmartNavigation.DecideAsync(
    wispLanding,
    fate2075.StartPosition!.Value,
    fate2075,
    new[] { Aethernet.WillOWispVillage.GetData() },
    Aethernet.NorthBaseCamp.GetData(),
    (start, destination, _) =>
    {
        southCrossingPathfindCalls.Add((start, destination));
        throw new InvalidOperationException("No generic route");
    },
    returnCost: 300f,
    teleportCost: 50f,
    destinationCandidateCount: 1,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(!southCrossingPlan.UsedFallback
       && southCrossingPlan.Candidates.Any(candidate => candidate.Type == NavigationType.Walk)
       && southCrossingPathfindCalls.All(call =>
           call.Start != wispLanding || call.Destination != fate2075.StartPosition!.Value),
    "FATE 2075 must price its fixed South Crossing route without requesting the known-bad generic vnavmesh segment.");

Assert(southFates.Count == 13, $"Expected 13 South Horn FATEs, got {southFates.Count}.");
Assert(northFates.Count == 13, $"Expected 13 North Horn FATEs, got {northFates.Count}.");
Assert(southCriticalEncounters.Count == 16, $"Expected 16 South Horn dynamic events, got {southCriticalEncounters.Count}.");
Assert(northCriticalEncounters.Count == 17, $"Expected 17 North Horn dynamic events, got {northCriticalEncounters.Count}.");
Assert(northFates.Count(fate => fate.IsPot) == 2, "North Horn must contain exactly two pot FATEs.");
Assert(EventData.Fates[2072].TerritoryId == ZoneData.NORTHHORN
       && EventData.Fates[2072].EffectiveTerritoryId == ZoneData.NORTHHORN
       && EventData.Fates[2072].IsPot && EventData.Fates[2072].Type == EventType.Fate,
    "North Horn FATE 2072 (被欺负的魔法罐) must be a North Horn pot FATE.");
Assert(EventData.Fates[2073].TerritoryId == ZoneData.NORTHHORN
       && EventData.Fates[2073].EffectiveTerritoryId == ZoneData.NORTHHORN
       && EventData.Fates[2073].IsPot && EventData.Fates[2073].Type == EventType.Fate,
    "North Horn FATE 2073 (被吹飞的魔法罐) must be a North Horn pot FATE.");
Assert(northCriticalEncounters.Count(encounter => encounter.Id is >= 49 and <= 63) == 15,
    "North Horn must contain CE IDs 49 through 63.");
Assert(EventData.CriticalEncounters[56].NavigationPositionOverride == new Vector3(238f, 15f, 352f),
    "Cornered Gemstone navigation must target the arena center instead of the north-edge event marker.");
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

var grandMagicTrapGroups = TrapData.GetGroups(TowerHelper.TowerType.GrandMagic);
var grandMagicTraps = grandMagicTrapGroups.SelectMany(group => group.Traps).ToArray();
Assert(grandMagicTrapGroups.Count == 141
       && grandMagicTraps.Count(trap => trap.Type == OccultObjectType.Trap) == 96
       && grandMagicTraps.Count(trap => trap.Type == OccultObjectType.BigTrap) == 45
       && grandMagicTraps.Select(trap => trap.GetKey()).Distinct().Count() == 141,
    "Grand Magic Tower must expose the 141 unique ARR/map/screenshot-extracted potential trap positions.");
Assert(!grandMagicTraps.SelectMany((left, index) => grandMagicTraps.Skip(index + 1)
           .Where(right => right.Type == left.Type)
           .Select(right => Vector3.Distance(left.Position, right.Position)))
       .Any(distance => distance <= 0.1f),
    "Grand Magic Tower candidates must be de-duplicated by type within a 0.1m 3D tolerance.");
Assert(grandMagicTraps.Any(trap => trap.GetKey() == "2014584:638.50,-700.00,922.50")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014585:807.00,-700.00,782.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:-0.02,-707.97,-433.01")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014585:386.00,-700.00,778.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:592.00,-699.95,132.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:600.00,-699.95,135.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:763.50,-690.00,660.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:825.00,-698.00,798.50")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:639.00,-680.00,825.50")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014584:0.00,-707.95,-421.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014585:723.50,-680.00,787.50")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014585:800.00,-699.94,782.00")
       && grandMagicTraps.Any(trap => trap.GetKey() == "2014585:807.00,-700.00,792.00")
       && TrapData.GetGroups(TowerHelper.TowerType.Magic).Count == 0,
    "Grand Magic known anchors must remain mapped without leaking its layout into normal Magic Tower.");

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
Assert(EventData.CriticalEncounters[63].Aethernet == Aethernet.SunkenTempleFront,
    "Morphing Mage must teleport to the north-shore aethernet instead of Will-o'-the-Wisp Village.");

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

foreach (var mob in northHornMobs)
{
    Assert(CommonMobCatalog.TryGet((uint)mob, out var profile),
        $"North Horn ordinary mob {(uint)mob} is missing from the aggro catalog.");
    Assert(MathF.Abs(profile.FallbackEdgeRange - 10f) < 0.001f,
        $"North Horn ordinary mob {(uint)mob} still uses model scale as a fake aggro range.");
}
Assert(!CommonMobCatalog.TryGet(14856, out _) && !CommonMobCatalog.TryGet(14924, out _),
    "Aggro catalog must not include actors outside the 67 ordinary North Horn BNpcName rows.");

var calibration = new AggroRangeCalibration();
Assert(!calibration.AddSample(3.9f) && !calibration.AddSample(20.1f),
    "Aggro calibration must reject implausible passive-pull distances.");
Assert(calibration.AddSample(8f) && calibration.AddSample(10f) && calibration.AddSample(9f),
    "Aggro calibration rejected valid passive-pull samples.");
Assert(MathF.Abs(calibration.ResolveEdgeRange(10f) - 10.5f) < 0.001f,
    "Aggro calibration must use the conservative upper quartile plus frame-latency allowance.");
var calibratedConfig = new AggroRangeConfig
{
    Calibrations = new Dictionary<uint, AggroRangeCalibration> { [14857] = calibration },
};
CommonMobCatalog.TryGet(14857, out var calibratedProfile);
Assert(MathF.Abs(AggroRangeResolver.ResolveTriggerRadius(calibratedProfile, 2f, calibratedConfig) - 12.5f) < 0.001f,
    "Resolved aggro radius must add the live monster hitbox exactly once.");

var crystal = new Vector3(10f, 2f, 10f);
var crystalApproach = KnowledgeCrystalApproachPolicy.GetDesiredApproachPosition(
    new Vector3(20f, 2f, 10f),
    crystal);
Assert(MathF.Abs(Vector3.Distance(crystalApproach, crystal) - KnowledgeCrystalApproachPolicy.DesiredOffset) < 0.001f,
    "Knowledge-crystal approach point must stay inside the verified cast range.");
Assert(KnowledgeCrystalApproachPolicy.HasArrived(new Vector3(13f, 2f, 10f), crystal)
       && !KnowledgeCrystalApproachPolicy.HasArrived(new Vector3(13.01f, 2f, 10f), crystal),
    "Knowledge-crystal arrival must be based on real distance, not vnavmesh stopping.");

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
RunCeCrowdsourceTests();
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

Assert(PromeRotationController.PluginInternalName == "PromeRotation"
       && PromeRotationController.StartIpcName == "PromeRotation.IPC.Start"
       && PromeRotationController.StopIpcName == "PromeRotation.IPC.Stop"
       && PromeRotationController.IsRunningIpcName == "PromeRotation.IPC.IsRunning"
       && PromeRotationController.AutoPullOnCommand == "/pr autopull on"
       && PromeRotationController.AutoPullOffCommand == "/pr autopull off",
    "PromeRotation integration must retain its official IPC names and AutoPull commands.");
Assert(typeof(PromeRotationController).GetMethod(nameof(PromeRotationController.Start))?.ReturnType == typeof(void)
       && typeof(PromeRotationController).GetMethod(nameof(PromeRotationController.Stop))?.ReturnType == typeof(void),
    "PromeRotation start/stop must be fire-and-forget so IPC false cannot block an Ocelot chain.");

Assert(NavigationActivityState.IsActive(false, true, false)
       && NavigationActivityState.IsActive(false, false, true),
    "Automator must keep one activity chain alive while vnavmesh SimpleMove/Nav is calculating a route.");
Assert(!NavigationActivityState.IsActive(false, false, false),
    "Automator must still detect a genuinely stopped vnavmesh route.");
Assert(!ActivityParticipationState.HasEnded(false, State.Idle),
    "Automator must not reselect the same activity while waiting for initial FATE/CE participation.");
Assert(ActivityParticipationState.IsInsideActivity(State.InFate)
       && ActivityParticipationState.IsInsideActivity(State.InCriticalEncounter)
       && !ActivityParticipationState.IsInsideActivity(State.InCombat),
    "Only an observed FATE/CE state may arm activity-completion detection.");
Assert(ActivityParticipationState.HasEnded(true, State.Idle),
    "Automator must finish an activity after an observed FATE/CE returns to Idle.");

Assert(automatorConfig.FatesMap.Keys.Where(northFateIds.Contains).Order().SequenceEqual(northFateIds),
    "Automator must expose one mapping for every North Horn FATE ID 2072 through 2084.");
Assert(automatorConfig.CriticalEncountersMap.Keys.Where(northCriticalEncounterIds.Contains).Order()
        .SequenceEqual(northCriticalEncounterIds),
    "Automator must expose one mapping for every ordinary North Horn CE ID 49 through 63.");
Assert(!automatorConfig.CriticalEncountersMap.ContainsKey(64)
       && !automatorConfig.CriticalEncountersMap.ContainsKey(65),
    "Forked Tower events 64/65 must remain under the tower module, not Automator participation.");
Assert(northFateIds.All(id => automatorConfig.FatesMap[id]),
    "Every North Horn Automator FATE switch, including the pot FATEs 2072/2073, must be enabled by default.");
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
       && legacyConfig.AutomatorConfig.DoPersistentPots
       && !legacyConfig.AutomatorConfig.DoPleadingPots,
    "Migration must preserve the user's explicit South Horn pot choices.");
Assert(legacyConfig.AutomatorConfig.DoNorthHornFate2072
       && legacyConfig.AutomatorConfig.DoNorthHornFate2073,
    "Version-1 configs predate the North Horn switches, so 2072/2073 must adopt the enabled-by-default state instead of inheriting unrelated South Horn pot choices.");
Assert(!legacyConfig.Migrate(), "Configuration migration must be idempotent.");

var explicitChoiceConfig = new Config
{
    Version = Config.CurrentVersion,
    AutomatorConfig = new AutomatorConfig
    {
        DoNorthHornFate2072 = false,
        DoNorthHornFate2073 = true,
    },
};
Assert(!explicitChoiceConfig.Migrate(),
    "A current-version configuration must not be flagged as migrated.");
Assert(!explicitChoiceConfig.AutomatorConfig.DoNorthHornFate2072
       && explicitChoiceConfig.AutomatorConfig.DoNorthHornFate2073,
    "Migration must never re-enable a user who explicitly disabled North Horn FATE 2072.");

automatorConfig.DoFates = false;
automatorConfig.DoCriticalEncounters = false;
Assert(northFateIds.All(id => !automatorConfig.FatesMap[id]),
    "The master FATE switch must suppress all North Horn FATE settings.");
Assert(northCriticalEncounterIds.All(id => !automatorConfig.CriticalEncountersMap[id]),
    "The master CE switch must suppress all North Horn CE settings.");

Assert(automatorConfig.InitialInstanceArea == InstanceEntryArea.NorthHorn
       && !automatorConfig.AutoRotateInstance && automatorConfig.InstanceStayMinutes == 90f
       && !automatorConfig.RotateWhenPopulationLow && automatorConfig.MinimumInstancePopulation == 10,
    "Unattended instance rotation must remain opt-in with conservative defaults.");
foreach (var propertyName in new[]
         {
             nameof(AutomatorConfig.InitialInstanceArea),
             nameof(AutomatorConfig.AutoRotateInstance), nameof(AutomatorConfig.InstanceStayMinutes),
             nameof(AutomatorConfig.RotateWhenPopulationLow), nameof(AutomatorConfig.MinimumInstancePopulation),
         })
{
    Assert(typeof(AutomatorConfig).GetProperty(propertyName)?.GetCustomAttributes().Any() == true,
        $"Instance rotation setting {propertyName} must be rendered by the configuration UI.");
}

var rotationStart = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
var initialNorthEntry = new InstanceRotationStateMachine();
Assert(initialNorthEntry.BeginEntryFromOutside(rotationStart, ZoneData.NORTHHORN)
       == InstanceRotationAction.EnterNorthHorn
       && initialNorthEntry.State == InstanceRotationState.WaitingForEntry
       && initialNorthEntry.OriginalTerritoryId == ZoneData.NORTHHORN,
    "Starting automation outside must request North Horn exactly once when configured.");
Assert(initialNorthEntry.BeginEntryFromOutside(rotationStart.AddSeconds(1), ZoneData.NORTHHORN)
       == InstanceRotationAction.None,
    "An in-progress outside entry must not emit the entry command again.");
var initialOutsideInput = new InstanceRotationInput(
    true,
    0,
    false,
    TimeSpan.MaxValue,
    false);
Assert(initialNorthEntry.Update(rotationStart.AddSeconds(2), initialOutsideInput)
       == InstanceRotationAction.None,
    "Waiting outside must remain command-silent after the initial North Horn request.");
Assert(initialNorthEntry.Update(
           rotationStart.AddSeconds(3),
           initialOutsideInput with { TerritoryId = ZoneData.NORTHHORN }) == InstanceRotationAction.None
       && initialNorthEntry.State == InstanceRotationState.Monitoring,
    "Arriving in the configured North Horn territory must complete initial entry.");

var initialSouthEntry = new InstanceRotationStateMachine();
Assert(initialSouthEntry.BeginEntryFromOutside(rotationStart, ZoneData.SOUTHHORN)
       == InstanceRotationAction.EnterSouthHorn
       && initialSouthEntry.OriginalTerritoryId == ZoneData.SOUTHHORN,
    "Starting automation outside must support the configured South Horn entry command.");
var invalidInitialEntry = new InstanceRotationStateMachine();
Assert(invalidInitialEntry.BeginEntryFromOutside(rotationStart, 0) == InstanceRotationAction.None
       && invalidInitialEntry.State == InstanceRotationState.Failed
       && invalidInitialEntry.FailureReason == "invalid_entry_territory",
    "Outside entry must reject an unknown territory without emitting a command.");

var rotation = new InstanceRotationStateMachine();
Assert(InstanceDutyTimerProvider.AddonName == "_ToDoList"
       && InstanceDutyTimerPolicy.TryParse("\uE0BB 153:02", out var parsedDutyTime)
       && parsedDutyTime == TimeSpan.FromMinutes(153) + TimeSpan.FromSeconds(2)
       && !InstanceDutyTimerPolicy.TryParse("153:60", out _)
       && !InstanceDutyTimerPolicy.TryParse("181:00", out _),
    "Instance rotation must parse the actual _ToDoList duty timer and reject invalid values.");
Assert(!InstanceDutyTimerPolicy.HasStayDurationElapsed(TimeSpan.FromMinutes(165).Add(TimeSpan.FromSeconds(1)), TimeSpan.FromMinutes(15))
       && InstanceDutyTimerPolicy.HasStayDurationElapsed(TimeSpan.FromMinutes(165), TimeSpan.FromMinutes(15)),
    "Actual duty time must drive the configured stay-duration boundary.");

var monitoringInput = new InstanceRotationInput(
    true,
    ZoneData.SOUTHHORN,
    true,
    TimeSpan.FromMinutes(90),
    false,
    TimeSpan.FromMinutes(153).Add(TimeSpan.FromSeconds(2)));
Assert(rotation.Update(rotationStart, monitoringInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Monitoring,
    "Instance rotation must begin by monitoring the current Occult Crescent territory.");

var missingTimerRotation = new InstanceRotationStateMachine();
var missingTimerInput = monitoringInput with { InstanceTimeRemaining = null };
missingTimerRotation.Update(rotationStart, missingTimerInput);
Assert(missingTimerRotation.Update(rotationStart.AddHours(3), missingTimerInput) == InstanceRotationAction.None
       && missingTimerRotation.State == InstanceRotationState.Monitoring,
    "A missing Duty Information addon timer must fail closed instead of using BOCCHI's local uptime.");

var unsafeLowPopulationInput = monitoringInput with { CanStart = false, PopulationLow = true };
Assert(rotation.Update(rotationStart.AddMinutes(1), unsafeLowPopulationInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Monitoring,
    "Low population must not force an exit during a FATE, CE, combat, or active automation task.");

var safeLowPopulationInput = monitoringInput with { PopulationLow = true };
Assert(rotation.Update(rotationStart.AddMinutes(1).AddSeconds(1), safeLowPopulationInput)
       == InstanceRotationAction.RequestExit,
    "A confirmed low-population condition must request one safe exit.");
Assert(rotation.Reason == InstanceRotationReason.PopulationLow
       && rotation.Update(rotationStart.AddMinutes(1).AddSeconds(2), safeLowPopulationInput)
       == InstanceRotationAction.None,
    "The exit command must not be emitted again while waiting to leave.");

var outsideInput = monitoringInput with { TerritoryId = 0, CanStart = false, PopulationLow = false };
var leftAt = rotationStart.AddMinutes(1).AddSeconds(3);
Assert(rotation.Update(leftAt, outsideInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Cooldown,
    "The 15-second re-entry delay must begin only after the original territory has been left.");
Assert(rotation.Update(leftAt.AddSeconds(14.999), outsideInput) == InstanceRotationAction.None,
    "Instance rotation must wait the full 15 seconds before requesting re-entry.");
Assert(rotation.Update(leftAt.AddSeconds(15), outsideInput) == InstanceRotationAction.EnterSouthHorn
       && rotation.State == InstanceRotationState.WaitingForEntry,
    "South Horn rotation must select the ocs entry action after the full cooldown.");
Assert(rotation.Update(leftAt.AddSeconds(16), outsideInput) == InstanceRotationAction.None,
    "The entry command must not be emitted again while waiting for territory confirmation.");
Assert(rotation.Update(leftAt.AddSeconds(20), monitoringInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Monitoring
       && rotation.IslandEnteredAt == leftAt.AddSeconds(20),
    "Returning to the original territory must reset the stay timer for the next cycle.");

var northRotation = new InstanceRotationStateMachine();
var northInput = new InstanceRotationInput(
    true,
    ZoneData.NORTHHORN,
    true,
    TimeSpan.FromMinutes(15),
    false,
    InstanceDutyTimerPolicy.OccultCrescentDuration);
northRotation.Update(rotationStart, northInput);
Assert(northRotation.Update(rotationStart.AddMinutes(15), northInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(165) })
       == InstanceRotationAction.RequestExit
       && northRotation.Reason == InstanceRotationReason.StayTimeElapsed,
    "The configured stay duration must use the actual duty timer rather than time since BOCCHI was enabled.");
northRotation.Update(rotationStart.AddMinutes(15).AddSeconds(1), northInput with { TerritoryId = 0 });
Assert(northRotation.Update(rotationStart.AddMinutes(15).AddSeconds(16), northInput with { TerritoryId = 0 })
       == InstanceRotationAction.EnterNorthHorn,
    "North Horn rotation must select the ocn entry action.");
Assert(InstanceRotationController.LeaveCommand == "/pdr leaveduty"
       && InstanceRotationController.SouthHornEntryCommand == "/pdrfe ocs"
       && InstanceRotationController.NorthHornEntryCommand == "/pdrfe ocn",
    "DailyRoutines rotation commands must remain aligned with the verified local command modules.");
Assert(DailyRoutinesModuleBridge.PluginInternalName == "DailyRoutines"
       && DailyRoutinesModuleBridge.IsModuleEnabledIpcName == "DailyRoutines.IsModuleEnabled"
       && DailyRoutinesModuleBridge.LoadModuleIpcName == "DailyRoutines.LoadModule"
       && DailyRoutinesModuleBridge.RequiredModuleNames.SequenceEqual(
           new[] { "AutoTalkSkip", "FieldEntryCommand" }),
    "pdrfe startup must enable its verified DailyRoutines prerequisite before the command module.");
Assert(!CriticalEncounterTracker.CanReadOccultCrescentEvents(false, true)
       && !CriticalEncounterTracker.CanReadOccultCrescentEvents(true, false)
       && CriticalEncounterTracker.CanReadOccultCrescentEvents(true, true),
    "CE tracking must stop reading PublicContentOccultCrescent after leaving the island.");

var cancelledRotation = new InstanceRotationStateMachine();
cancelledRotation.Update(rotationStart, monitoringInput);
cancelledRotation.Update(rotationStart.AddMinutes(90), monitoringInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(90) });
Assert(cancelledRotation.Update(rotationStart.AddMinutes(90).AddSeconds(1), monitoringInput with { Enabled = false })
       == InstanceRotationAction.None
       && cancelledRotation.State == InstanceRotationState.Idle,
    "Disabling automation must cancel an in-progress instance rotation.");

var timedOutRotation = new InstanceRotationStateMachine();
timedOutRotation.Update(rotationStart, monitoringInput);
timedOutRotation.Update(rotationStart.AddMinutes(90), monitoringInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(90) });
Assert(timedOutRotation.Update(rotationStart.AddMinutes(90) + InstanceRotationStateMachine.ExitTimeout,
           monitoringInput) == InstanceRotationAction.None
       && timedOutRotation.State == InstanceRotationState.Failed,
    "An unconfirmed exit must stop after its timeout instead of retrying every frame.");
Assert(timedOutRotation.Update(rotationStart.AddHours(3), monitoringInput) == InstanceRotationAction.None,
    "A failed rotation must remain command-silent until explicitly reset.");

var entryTimedOutRotation = new InstanceRotationStateMachine();
entryTimedOutRotation.Update(rotationStart, monitoringInput);
entryTimedOutRotation.Update(rotationStart.AddMinutes(90), monitoringInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(90) });
var entryTimeoutLeftAt = rotationStart.AddMinutes(90).AddSeconds(1);
entryTimedOutRotation.Update(entryTimeoutLeftAt, outsideInput);
entryTimedOutRotation.Update(entryTimeoutLeftAt + InstanceRotationStateMachine.ReentryCooldown, outsideInput);
Assert(entryTimedOutRotation.Update(
           entryTimeoutLeftAt + InstanceRotationStateMachine.ReentryCooldown + InstanceRotationStateMachine.EntryTimeout,
           outsideInput) == InstanceRotationAction.None
       && entryTimedOutRotation.State == InstanceRotationState.Failed,
    "An unconfirmed re-entry must stop after its timeout without re-sending the entry command.");

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
    foreach (var key in new[]
             {
                 "initial_instance_area", "auto_rotate_instance", "instance_stay_minutes", "rotate_when_population_low",
                 "minimum_instance_population",
             })
    {
        Assert(configKeys.TryGetProperty(key, out _),
            $"{language} Automator translation is missing instance rotation key {key}.");
    }

    var rotationMessages = document.RootElement
        .GetProperty("modules")
        .GetProperty("automator")
        .GetProperty("messages")
        .GetProperty("rotation");
    Assert(rotationMessages.TryGetProperty("modules_enabling", out _),
        $"{language} Automator translation is missing the DailyRoutines module-enable message.");

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

RunTreasureRoutePolicyTests();

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

Assert(VnavmeshAvailabilityPolicy.Evaluate(true, true, new Version(0, 7, 6, 0)).IsAvailable
       && VnavmeshAvailabilityPolicy.Evaluate(true, true, new Version(0, 7, 7, 0)).IsAvailable
       && VnavmeshAvailabilityPolicy.Evaluate(true, true, null).IsAvailable,
    "Every loaded vnavmesh version must be accepted without an exact-version gate.");
Assert(VnavmeshAvailabilityPolicy.Evaluate(false, false, null).Status == VnavmeshAvailabilityStatus.Missing
       && VnavmeshAvailabilityPolicy.Evaluate(true, false, new Version(0, 7, 7, 0)).Status == VnavmeshAvailabilityStatus.NotLoaded,
    "Missing and unloaded vnavmesh installations must still be reported.");

Console.WriteLine("BOCCHI 7.55 North Horn data, lifecycle, config, translation, routing, precompute, and vnavmesh availability smoke tests passed.");
