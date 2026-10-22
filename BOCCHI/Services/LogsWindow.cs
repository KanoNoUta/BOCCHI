using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Ocelot;
using Ocelot.Windows;
using Serilog.Events;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Services;

/// <summary>Recent BOCCHI log lines with a level filter, search and copy, for bug reports.</summary>
[OcelotWindow]
public class LogsWindow : OcelotWindow
{
    private static readonly LogEventLevel[] Levels =
    [
        LogEventLevel.Debug,
        LogEventLevel.Information,
        LogEventLevel.Warning,
        LogEventLevel.Error,
    ];

    private int minimumLevel = 1;

    private string search = string.Empty;

    private bool autoScroll = true;

    public LogsWindow(Plugin plugin, Config config)
        : base(plugin, config)
    {
        Size = new Vector2(760, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 260),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    protected override void Render(RenderContext context)
    {
        var labels = Levels.Select(level => I18N.T($"windows.logs.levels.{level.ToString().ToLowerInvariant()}")).ToArray();
        ImGui.SetNextItemWidth(130);
        ImGui.Combo("##bocchi-logs-level", ref minimumLevel, labels, labels.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##bocchi-logs-search", I18N.T("windows.logs.search"), ref search, 200);
        ImGui.SameLine();
        ImGui.Checkbox(I18N.T("windows.logs.auto_scroll"), ref autoScroll);

        var entries = LogBuffer.Snapshot()
            .Where(entry => entry.Level >= Levels[Math.Clamp(minimumLevel, 0, Levels.Length - 1)])
            .Where(entry => search.Length == 0 || entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ImGui.SameLine();
        if (ImGui.Button(I18N.T("windows.logs.copy")))
        {
            ImGui.SetClipboardText(string.Join('\n', entries.Select(LogBuffer.Format)));
        }

        ImGui.SameLine();
        if (ImGui.Button(I18N.T("windows.logs.clear")))
        {
            LogBuffer.Clear();
        }

        ImGui.TextDisabled(string.Format(I18N.T("windows.logs.count"), entries.Count, LogBuffer.Capacity));
        ImGui.BeginChild("##bocchi-logs-body", Vector2.Zero, true, ImGuiWindowFlags.HorizontalScrollbar);
        foreach (var entry in entries)
        {
            var color = entry.Level switch
            {
                >= LogEventLevel.Error => new Vector4(0.95f, 0.4f, 0.35f, 1f),
                LogEventLevel.Warning => new Vector4(0.95f, 0.8f, 0.35f, 1f),
                <= LogEventLevel.Debug => new Vector4(0.6f, 0.6f, 0.6f, 1f),
                _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
            };
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(LogBuffer.Format(entry));
            ImGui.PopStyleColor();
        }

        if (autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20f)
        {
            ImGui.SetScrollHereY(1f);
        }

        ImGui.EndChild();
    }

    protected override string GetWindowName()
    {
        return I18N.T("windows.logs.title") + "###bocchi-logs";
    }
}
