using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Ui.Lumin;

/// <summary>
/// Port of widgets/widgets.cpp from the Lumin ImGui framework: tab buttons,
/// brand header, checkbox switch, primary button and section panels.
/// </summary>
public static class LuminWidgets
{
    public sealed class TabButtonState
    {
        public float Width;
        public float Alpha;
        public Vector4 Icon;
    }

    /// <summary>
    /// Sidebar tab item (32 px tall, icon slot 32 px). Mirrors c_widgets::tab_button.
    /// Writes the selected item rect into <paramref name="selectedRect"/> so the
    /// sidebar can draw the sliding overlay + indicator.
    /// </summary>
    public static bool TabButton(string name, int tab, ref int selectedTab, ref Vector4 selectedRect, FontAwesomeIcon? icon = null)
    {
        var id = ImGui.GetID(name);
        var state = LuminDraw.Anim<TabButtonState>(id);

        var isSelected = tab == selectedTab;
        var tabHeight = LuminTheme.SlotHeight(LuminTheme.TabHeight);
        var targetWidth = MathF.Max(ImGui.GetContentRegionAvail().X, tabHeight);
        if (state.Width <= 0f)
        {
            state.Width = targetWidth;
        }

        LuminDraw.Ease(ref state.Width, targetWidth, 18f);

        var size = new Vector2(MathF.Round(state.Width), tabHeight);
        ImGui.InvisibleButton(name, size);

        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        // Fire on release inside the item (ImGui's own button semantics) so a
        // press that drags off the tab does not switch pages.
        var pressed = ImGui.IsItemDeactivated() && hovered;

        if (pressed)
        {
            selectedTab = tab;
        }

        if (isSelected)
        {
            selectedRect = new Vector4(rectMin.X, rectMin.Y, rectMax.X, rectMax.Y);
        }

        var alphaTarget = (isSelected || hovered) ? 1f : 0.58f;
        var iconTarget = (isSelected || hovered) ? LuminTheme.White : LuminTheme.Text;
        LuminDraw.Ease(ref state.Alpha, alphaTarget, 18f);
        LuminDraw.Ease(ref state.Icon, iconTarget, 18f);

        var drawList = ImGui.GetWindowDrawList();
        var iconColor = state.Icon with { W = state.Icon.W * state.Alpha };
        var iconSlot = MathF.Max(LuminTheme.S(LuminTheme.IconSlot), tabHeight);

        if (icon is { } iconValue)
        {
            LuminDraw.IconClipped(
                drawList,
                rectMin,
                new Vector2(rectMin.X + iconSlot, rectMax.Y),
                LuminTheme.Col(iconColor),
                iconValue,
                new Vector2(0.5f, 0.5f));
        }

        LuminDraw.TextClipped(
            drawList,
            new Vector2(rectMin.X + iconSlot, rectMin.Y),
            new Vector2(rectMax.X - LuminTheme.S(8), rectMax.Y),
            LuminTheme.Col(LuminTheme.White, state.Alpha),
            name,
            new Vector2(0f, 0.5f));

        // The label is clipped to its slot, so long page names would silently
        // truncate without any way to read them.
        if (hovered)
        {
            var labelWidth = rectMax.X - LuminTheme.S(8) - (rectMin.X + iconSlot);
            if (ImGui.CalcTextSize(name).X > labelWidth)
            {
                ImGui.SetTooltip(name);
            }
        }

        return pressed;
    }

    /// <summary>c_widgets::brand_header — logo mark + name + subtitle.</summary>
    /// <param name="icon">
    /// Optional texture drawn in the logo slot. When null the vector mark from
    /// the Lumin design is drawn instead.
    /// </param>
    public static void BrandHeader(string name, string subtitle, IDalamudTextureWrap? icon = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var padding = LuminTheme.S(8);
        // Two stacked text lines plus padding; the design's fixed 50px is only
        // enough at the 12px reference font.
        var height = MathF.Max(LuminTheme.S(LuminTheme.BrandHeaderHeight), lineHeight * 2f + padding * 2f);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, height);
        var markSize = MathF.Min(LuminTheme.S(34), height - padding * 2f);
        var inner = new Vector2(pos.X + LuminTheme.S(10), pos.Y + (height - markSize) * 0.5f);
        var mark = new Vector4(inner.X, inner.Y, inner.X + markSize, inner.Y + markSize);

        ImGui.Dummy(size);

        var drawList = ImGui.GetWindowDrawList();
        LuminDraw.RectFilled(drawList, pos, pos + size, LuminTheme.Col(LuminTheme.Child), LuminTheme.S(12));

