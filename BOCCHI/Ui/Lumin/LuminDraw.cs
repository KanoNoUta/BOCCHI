using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Ui.Lumin;

/// <summary>
/// Port of helpers/draw.cpp + the easing/animation helpers from the Lumin
/// ImGui framework (functions.h easing + anim_container).
/// </summary>
public static class LuminDraw
{
    private static readonly Dictionary<uint, object> AnimStates = new();

    public static T Anim<T>(uint id) where T : class, new()
    {
        if (AnimStates.TryGetValue(id, out var existing) && existing is T typed)
        {
            return typed;
        }

        var state = new T();
        AnimStates[id] = state;
        return state;
    }

    public static float FixedSpeed(float speed) =>
        speed / MathF.Max(ImGui.GetIO().Framerate, 1f);

    /// <summary>static_easing: linear step of speed / framerate toward the target.</summary>
    public static float StaticEase(ref float value, float target, float speed)
    {
        var step = FixedSpeed(speed);
        if (value < target)
        {
            value += step;
            if (value > target)
            {
                value = target;
            }
        }
        else if (value > target)
        {
            value -= step;
            if (value < target)
            {
                value = target;
            }
        }

        return value;
    }

    /// <summary>dynamic_easing: ImLerp(value, target, speed / framerate)</summary>
    public static float Ease(ref float value, float target, float speed)
    {
        var t = MathF.Min(FixedSpeed(speed), 1f);
        value += (target - value) * t;
        return value;
    }

    /// <summary>dynamic_easing for Vector4 (color)</summary>
    public static Vector4 Ease(ref Vector4 value, Vector4 target, float speed)
    {
        var t = MathF.Min(FixedSpeed(speed), 1f);
        value += (target - value) * t;
        return value;
    }

    /// <summary>dynamic_easing for Vector4 interpreted as a rect (x,y,z,w)</summary>
    public static Vector4 EaseRect(ref Vector4 value, Vector4 target, float speed)
    {
        var t = MathF.Min(FixedSpeed(speed), 1f);
        value += (target - value) * t;
        return value;
    }

    public static void RectFilled(
        ImDrawListPtr drawList,
        Vector2 pMin,
        Vector2 pMax,
        uint col,
        float rounding,
        ImDrawFlags flags = ImDrawFlags.RoundCornersAll)
    {
        if ((col & 0xFF000000) == 0)
        {
            return;
        }

        if (rounding < 0.5f || (flags & ImDrawFlags.RoundCornersNone) == ImDrawFlags.RoundCornersNone)
        {
            drawList.AddRectFilled(pMin, pMax, col);
        }
        else
        {
            drawList.AddRectFilled(pMin, pMax, col, rounding, flags);
        }
    }

    public static void Rect(
        ImDrawListPtr drawList,
        Vector2 pMin,
        Vector2 pMax,
        uint col,
        float rounding,
        float thickness,
        ImDrawFlags flags = ImDrawFlags.RoundCornersAll)
    {
        if ((col & 0xFF000000) == 0)
        {
            return;
        }

        var min = pMin;
        var max = pMax;
        if ((int)thickness % 2 != 0)
        {
            min += new Vector2(0.50f, 0.50f);
            max -= new Vector2(0.50f, 0.50f);
        }

        if (rounding < 0.5f || (flags & ImDrawFlags.RoundCornersNone) == ImDrawFlags.RoundCornersNone)
        {
            drawList.AddRect(min, max, col, 0f, flags, thickness);
        }
        else
        {
            drawList.AddRect(min, max, col, rounding, flags, thickness);
        }
    }

    public static void CircleFilled(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        uint col,
        int segments = 0)
    {
        if ((col & 0xFF000000) == 0 || radius < 0.5f)
        {
            return;
        }

        drawList.AddCircleFilled(center, radius, col, segments > 0 ? segments : 0);
    }

    public static void Circle(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        uint col,
        float thickness,
        int segments = 0)
    {
        if ((col & 0xFF000000) == 0 || radius < 0.5f)
        {
            return;
        }

        drawList.AddCircle(center, radius, col, segments > 0 ? segments : 0, thickness);
    }

