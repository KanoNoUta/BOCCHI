using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Ui.Lumin;

/// <summary>
/// Lumin visual theme: colors, element metrics and global style stack.
/// Values mirror framework/settings/colors.h + elements.h from the
/// Lumin-Free-Imgui-Menu project. Scale follows the active ImGui font so the
/// design stays crisp at any UI scale without applying Dalamud's scale twice.
/// </summary>
public static class LuminTheme
{
    // colors.h
    public static readonly Vector4 Layout = Rgb(25, 25, 28);
    public static readonly Vector4 White = Rgb(255, 255, 255);
    public static readonly Vector4 Black = Rgb(0, 0, 0);
    public static readonly Vector4 Accent = Rgb(176, 180, 255);
    public static readonly Vector4 Child = Rgb(28, 28, 33);
    public static readonly Vector4 Widget = Rgb(33, 33, 40);
    public static readonly Vector4 Text = Rgb(110, 110, 129);
    public static readonly Vector4 Circle = Rgb(50, 50, 63);
    public static readonly Vector4 Border = Rgb(35, 35, 44);

    // Derived colors used by widgets.
    public static readonly Vector4 OnTrack = Rgb(38, 37, 50);
    public static readonly Vector4 OffTrack = Widget;
    public static readonly Vector4 OnKnob = Accent;
    public static readonly Vector4 OffKnob = Rgb(96, 88, 142);
    public static readonly Vector4 OnKnobInner = Rgb(28, 28, 33);
    public static readonly Vector4 OffKnobInner = Rgb(31, 31, 39);
    public static readonly Vector4 ErrorAccent = Rgb(250, 75, 85);

    // elements.h — design-space metrics, scaled through S() at draw time.
    public static readonly Vector2 Padding = new(10, 10);
    public const float WindowRounding = 12f;
    public const float SidebarWidth = 162.5f;
    public const float SidebarRounding = 14f;
    public const float SidebarTabRounding = 9.1f;
    public const float TabHeight = 32f;
    public const float IconSlot = 32f;
    public const float WindowWidth = 900f;
    public const float WindowHeight = 527f;
    public const float OuterPadding = 10f;
    public const float ColumnGap = 14f;
    public const float BrandHeaderHeight = 50f;

    /// <summary>
    /// The UI font size used when the Lumin metrics were tuned. Dalamud's
    /// default UI font is approximately 17px.
    /// </summary>
    public const float DesignFontSize = 17f;

    /// <summary>
    /// Design-space to screen-space scale. ImGui.GetFontSize() already includes
    /// the user's Dalamud/DPI scale, so multiplying GlobalScale again would make
    /// title-bar controls, switches and command buttons grow twice.
    /// </summary>
    public static float Scale => CalculateScale(ImGui.GetFontSize());

    public static float CalculateScale(float fontSize) =>
        fontSize > 0f ? fontSize / DesignFontSize : 1f;

    /// <summary>Frame height implied by the design font plus its 6px frame padding.</summary>
    private const float DesignFrameHeight = DesignFontSize + 6f * 2f;

    public static float S(float value) => MathF.Round(value * Scale);

    public static Vector2 S(float x, float y) => new(MathF.Round(x * Scale), MathF.Round(y * Scale));

    public static Vector2 S(Vector2 value) => new(MathF.Round(value.X * Scale), MathF.Round(value.Y * Scale));

    /// <summary>
    /// Inverse of <see cref="S(float)"/>: converts a measured screen width back
    /// into design space, so layout breakpoints stay comparable to the design
    /// constants they are tested against instead of drifting with font size.
    /// </summary>
    public static float ToDesign(float screenValue) => screenValue / Scale;

    /// <summary>
    /// Height of an element slot that holds text. The design's absolute pixel
    /// heights assume a 12px font; expressing them as a multiple of ImGui's own
    /// frame height keeps Lumin widgets proportional to the stock widgets they
    /// sit next to, whatever font size the user runs.
    /// </summary>
    public static float SlotHeight(float designHeight) =>
        MathF.Round(ImGui.GetFrameHeight() * (designHeight / DesignFrameHeight));