        if (icon != null)
        {
            DrawBrandTexture(drawList, icon, mark);
        }
        else
        {
            DrawBrandGlyph(drawList, mark, markSize);
        }

        var textLeft = mark.Z + LuminTheme.S(10);
        var textRight = pos.X + size.X - LuminTheme.S(8);
        var textTop = pos.Y + (height - lineHeight * 2f) * 0.5f;
        LuminDraw.TextClipped(
            drawList,
            new Vector2(textLeft, textTop),
            new Vector2(textRight, textTop + lineHeight),
            LuminTheme.Col(LuminTheme.White),
            name,
            new Vector2(0f, 0.5f));
        LuminDraw.TextClipped(
            drawList,
            new Vector2(textLeft, textTop + lineHeight),
            new Vector2(textRight, textTop + lineHeight * 2f),
            LuminTheme.Col(LuminTheme.Text),
            subtitle,
            new Vector2(0f, 0.5f));
    }

    /// <summary>Plugin icon in the logo slot, inset inside the Lumin plate.</summary>
    private static void DrawBrandTexture(ImDrawListPtr drawList, IDalamudTextureWrap icon, Vector4 mark)
    {
        var min = new Vector2(mark.X, mark.Y);
        var max = new Vector2(mark.Z, mark.W);
        var rounding = LuminTheme.S(10);

        LuminDraw.RectFilled(drawList, min, max, LuminTheme.Col(LuminTheme.Widget), rounding);

        // Inset so the plate reads as a frame around the art instead of the
        // icon's own corners fighting the card's rounding.
        var inset = LuminTheme.S(2);
        drawList.AddImageRounded(
            icon.Handle,
            min + new Vector2(inset, inset),
            max - new Vector2(inset, inset),
            Vector2.Zero,
            Vector2.One,
            LuminTheme.Col(LuminTheme.White),
            MathF.Max(rounding - inset, 0f));
    }

    /// <summary>Fallback vector mark from the Lumin design.</summary>
    private static void DrawBrandGlyph(ImDrawListPtr drawList, Vector4 mark, float markSize)
    {
        LuminDraw.RectFilled(drawList, new Vector2(mark.X, mark.Y), new Vector2(mark.Z, mark.W), LuminTheme.Col(LuminTheme.Widget), LuminTheme.S(10));
        LuminDraw.RectFilled(drawList, new Vector2(mark.X, mark.Y) + LuminTheme.S(3, 3), new Vector2(mark.Z, mark.W) - LuminTheme.S(3, 3), LuminTheme.Col(LuminTheme.Accent, 0.12f), LuminTheme.S(8));

        var center = new Vector2((mark.X + mark.Z) * 0.5f, (mark.Y + mark.W) * 0.5f);
        var accent = LuminTheme.Col(LuminTheme.Accent);
        var glyph = markSize / LuminTheme.S(34);
        LuminDraw.CircleFilled(drawList, center, 12f * glyph, LuminTheme.Col(LuminTheme.Accent, 0.08f));
        LuminDraw.Line(drawList, center + new Vector2(0, -13f * glyph), center + new Vector2(0, 12f * glyph), accent, MathF.Max(1f, 2f * glyph));
        LuminDraw.CircleFilled(drawList, center + new Vector2(0, -3f * glyph), 5f * glyph, accent);
        LuminDraw.CircleFilled(drawList, center + new Vector2(0, -3f * glyph), 2f * glyph, LuminTheme.Col(LuminTheme.Black, 0.38f));
        LuminDraw.Line(drawList, center + new Vector2(0, 3f * glyph), center + new Vector2(-8f * glyph, 13f * glyph), LuminTheme.Col(LuminTheme.Accent, 0.85f), MathF.Max(1f, 1.5f * glyph));
        LuminDraw.Line(drawList, center + new Vector2(0, 3f * glyph), center + new Vector2(8f * glyph, 13f * glyph), LuminTheme.Col(LuminTheme.Accent, 0.85f), MathF.Max(1f, 1.5f * glyph));
    }

    public sealed class CheckboxState
    {
        public float Pos;
        public float HoverAlpha;
        public float GlowAlpha;
        public Vector4 Text;
        public Vector4 Background;
        public Vector4 Circle;
    }

    /// <summary>c_widgets::checkbox — animated switch with title and description tooltip.</summary>
    public static bool Checkbox(string name, string? description, ref bool value)
    {
        var id = ImGui.GetID(name);
        var state = LuminDraw.Anim<CheckboxState>(id);

        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = LuminTheme.SlotHeight(40f);
        var innerMin = new Vector2(pos.X, pos.Y + LuminTheme.S(6));
        var innerMax = new Vector2(pos.X + width, pos.Y + height - LuminTheme.S(6));
        var switchSize = LuminTheme.S(32f, 18f);
        var centerY = pos.Y + height * 0.5f;
        var buttonMin = new Vector2(innerMax.X - switchSize.X, centerY - switchSize.Y * 0.5f);
        var buttonMax = new Vector2(innerMax.X, centerY + switchSize.Y * 0.5f);

        ImGui.InvisibleButton(name, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var held = ImGui.IsItemActive();
        // Toggle on release inside the row, matching ImGui.Checkbox, so dragging
        // off the row cancels instead of flipping the setting.
        var pressed = ImGui.IsItemDeactivated() && hovered;

        if (pressed)
        {
            value = !value;
        }

        LuminDraw.Ease(ref state.Pos, value ? 1f : 0f, 20f);
        LuminDraw.Ease(ref state.HoverAlpha, hovered || held ? 1f : 0f, 14f);
        LuminDraw.Ease(ref state.GlowAlpha, value ? 1f : 0f, 18f);
        var dimText = (LuminTheme.Text * 0.82f) with { W = LuminTheme.Text.W };
        LuminDraw.Ease(ref state.Text, value ? LuminTheme.White : dimText, 18f);
        LuminDraw.Ease(ref state.Background, value ? LuminTheme.OnTrack : LuminTheme.OffTrack, 18f);
        LuminDraw.Ease(ref state.Circle, value ? LuminTheme.OnKnob : LuminTheme.OffKnob, 18f);

        var drawList = ImGui.GetWindowDrawList();

        var labelMax = new Vector2(buttonMin.X - LuminTheme.S(12), innerMax.Y);
        LuminDraw.TextClipped(
            drawList,
            innerMin,
            labelMax,
            LuminTheme.Col(state.Text),
            name,
            new Vector2(0f, 0.5f));

        if (hovered)
        {
            // Config labels are long and the label slot is clipped; show the
            // full text alongside the description rather than losing either.
            var truncated = ImGui.CalcTextSize(name).X > labelMax.X - innerMin.X;
            if (!string.IsNullOrEmpty(description))
            {
                ImGui.SetTooltip(truncated ? $"{name}\n\n{description}" : description);
            }
            else if (truncated)
            {
                ImGui.SetTooltip(name);
            }
        }

        var switchRounding = LuminTheme.S(999);
        var knobCenter = new Vector2(
            MathF.Round(buttonMin.X + LuminTheme.S(9f) + (buttonMax.X - LuminTheme.S(9f) - buttonMin.X - LuminTheme.S(9f)) * state.Pos),
            centerY);
        var markerX = buttonMax.X - LuminTheme.S(8.6f) + (buttonMin.X + LuminTheme.S(8.6f) - (buttonMax.X - LuminTheme.S(8.6f))) * state.Pos;
        var markerMin = new Vector2(markerX - LuminTheme.S(1.2f), centerY - LuminTheme.S(3.8f));
        var markerMax = new Vector2(markerX + LuminTheme.S(1.2f), centerY + LuminTheme.S(3.8f));

        LuminDraw.RectFilled(drawList, buttonMin, buttonMax, LuminTheme.Col(state.Background), switchRounding);
        var markerCol = LuminTheme.Col(value ? LuminTheme.Accent : LuminTheme.Rgb(132, 113, 198), value ? 0.92f : 0.58f);
        drawList.AddRectFilled(markerMin, markerMax, markerCol, markerMax.X - markerMin.X);
        LuminDraw.CircleFilled(drawList, knobCenter, LuminTheme.S(5.8f), LuminTheme.Col(state.Circle), 32);
        LuminDraw.CircleFilled(
            drawList,
            knobCenter,
            LuminTheme.S(3f),
            LuminTheme.Col(value ? LuminTheme.OnKnobInner : LuminTheme.OffKnobInner, value ? 1f : 0.95f),
            32);

        if (ImGui.GetContentRegionAvail().Y > 0)
        {
            var y = MathF.Floor(pos.Y + height - 0.5f) + 0.5f;
            drawList.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + width, y), LuminTheme.Col(LuminTheme.Border, 0.72f), 1f);
        }

        return pressed;
    }

    public sealed class PrimaryButtonState
    {
        public Vector4 Background;
        public Vector4 Text;
        public float Shadow;
        public float IconSpacing;
        public float Radius;
        public float CircleAlpha;
        public bool Clicked;
    }

    /// <summary>c_widgets::primary_button — accent button with ripple + shadow.</summary>
    public static bool PrimaryButton(
        string name,
        string? tooltip = null,
        float? width = null,
        bool enabled = true,
        Vector4? backgroundOverride = null,
        Vector4? textOverride = null)
    {
        var id = ImGui.GetID(name);
        var state = LuminDraw.Anim<PrimaryButtonState>(id);

        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width ?? ImGui.GetContentRegionAvail().X, LuminTheme.SlotHeight(42f));

        ImGui.BeginDisabled(!enabled);
        ImGui.InvisibleButton(name, size);
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        // Query the interaction state before EndDisabled: a disabled item still
        // reports its rect, but hover/active have to be read while the disabled
        // scope is open for AllowWhenDisabled tooltips to behave.
        var pressed = enabled && ImGui.IsItemDeactivated() && hovered;
        ImGui.EndDisabled();

        if (pressed)
        {
            state.Clicked = true;
        }

        var baseBackground = backgroundOverride ?? LuminTheme.Accent;
        var baseText = textOverride ?? LuminTheme.Black;
        LuminDraw.Ease(ref state.Background, held ? LuminTheme.White : baseBackground, 18f);
        LuminDraw.Ease(ref state.Text, baseText, 18f);
        state.Shadow = MathF.Min(state.Shadow + LuminDraw.FixedSpeed(10f) * ((hovered || held) ? 1f : -1f), hovered || held ? 1f : 0.45f);
        state.Shadow = MathF.Max(state.Shadow, 0f);
        LuminDraw.Ease(ref state.IconSpacing, hovered || held ? 14f : 7f, 18f);

        var rectWidth = rectMax.X - rectMin.X;
        state.Radius = Math.Clamp(
            state.Radius + LuminDraw.FixedSpeed(rectWidth * 7f) * (state.Clicked ? 1f : -1f),
            0f,
            rectWidth * 0.5f + LuminTheme.S(8));
        state.CircleAlpha = Math.Clamp(
            state.CircleAlpha + LuminDraw.FixedSpeed(3.5f) * (state.Radius > rectWidth * 0.5f - LuminTheme.S(1) ? 1f : -1f),
            0f,
            1f);

        if (state.CircleAlpha > 0.95f)
        {
            state.Radius = 0f;
            state.CircleAlpha = 0f;
            state.Clicked = false;
        }

        var drawList = ImGui.GetWindowDrawList();
        LuminDraw.ShadowRect(
            drawList,
            rectMin,
            rectMax,
            LuminTheme.Col(baseBackground, 0.16f * state.Shadow),
            LuminTheme.S(12),
            Vector2.Zero,
            ImDrawFlags.RoundCornersAll,
            LuminTheme.S(11));

        LuminDraw.RectFilled(drawList, rectMin, rectMax, LuminTheme.Col(state.Background, enabled ? 1f : 0.55f), LuminTheme.S(11));

        ImGui.PushClipRect(rectMin, rectMax, true);
        LuminDraw.CircleFilled(
            drawList,
            new Vector2((rectMin.X + rectMax.X) * 0.5f, (rectMin.Y + rectMax.Y) * 0.5f),
            state.Radius,
            LuminTheme.Col(LuminTheme.Black, 0.28f * (1f - state.CircleAlpha)),
            48);
        ImGui.PopClipRect();

        LuminDraw.TextClipped(
            drawList,
            rectMin,
            rectMax,
            LuminTheme.Col(state.Text, enabled ? 1f : 0.5f),
            name,
            new Vector2(0.5f, 0.5f));

        if (tooltip != null && hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        return pressed;
    }

    public sealed class PillState
    {
        public Vector4 Background;
        public Vector4 Text;
        public float Hover;
    }

    /// <summary>Total width <see cref="PillToggle"/> will occupy, for table column sizing.</summary>
    public static float PillToggleWidth(string statusLabel, string actionLabel)
    {
        var padding = LuminTheme.S(10);
        var gap = LuminTheme.S(6);
        var dotRadius = MathF.Max(LuminTheme.S(3f), 2f);
        var statusWidth = padding + dotRadius * 2f + gap + ImGui.CalcTextSize(statusLabel).X + padding;
        var actionWidth = padding + ImGui.CalcTextSize(actionLabel).X + padding;
        return statusWidth + gap + actionWidth;
    }

    /// <summary>
    /// Compact rounded pill used for the command bar's mode controls: a status
    /// half showing a coloured dot plus label, and an action half that toggles.
    /// Sized to the frame height so it lines up with the icon buttons next to it.
    /// </summary>
    public static bool PillToggle(
        string id,
        string statusLabel,
        Vector4 statusColor,
        string actionLabel,
        string? tooltip = null)
    {
        // Matched to the frame height so the pill lines up with the icon buttons
        // it shares a row with instead of floating above them.
        var height = ImGui.GetFrameHeight();
        var padding = LuminTheme.S(10);
        var gap = LuminTheme.S(6);
        var dotRadius = MathF.Max(LuminTheme.S(3f), 2f);
        var statusWidth = padding + dotRadius * 2f + gap + ImGui.CalcTextSize(statusLabel).X + padding;
        var actionWidth = padding + ImGui.CalcTextSize(actionLabel).X + padding;

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = height * 0.5f;

        // The status half is display-only; only the action half is clickable, so
        // clicking the state text does not toggle the mode by accident.
        var statusMin = pos;
        var statusMax = new Vector2(pos.X + statusWidth, pos.Y + height);
        LuminDraw.RectFilled(drawList, statusMin, statusMax, LuminTheme.Col(LuminTheme.Widget), rounding);
        LuminDraw.CircleFilled(
            drawList,
            new Vector2(statusMin.X + padding + dotRadius, pos.Y + height * 0.5f),
            dotRadius,
            LuminTheme.Col(statusColor),
            16);
        LuminDraw.TextClipped(
            drawList,
            new Vector2(statusMin.X + padding + dotRadius * 2f + gap, statusMin.Y),
            new Vector2(statusMax.X - padding, statusMax.Y),
            LuminTheme.Col(statusColor),
            statusLabel,
            new Vector2(0f, 0.5f));

        ImGui.Dummy(new Vector2(statusWidth, height));
        ImGui.SameLine(0f, gap);

        var state = LuminDraw.Anim<PillState>(ImGui.GetID(id));
        ImGui.InvisibleButton(id, new Vector2(actionWidth, height));
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        // Release-inside semantics, matching the other Lumin widgets.
        var pressed = ImGui.IsItemDeactivated() && hovered;

        LuminDraw.Ease(ref state.Hover, hovered || held ? 1f : 0f, 16f);
        LuminDraw.Ease(ref state.Background, held ? LuminTheme.Circle : LuminTheme.Widget, 18f);
        LuminDraw.Ease(ref state.Text, hovered || held ? LuminTheme.White : LuminTheme.Text, 18f);

        LuminDraw.RectFilled(drawList, rectMin, rectMax, LuminTheme.Col(state.Background), rounding);
        if (state.Hover > 0.01f)
        {
            LuminDraw.Rect(
                drawList,
                rectMin,
                rectMax,
                LuminTheme.Col(LuminTheme.Accent, 0.55f * state.Hover),
                rounding,
                1f);
        }

        LuminDraw.TextClipped(
            drawList,
            rectMin,
            rectMax,
            LuminTheme.Col(state.Text),
            actionLabel,
            new Vector2(0.5f, 0.5f));

        if (hovered && !string.IsNullOrEmpty(tooltip))
        {
            ImGui.SetTooltip(tooltip);
        }

        return pressed;
    }

    private static readonly Dictionary<string, float> SectionHeights = new();
    private static readonly Stack<string> OpenSections = new();

    /// <summary>Rounded section card with a title, mirroring begin_visual_section.</summary>
    public static void BeginSection(string title)
    {
        OpenSections.Push(title);

        var width = ImGui.GetContentRegionAvail().X;
        // A height of 0 makes BeginChild claim every remaining pixel, which is
        // what pushed everything below the card off the page. ImGui cannot size
        // a child to its content in a single pass, so reuse the height measured
        // in EndSection last frame; only the first frame of a layout change is
        // a pass behind.
        var height = SectionHeights.TryGetValue(title, out var measured) ? measured : 0f;

        // The rounded card is painted manually, so the child's own square
        // background would poke out at the corners.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.BeginChild(
            $"##LuminSection-{title}",
            new Vector2(width, height),
            false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        LuminDraw.RectFilled(drawList, min, max, LuminTheme.Col(LuminTheme.Child), LuminTheme.S(14));

        ImGui.PushStyleColor(ImGuiCol.Text, LuminTheme.White);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    public static void EndSection()
    {
        var title = OpenSections.Count > 0 ? OpenSections.Pop() : null;
        // Content height plus the child's bottom padding, so the card wraps its
        // content rather than the remaining window.
        var height = ImGui.GetCursorPosY() + ImGui.GetStyle().WindowPadding.Y;
        ImGui.EndChild();

        if (title != null)
        {
            SectionHeights[title] = height;
        }
    }
}
