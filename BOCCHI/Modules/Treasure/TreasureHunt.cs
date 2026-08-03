using BOCCHI.Data;
using BOCCHI.Pathfinding;
using BOCCHI.Chains;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.Modules;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Modules.Treasure;

public class TreasureHunt(TreasureModule module) : Hunter(module)
{
    private const float LiveTreasureMatchRadius = 12f;

    private const float LiveTreasurePriorityRadius = 120f;

    private List<TreasureData.TreasureDatum> Treasure = [];

    private readonly Dictionary<uint, Vector3> liveTreasurePositions = [];

    private uint? loadedTreasureTerritory;

    private ISharedImmediateTexture? routeMapTexture;

    private string startError = string.Empty;

    public int? CurrentRouteNumber
    {
        get
        {
            var nodeId = ActiveNodeId;
            return nodeId != null
                   && NorthHornTreasureRoute.TryGetRouteNumber(nodeId.Value, out var routeNumber)
                ? routeNumber
                : null;
        }
    }

    public override void Draw(Module<Plugin, Config> _)
    {
        OcelotUi.Title($"{module.T("panel.hunt.title")}:");
        OcelotUi.Indent(() =>
        {
            var runLabel = running
                ? module.T("panel.hunt.stop")
                : module.T("panel.hunt.start");
            if (ImGui.Button($"{runLabel}##treasure-run", new Vector2(118, 0)))
            {
                if (running)
                {
                    Stop();
                    startError = string.Empty;
                }
                else if (!module.TryStartHunt(out startError))
                {
                    Svc.Log.Warning(startError);
                }
            }

            if (ZoneData.IsInNorthHorn())
            {
                ImGui.SameLine();
                var mapLabel = module.Config.ShowNorthHornRouteMap
                    ? module.T("panel.hunt.hide_map")
                    : module.T("panel.hunt.show_map");
                if (ImGui.Button($"{mapLabel}##treasure-map"))
                {
                    module.Config.ShowNorthHornRouteMap = !module.Config.ShowNorthHornRouteMap;
                    module.PluginConfig.Save();
                }

                ImGui.BeginDisabled(running);
                DrawStartModeButtons();
                ImGui.EndDisabled();
            }

            if (!string.IsNullOrEmpty(startError))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.35f, 0.3f, 1f));
                ImGui.TextWrapped(startError);
                ImGui.PopStyleColor();
            }

            if (running)
            {
                var routeNumber = CurrentRouteNumber;
                if (routeNumber != null)
                {
                    OcelotUi.LabelledValue(
                        module.T("panel.hunt.route_point"),
                        $"{routeNumber}/{NorthHornTreasureRoute.RouteCount}");
                }

                OcelotUi.LabelledValue(
                    module.T("panel.hunt.live_count"),
                    GetValidObjects().Count().ToString());
                OcelotUi.LabelledValue(
                    module.T("panel.hunt.elapsed"),
                    $"{stopwatch.Elapsed:mm\\:ss}");

                if (stepIndex < Steps.Count && CurrentStep.Type == PathfinderStepType.WalkToNode)
                {
                    OcelotUi.LabelledValue(
                        module.T("panel.hunt.distance_node"),
                        $"{distance:f1}");
                }
            }

