using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using BOCCHI.Ui.Lumin;
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
    // Breakpoints are design-space widths (see LuminTheme.Scale). Callers pass
    // widths through LuminTheme.ToDesign so a larger UI font does not silently
    // drop the layout to fewer columns.
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
            ImGui.PushStyleColor(ImGuiCol.Header, LuminTheme.Widget);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, LuminTheme.Circle);
        }

        // Selectable renders its label with the body font, which has no
        // FontAwesome glyphs, so the icon is drawn separately over the item.
        var cursor = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var clicked = ImGui.Selectable(
            $"##MainNav-{page}",
            selected,
            ImGuiSelectableFlags.None,
            new Vector2(width, height));

        if (selected)
        {
            ImGui.PopStyleColor(2);
        }

        var drawList = ImGui.GetWindowDrawList();
        var iconSlot = height;
        var textColor = LuminTheme.Col(selected ? LuminTheme.White : LuminTheme.Text);
        LuminDraw.IconClipped(
            drawList,
            cursor,
            new Vector2(cursor.X + iconSlot, cursor.Y + height),
            textColor,
            icon,
            new Vector2(0.5f, 0.5f));
        LuminDraw.TextClipped(
            drawList,
            new Vector2(cursor.X + iconSlot, cursor.Y),
            new Vector2(cursor.X + width - LuminTheme.S(6), cursor.Y + height),
            textColor,
            label,
            new Vector2(0f, 0.5f));

        return clicked;
    }

    public static void PageHeading(string title, string? subtitle = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = LuminTheme.SlotHeight(40f);
        var max = pos + new Vector2(width, height);
        var padding = LuminTheme.S(14);

        LuminDraw.RectFilled(drawList, pos, max, LuminTheme.Col(LuminTheme.Child), LuminTheme.S(14));
        var titleMin = new Vector2(pos.X + padding, pos.Y);
        LuminDraw.TextClipped(
            drawList,
            titleMin,
            new Vector2(max.X - padding, max.Y),
            LuminTheme.Col(LuminTheme.White),
            title,
            new Vector2(0f, 0.5f));

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var subtitleMin = new Vector2(titleMin.X + ImGui.CalcTextSize(title).X + LuminTheme.S(12), pos.Y);
            LuminDraw.TextClipped(
                drawList,
                subtitleMin,
                new Vector2(max.X - padding, max.Y),
                LuminTheme.Col(LuminTheme.Text),
                subtitle,
                new Vector2(0f, 0.5f));
        }

        ImGui.Dummy(new Vector2(width, height));
        ImGui.Spacing();
    }

    public static void SectionHeading(string title)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetTextLineHeight();
        var barWidth = LuminTheme.S(3);

        LuminDraw.RectFilled(
            drawList,
            new Vector2(pos.X, pos.Y + LuminTheme.S(2)),
            new Vector2(pos.X + barWidth, pos.Y + height - LuminTheme.S(2)),
            LuminTheme.Col(LuminTheme.Accent),
            barWidth * 0.5f);

        var textPos = new Vector2(pos.X + barWidth + LuminTheme.S(6), pos.Y);
        LuminDraw.TextClipped(
            drawList,
            textPos,
            new Vector2(pos.X + width, pos.Y + height),
            LuminTheme.Col(LuminTheme.White),
            title,
            new Vector2(0f, 0.5f));
        ImGui.Dummy(new Vector2(width, height));
        ImGui.Spacing();
    }

    public static void Metric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
    }

    public static void EmptyState(string title, string detail)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var height = MathF.Max(LuminTheme.S(96), lineHeight * 4f);
        var max = pos + new Vector2(width, height);
        var padding = LuminTheme.S(12);

        LuminDraw.RectFilled(drawList, pos, max, LuminTheme.Col(LuminTheme.Child), LuminTheme.S(14));

        // Stack the two lines around the vertical centre instead of using
        // fractional alignment, which overlapped them once the font grew.
        var titleTop = pos.Y + (height - lineHeight * 2f) * 0.5f;
        LuminDraw.TextClipped(
            drawList,
            new Vector2(pos.X + padding, titleTop),
            new Vector2(max.X - padding, titleTop + lineHeight),
            LuminTheme.Col(LuminTheme.White),
            title,
            new Vector2(0.5f, 0.5f));
        LuminDraw.TextClipped(
            drawList,
            new Vector2(pos.X + padding, titleTop + lineHeight),
            new Vector2(max.X - padding, titleTop + lineHeight * 2f),
            LuminTheme.Col(LuminTheme.Text),
            detail,
            new Vector2(0.5f, 0.5f));
        ImGui.Dummy(new Vector2(width, height));
        ImGui.Spacing();
    }
}



