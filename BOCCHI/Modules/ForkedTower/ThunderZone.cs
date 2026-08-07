using BOCCHI.Enums;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.ForkedTower;

/// <summary>
/// 两歧塔 魔之塔/超魔之塔 元素雷区绘制。
/// 几何参数来自 BossMod ForkedTowerMagic FTMN4Index ElementalSectors 逆向：
/// Fire/Ice/Thunder sector EventObject 定义一个 60° 扇形及其对侧 180° 的
/// 同名扇形（AOEShapeCone(30, 30°)），朝向取 EventObject.Rotation；
/// Ring 与 Ball 命中时同样生成两个对侧扇形。南岛血塔画法参考
/// TowerRun.Render（实时枚举 EventObj 按 BaseId 绘制）。
/// </summary>
public static class ThunderZone
{
    public const float SectorRadius = 30f;
    public const float SectorHalfAngle = MathF.PI / 6f; // 30°
    public const float SectorAngle = MathF.PI / 3f;     // 60° 张角

    // 雷=亮蓝紫，火=橙红，冰=青（与元素视觉一致）
    private static readonly Vector4 ThunderColor = new(0.55f, 0.62f, 1f, 0.85f);
    private static readonly Vector4 FireColor = new(1f, 0.48f, 0.28f, 0.85f);
    private static readonly Vector4 IceColor = new(0.45f, 0.85f, 1f, 0.85f);
    private static readonly Vector4 BallColor = new(0.55f, 0.62f, 1f, 0.95f);

    public static void Render(RenderContext context, ForkedTowerConfig config)
    {
        var drawRange = config.ElementDrawRange;

        foreach (var obj in Svc.Objects.OfType<IEventObj>())
        {
            if (Player.DistanceTo(obj) > drawRange)
            {
                continue;
            }

            switch (obj.BaseId)
            {
                case (uint)OccultObjectType.FireSector when config.DrawElementSectors:
                    DrawOpposedSectors(context, obj, FireColor);
                    break;
                case (uint)OccultObjectType.IceSector when config.DrawElementSectors:
                    DrawOpposedSectors(context, obj, IceColor);
                    break;
                case (uint)OccultObjectType.ThunderSector when config.DrawElementSectors:
                    DrawOpposedSectors(context, obj, ThunderColor);
                    break;
                case (uint)OccultObjectType.FireRing when config.DrawElementRings:
                    DrawOpposedSectors(context, obj, FireColor);
                    break;
                case (uint)OccultObjectType.IceRing when config.DrawElementRings:
                    DrawOpposedSectors(context, obj, IceColor);
                    break;
                case (uint)OccultObjectType.ThunderRing when config.DrawElementRings:
                    DrawOpposedSectors(context, obj, ThunderColor);
                    break;
            }
        }

        if (config.DrawElementBalls)
        {
            foreach (var actor in Svc.Objects.OfType<IBattleNpc>())
            {
                if (Player.DistanceTo(actor) > drawRange)
                {
                    continue;
                }

                var color = actor.NameId switch
                {
                    (uint)OccultObjectType.BallOfLevin => BallColor,
                    (uint)OccultObjectType.BallOfFire => FireColor,
                    (uint)OccultObjectType.SwirlingOrb => IceColor,
                    _ => (Vector4?)null,
                };
                if (color is { } ballColor)
                {
                    context.DrawCircle(actor.Position, 4f, ballColor, RenderContext.CircleDrawMode.Filled);
                }
            }
        }
    }

    /// <summary>
    /// 画 60° 扇形 + 对侧 180° 扇形。中心取 EventObject.Position：
    /// sector 对象定义元素扇区，位置即平台中心附近；若实测有偏移，可先经
    /// TowerCapture 校准后改由 TowerHelper 平台几何提供中心。
    /// </summary>
    private static void DrawOpposedSectors(RenderContext context, IEventObj obj, Vector4 color)
    {
        var center = obj.Position;
        var rotation = obj.Rotation;
        var pct = context.Pictomancy;
        var col = ImGui.GetColorU32(color);
        pct.AddConeFilled(center, SectorRadius, rotation, SectorAngle, col);
        pct.AddConeFilled(center, SectorRadius, rotation + MathF.PI, SectorAngle, col);
    }
}