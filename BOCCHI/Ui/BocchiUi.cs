using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Ui;

public enum MainWindowPage
{
    Overview,
    Events,
    Explore,
    Farming,
    Statistics,
    Tower,
    AggroRange,
    Crowdsource,
}
public enum BocchiSettingsGroup
{
    Automation,
    Events,
    Explore,
    Farming,
    DisplayAndNotifications,
    Advanced,
}

public enum BocchiOperationState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Completed,
    Failed,
}

public enum BocchiOperationSource
{
    None,
    Automatic,
    Manual,
}

public readonly record struct BocchiOperationInput(
    BocchiOperationState AutomatorState,
    string? AutomatorDetail,
    string? AutomatorOperation,
    bool TreasureRunning,
    bool CarrotRunning,
    bool MobFarmerRunning);

public readonly record struct BocchiOperationSnapshot(
    BocchiOperationState State,
    string Operation,
    BocchiOperationSource Source,
    string? Detail,
    bool CanStopAll);

public static class BocchiOperationPolicy
{
    public static BocchiOperationSnapshot Create(BocchiOperationInput input)
    {
        if (input.AutomatorState is BocchiOperationState.Starting
            or BocchiOperationState.Running
            or BocchiOperationState.Stopping)
        {
            return new BocchiOperationSnapshot(
                input.AutomatorState,
                string.IsNullOrWhiteSpace(input.AutomatorOperation)
                    ? "Automatic operation"
                    : input.AutomatorOperation,
                BocchiOperationSource.Automatic,
                input.AutomatorDetail,
                true);
        }

        if (input.TreasureRunning)
        {
            return Manual("Treasure hunt");
        }

        if (input.CarrotRunning)
        {
            return Manual("Carrot hunt");
        }

        if (input.MobFarmerRunning)
        {
            return Manual("Mob farming");
        }

        if (input.AutomatorState == BocchiOperationState.Failed
            || !string.IsNullOrWhiteSpace(input.AutomatorDetail))
        {
            return new BocchiOperationSnapshot(
                BocchiOperationState.Failed,
                "Automatic operation",
                BocchiOperationSource.Automatic,
                input.AutomatorDetail,
                false);
        }

        return new BocchiOperationSnapshot(
            BocchiOperationState.Stopped,
            "No active operation",
            BocchiOperationSource.None,
            null,
            false);
    }

    private static BocchiOperationSnapshot Manual(string operation)
    {
        return new BocchiOperationSnapshot(
            BocchiOperationState.Running,
            operation,
            BocchiOperationSource.Manual,
            null,
            true);
    }
}

public static class BocchiUiPolicy
{
    public const float SidebarBreakpoint = 720f;

    public const float TwoColumnBreakpoint = 620f;

    public static bool UseSidebar(float availableWidth) => availableWidth >= SidebarBreakpoint;

    public static int GetWorkspaceColumns(float availableWidth) =>
        availableWidth >= TwoColumnBreakpoint ? 2 : 1;

    public static IReadOnlyList<MainWindowPage> GetVisiblePages(
        bool inOccultCrescent,
        bool inForkedTower,
        bool inNorthHorn)
    {
        var pages = new List<MainWindowPage> { MainWindowPage.Overview };
        if (inOccultCrescent)
        {
            pages.Add(MainWindowPage.Events);
            pages.Add(MainWindowPage.Explore);
            pages.Add(MainWindowPage.Farming);
        }

        pages.Add(MainWindowPage.Statistics);

        if (inForkedTower)
        {
            pages.Add(MainWindowPage.Tower);
        }

        if (inNorthHorn)
        {
            pages.Add(MainWindowPage.AggroRange);
        }

        return pages;
    }

    public static BocchiSettingsGroup GetSettingsGroup(string moduleTypeName)
    {
        return moduleTypeName switch
        {
            "AutomatorModule" or "BuffModule" => BocchiSettingsGroup.Automation,
            "FatesModule" or "CriticalEncountersModule" or "ForkedTowerModule" => BocchiSettingsGroup.Events,
            "TreasureModule" or "CarrotsModule" => BocchiSettingsGroup.Explore,
            "MobFarmerModule" => BocchiSettingsGroup.Farming,
            "WindowManagerModule" or "CurrencyModule" or "ExpModule" or "AggroRangeModule" =>
                BocchiSettingsGroup.DisplayAndNotifications,
            "CeCrowdsourceModule" => BocchiSettingsGroup.DisplayAndNotifications,
            _ => BocchiSettingsGroup.Advanced,
        };
    }

