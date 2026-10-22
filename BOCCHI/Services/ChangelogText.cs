using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BOCCHI.Services;

public enum ChangelogLineKind
{
    Heading,
    Bullet,
    Paragraph,
}

public readonly record struct ChangelogLine(ChangelogLineKind Kind, string Text);

/// <summary>Reads this version's notes from the CHANGELOG.md embedded in the assembly. Ported from upstream BOCCHI 4.2.</summary>
public static class ChangelogText
{
    private const string ResourceName = "BOCCHI.CHANGELOG.md";

    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    private static readonly Regex Code = new(@"`([^`]+)`", RegexOptions.Compiled);

    /// <summary>The plugin version as CHANGELOG.md writes it ("3.6.0", not "3.6.0.0").</summary>
    public static string CurrentPluginVersion => NormalizeVersion(typeof(Plugin).Assembly.GetName().Version?.ToString());

    public static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        var parts = version.Trim().Split('.');
        return parts.Length == 4 && parts[3] == "0" ? string.Join('.', parts[..3]) : version.Trim();
    }

    public static bool TryGetSection(string version, out IReadOnlyList<ChangelogLine> lines)
    {
        lines = [];
        var markdown = ReadEmbedded();
        var section = markdown == null ? null : ExtractVersionSection(markdown, version);
        if (section == null)
        {
            return false;
        }

        lines = ParseLines(section);
        return lines.Count > 0;
    }

    public static string? ExtractVersionSection(string markdown, string version)
    {
        var heading = $"# {version.Trim()}";
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == heading);
        if (start < 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("# ", StringComparison.Ordinal))
            {
                break;
            }

            builder.AppendLine(lines[i]);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<ChangelogLine> ParseLines(string section)
    {
        List<ChangelogLine> lines = [];
        foreach (var raw in section.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal))
            {
                lines.Add(new ChangelogLine(ChangelogLineKind.Heading, Strip(line.TrimStart('#').Trim())));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                lines.Add(new ChangelogLine(ChangelogLineKind.Bullet, Strip(line[2..].Trim())));
            }
            else
            {
                lines.Add(new ChangelogLine(ChangelogLineKind.Paragraph, Strip(line)));
            }
        }

        return lines;
    }

    private static string Strip(string text)
    {
        return Code.Replace(Bold.Replace(text, "$1"), "$1");
    }

    private static string? ReadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
