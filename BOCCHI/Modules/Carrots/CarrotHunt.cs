using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.ItemHelpers;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Modules.Carrots;

public class CarrotHunt(CarrotsModule module) : Hunter(module)
{
    private const int BunnyChestSpawnTimeoutMs = 8000;

    private const int BunnyChestApproachTimeoutMs = 10000;

    private const int BunnyChestInteractionTimeoutMs = 10000;

    private const float BunnyChestSearchRadius = 10f;

    private readonly Dictionary<uint, Vector3> knownNodePositions = [];

    private uint nodeTerritory;

    private bool runtimeNodesLoaded;

    private bool missingNorthHornNodeWarningShown;

    protected override IEnumerable<IGameObject> GetValidObjects()
    {
        return Svc.Objects
            .Where(o => o is
            {
                ObjectKind: ObjectKind.EventObj,
                BaseId: (uint)OccultObjectType.Carrot,
                IsDead: false,
            } && o.IsValid());
    }

    protected override bool TryGetDestinationForCurrentStep(out Vector3 destination)
    {
        if (pathfinder?.TryGetNodePosition(CurrentStep.NodeId, out destination) == true)
        {
            return true;
        }

        return knownNodePositions.TryGetValue(CurrentStep.NodeId, out destination);
    }

    protected override IPathfinder CreatePathfinder()
    {
        RefreshKnownNodePositions(warnWhenEmpty: true);
        return new Pathfinder(
            new Dictionary<uint, Vector3>(knownNodePositions),
            module.PluginConfig.PathfinderConfig.ReturnCost,
            module.PluginConfig.PathfinderConfig.TeleportCost);
    }

