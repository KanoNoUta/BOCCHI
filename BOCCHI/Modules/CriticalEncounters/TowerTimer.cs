using BOCCHI.Data;
using BOCCHI.Modules.Fates;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.CriticalEncounters;

/// <summary>
/// Mutable timing state for one tower dynamic event.  North Horn has two
/// independent tower events, so none of these values may be shared globally.
/// </summary>
public sealed class TowerCycleState
{
    private static readonly TimeSpan InitialSpawnInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NormalSpawnInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan RegistrationInterval = TimeSpan.FromSeconds(330);

    public DateTime LastTowerEnd { get; private set; }

    public DateTime LastTowerRegister { get; private set; }

    public TimeSpan SpawnInterval { get; private set; }

    public int FatesCompleted { get; private set; }

    public int CriticalEncountersCompleted { get; private set; }

    public TowerCycleState(DateTime now)
    {
        LastTowerEnd = now;
        LastTowerRegister = now;
        SpawnInterval = InitialSpawnInterval;
    }

    public void RecordFate()
    {
        FatesCompleted++;
    }

    public void RecordCriticalEncounter()
    {
        CriticalEncountersCompleted++;
    }

    public void MarkRegistered(DateTime now)
    {
        LastTowerRegister = now;
    }

    public void MarkEnded(DateTime now)
    {
        LastTowerEnd = now;
        LastTowerRegister = now;
        FatesCompleted = 0;
        CriticalEncountersCompleted = 0;
        SpawnInterval = NormalSpawnInterval;
    }

    public TimeSpan GetTimeToSpawn(DynamicEventState state, DateTime now)
    {
        if (state != DynamicEventState.Inactive)
        {
            return TimeSpan.Zero;
        }

        var fateModifier = TimeSpan.FromMinutes(FatesCompleted);
        var criticalModifier = TimeSpan.FromMinutes(5 * CriticalEncountersCompleted);
        var time = LastTowerEnd + SpawnInterval - fateModifier - criticalModifier - now;

        // The first observation after zoning in uses a five-minute probing
        // interval.  Once it elapses, switch only this tower to its regular
        // sixty-minute cycle.
        if (time < TimeSpan.Zero && SpawnInterval == InitialSpawnInterval)
        {
            SpawnInterval = NormalSpawnInterval;
            time = LastTowerEnd + SpawnInterval - fateModifier - criticalModifier - now;
        }

        return time < TimeSpan.Zero ? TimeSpan.Zero : time;
    }

    public TimeSpan GetTimeRemainingToRegister(DynamicEventState state, DateTime now)
    {
        if (state != DynamicEventState.Register && state != DynamicEventState.Warmup)
        {
            return TimeSpan.Zero;
        }

        var time = LastTowerRegister + RegistrationInterval - now;
        return time < TimeSpan.Zero ? TimeSpan.Zero : time;
    }

    public TimeSpan GetTimeRemainingToRegister(CriticalEncounterSnapshot ev, DateTime now)
    {
        if (ev.State != DynamicEventState.Register && ev.State != DynamicEventState.Warmup)
        {
            return TimeSpan.Zero;
        }

        // Prefer the live client values. They stay correct when the plugin is
        // loaded mid-registration and avoid assuming North Horn uses Blood
        // Tower's historical 330-second window.
        if (ev.SecondsLeft > 0)
        {
            return TimeSpan.FromSeconds(ev.SecondsLeft);
        }

        if (ev.SecondsRegistrationTime > 0)
        {
            var time = LastTowerRegister + TimeSpan.FromSeconds(ev.SecondsRegistrationTime) - now;
            return time < TimeSpan.Zero ? TimeSpan.Zero : time;
        }

        return GetTimeRemainingToRegister(ev.State, now);
    }
}

public sealed class TowerTimer : IDisposable
{
    private readonly CriticalEncounterTracker tracker;

    private readonly FatesModule fates;

    private readonly Dictionary<uint, TowerCycleState> states = [];

    public IReadOnlyDictionary<uint, TowerCycleState> States => states;

