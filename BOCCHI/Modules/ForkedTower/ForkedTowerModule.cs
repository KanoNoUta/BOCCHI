using BOCCHI.Data;
using BOCCHI.Data.Traps;
using BOCCHI.Enums;
using BOCCHI.Modules.CriticalEncounters;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Modules;
using Ocelot.Windows;
using Pictomancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace BOCCHI.Modules.ForkedTower;

[OcelotModule]
public class ForkedTowerModule(Plugin plugin, Config config) : Module(plugin, config)
{
    public override ForkedTowerConfig Config
    {
        get => PluginConfig.ForkedTowerConfig;
    }

    public override bool ShouldInitialize
    {
        get => true;
    }

    public TowerRun TowerRun { get; private set; } = new("");

    private readonly Panel panel = new();

    private ulong runEpoch;

    private uint activeEventId;

    private DynamicEventState activeEventState = DynamicEventState.Inactive;

    // DynamicEventContainer.CurrentEventId can remain populated briefly after
    // the event becomes inactive. Keep that stale ID from immediately creating
    // a fresh, empty run and replacing the just-finished capture.
    private uint endedEventId;

    public override void PostInitialize()
    {
        var tracker = GetModule<CriticalEncountersModule>().Tracker;
        tracker.OnRegisterState += OnCriticalEncounterRegister;
        tracker.OnBattleState += OnCriticalEncounterBattle;
        tracker.OnInactiveState += OnCriticalEncounterInactive;

        var currentEventId = ZoneData.GetCurrentForkedTowerEventId();
        if (currentEventId != 0)
        {
            StartNewRun(currentEventId);
        }
    }

    public override void Update(UpdateContext context)
    {
        if (!ZoneData.IsInForkedTower())
        {
            return;
        }

        EnsureCurrentTowerRun();
        TowerRun.Update(context);
    }

    public override void Render(RenderContext context)
    {
        if (!ZoneData.IsInOccultCrescent())
        {
            return;
        }

#if RELEASE
        if (!ZoneData.IsInForkedTower())
        {
            return;
        }
#endif

        EnsureCurrentTowerRun();

        // Potential layouts are tower-specific. Grand Magic uses the union of
        // observed ARR spawn positions; the normal Magic Tower deliberately
        // stays empty until its own layout has been captured.
        if (Config.DrawPotentialTrapPositions)
        {
            var traps = GetTrapsToRender().ToList();
            foreach (var trap in traps)
            {
                if (Config.DrawSimpleMode || Config.DrawOutlineForComplexMode)
                {
                    context.DrawCircle(trap.Position, 4f, GetTrapColor(trap.Type));
                }

                if (!Config.DrawSimpleMode)
                {
                    var key = $"{trap.Position.X:f2}:{trap.Position.Y:f2}:{trap.Position.Z:f2}.{trap.Type}";
                    PctService.VfxRenderer.AddCircle(key, trap.Position, 4f, GetTrapColor(trap.Type));
                }
            }
        }

        TowerRun.Render(context);
    }

    private Vector4 GetTrapColor(OccultObjectType type)
    {
        return type switch
        {
            OccultObjectType.Trap => Config.TrapDrawColor,
            OccultObjectType.BigTrap => Config.BigTrapDrawColor,
            _ => new Vector4(4f, 7f, 1f, 1f),
        };
    }


    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    private IEnumerable<TrapDatum> GetTrapsToRender()
    {
        var groups = TrapData.GetGroups(TowerRun.TowerType).AsEnumerable();

#if DEBUG
        if (!Config.IgnoreDrawRange)
        {
            groups = groups.Where(group => group.GetDistance() <= Config.TrapDrawRange);
        }
#else
        groups = groups.Where(group => group.GetDistance() <= Config.TrapDrawRange);
#endif

        // Grand Magic's middle-floor mechanic can rearrange traps during the
        // same run, so a previously discovered point must remain a candidate.
        if (Config.StopRenderingCompleteGroups && TowerRun.TowerType == TowerHelper.TowerType.Blood)
        {
            groups = groups.Where(group => !TowerRun.HasDiscoveredAllTraps(group));
        }

        return groups.SelectMany(group => group.Traps);
    }

    private void OnCriticalEncounterBattle(CriticalEncounterSnapshot ev)
    {
        var eventId = (uint)ev.DynamicEventId;
        if (!IsCurrentTerritoryTower(eventId))
        {
            return;
        }

        var currentEventId = ZoneData.GetCurrentForkedTowerEventId();
        if (!CanRouteToCurrentRun(eventId, currentEventId))
        {
            return;
        }

        // A delayed Battle callback must never erase traps captured earlier in
        // the same run. Register/current-event routing owns run creation; the
        // battle event only fills that gap when the plugin was loaded late.
        if (activeEventId != eventId)
        {
            StartNewRun(eventId);
        }

        activeEventState = DynamicEventState.Battle;
    }

