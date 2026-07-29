using BOCCHI.Data;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextCopy;

namespace BOCCHI.Modules.ForkedTower;

public static class TowerCapture
{
    private const float ObjectCaptureRadius = 250f;

    public sealed record PositionSnapshot(float X, float Y, float Z)
    {
        public static PositionSnapshot From(Vector3 position)
        {
            return new PositionSnapshot(position.X, position.Y, position.Z);
        }
    }

    public sealed record ObjectSnapshot(
        uint BaseId,
        string ObjectKind,
        string Name,
        PositionSnapshot Position,
        float DistanceFromOrigin);

    public sealed record CaptureDocument(
        int SchemaVersion,
        DateTimeOffset CapturedAt,
        uint TerritoryId,
        uint DynamicEventId,
        string TowerName,
        string EventState,
        PositionSnapshot? EventMapMarker,
        PositionSnapshot? PlayerPosition,
        IReadOnlyList<uint> PlayerStatuses,
        IReadOnlyList<ObjectSnapshot> NearbyPlayers,
        IReadOnlyList<ObjectSnapshot> NearbyEventObjects,
        IReadOnlyList<ObjectSnapshot> ObservedTraps);

    public sealed record CaptureResult(string Path, string Json, string? ClipboardError);

    public static CaptureResult Save(
        uint dynamicEventId,
        string eventState,
        Vector3? eventMapMarker = null,
        IEnumerable<TowerRun.TrapSnapshot>? capturedTraps = null)
    {
        var localPlayer = Svc.Objects.LocalPlayer;
        var origin = eventMapMarker ?? localPlayer?.Position ?? Vector3.Zero;
        var towerName = TowerHelper.TryGetDefinitionByEventId(dynamicEventId, out var definition)
            ? definition.DisplayName
            : EventData.GetCriticalEncounter(dynamicEventId, Svc.ClientState.TerritoryType).InternalName;

        var nearbyObjects = Svc.Objects
            .Where(gameObject => Vector3.Distance(origin, gameObject.Position) <= ObjectCaptureRadius)
            .ToArray();

        // Merge the current object table with the managed whole-run snapshot.
        // A trap that despawned or moved out of object range remains in the
        // capture instead of silently disappearing from the exported layout.
        var observedTraps = new Dictionary<(uint BaseId, int X, int Y, int Z), ObjectSnapshot>();
        if (capturedTraps != null)
        {
            foreach (var trap in capturedTraps)
            {
                observedTraps[TrapKey(trap.BaseId, trap.Position)] = Snapshot(
                    trap.BaseId,
                    ObjectKind.EventObj.ToString(),
                    trap.Name,
                    trap.Position,
                    origin);
            }
        }

        foreach (var gameObject in nearbyObjects.Where(gameObject =>
                     gameObject.BaseId is (uint)OccultObjectType.Trap or (uint)OccultObjectType.BigTrap))
        {
            observedTraps[TrapKey(gameObject.BaseId, gameObject.Position)] = Snapshot(
                gameObject.BaseId,
                gameObject.ObjectKind.ToString(),
                gameObject.Name.TextValue,
                gameObject.Position,
                origin);
        }

        var document = new CaptureDocument(
            SchemaVersion: 2,
            CapturedAt: DateTimeOffset.Now,
            TerritoryId: Svc.ClientState.TerritoryType,
            DynamicEventId: dynamicEventId,
            TowerName: towerName,
            EventState: eventState,
            EventMapMarker: eventMapMarker.HasValue ? PositionSnapshot.From(eventMapMarker.Value) : null,
            PlayerPosition: localPlayer == null ? null : PositionSnapshot.From(localPlayer.Position),
            PlayerStatuses: localPlayer?.StatusList.Select(status => status.StatusId).ToArray() ?? [],
            NearbyPlayers: nearbyObjects
                .Where(gameObject => gameObject.ObjectKind == ObjectKind.Pc)
                .Select(gameObject => Snapshot(gameObject.BaseId, gameObject.ObjectKind, gameObject.Name.TextValue,
                    gameObject.Position, origin))
                .ToArray(),
            NearbyEventObjects: nearbyObjects
                .Where(gameObject => gameObject.ObjectKind == ObjectKind.EventObj)
                .Select(gameObject => Snapshot(gameObject.BaseId, gameObject.ObjectKind, gameObject.Name.TextValue,
                    gameObject.Position, origin))
                .ToArray(),
            ObservedTraps: observedTraps.Values
                .OrderBy(trap => trap.BaseId)
                .ThenBy(trap => trap.Position.X)
                .ThenBy(trap => trap.Position.Y)
                .ThenBy(trap => trap.Position.Z)
                .ToArray());

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var json = JsonSerializer.Serialize(document, options);

        var outputDirectory = Path.Join(Svc.PluginInterface.ConfigDirectory.FullName, "tower-captures");
        Directory.CreateDirectory(outputDirectory);

        var timestamp = document.CapturedAt.ToString("yyyyMMdd-HHmmss-fff");
        var outputPath = Path.Join(outputDirectory, $"tower-{dynamicEventId}-{timestamp}.json");
        var temporaryPath = outputPath + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, outputPath, true);

        string? clipboardError = null;
        try
        {
            ClipboardService.SetText(json);
        }
        catch (Exception exception)
        {
            clipboardError = exception.GetBaseException().Message;
            Svc.Log.Warning(exception, $"Tower capture was saved, but copying it to the clipboard failed: {outputPath}");
        }

        Svc.Log.Info($"Tower capture saved to {outputPath}");
        return new CaptureResult(outputPath, json, clipboardError);
    }

    private static ObjectSnapshot Snapshot(
        uint baseId,
        ObjectKind objectKind,
        string name,
        Vector3 position,
        Vector3 origin)
    {
        return Snapshot(baseId, objectKind.ToString(), name, position, origin);
    }

    private static ObjectSnapshot Snapshot(
        uint baseId,
        string objectKind,
        string name,
        Vector3 position,
        Vector3 origin)
    {
        return new ObjectSnapshot(
            baseId,
            objectKind,
            name,
            PositionSnapshot.From(position),
            Vector3.Distance(origin, position));
    }

    private static (uint BaseId, int X, int Y, int Z) TrapKey(uint baseId, Vector3 position)
    {
        return (
            baseId,
            BitConverter.SingleToInt32Bits(position.X),
            BitConverter.SingleToInt32Bits(position.Y),
            BitConverter.SingleToInt32Bits(position.Z));
    }
}
