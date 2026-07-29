using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.ItemHelpers;
using BOCCHI.Pathfinding;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BOCCHI.Modules.Debug.Panels;

public sealed class CarrotHuntPanel : Panel
{
    private readonly Stopwatch stopwatch = new();

    private CancellationTokenSource? cancellation;

    private Task? task;

    private bool runRequested;

    private bool hasRun;

    private int progress;

    private int maxProgress;

    private int nodeCount;

    private string status = string.Empty;

    public override string GetName() => "Carrot Hunt Helper";

    public override unsafe void Render(DebugModule module)
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        OcelotUi.LabelledValue("Carrots", nodeCount > 0 ? nodeCount : GetSourceNodes(saveRuntimeNodes: false).Count);

        OcelotUi.Indent(() =>
        {
            if (ImGui.Button("Test carrot usage chain"))
            {
                Plugin.Chain.Submit(() => Chain.Create()
                    .ConditionalThen(_ => Player.Mounted, _ => Actions.Unmount.Cast())
                    .Wait(500)
                    .BreakIf(() => Items.FortuneCarrot.Count() <= 0)
                    .Then(_ => Items.FortuneCarrot.Use())
                    .WaitToCast()
                    .Then(_ => GetBunnyChests().Any())
                    .Then(_ =>
                    {
                        var target = GetBunnyChests().FirstOrDefault();
                        if (target == null)
                        {
                            return true;
                        }

                        Svc.Targets.Target = target;
                        if (!vnav.IsRunning())
                        {
                            vnav.PathfindAndMoveTo(target.Position, false);
                        }

                        if (Player.DistanceTo(target) <= 2f)
                        {
                            var gameObject = (GameObject*)(void*)target.Address;
                            TargetSystem.Instance()->InteractWithObject(gameObject);
                            return true;
                        }

                        return false;
                    })
                    .WaitToCast());
            }

            var isRunning = task is { IsCompleted: false };
            if (!isRunning)
            {
                if (ImGui.Button(hasRun ? "Run again" : "Run"))
                {
                    runRequested = true;
                }
            }
            else if (ImGui.Button("Cancel"))
            {
                cancellation?.Cancel();
            }

            if (!hasRun)
            {
                return;
            }

            var completed = Volatile.Read(ref progress);
            var completion = maxProgress == 0 ? 0f : completed / (float)maxProgress * 100f;
            OcelotUi.LabelledValue("Progress", $"{completion:F2}%");
            OcelotUi.Indent(() => OcelotUi.LabelledValue("Calculations", $"{completed}/{maxProgress}"));
            OcelotUi.LabelledValue("Elapsed", stopwatch.Elapsed.ToString("mm\\:ss"));
            if (!string.IsNullOrEmpty(status))
            {
                ImGui.TextWrapped(status);
            }
        });
    }

    public override void Update(DebugModule module)
    {
        if (!runRequested || task is { IsCompleted: false })
        {
            return;
        }

        runRequested = false;
        hasRun = true;
        progress = 0;
        status = string.Empty;
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        task = PrecomputeAsync(module, cancellation.Token);
    }

    public override void OnTerritoryChanged(uint id, DebugModule module)
    {
        CancelAndReset();
        nodeCount = 0;
    }

    public override void Dispose()
    {
        CancelAndReset();
        cancellation?.Dispose();
        cancellation = null;
    }

    private async Task PrecomputeAsync(DebugModule module, CancellationToken token)
    {
        stopwatch.Restart();
        try
        {
            var nodes = GetSourceNodes(saveRuntimeNodes: true).ToArray();
            nodeCount = nodes.Length;
            var shards = AethernetData.All()
                .Select(data => new HuntAethernet(data.Aethernet, data.Destination, data.Position))
                .ToArray();
            maxProgress = NodeDataPrecomputer.GetTaskCount(nodes.Length, shards.Length);
            if (nodes.Length == 0)
            {
                status = "No verified carrot nodes are available in the current territory.";
                return;
            }

            var vnav = module.GetIPCSubscriber<VNavmesh>();
            var data = await NodeDataPrecomputer.ComputeAsync(
                nodes,
                shards,
                (start, destination, segmentToken) =>
                    vnav.PathfindCancelable(start, destination, false, segmentToken),
                completed => Volatile.Write(ref progress, completed),
                message => Svc.Log.Warning(message),
                segmentTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: token);

            var outputFile = Path.Join(
                ZoneData.GetCurrentZoneDataDirectory(),
                "precomputed_carrot_hunt_data.json");
            await NodeDataPrecomputer.WriteAtomicAsync(outputFile, data, token);
            status = $"Saved {progress}/{maxProgress} calculations to {outputFile}";
            Svc.Log.Info(status);
        }
        catch (OperationCanceledException)
        {
            status = "Carrot precomputation cancelled; no partial file was written.";
        }
        catch (Exception exception)
        {
            status = $"Carrot precomputation failed: {exception.GetBaseException().Message}";
            Svc.Log.Error(exception, status);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private List<HuntNode> GetSourceNodes(bool saveRuntimeNodes)
    {
        if (ZoneData.IsInSouthHorn())
        {
            return CarrotData.Data
                .Select(node => new HuntNode(node.Id, node.Position))
                .ToList();
        }

        if (!ZoneData.IsInNorthHorn())
        {
            return [];
        }

        var territory = Svc.ClientState.TerritoryType;
        var positions = RuntimeNodeStore.Load("carrot", territory);
        var changed = false;
        foreach (var carrot in GetCarrots())
        {
            var nodeId = RuntimeNodeId.FromPosition(carrot.Position);
            if (positions.TryAdd(nodeId, carrot.Position))
            {
                changed = true;
            }
        }

        if (saveRuntimeNodes && changed)
        {
            RuntimeNodeStore.Save("carrot", territory, positions);
        }

        return positions
            .OrderBy(pair => pair.Key)
            .Select(pair => new HuntNode(pair.Key, pair.Value))
            .ToList();
    }

    private static IEnumerable<IEventObj> GetCarrots()
    {
        return Svc.Objects.OfType<IEventObj>()
            .Where(gameObject => gameObject.BaseId == (uint)OccultObjectType.Carrot && gameObject.IsValid());
    }

    private static IEnumerable<IEventObj> GetBunnyChests()
    {
        return Svc.Objects.OfType<IEventObj>()
            .Where(gameObject => gameObject.BaseId == (uint)OccultObjectType.BunnyChest);
    }

    private void CancelAndReset()
    {
        cancellation?.Cancel();
        runRequested = false;
        stopwatch.Stop();
    }
}