    private void OnCriticalEncounterRegister(CriticalEncounterSnapshot ev)
    {
        var eventId = (uint)ev.DynamicEventId;
        if (!IsCurrentTerritoryTower(eventId))
        {
            return;
        }

        var currentEventId = ZoneData.GetCurrentForkedTowerEventId();
        if (!CanRouteToCurrentRun(eventId, currentEventId))
        {
            return;
        }

        if (activeEventId != eventId || activeEventState == DynamicEventState.Inactive)
        {
            StartNewRun(eventId);
        }

        activeEventState = DynamicEventState.Register;
    }

    private void OnCriticalEncounterInactive(CriticalEncounterSnapshot ev)
    {
        var eventId = (uint)ev.DynamicEventId;
        if (!IsCurrentTerritoryTower(eventId) || activeEventId != eventId)
        {
            return;
        }

        // Preserve TowerRun for post-run export while invalidating the active
        // epoch. The next occurrence of the same event ID receives a new run.
        endedEventId = eventId;
        activeEventId = 0;
        activeEventState = DynamicEventState.Inactive;
    }

    private void EnsureCurrentTowerRun()
    {
        var currentEventId = ZoneData.GetCurrentForkedTowerEventId();
        if (currentEventId == endedEventId)
        {
            return;
        }

        if (currentEventId != 0 && currentEventId != activeEventId)
        {
            StartNewRun(currentEventId);
        }
    }

    private bool CanRouteToCurrentRun(uint eventId, uint currentEventId)
    {
        // The tracker publishes state changes for every tower in the territory.
        // When the client identifies a current tower, callbacks for the other
        // independent North Horn tower must not replace the live run. During a
        // short CurrentEventId gap, only callbacks for the already-active run
        // are accepted; EnsureCurrentTowerRun will create late-loaded runs once
        // the client ID becomes available.
        return currentEventId != 0
            ? currentEventId == eventId
            : activeEventId == eventId;
    }

    private void StartNewRun(uint dynamicEventId = 0)
    {
        if (!IsCurrentTerritoryTower(dynamicEventId))
        {
            return;
        }

        TowerHelper.TowerType? towerType = null;
        if (TowerHelper.TryGetTowerType(dynamicEventId, out var knownTowerType))
        {
            towerType = knownTowerType;
        }

        runEpoch++;
        endedEventId = 0;
        activeEventId = dynamicEventId;
        activeEventState = DynamicEventState.Register;
        TowerRun = new TowerRun(GenerateHash(dynamicEventId, runEpoch), dynamicEventId, towerType);
    }

    private static bool IsCurrentTerritoryTower(uint dynamicEventId)
    {
        return TowerHelper.TryGetDefinitionByEventId(dynamicEventId, out var definition)
               && definition.TerritoryId == Svc.ClientState.TerritoryType;
    }

    private string GenerateHash(uint dynamicEventId, ulong epoch)
    {
        using var sha256 = SHA256.Create();

        var timeBytes = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var contentIdBytes = BitConverter.GetBytes(Player.CID);
        var eventIdBytes = BitConverter.GetBytes(dynamicEventId);
        var epochBytes = BitConverter.GetBytes(epoch);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(timeBytes);
            Array.Reverse(contentIdBytes);
            Array.Reverse(eventIdBytes);
            Array.Reverse(epochBytes);
        }

        var combined = new byte[timeBytes.Length + contentIdBytes.Length + eventIdBytes.Length + epochBytes.Length];
        Buffer.BlockCopy(timeBytes, 0, combined, 0, timeBytes.Length);
        Buffer.BlockCopy(contentIdBytes, 0, combined, timeBytes.Length, contentIdBytes.Length);
        Buffer.BlockCopy(eventIdBytes, 0, combined, timeBytes.Length + contentIdBytes.Length, eventIdBytes.Length);
        Buffer.BlockCopy(
            epochBytes,
            0,
            combined,
            timeBytes.Length + contentIdBytes.Length + eventIdBytes.Length,
            epochBytes.Length);

        var hashBytes = sha256.ComputeHash(combined);

        return Convert.ToBase64String(hashBytes);
    }

    public override void OnTerritoryChanged(uint id)
    {
        activeEventId = 0;
        activeEventState = DynamicEventState.Inactive;
        endedEventId = 0;
        TowerRun = new TowerRun("");
        panel.Reset();
    }

    public override void Dispose()
    {
        var tracker = GetModule<CriticalEncountersModule>().Tracker;
        tracker.OnRegisterState -= OnCriticalEncounterRegister;
        tracker.OnBattleState -= OnCriticalEncounterBattle;
        tracker.OnInactiveState -= OnCriticalEncounterInactive;
        base.Dispose();
    }
}