    public static bool MatchesSettingsSearch(
        string moduleTitle,
        BocchiSettingsGroup group,
        string search,
        string? localizedGroupLabel = null)
    {
        var term = search.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        return moduleTitle.Contains(term, StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(localizedGroupLabel)
                   && localizedGroupLabel.Contains(term, StringComparison.OrdinalIgnoreCase))
               || GetSettingsSearchTerms(group).Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSettingsGroupLabel(BocchiSettingsGroup group)
    {
        return group.ToString();
    }

    public static string GetSettingsGroupKey(BocchiSettingsGroup group)
    {
        return group switch
        {
            BocchiSettingsGroup.Automation => "automation",
            BocchiSettingsGroup.Events => "events",
            BocchiSettingsGroup.Explore => "explore",
            BocchiSettingsGroup.Farming => "farming",
            BocchiSettingsGroup.DisplayAndNotifications => "display_notifications",
            BocchiSettingsGroup.Advanced => "advanced",
            _ => "advanced",
        };
    }

    private static string GetSettingsSearchTerms(BocchiSettingsGroup group)
    {
        return group switch
        {
            BocchiSettingsGroup.Automation => "Automation 自动化 运行 Buff",
            BocchiSettingsGroup.Events => "Events FATE CE Tower 事件 塔",
            BocchiSettingsGroup.Explore => "Explore Treasure Carrot 探索 宝箱 胡萝卜",
            BocchiSettingsGroup.Farming => "Farming Mob 刷怪 怪物",
            BocchiSettingsGroup.DisplayAndNotifications => "Display Notifications Window Stats 显示 通知 窗口 统计",
            BocchiSettingsGroup.Advanced => "Advanced Debug Data Pathfinder Teleporter 高级 调试 数据 导航",
            _ => group.ToString(),
        };
    }
}

public static class BocchiUi
{
    public static readonly Vector4 SouthHornAccent = new(0.31f, 0.72f, 0.48f, 1f);
    public static readonly Vector4 NorthHornAccent = new(0.42f, 0.68f, 0.95f, 1f);
    public static readonly Vector4 Muted = new(0.48f, 0.53f, 0.61f, 1f);
    public static readonly Vector4 Running = new(0.31f, 0.76f, 0.48f, 1f);
    public static readonly Vector4 Transition = new(0.88f, 0.67f, 0.24f, 1f);
    public static readonly Vector4 Failed = new(0.88f, 0.36f, 0.32f, 1f);

    public static Vector4 GetStatusColor(BocchiOperationState state)
    {
        return state switch
        {
            BocchiOperationState.Running or BocchiOperationState.Completed => Running,
            BocchiOperationState.Starting or BocchiOperationState.Stopping => Transition,
            BocchiOperationState.Failed => Failed,
            _ => Muted,
        };
    }

    public static void StatusDot(BocchiOperationState state)
    {
        ImGui.TextColored(GetStatusColor(state), "●");
    }

    public static bool IconButton(
        FontAwesomeIcon icon,
        string id,
        string tooltip,
        bool enabled = true)
    {
        ImGui.BeginDisabled(!enabled);
        ImGui.PushFont(UiBuilder.IconFont);
        var clicked = ImGui.Button($"{icon.ToIconString()}##{id}", new Vector2(ImGui.GetFrameHeight()));
        ImGui.PopFont();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !string.IsNullOrWhiteSpace(tooltip))
        {
            ImGui.SetTooltip(tooltip);
        }
        return clicked;
    }

    public static bool NavigationItem(
        MainWindowPage page,
        string label,
        FontAwesomeIcon icon,
        bool selected,
        float width)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.22f, 0.39f, 0.49f, 0.72f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.44f, 0.55f, 0.82f));
        }

        var clicked = ImGui.Selectable(
            $"{icon.ToIconString()}  {label}##MainNav-{page}",
            selected,
            ImGuiSelectableFlags.None,
            new Vector2(width, ImGui.GetFrameHeight()));

        if (selected)
        {
            ImGui.PopStyleColor(2);
        }

        return clicked;
    }

    public static void PageHeading(string title, string? subtitle = null)
    {
        ImGui.TextUnformatted(title);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }
        ImGui.Separator();
        ImGui.Spacing();
    }

    public static void SectionHeading(string title)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(title);
        ImGui.Separator();
    }

    public static void Metric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
    }

    public static void EmptyState(string title, string detail)
    {
        ImGui.Dummy(new Vector2(0f, 12f));
        ImGui.TextDisabled(title);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Min(460f, ImGui.GetContentRegionAvail().X));
        ImGui.TextDisabled(detail);
        ImGui.PopTextWrapPos();
    }
}



