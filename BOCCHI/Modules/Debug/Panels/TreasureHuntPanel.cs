using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Pathfinding;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Ocelot.IPC;
using Ocelot.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BOCCHI.Modules.Debug.Panels;

using TreasureData = (uint id, Vector3 position, uint type);

public sealed class TreasureHuntPanel : Panel
{
    private readonly List<TreasureData> treasure = [];

    private readonly Stopwatch stopwatch = new();

    private CancellationTokenSource? cancellation;

    private Task? task;

    private bool runRequested;

    private bool hasRun;

    private int progress;

    private int maxProgress;

    private uint snapshotTerritory;

    private string status = string.Empty;

    public override string GetName() => "Treasure Hunt Helper";

    public override unsafe void Render(DebugModule module)
    {
        if (snapshotTerritory != Svc.ClientState.TerritoryType)
        {
            RefreshTreasureSnapshot();
        }

        OcelotUi.LabelledValue("Bronze", treasure.Count(node => node.type == 1596));
        OcelotUi.LabelledValue("Silver", treasure.Count(node => node.type == 1597));

        OcelotUi.Indent(() =>
        {
            var isRunning = task is { IsCompleted: false };
            if (!isRunning)
            {
                if (ImGui.Button(hasRun ? "Run again" : "Run"))
                {
                    RefreshTreasureSnapshot();
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
        snapshotTerritory = 0;
        treasure.Clear();
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
            var nodes = treasure.Select(node => new HuntNode(node.id, node.position)).ToArray();
            var shards = AethernetData.All()
                .Select(data => new HuntAethernet(data.Aethernet, data.Destination, data.Position))
                .ToArray();
            maxProgress = NodeDataPrecomputer.GetTaskCount(nodes.Length, shards.Length);
            if (nodes.Length == 0)
            {
                status = "No treasure layout nodes are available in the current territory.";
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
                "precomputed_treasure_hunt_data.json");
            await NodeDataPrecomputer.WriteAtomicAsync(outputFile, data, token);
            status = $"Saved {progress}/{maxProgress} calculations to {outputFile}";
            Svc.Log.Info(status);
        }
        catch (OperationCanceledException)
        {
            status = "Treasure precomputation cancelled; no partial file was written.";
        }
        catch (Exception exception)
        {
            status = $"Treasure precomputation failed: {exception.GetBaseException().Message}";
            Svc.Log.Error(exception, status);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private unsafe void RefreshTreasureSnapshot()
    {
        treasure.Clear();
        snapshotTerritory = Svc.ClientState.TerritoryType;

        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null || !layout->InstancesByType.TryGetValue(InstanceType.Treasure, out var mapPtr, false))
        {
            return;
        }

        var treasureSheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Treasure>();
        foreach (ILayoutInstance* instance in mapPtr.Value->Values)
        {
            var position = instance->GetTransformImpl()->Translation;
            var minimumFieldHeight = ZoneData.IsInNorthHorn() ? -500f : -10f;
            if (position.Y <= minimumFieldHeight)
            {
                continue;
            }

            var rowId = Unsafe.Read<uint>((byte*)instance + 0x30);
            if (!treasureSheet.TryGetRow(rowId, out var row))
            {
                continue;
            }

            var sgbId = row.SGB.RowId;
            if (sgbId is 1596 or 1597)
            {
                treasure.Add((rowId, position, sgbId));
            }
        }

        treasure.Sort((left, right) => left.id.CompareTo(right.id));
    }

    private void CancelAndReset()
    {
        cancellation?.Cancel();
        runRequested = false;
        stopwatch.Stop();
    }
}
