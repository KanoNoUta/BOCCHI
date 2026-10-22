using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using Ocelot;
using Ocelot.Windows;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Services;

/// <summary>Shows what changed the first time a new version loads, and on /bocchi changelog.</summary>
[OcelotWindow]
public class ChangelogWindow : OcelotWindow
{
    private readonly Config config;

    private string version = ChangelogText.CurrentPluginVersion;

    private IReadOnlyList<ChangelogLine> lines = [];

    public ChangelogWindow(Plugin plugin, Config config)
        : base(plugin, config)
    {
        this.config = config;
        Size = new Vector2(560, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 240),
            MaximumSize = new Vector2(1000, 900),
        };
    }

    public override void PostInitialize()
    {
        // Wait a tick so the window system is ready before opening.
        Svc.Framework.RunOnTick(ShowIfNewVersion);
    }

    private void ShowIfNewVersion()
    {
        var current = ChangelogText.CurrentPluginVersion;
        var lastSeen = config.LastSeenPluginVersion;
        if (string.IsNullOrWhiteSpace(lastSeen))
        {
            // A fresh install has nothing "new" to announce.
            MarkSeen();
            return;
        }

        if (lastSeen != current && ChangelogText.TryGetSection(current, out _))
        {
            Show();
            return;
        }

        MarkSeen();
    }

    public void Show()
    {
        version = ChangelogText.CurrentPluginVersion;
        ChangelogText.TryGetSection(version, out lines);
        WindowName = GetWindowName();
        IsOpen = true;
    }

    public override void OnClose()
    {
        MarkSeen();
    }

    protected override void Render(RenderContext context)
    {
        if (lines.Count == 0)
        {
            ImGui.TextWrapped(I18N.T("windows.changelog.empty"));
        }
        else
        {
            ImGui.BeginChild("##bocchi-changelog", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 1.4f), false);
            foreach (var line in lines)
            {
                switch (line.Kind)
                {
                    case ChangelogLineKind.Heading:
                        ImGui.Spacing();
                        ImGui.TextUnformatted(line.Text);
                        ImGui.Separator();
                        break;
                    case ChangelogLineKind.Bullet:
                        ImGui.Bullet();
                        ImGui.SameLine();
                        ImGui.TextWrapped(line.Text);
                        break;
                    default:
                        ImGui.TextWrapped(line.Text);
                        break;
                }
            }

            ImGui.EndChild();
        }

        if (ImGui.Button(I18N.T("windows.changelog.got_it"), new Vector2(-1, 0)))
        {
            IsOpen = false;
        }
    }

    private void MarkSeen()
    {
        var current = ChangelogText.CurrentPluginVersion;
        if (config.LastSeenPluginVersion == current)
        {
            return;
        }

        config.LastSeenPluginVersion = current;
        config.Save();
    }

    protected override string GetWindowName()
    {
        return string.Format(I18N.T("windows.changelog.title"), version) + "###bocchi-changelog";
    }
}
