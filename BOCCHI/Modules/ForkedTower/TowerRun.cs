using BOCCHI.Data.Traps;
using BOCCHI.Enums;
using BOCCHI.Modules.Data;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Colors;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Modules;
using Ocelot.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.ForkedTower;

public class TowerRun(string hash, uint dynamicEventId = 0, TowerHelper.TowerType? towerType = null)
{
    public sealed record TrapSnapshot(uint BaseId, string Name, Vector3 Position);

    public readonly string Hash = hash;

    public readonly uint DynamicEventId = dynamicEventId;

    public readonly TowerHelper.TowerType? TowerType = towerType;

    private readonly HashSet<string> DiscoveredTraps = [];

    private readonly HashSet<string> DiscoveredUnmappedTraps = [];

    private readonly Dictionary<string, TrapSnapshot> CapturedTraps = [];

    private readonly Dictionary<string, TrackedGroup> TrackedGroups = [];

    public bool HasDiscoveredAllTraps(TrapGroup group)
    {
        if (TrackedGroups.TryGetValue(group.GetKey(), out var trackedGroup))
        {
            return trackedGroup.HasDiscoveredAllTraps();
        }

        return false;
    }

    public void Update(UpdateContext context)
    {
        foreach (var trap in GetNearbyTraps())
        {
            var trapKey = trap.GetKey();
            CapturedTraps.TryAdd(
                trapKey,
                new TrapSnapshot(trap.BaseId, trap.Name.TextValue, trap.Position));

            if (!DiscoveredTraps.Add(trapKey))
            {
                continue;
            }

            // The precomputed group table is Blood Tower-only. North Horn is
            // captured as managed coordinates until its real layout is known.
            if (TowerType != TowerHelper.TowerType.Blood
                || !TrapData.TryGetGroup(trap, out var group))
            {
                DiscoveredUnmappedTraps.Add(trapKey);
                Svc.Log.Info(
                    $"Unmapped tower trap: event={DynamicEventId}, territory={Svc.ClientState.TerritoryType}, " +
                    $"baseId={trap.BaseId}, position=({trap.Position.X:F3}, {trap.Position.Y:F3}, {trap.Position.Z:F3})");
                continue;
            }

            if (!TrackedGroups.TryGetValue(group.GetKey(), out var trackedGroup))
            {
                trackedGroup = new TrackedGroup(group);
                TrackedGroups.Add(group.GetKey(), trackedGroup);
            }

            trackedGroup.RecordTrap(trapKey);
        }
    }

    public int DiscoveredTrapCount => DiscoveredTraps.Count;

    public int DiscoveredUnmappedTrapCount => DiscoveredUnmappedTraps.Count;

    public IReadOnlyCollection<TrapSnapshot> CapturedTrapSnapshots => CapturedTraps.Values;

    public void Render(RenderContext context)
    {
        if (context.Config is not Config config)
        {
            return;
        }

        foreach (var trap in GetNearbyTraps())
        {
            if (Player.DistanceTo(trap) > config.ForkedTowerConfig.TrapDrawRange)
            {
                continue;
            }

            if (config.ForkedTowerConfig.DrawSmallTrapRange && trap.BaseId == (uint)OccultObjectType.Trap)
            {
                context.DrawCircle(trap.Position, 7f, ImGuiColors.DPSRed);
            }

            if (config.ForkedTowerConfig.DrawBigTrapRange && trap.BaseId == (uint)OccultObjectType.BigTrap)
            {
                context.DrawCircle(trap.Position, 30f, ImGuiColors.DalamudOrange);
            }
        }
    }

    private IEnumerable<IEventObj> GetNearbyTraps()
    {
        return Svc.Objects.OfType<IEventObj>().Where(o => o.BaseId is (uint)OccultObjectType.Trap or (uint)OccultObjectType.BigTrap);
    }
}
