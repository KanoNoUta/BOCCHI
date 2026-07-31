using BOCCHI.Data;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.Modules;
using Ocelot.Windows;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.AggroRange;

[OcelotModule(1006)]
public sealed class AggroRangeModule(Plugin plugin, Config config) : Module(plugin, config)
{
    private readonly Panel panel = new();
    private readonly AggroRangeObserver observer = new();

    public override AggroRangeConfig Config
    {
        get => PluginConfig.AggroRangeConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public int VisibleMobCount { get; private set; }

    public int InsideRangeCount { get; private set; }

    public int CalibratedMobCount => Config.Calibrations?.Count ?? 0;

    public override void Update(UpdateContext context)
    {
        AggroAvoidanceNavigation.Update();
        if (observer.Update(Config))
        {
            PluginConfig.Save();
        }
    }

    public override unsafe void Render(RenderContext context)
    {
        VisibleMobCount = 0;
        InsideRangeCount = 0;

        if (!ZoneData.IsInNorthHorn() || Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        if (player.Address == IntPtr.Zero)
        {
            return;
        }

        var playerLevel = ((BattleChara*)player.Address)->ForayInfo.Level;
        var playerPosition = player.Position;
        var maximumDistance = Math.Max(1f, Config.MaxDrawDistance);

        foreach (var mob in Svc.Objects.OfType<IBattleNpc>())
        {
            if (mob is not { IsDead: false, IsTargetable: true }
                || !mob.IsHostile()
                || !CommonMobCatalog.TryGet(mob.NameId, out var profile)
                || mob.Address == IntPtr.Zero)
            {
                continue;
            }

            var battleChara = (BattleChara*)mob.Address;
            if (battleChara->FateId != 0
                || Config.HideEngagedMobs && mob.HasTarget()
                || !AggroAvoidanceLevelPolicy.ShouldAvoid(playerLevel, battleChara->ForayInfo.Level))
            {
                continue;
            }

            var position = mob.Position;
            if (MathF.Abs(playerPosition.Y - position.Y) > Config.VerticalTolerance)
            {
                continue;
            }

            var distance = AggroRangeGeometry.HorizontalDistance(playerPosition, position);
            if (distance > maximumDistance)
            {
                continue;
            }

            var radius = AggroRangeResolver.ResolveTriggerRadius(profile, mob.HitboxRadius, Config);
            var state = AggroRangeGeometry.GetState(distance, radius, Config.NearBoundaryDistance);
            var color = GetColor(state);

            if (Config.FillCircles)
            {
                var fillColor = color;
                fillColor.W = Math.Min(fillColor.W, Config.FillOpacity);
                context.DrawCircle(position, radius, fillColor, RenderContext.CircleDrawMode.Filled);
            }

            context.DrawCircle(position, radius, color);
            VisibleMobCount++;
            if (state == AggroRangeState.Inside)
            {
                InsideRangeCount++;
            }
        }

        if (Config.DrawAvoidancePath)
        {
            foreach (var waypoint in AggroAvoidanceNavigation.DebugPath)
            {
                context.DrawCircle(waypoint, 0.35f, new Vector4(0.1f, 0.9f, 1f, 0.9f));
            }
        }
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        VisibleMobCount = 0;
        InsideRangeCount = 0;
        observer.Reset();
        AggroAvoidanceNavigation.Stop();
    }

    private Vector4 GetColor(AggroRangeState state)
    {
        return state switch
        {
            AggroRangeState.Inside => Config.InsideColor,
            AggroRangeState.Near => Config.NearColor,
            _ => Config.OutsideColor,
        };
    }
}
