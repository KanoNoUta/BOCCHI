using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.ItemHelpers;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
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

    protected override unsafe Func<Chain> GetInteractionChain(IGameObject obj)
    {
        return () => Chain.Create()
            .BreakIf(() => !GetValidObjects().Any(o => Vector3.Distance(o.Position, obj.Position) <= DISTANCE_TO_NODE_TO_USE))
            .ConditionalThen(_ => Player.Mounted, _ => Actions.Unmount.Cast())
            .Wait(500)
            .BreakIf(() => Items.FortuneCarrot.Count() <= 0)
            .Then(_ => Items.FortuneCarrot.Use())
            .WaitToCast()
            .Then(_ => GetBunnyChests().Any())
            .Then(_ =>
            {
                var chest = GetBunnyChests().FirstOrDefault();
                if (chest == null)
                {
                    return true;
                }

                Svc.Targets.Target = chest;

                var gameObject = (GameObject*)(void*)chest.Address;
                TargetSystem.Instance()->InteractWithObject(gameObject);
                return Svc.Objects.LocalPlayer?.IsCasting == true;
            })
            .WaitToCast();
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

    protected override void Teardown()
    {
        if (!module.Config.RepeatCarrotHunt)
        {
            stopwatch.Stop();
            running = false;
        }

        stepIndex = 0;
        Steps.Clear();
        if (module.TryGetIPCSubscriber<Ocelot.IPC.VNavmesh>(out var navigation)
            && navigation != null
            && navigation.IsReady())
        {
            navigation.Stop();
        }
        Plugin.Chain.Abort();
        StepProcessor.Abort();
        pathfinder = null;
        ResetNodeNavigation();
        ResetTransitNavigation();
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