    public static uint Col(Vector4 color, float alphaMul = 1f)
    {
        var c = color;
        return ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, c.W * alphaMul));
    }

    public static Vector4 Rgb(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>
    /// Push the Lumin look onto the current window's full frame. The returned
    /// scope owns every pushed color/var and restores them on Dispose, so the
    /// style never leaks into other plugins even if a draw is interrupted.
    /// </summary>
    public static LuminUiStyleScope PushGlobalStyle()
    {
        var scope = new LuminUiStyleScope();
        PushColor(scope, ImGuiCol.WindowBg, Layout);
        PushColor(scope, ImGuiCol.ChildBg, Child);
        PushColor(scope, ImGuiCol.FrameBg, Widget);
        PushColor(scope, ImGuiCol.FrameBgHovered, new Vector4(Widget.X * 1.15f, Widget.Y * 1.15f, Widget.Z * 1.15f, 1f));
        PushColor(scope, ImGuiCol.FrameBgActive, Widget);
        PushColor(scope, ImGuiCol.Text, White);
        PushColor(scope, ImGuiCol.TextDisabled, Text);
        PushColor(scope, ImGuiCol.Border, Border);
        PushColor(scope, ImGuiCol.Separator, Border);
        PushColor(scope, ImGuiCol.Header, Widget);
        PushColor(scope, ImGuiCol.HeaderHovered, new Vector4(Widget.X * 1.25f, Widget.Y * 1.25f, Widget.Z * 1.25f, 1f));
        PushColor(scope, ImGuiCol.HeaderActive, Accent);
        PushColor(scope, ImGuiCol.Button, Widget);
        PushColor(scope, ImGuiCol.ButtonHovered, new Vector4(Widget.X * 1.2f, Widget.Y * 1.2f, Widget.Z * 1.2f, 1f));
        PushColor(scope, ImGuiCol.ButtonActive, Accent);
        PushColor(scope, ImGuiCol.CheckMark, Accent);
        PushColor(scope, ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0));
        PushColor(scope, ImGuiCol.ScrollbarGrab, Circle);
        PushColor(scope, ImGuiCol.ScrollbarGrabHovered, Text);
        PushColor(scope, ImGuiCol.ScrollbarGrabActive, Accent);
        PushColor(scope, ImGuiCol.Tab, Widget);
        PushColor(scope, ImGuiCol.TabHovered, new Vector4(Widget.X * 1.2f, Widget.Y * 1.2f, Widget.Z * 1.2f, 1f));
        // Left unstyled these keep Dalamud's default red, which clashes badly
        // with the Lumin palette on the config window's tab bars and sliders.
        PushColor(scope, ImGuiCol.TabActive, Circle);
        PushColor(scope, ImGuiCol.TabUnfocused, Widget);
        PushColor(scope, ImGuiCol.TabUnfocusedActive, Circle);
        PushColor(scope, ImGuiCol.SliderGrab, Accent);
        PushColor(scope, ImGuiCol.SliderGrabActive, White);
        PushColor(scope, ImGuiCol.PopupBg, Child);
        PushColor(scope, ImGuiCol.TitleBg, Child);
        PushColor(scope, ImGuiCol.TitleBgActive, Child);
        PushColor(scope, ImGuiCol.TitleBgCollapsed, Child);
        PushColor(scope, ImGuiCol.TableHeaderBg, Widget);
        PushColor(scope, ImGuiCol.TableBorderStrong, Border);
        PushColor(scope, ImGuiCol.TableBorderLight, Border);
        PushColor(scope, ImGuiCol.TableRowBg, new Vector4(0, 0, 0, 0));
        PushColor(scope, ImGuiCol.TableRowBgAlt, new Vector4(White.X, White.Y, White.Z, 0.02f));
        PushColor(scope, ImGuiCol.TextSelectedBg, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.28f));

        PushVar(scope, ImGuiStyleVar.WindowRounding, S(WindowRounding));
        PushVar(scope, ImGuiStyleVar.ChildRounding, S(10f));
        PushVar(scope, ImGuiStyleVar.FrameRounding, S(8f));
        PushVar(scope, ImGuiStyleVar.PopupRounding, S(10f));
        PushVar(scope, ImGuiStyleVar.TabRounding, S(8f));
        PushVar(scope, ImGuiStyleVar.WindowPadding, S(Padding));
        PushVar(scope, ImGuiStyleVar.FramePadding, S(8, 6));
        PushVar(scope, ImGuiStyleVar.ItemSpacing, S(8, 8));
        PushVar(scope, ImGuiStyleVar.ItemInnerSpacing, S(6, 6));
        PushVar(scope, ImGuiStyleVar.ScrollbarRounding, S(6f));
        PushVar(scope, ImGuiStyleVar.WindowBorderSize, 1f);
        PushVar(scope, ImGuiStyleVar.ChildBorderSize, 1f);
        PushVar(scope, ImGuiStyleVar.FrameBorderSize, 0f);

        return scope;
    }

    internal static void PushColor(LuminUiStyleScope scope, ImGuiCol col, Vector4 value)
    {
        ImGui.PushStyleColor(col, value);
        scope.RecordColor();
    }

    internal static void PushVar(LuminUiStyleScope scope, ImGuiStyleVar var, float value)
    {
        ImGui.PushStyleVar(var, value);
        scope.RecordVar();
    }

    internal static void PushVar(LuminUiStyleScope scope, ImGuiStyleVar var, Vector2 value)
    {
        ImGui.PushStyleVar(var, value);
        scope.RecordVar();
    }
}

/// <summary>Owns the pushed Lumin style stack for one window frame.</summary>
public sealed class LuminUiStyleScope : IDisposable
{
    private int _colorCount;
    private int _varCount;
    private bool _disposed;

    internal void RecordColor() => _colorCount++;

    internal void RecordVar() => _varCount++;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_varCount > 0)
        {
            ImGui.PopStyleVar(_varCount);
        }

        if (_colorCount > 0)
        {
            ImGui.PopStyleColor(_colorCount);
        }

        _disposed = true;
    }
}