    protected override unsafe Func<Chain> GetInteractionChain(
        uint nodeId,
        Vector3 expectedPosition,
        ulong gameObjectId,
        Action<HuntInteractionOutcome> complete)
    {
        return () =>
        {
            var outcome = HuntInteractionOutcome.None;
            var existingChestIds = new HashSet<ulong>();
            ulong spawnedChestId = 0;
            var chestInteractionAttempts = 0;
            var lastChestInteractionAt = long.MinValue;
            var lastUnmountAttemptAt = Environment.TickCount64 - 1000;
            var chain = Chain.Create($"Carrot.Interact({nodeId})")
                .Then(_ =>
                {
                    var target = ResolveCarrot(expectedPosition, gameObjectId);
                    if (target == null)
                    {
                        outcome = HuntInteractionOutcome.TargetGone;
                    }
                    else if (Player.DistanceTo(target) > DISTANCE_TO_NODE_TO_USE + 0.75f)
                    {
                        outcome = HuntInteractionOutcome.OutOfRange;
                    }
                })
                .BreakIf(() => outcome != HuntInteractionOutcome.None)
                .Then(new TaskManagerTask((Func<bool?>)(() =>
                {
                    if (!Player.Mounted)
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
                .BreakIf(() => Items.FortuneCarrot.Count() <= 0)
                .Then(_ => existingChestIds = GetBunnyChests()
                    .Where(chest => chest.IsValid() && chest.IsTargetable)
                    .Select(chest => chest.GameObjectId)
                    .ToHashSet())
                .Then(_ => Items.FortuneCarrot.Use())
                .WaitToCast()
                .Then(new TaskManagerTask(() =>
                {
                    var chest = GetBunnyChests()
                        .Where(candidate => !existingChestIds.Contains(candidate.GameObjectId)
                                            && candidate.IsValid()
                                            && candidate.IsTargetable
                                            && Vector3.DistanceSquared(candidate.Position, expectedPosition)
                                            <= BunnyChestSearchRadius * BunnyChestSearchRadius)
                        .OrderBy(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition))
                        .FirstOrDefault();
                    if (chest == null)
                    {
                        return false;
                    }

                    spawnedChestId = chest.GameObjectId;
                    return true;
                }, new TaskManagerConfiguration
                {
                    TimeLimitMS = BunnyChestSpawnTimeoutMs,
                    AbortOnTimeout = true,
                    OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                        outcome = HuntInteractionOutcome.Failed,
                }))
                .Then(new TaskManagerTask((Func<bool?>)(() =>
                {
                    var chest = GetBunnyChests()
                        .FirstOrDefault(candidate => candidate.GameObjectId == spawnedChestId
                                                     && candidate.IsValid());
                    if (chest == null)
                    {
                        return false;
                    }

                    if (Player.DistanceTo(chest) <= DISTANCE_TO_NODE_TO_USE + 0.75f)
                    {
                        if (vnav.IsRunning())
                        {
                            vnav.Stop();
                        }

                        return true;
                    }

                    if (!vnav.IsRunning())
                    {
                        vnav.PathfindAndMoveTo(chest.Position, false);
                    }

                    return false;
                }), new TaskManagerConfiguration
                {
                    TimeLimitMS = BunnyChestApproachTimeoutMs,
                    AbortOnTimeout = true,
                    OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                    {
                        if (vnav.IsRunning())
                        {
                            vnav.Stop();
                        }

                        outcome = HuntInteractionOutcome.Failed;
                    },
                }))
                .Then(new TaskManagerTask((Func<bool?>)(() =>
                {
                    if (Svc.Objects.LocalPlayer?.IsCasting == true)
                    {
                        return true;
                    }

                    var chest = GetBunnyChests()
                        .FirstOrDefault(candidate => candidate.GameObjectId == spawnedChestId);
                    if (chest == null
                        || !chest.IsValid()
                        || !chest.IsTargetable
                        || chest.Address == nint.Zero)
                    {
                        if (chestInteractionAttempts > 0)
                        {
                            outcome = HuntInteractionOutcome.Succeeded;
                            return null;
                        }

                        return false;
                    }

                    var now = Environment.TickCount64;
                    if (chestInteractionAttempts > 0 && now - lastChestInteractionAt < 1000)
                    {
                        return false;
                    }

                    if (chestInteractionAttempts >= 3)
                    {
                        outcome = HuntInteractionOutcome.Failed;
                        return null;
                    }

                    Svc.Targets.Target = chest;

                    var gameObject = (GameObject*)(void*)chest.Address;
                    var targetSystem = TargetSystem.Instance();
                    if (targetSystem == null)
                    {
                        return false;
                    }

                    targetSystem->InteractWithObject(gameObject);
                    chestInteractionAttempts++;
                    lastChestInteractionAt = now;
                    return Svc.Objects.LocalPlayer?.IsCasting == true;
                }), new TaskManagerConfiguration
                {
                    TimeLimitMS = BunnyChestInteractionTimeoutMs,
                    AbortOnTimeout = true,
                    OnTaskTimeout = (TaskManagerTask _, ref long _) =>
                        outcome = HuntInteractionOutcome.Failed,
                }))
                .WaitToCast()
                .Then(_ => outcome = HuntInteractionOutcome.Succeeded)
                .OnFinally(() =>
                {
                    if (vnav.IsRunning())
                    {
                        vnav.Stop();
                    }

                    complete(
                        outcome == HuntInteractionOutcome.None
                            ? HuntInteractionOutcome.Failed
                            : outcome);
                });

            return chain;
        };
    }

    private IGameObject? ResolveCarrot(Vector3 expectedPosition, ulong gameObjectId)
    {
        var candidates = GetValidObjects()
            .Where(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition) <= 25f)
            .ToList();
        return candidates.FirstOrDefault(candidate => candidate.GameObjectId == gameObjectId)
               ?? candidates.OrderBy(candidate => Vector3.DistanceSquared(candidate.Position, expectedPosition)).FirstOrDefault();
    }

    protected override List<uint> GetValidNodes(int max)
    {
        RefreshKnownNodePositions(warnWhenEmpty: true);
        if (ZoneData.IsInSouthHorn())
        {
            return CarrotData.Data
                .Where(node => node.Level <= max)
                .Select(node => node.Id)
                .ToList();
        }

        var nodes = pathfinder?.KnownNodeIds.ToHashSet() ?? [];
        nodes.UnionWith(knownNodePositions.Keys);
        if (nodes.Count > 0)
        {
            Svc.Log.Info($"Using {nodes.Count} verified North Horn carrot positions from precomputed/runtime data.");
        }

        return nodes.ToList();
    }

    protected override bool ShouldRepeatAfterCompletion()
    {
        return module.Config.RepeatCarrotHunt && Items.FortuneCarrot.Count() > 0;
    }

    protected override bool ShouldStopEarly()
    {
        return Items.FortuneCarrot.Count() <= 0;
    }

    public void ObserveRuntimeNodes()
    {
        RefreshKnownNodePositions(warnWhenEmpty: false);
    }

    private void RefreshKnownNodePositions(bool warnWhenEmpty)
    {
        var territory = Svc.ClientState.TerritoryType;
        if (nodeTerritory != territory)
        {
            knownNodePositions.Clear();
            nodeTerritory = territory;
            runtimeNodesLoaded = false;
            missingNorthHornNodeWarningShown = false;
        }

        if (ZoneData.IsInSouthHorn())
        {
            foreach (var node in CarrotData.Data)
            {
                knownNodePositions[node.Id] = node.Position;
            }

            return;
        }

        if (!ZoneData.IsInNorthHorn())
        {
            return;
        }

        if (!runtimeNodesLoaded)
        {
            foreach (var (nodeId, position) in RuntimeNodeStore.Load("carrot", territory))
            {
                knownNodePositions[nodeId] = position;
            }

            runtimeNodesLoaded = true;
        }

        var addedNode = false;
        foreach (var obj in GetValidObjects())
        {
            var nodeId = RuntimeNodeId.FromPosition(obj.Position);
            if (knownNodePositions.TryGetValue(nodeId, out var knownPosition)
                && Vector3.DistanceSquared(knownPosition, obj.Position) > 0.25f)
            {
                Svc.Log.Error($"Runtime carrot node ID collision at {obj.Position}; keeping the first observed position {knownPosition}.");
                continue;
            }

            if (knownNodePositions.TryAdd(nodeId, obj.Position))
            {
                addedNode = true;
            }
        }

        if (addedNode)
        {
            try
            {
                RuntimeNodeStore.Save("carrot", territory, knownNodePositions);
                Svc.Log.Info($"Saved {knownNodePositions.Count} verified North Horn carrot positions to the runtime cache.");
            }
            catch (Exception exception)
            {
                Svc.Log.Warning(exception, "Failed to save the North Horn carrot runtime-node cache.");
            }
        }

        if (warnWhenEmpty && knownNodePositions.Count == 0 && !missingNorthHornNodeWarningShown)
        {
            missingNorthHornNodeWarningShown = true;
            const string message =
                "[BOCCHI] 北岛尚无可信胡萝卜坐标。请先在野外靠近并看到胡萝卜节点，让插件采集缓存后再启动自动路线。";
            Svc.Log.Warning(message);
            Svc.Chat.Print(message);
        }
    }

    private IEnumerable<IEventObj> GetBunnyChests()
    {
        return Svc.Objects.OfType<IEventObj>().Where(o => o.BaseId == (uint)OccultObjectType.BunnyChest);
    }
}
