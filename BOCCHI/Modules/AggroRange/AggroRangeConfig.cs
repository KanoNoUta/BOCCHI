using Ocelot.Config.Attributes;
using Ocelot.Modules;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Modules.AggroRange;

public class AggroRangeConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool HideEngagedMobs { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool FillCircles { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool AutoCalibrate { get; set; } = true;

    [Checkbox]
    [DependsOn(nameof(Enabled))]
    public bool AutoAvoidance { get; set; } = true;

    [FloatRange(0.5f, 6f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled), nameof(AutoAvoidance))]
    public float AvoidanceSafetyMargin { get; set; } = 2f;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(AutoAvoidance))]
    public bool DynamicReplanning { get; set; } = true;

    [FloatRange(0.5f, 5f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled), nameof(AutoAvoidance), nameof(DynamicReplanning))]
    public float ReplanCooldownSeconds { get; set; } = 1.25f;

    [Checkbox]
    [DependsOn(nameof(Enabled), nameof(AutoAvoidance))]
    public bool DrawAvoidancePath { get; set; }

    [FloatRange(20f, 150f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled))]
    public float MaxDrawDistance { get; set; } = 60f;

    [FloatRange(0f, 10f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled))]
    public float NearBoundaryDistance { get; set; } = 2f;

    [FloatRange(-3f, 8f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled))]
    public float RadiusAdjustment { get; set; } = 0f;

    [FloatRange(1f, 20f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled))]
    public float VerticalTolerance { get; set; } = 6f;

    [FloatRange(0f, 0.35f)]
    [RangeIndicator]
    [DependsOn(nameof(Enabled), nameof(FillCircles))]
    public float FillOpacity { get; set; } = 0.08f;

    [Color4]
    [DependsOn(nameof(Enabled))]
    public Vector4 OutsideColor { get; set; } = new(0.95f, 0.76f, 0.12f, 0.78f);

    [Color4]
    [DependsOn(nameof(Enabled))]
    public Vector4 NearColor { get; set; } = new(1f, 0.42f, 0.04f, 0.9f);

    [Color4]
    [DependsOn(nameof(Enabled))]
    public Vector4 InsideColor { get; set; } = new(1f, 0.08f, 0.08f, 1f);

    public Dictionary<uint, AggroRangeCalibration> Calibrations { get; set; } = [];
}
