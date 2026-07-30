using BOCCHI.Data;
using BOCCHI.Pathfinding;
using BOCCHI.ActionHelpers;
using BOCCHI.Chains;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Modules.Treasure;

public class TreasureHunt(TreasureModule module) : Hunter(module)
{
    private const float LiveTreasureMatchRadius = 12f;

    private List<TreasureData.TreasureDatum> Treasure = [];

    protected override void OnStarted()
    {
        if (module.Config.CastTreasureSightBeforeHunt)
        {
            module.Tracker.InvalidateCount();
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
        return Svc.Objects
            .Where(o => o is
            {
                ObjectKind: ObjectKind.Treasure,
                IsDead: false,
                IsTargetable: true,
            } && o.IsValid());
    }

    protected override IGameObject? ResolveLiveObjectForNode(uint nodeId, Vector3 expectedPosition)
    {
        return GetValidObjects()
            .Where(candidate => candidate.BaseId == nodeId
                                && Vector3.DistanceSquared(candidate.Position, expectedPosition)
                                <= LiveTreasureMatchRadius * LiveTreasureMatchRadius)
            .OrderBy(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition))
            .FirstOrDefault();
    }

    protected override bool TryGetDestinationForCurrentStep(out Vector3 destination)
    {
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
        Treasure.Clear();
        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            Svc.Log.Warning("No active layout");
            return new Pathfinder(Treasure, module.PluginConfig.PathfinderConfig.ReturnCost, module.PluginConfig.PathfinderConfig.TeleportCost);
        }

        if (!layout->InstancesByType.TryGetValue(InstanceType.Treasure, out var mapPtr, false))
        {
            Svc.Log.Warning("No active treasure map");
            return new Pathfinder(Treasure, module.PluginConfig.PathfinderConfig.ReturnCost, module.PluginConfig.PathfinderConfig.TeleportCost);
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

        return new Pathfinder(Treasure, module.PluginConfig.PathfinderConfig.ReturnCost, module.PluginConfig.PathfinderConfig.TeleportCost);
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
            var lastUnmountAttemptAt = Environment.TickCount64 - 1000;

            return Chain.Create($"Treasure.Interact({nodeId})")
                .Then(new TaskManagerTask((Func<bool?>)(() =>
                {
                    if (!Svc.Condition[ConditionFlag.Mounted])
                    {
                        return true;
                    }

                    var now = Environment.TickCount64;
                    if (now - lastUnmountAttemptAt >= 1000)
                    {
                        Actions.TryUnmount();
                        lastUnmountAttemptAt = now;
                    }

                    return false;
                }), new TaskManagerConfiguration
                {
                    TimeLimitMS = 8000,
                    AbortOnTimeout = true,
                    OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                        outcome = HuntInteractionOutcome.Failed,
                }))
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

                    if (Svc.Condition[ConditionFlag.Mounted]
                        || Svc.Condition[ConditionFlag.InCombat]
                        || Player.IsCasting)
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

    private static IGameObject? ResolveTarget(uint nodeId, Vector3 expectedPosition, ulong gameObjectId)
    {
        var candidates = Svc.Objects
            .Where(candidate => candidate.ObjectKind == ObjectKind.Treasure
                                && candidate.BaseId == nodeId
                                && candidate.IsValid()
                                && Vector3.DistanceSquared(candidate.Position, expectedPosition)
                                <= LiveTreasureMatchRadius * LiveTreasureMatchRadius)
            .ToList();

        return candidates.FirstOrDefault(candidate => candidate.GameObjectId == gameObjectId)
               ?? candidates.OrderBy(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition)).FirstOrDefault();
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
        var unknownLevelNodes = Treasure
            .Where(treasure => !TreasureData.Levels.ContainsKey(treasure.Id))
            .Select(treasure => treasure.Id)
            .ToHashSet();

        if (ZoneData.IsInNorthHorn() && unknownLevelNodes.Count > 0)
        {
            Svc.Log.Warning($"North Horn has {unknownLevelNodes.Count} runtime treasure nodes without verified level metadata; MaxLevel filtering is not applied to those nodes.");
        }

        return Treasure
            .Where(treasure => TreasureData.Levels.TryGetValue(treasure.Id, out var level)
                ? level <= max
                : ZoneData.IsInNorthHorn())
            .Select(treasure => treasure.Id)
            .ToList();
    }
}
