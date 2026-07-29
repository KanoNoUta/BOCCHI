using BOCCHI.Modules.Data;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace BOCCHI.Pathfinding;

public static class RuntimeNodeStore
{
    private sealed class Document
    {
        public int SchemaVersion { get; set; } = 1;

        public uint TerritoryId { get; set; }

        public Dictionary<uint, Position> Nodes { get; set; } = [];
    }

    public static Dictionary<uint, Vector3> Load(string huntKind, uint territoryId)
    {
        var path = GetPath(huntKind, territoryId);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document == null || document.SchemaVersion != 1 || document.TerritoryId != territoryId)
            {
                Svc.Log.Warning($"Ignoring incompatible runtime hunt-node cache: {path}");
                return [];
            }

            return document.Nodes
                .Select(pair => (pair.Key, Position: new Vector3(pair.Value.X, pair.Value.Y, pair.Value.Z)))
                .Where(pair => IsFinite(pair.Position) && RuntimeNodeId.FromPosition(pair.Position) == pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Position);
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, $"Failed to load runtime hunt-node cache: {path}");
            return [];
        }
    }

    public static void Save(string huntKind, uint territoryId, IReadOnlyDictionary<uint, Vector3> nodes)
    {
        var path = GetPath(huntKind, territoryId);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var document = new Document
        {
            TerritoryId = territoryId,
            Nodes = nodes
                .Where(pair => IsFinite(pair.Value) && RuntimeNodeId.FromPosition(pair.Value) == pair.Key)
                .ToDictionary(pair => pair.Key, pair => Position.Create(pair.Value)),
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = Path.Join(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetPath(string huntKind, uint territoryId)
    {
        var safeKind = new string(huntKind.Where(character => char.IsAsciiLetterOrDigit(character) || character == '-').ToArray());
        if (string.IsNullOrWhiteSpace(safeKind))
        {
            throw new ArgumentException("Hunt kind must contain a file-safe character.", nameof(huntKind));
        }

        return Path.Join(
            Svc.PluginInterface.ConfigDirectory.FullName,
            "runtime-hunt-nodes",
            $"{safeKind}-{territoryId}.json");
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }
}