    public static void Line(
        ImDrawListPtr drawList,
        Vector2 p1,
        Vector2 p2,
        uint col,
        float thickness = 1f)
    {
        drawList.AddLine(p1, p2, col, thickness);
    }


    /// <summary>
    /// Cheap layered glow (Lumin uses a 9-patch shadow texture; ImGui.NET here
    /// has no AddShadowRect, so we emulate with expanding alpha rectangles).
    /// The rounding has to grow by the full ring offset, otherwise the corners
    /// of successive rings drift apart and the halo reads as visible banding
    /// rather than a blur. Alpha falls off quadratically so the outermost rings
    /// fade out instead of ending on a hard edge.
    /// </summary>
    public static void ShadowRect(
        ImDrawListPtr drawList,
        Vector2 objMin,
        Vector2 objMax,
        uint col,
        float thickness,
        Vector2 offset,
        ImDrawFlags flags,
        float rounding)
    {
        if ((col & 0xFF000000) == 0 || thickness < 0.5f)
        {
            return;
        }

        // One ring per pixel of spread keeps the steps below the eye's banding
        // threshold; the old 6 rings over an 18px spread were 3px apart.
        var layers = Math.Clamp((int)MathF.Ceiling(thickness), 4, 24);
        var baseAlpha = (col >> 24) & 0xFF;
        var rgb = col & 0xFFFFFF;
        for (var i = layers - 1; i >= 0; i--)
        {
            var u = (i + 1) / (float)layers;
            var t = thickness * u;
            var falloff = (1f - u) * (1f - u);
            var alpha = (uint)MathF.Round(baseAlpha * falloff);
            if (alpha == 0)
            {
                continue;
            }

            var layerCol = rgb | (alpha << 24);
            RectFilled(
                drawList,
                objMin - new Vector2(t, t) + offset,
                objMax + new Vector2(t, t) + offset,
                layerCol,
                rounding + t,
                flags);
        }
    }
    /// <summary>
    /// text_clipped with alignment, ported from helpers/draw.cpp.
    /// Lumin's original draws at hard-coded design pixel sizes (11/12/13px);
    /// the Dalamud font atlas is only rasterised at the user's configured size,
    /// so re-scaling glyphs there makes CJK text blurry and undersized. We keep
    /// the layout maths and always render at the font's native size instead.
    /// </summary>
    public static void TextClipped(
        ImDrawListPtr drawList,
        Vector2 posMin,
        Vector2 posMax,
        uint color,
        string text,
        Vector2 align)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DrawAligned(drawList, ImGui.GetFont(), ImGui.GetFontSize(), ImGui.CalcTextSize(text), posMin, posMax, color, text, align);
    }

    /// <summary>
    /// TextClipped for a FontAwesome glyph. The icon font has to be pushed
    /// around CalcTextSize as well, otherwise the glyph is measured with the
    /// body font and lands off-centre in its slot.
    /// </summary>
    public static void IconClipped(
        ImDrawListPtr drawList,
        Vector2 posMin,
        Vector2 posMax,
        uint color,
        FontAwesomeIcon icon,
        Vector2 align)
    {
        var glyph = icon.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var glyphSize = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();

        DrawAligned(drawList, font, fontSize, glyphSize, posMin, posMax, color, glyph, align);
    }

    private static void DrawAligned(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float fontSize,
        Vector2 textSize,
        Vector2 posMin,
        Vector2 posMax,
        uint color,
        string text,
        Vector2 align)
    {
        var pos = new Vector2(
            MathF.Round(posMin.X + (posMax.X - posMin.X - textSize.X) * align.X),
            MathF.Round(posMin.Y + (posMax.Y - posMin.Y - textSize.Y) * align.Y));

        // Anything that does not fit its slot is cut off at the slot edge rather
        // than bleeding over neighbouring widgets.
        drawList.PushClipRect(posMin, posMax, true);
        drawList.AddText(font, fontSize, pos, color, text);
        drawList.PopClipRect();
    }
}