            if (ZoneData.IsInNorthHorn() && module.Config.ShowNorthHornRouteMap)
            {
                DrawRouteMap();
            }
        });
    }

    private void DrawStartModeButtons()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(module.T("panel.hunt.start_mode"));
        var mode = module.Config.NorthHornRouteStartMode;
        var buttonWidth = Math.Max(1f, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f);
        if (DrawModeButton(
                module.T("panel.hunt.nearest_start"),
                mode == TreasureRouteStartMode.Nearest,
                "nearest",
                buttonWidth))
        {
            module.Config.NorthHornRouteStartMode = TreasureRouteStartMode.Nearest;
            module.PluginConfig.Save();
        }

        ImGui.SameLine();
        if (DrawModeButton(
                module.T("panel.hunt.manual_start"),
                mode == TreasureRouteStartMode.Manual,
                "manual",
                buttonWidth))
        {
            module.Config.NorthHornRouteStartMode = TreasureRouteStartMode.Manual;
            module.PluginConfig.Save();
        }

        if (module.Config.NorthHornRouteStartMode != TreasureRouteStartMode.Manual)
        {
            return;
        }

        var routeStart = module.Config.NorthHornManualRouteStart;
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt(
                $"{module.T("panel.hunt.start_number")}##treasure-start-number",
                ref routeStart,
                1,
                5))
        {
            module.Config.NorthHornManualRouteStart = Math.Clamp(
                routeStart,
                1,
                NorthHornTreasureRoute.RouteCount);
            module.PluginConfig.Save();
        }
    }

    private static bool DrawModeButton(string label, bool selected, string id, float width)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.56f, 0.38f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.64f, 0.44f, 1f));
        }

        var clicked = ImGui.Button($"{label}##treasure-mode-{id}", new Vector2(width, 0));
        if (selected)
        {
            ImGui.PopStyleColor(2);
        }

        return clicked;
    }

    private void DrawRouteMap()
    {
        var assetPath = Path.Join(
            Svc.PluginInterface.AssemblyLocation.DirectoryName,
            "assets",
            "treasure-route-north-horn.png");
        if (!File.Exists(assetPath))
        {
            ImGui.TextColored(
                new Vector4(0.95f, 0.55f, 0.25f, 1f),
                module.T("panel.hunt.map_missing"));
            return;
        }

        routeMapTexture ??= Svc.Texture.GetFromFile(assetPath);
        var texture = routeMapTexture.GetWrapOrEmpty();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var imageSize = Math.Min(availableWidth, 520f);
        if (imageSize <= 1f)
        {
            return;
        }
        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.Image(texture.Handle, new Vector2(imageSize, imageSize));

        var scale = imageSize / NorthHornTreasureRoute.MapImageSize;
        var drawList = ImGui.GetWindowDrawList();
        foreach (var treasure in GetValidObjects())
        {
            DrawMapMarker(
                drawList,
                topLeft,
                NorthHornTreasureRoute.WorldToMapPoint(treasure.Position),
                scale,
                3.5f,
                new Vector4(0.95f, 0.74f, 0.22f, 0.95f));
        }

        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer != null)
        {
            DrawMapMarker(
                drawList,
                topLeft,
                NorthHornTreasureRoute.WorldToMapPoint(localPlayer.Position),
                scale,
                5f,
                new Vector4(0.18f, 0.82f, 0.92f, 1f));
        }

        var routeNumber = CurrentRouteNumber;
        if (routeNumber != null)
        {
            DrawMapMarker(
                drawList,
                topLeft,
                NorthHornTreasureRoute.GetMapPoint(routeNumber.Value),
                scale,
                6f,
                new Vector4(0.96f, 0.26f, 0.2f, 1f));
        }
    }

    private static void DrawMapMarker(
        ImDrawListPtr drawList,
        Vector2 topLeft,
        Vector2 mapPoint,
        float scale,
        float radius,
        Vector4 color)
    {
        var point = topLeft + mapPoint * scale;
        drawList.AddCircleFilled(point, radius, ImGui.GetColorU32(color));
        drawList.AddCircle(
            point,
            radius + 1f,
            ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.05f, 0.9f)),
            0,
            1.5f);
    }

    protected override void OnStarted()
    {
        liveTreasurePositions.Clear();
        if (TreasureSightRefreshPolicy.ShouldCast(
                module.Config.CastTreasureSightBeforeHunt,
                module.Tracker.CountInitialised))
        {
            Plugin.Chain.Submit(new TreasureSightChain(module, force: true));
        }
    }

    protected override bool ShouldStopEarly()
    {
        return module.Tracker.CountInitialised
               && module.Tracker.BronzeChests <= 0
               && module.Tracker.SilverChests <= 0;
    }

    protected override IEnumerable<IGameObject> GetValidObjects()
    {
        var isNorthHorn = ZoneData.IsInNorthHorn();
        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.Treasure
                        && !o.IsDead
                        && o.IsValid()
                        && o.IsTargetable
                        && (!isNorthHorn || !IsOpened(o)));
    }

    protected override bool ShouldUseInitialTransit(uint nodeId)
    {
        _ = nodeId;
        return NorthHornRouteTransitPolicy.AllowsInitialTransit(ZoneData.IsInNorthHorn(), stepIndex);
    }

    protected override bool ShouldUseForcedRecovery(uint nodeId)
    {
        _ = nodeId;
        return NorthHornRouteTransitPolicy.AllowsForcedRecovery(ZoneData.IsInNorthHorn());
    }

    protected override IGameObject? ResolveLiveObjectForNode(uint nodeId, Vector3 expectedPosition)
    {
        var isNorthHorn = ZoneData.IsInNorthHorn();
        return GetValidObjects()
            .Where(candidate => TreasureObjectMatchPolicy.IsMatch(
                isNorthHorn,
                nodeId,
                candidate.BaseId,
                Vector3.DistanceSquared(candidate.Position, expectedPosition),
                LiveTreasureMatchRadius,
                candidate.BaseId == nodeId || IsLiveTreasureEligible(candidate)))
            .OrderBy(candidate => candidate.BaseId == nodeId ? 0 : 1)
            .ThenBy(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition))
            .FirstOrDefault();
    }

    protected override bool TryGetDestinationForCurrentStep(out Vector3 destination)
    {
        if (liveTreasurePositions.TryGetValue(CurrentStep.NodeId, out destination))
        {
            return true;
        }

        if (pathfinder?.TryGetNodePosition(CurrentStep.NodeId, out destination) == true)
        {
            return true;
        }

        var treasure = Treasure.FirstOrDefault(node => node.Id == CurrentStep.NodeId);
        destination = treasure.Position;
        return treasure.Id == CurrentStep.NodeId;
    }

    protected override unsafe IPathfinder CreatePathfinder()
    {
        var territoryId = Svc.ClientState.TerritoryType;
        if (!TreasureHuntDataPolicy.ShouldReload(
                loadedTreasureTerritory,
                territoryId,
                Treasure.Count))
        {
            Svc.Log.Verbose(
                $"Reusing {Treasure.Count} cached treasure nodes for territory {territoryId}.");
            return CreateTreasurePathfinder();
        }

        Treasure.Clear();

        // Prefer the packaged layout. ActiveLayout is streamed around the
        // player and is therefore only a partial snapshot on the much larger
        // North Horn map; using it as the route source silently omits coffers
        // that are not currently loaded.
        var packagedTreasure = TreasureLayoutData.Read(territoryId);
        if (packagedTreasure.Count > 0)
        {
            Treasure.AddRange(packagedTreasure.Select(node =>
                new TreasureData.TreasureDatum(node.Id, node.Position, node.Sgb)));
            Treasure = Treasure.OrderBy(node => node.Id).ToList();
            Svc.Log.Info(
                $"Loaded {Treasure.Count} treasure nodes from packaged layout " +
                $"for territory {territoryId}.");
            loadedTreasureTerritory = territoryId;
            return CreateTreasurePathfinder();
        }

        // Keep the runtime reader as a compatibility fallback for unexpected
        // game-data layouts. Its result may be incomplete, but it is still
        // better than making the hunt unavailable altogether.
        Svc.Log.Warning(
            $"Packaged treasure layout was unavailable for territory " +
            $"{territoryId}; falling back to ActiveLayout.");
        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            Svc.Log.Warning("No active layout");
            return CreateTreasurePathfinder();
        }

        if (!layout->InstancesByType.TryGetValue(InstanceType.Treasure, out var mapPtr, false))
        {
            Svc.Log.Warning("No active treasure map");
            return CreateTreasurePathfinder();
        }

        var treasureSheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Treasure>();
        foreach (ILayoutInstance* instance in mapPtr.Value->Values)
        {
            var transform = instance->GetTransformImpl();
            var position = transform->Translation;
            var minimumFieldHeight = ZoneData.IsInNorthHorn() ? -500f : -10f;
            if (!IsFinite(position) || position.Y <= minimumFieldHeight)
            {
                continue;
            }

            var treasureRowId = Unsafe.Read<uint>((byte*)instance + 0x30);
            if (!treasureSheet.TryGetRow(treasureRowId, out var treasureRow))
            {
                Svc.Log.Warning($"Skipping unknown treasure layout row {treasureRowId} at {position}.");
                continue;
            }

            var sgbId = treasureRow.SGB.RowId;
            if (sgbId != 1596 && sgbId != 1597)
            {
                continue;
            }

            Treasure.Add(new TreasureData.TreasureDatum(treasureRowId, position, sgbId));
        }

        Treasure = Treasure.OrderBy(t => t.Id).ToList();

        if (Treasure.Count > 0)
        {
            loadedTreasureTerritory = territoryId;
        }

        return CreateTreasurePathfinder();
    }

    private Pathfinder CreateTreasurePathfinder()
    {
        var routeStartMode = ZoneData.IsInNorthHorn()
            ? module.Config.NorthHornRouteStartMode
            : (TreasureRouteStartMode?)null;
        return new Pathfinder(
            Treasure,
            module.PluginConfig.PathfinderConfig.ReturnCost,
            module.PluginConfig.PathfinderConfig.TeleportCost,
            routeStartMode,
            module.Config.NorthHornManualRouteStart);
    }

    protected override uint? GetPriorityNode(
        uint currentNodeId,
        IReadOnlyCollection<uint> remainingNodeIds)
    {
        if (!ZoneData.IsInNorthHorn() || pathfinder == null)
        {
            return null;
        }

        _ = remainingNodeIds;
        var candidates = GetValidObjects()
            .Where(IsLiveTreasureEligible)
            .Select(candidate => new LiveTreasureCandidate(candidate.BaseId, candidate.Position))
            .ToArray();
        foreach (var candidatesById in candidates.GroupBy(candidate => candidate.BaseId))
        {
            var nearest = candidatesById
                .OrderBy(candidate => Vector3.DistanceSquared(Player.Position, candidate.Position))
                .First();
            liveTreasurePositions[candidatesById.Key] = nearest.Position;
        }

        var priorityNodeId = LiveTreasurePriorityPolicy.Select(
            Player.Position,
            currentNodeId,
            candidates,
            LiveTreasurePriorityRadius);
        if (priorityNodeId != null)
        {
            var selected = candidates
                .Where(candidate => candidate.BaseId == priorityNodeId.Value)
                .OrderBy(candidate => Vector3.DistanceSquared(Player.Position, candidate.Position))
                .First();
            liveTreasurePositions[priorityNodeId.Value] = selected.Position;
        }

        return priorityNodeId;
    }

    protected override IReadOnlyList<uint>? ReorderRemainingNodes(
        Vector3 start,
        IReadOnlyCollection<uint> remainingNodeIds)
    {
        _ = start;
        if (!ZoneData.IsInNorthHorn())
        {
            return null;
        }

        return NorthHornRouteRejoinPolicy.PreservePlannedOrder(remainingNodeIds);
    }

    private bool IsLiveTreasureEligible(IGameObject candidate)
    {
        if (NorthHornTreasureRoute.IsWaypointId(candidate.BaseId))
        {
            return false;
        }

        if (TreasureData.Levels.TryGetValue(candidate.BaseId, out var level))
        {
            return TreasureLevelPolicy.IsEligible(
                ZoneData.IsInNorthHorn(),
                level,
                config.MaxLevel);
        }

        if (TreasureLevelPolicy.IsEligible(
                ZoneData.IsInNorthHorn(),
                verifiedLevel: null,
                maximumLevel: config.MaxLevel))
        {
            return true;
        }

        var nearestLayoutNode = Treasure
            .Where(node => TreasureData.Levels.ContainsKey(node.Id))
            .Select(node => new
            {
                Node = node,
                DistanceSquared = Vector3.DistanceSquared(node.Position, candidate.Position),
            })
            .Where(match => match.DistanceSquared
                            <= LiveTreasureMatchRadius * LiveTreasureMatchRadius)
            .OrderBy(match => match.DistanceSquared)
            .ThenBy(match => match.Node.Id)
            .FirstOrDefault();
        return nearestLayoutNode != null
               && TreasureData.Levels[nearestLayoutNode.Node.Id] <= config.MaxLevel;
    }

    protected override Func<Chain> GetInteractionChain(
        uint nodeId,
        Vector3 expectedPosition,
        ulong gameObjectId,
        Action<HuntInteractionOutcome> complete)
    {
        return () =>
        {
            var outcome = HuntInteractionOutcome.None;
            var attempts = 0;
            var hasInteracted = false;
            var lastAttemptAt = long.MinValue;

            return Chain.Create($"Treasure.Interact({nodeId})")
                .Then(new TaskManagerTask((Func<bool?>)(() =>
                {
                    var target = ResolveTarget(nodeId, expectedPosition, gameObjectId);
                    if (target == null)
                    {
                        outcome = HuntInteractionOutcome.TargetGone;
                        return true;
                    }

                    if (IsOpened(target))
                    {
                        outcome = HuntInteractionOutcome.Succeeded;
                        return true;
                    }

                    if (Player.DistanceTo(target) > DISTANCE_TO_NODE_TO_USE + 0.75f)
                    {
                        outcome = HuntInteractionOutcome.OutOfRange;
                        return true;
                    }

                    if (!target.IsTargetable)
                    {
                        if (hasInteracted && Environment.TickCount64 - lastAttemptAt >= 500)
                        {
                            outcome = HuntInteractionOutcome.TargetGone;
                            return true;
                        }

                        return false;
                    }

                    if (!TreasureInteractionPolicy.CanAttempt(
                            Svc.Condition[ConditionFlag.Mounted],
                            Svc.Condition[ConditionFlag.InCombat],
                            Player.IsCasting))
                    {
                        return false;
                    }

                    var now = Environment.TickCount64;
                    if (attempts >= 3)
                    {
                        if (now - lastAttemptAt < 1500)
                        {
                            return false;
                        }

                        outcome = HuntInteractionOutcome.Failed;
                        return true;
                    }

                    if (attempts > 0 && now - lastAttemptAt < 1000)
                    {
                        return false;
                    }

                    attempts++;
                    lastAttemptAt = now;
                    if (TryInteract(target))
                    {
                        hasInteracted = true;
                    }

                    return false;
                }), new TaskManagerConfiguration
                {
                    TimeLimitMS = 12000,
                    AbortOnTimeout = true,
                    OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                        outcome = HuntInteractionOutcome.Failed,
                }))
                .OnFinally(() => complete(
                    outcome == HuntInteractionOutcome.None
                        ? HuntInteractionOutcome.Failed
                        : outcome));
        };
    }

    private IGameObject? ResolveTarget(uint nodeId, Vector3 expectedPosition, ulong gameObjectId)
    {
        var isNorthHorn = ZoneData.IsInNorthHorn();
        var candidates = Svc.Objects
            .Where(candidate => candidate.ObjectKind == ObjectKind.Treasure
                                && candidate.IsValid()
                                && TreasureObjectMatchPolicy.IsMatch(
                                    isNorthHorn,
                                    nodeId,
                                    candidate.BaseId,
                                    Vector3.DistanceSquared(candidate.Position, expectedPosition),
                                    LiveTreasureMatchRadius,
                                    candidate.BaseId == nodeId || IsLiveTreasureEligible(candidate)))
            .ToList();

        return candidates.FirstOrDefault(candidate => candidate.GameObjectId == gameObjectId)
               ?? candidates
                   .OrderBy(candidate => candidate.BaseId == nodeId ? 0 : 1)
                   .ThenBy(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition))
                   .FirstOrDefault();
    }

    private static unsafe bool IsOpened(IGameObject target)
    {
        if (!target.IsValid() || target.Address == nint.Zero)
        {
            return false;
        }

        var gameObject = (GameObject*)(void*)target.Address;
        if (gameObject == null)
        {
            return false;
        }

        var treasure = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)gameObject;
        return treasure->Flags.HasFlag(
            FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags.Opened);
    }

    private static unsafe bool TryInteract(IGameObject target)
    {
        if (!target.IsValid() || !target.IsTargetable || target.Address == nint.Zero)
        {
            return false;
        }

        var gameObject = (GameObject*)(void*)target.Address;
        if (gameObject == null)
        {
            return false;
        }

        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null)
        {
            return false;
        }

        Svc.Targets.Target = target;
        targetSystem->InteractWithObject(gameObject);
        return true;
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X)
               && float.IsFinite(position.Y)
               && float.IsFinite(position.Z);
    }

    protected override List<uint> GetValidNodes(int max)
    {
        if (ZoneData.IsInNorthHorn())
        {
            return NorthHornTreasureRoute.NodeIds.ToList();
        }

        return Treasure
            .Where(treasure => TreasureData.Levels.TryGetValue(treasure.Id, out var level)
                               && level <= max)
            .Select(treasure => treasure.Id)
            .ToList();
    }
}

public static class TreasureInteractionPolicy
{
    public static bool CanAttempt(bool isMounted, bool inCombat, bool isCasting)
    {
        // Occult Crescent coffers are directly interactable while mounted.
        // Keep the parameter explicit as a regression guard: mount state must
        // not block interaction or force a dismount/remount cycle per chest.
        _ = isMounted;
        return !inCombat && !isCasting;
    }
}

public static class TreasureHuntDataPolicy
{
    public static bool ShouldReload(uint? loadedTerritory, uint currentTerritory, int cachedNodeCount)
    {
        return loadedTerritory != currentTerritory || cachedNodeCount <= 0;
    }
}