    public TowerTimer(CriticalEncounterTracker tracker, FatesModule fates)
    {
        this.tracker = tracker;
        this.fates = fates;

        fates.tracker.OnFateDespawned += OnFateDespawned;
        tracker.OnInactiveState += OnCriticalEncounterDespawned;
        tracker.OnRegisterState += OnCriticalEncounterRegistered;
        tracker.OnCompletedState += OnCriticalEncounterCompleted;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;

        ResetForTerritory(Svc.ClientState.TerritoryType, DateTime.Now);
    }

    public TowerCycleState GetState(uint dynamicEventId)
    {
        if (!states.TryGetValue(dynamicEventId, out var state))
        {
            state = new TowerCycleState(DateTime.Now);
            states.Add(dynamicEventId, state);
        }

        return state;
    }

    public TimeSpan GetTimeToForkedTowerSpawn(uint dynamicEventId, DynamicEventState state)
    {
        return GetState(dynamicEventId).GetTimeToSpawn(state, DateTime.Now);
    }

    public TimeSpan GetTimeRemainingToRegister(uint dynamicEventId, DynamicEventState state)
    {
        return GetState(dynamicEventId).GetTimeRemainingToRegister(state, DateTime.Now);
    }

    public TimeSpan GetTimeRemainingToRegister(CriticalEncounterSnapshot ev)
    {
        return GetState((uint)ev.DynamicEventId).GetTimeRemainingToRegister(ev, DateTime.Now);
    }

    private IEnumerable<TowerCycleState> GetCurrentTerritoryStates()
    {
        var definitions = TowerHelper.GetDefinitionsForTerritory(Svc.ClientState.TerritoryType);
        return definitions.Select(definition => GetState(definition.DynamicEventId));
    }

    private void OnFateDespawned(Fate fate)
    {
        if (fate.CurrentProgress < 100)
        {
            return;
        }

        foreach (var state in GetCurrentTerritoryStates())
        {
            state.RecordFate();
        }
    }

    private void OnCriticalEncounterDespawned(CriticalEncounterSnapshot ev)
    {
        var eventId = (uint)ev.DynamicEventId;
        if (TowerHelper.TryGetDefinitionByEventId(eventId, out var definition)
            && definition.TerritoryId == Svc.ClientState.TerritoryType)
        {
            GetState(eventId).MarkEnded(DateTime.Now);
        }
    }

    private void OnCriticalEncounterRegistered(CriticalEncounterSnapshot ev)
    {
        var eventId = (uint)ev.DynamicEventId;
        if (!TowerHelper.TryGetDefinitionByEventId(eventId, out var definition)
            || definition.TerritoryId != Svc.ClientState.TerritoryType)
        {
            return;
        }

        GetState(eventId).MarkRegistered(DateTime.Now);
    }

    private void OnCriticalEncounterCompleted(CriticalEncounterSnapshot ev)
    {
        var eventId = (uint)ev.DynamicEventId;
        if (TowerHelper.TryGetDefinitionByEventId(eventId, out _))
        {
            return;
        }

        // Ordinary CEs reduce every tower cycle in the current zone, but only
        // after the tracker observed 100% before the inactive transition.
        foreach (var state in GetCurrentTerritoryStates())
        {
            state.RecordCriticalEncounter();
        }
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        if (!ZoneData.IsOccultCrescentTerritory(territoryId))
        {
            states.Clear();
            return;
        }

        ResetForTerritory(territoryId, DateTime.Now);
    }

    private void ResetForTerritory(uint territoryId, DateTime now)
    {
        states.Clear();
        foreach (var definition in TowerHelper.GetDefinitionsForTerritory(territoryId))
        {
            states.Add(definition.DynamicEventId, new TowerCycleState(now));
        }
    }

    public void Dispose()
    {
        fates.tracker.OnFateDespawned -= OnFateDespawned;
        tracker.OnInactiveState -= OnCriticalEncounterDespawned;
        tracker.OnRegisterState -= OnCriticalEncounterRegistered;
        tracker.OnCompletedState -= OnCriticalEncounterCompleted;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }
}
