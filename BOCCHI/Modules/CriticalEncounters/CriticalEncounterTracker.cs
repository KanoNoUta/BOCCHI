using BOCCHI.Data;
using BOCCHI.Modules.Fates;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.CriticalEncounters;

public class CriticalEncounterTracker
{
    public Dictionary<uint, CriticalEncounterSnapshot> CriticalEncounters = new();

    public Dictionary<uint, EventProgress> Progress { get; } = new();

    public TowerTimer TowerTimer { get; private set; }

    // Store last known states of each event by ID
    private readonly Dictionary<uint, DynamicEventState> lastStates = new();

    // DynamicEvent.Progress resets when an event becomes inactive. Keep the
    // last managed value long enough to identify real completions.
    private readonly Dictionary<uint, uint> lastProgress = new();

    public CriticalEncounterTracker(CriticalEncountersModule module)
    {
        TowerTimer = new TowerTimer(this, module.GetModule<FatesModule>());
    }

    public event Action<CriticalEncounterSnapshot>? OnInactiveState;

    public event Action<CriticalEncounterSnapshot>? OnRegisterState;

    public event Action<CriticalEncounterSnapshot>? OnWarmupState;

    public event Action<CriticalEncounterSnapshot>? OnBattleState;

    public event Action<CriticalEncounterSnapshot>? OnCompletedState;

    public static bool CanReadOccultCrescentEvents(bool inOccultCrescent, bool instanceAvailable)
    {
        return inOccultCrescent && instanceAvailable;
    }

    public void Reset()
    {
        CriticalEncounters.Clear();
        Progress.Clear();
        lastStates.Clear();
        lastProgress.Clear();
    }


    public unsafe void Tick(IFramework _)
    {
        var inOccultCrescent = ZoneData.IsInOccultCrescent();
        if (!inOccultCrescent)
        {
            Reset();
            return;
        }

        var instance = PublicContentOccultCrescent.GetInstance();
        if (!CanReadOccultCrescentEvents(inOccultCrescent, instance != null))
        {
            Reset();
            return;
        }

        CriticalEncounters = instance->DynamicEventContainer.Events
            .ToArray()
            .Select(CriticalEncounterSnapshot.From)
            .ToDictionary(ev => ev.DynamicEventId);

        foreach (var ev in CriticalEncounters.Values)
        {
            // Get previous state, default to Inactive if unknown
            lastStates.TryGetValue(ev.DynamicEventId, out var previousState);

            var currentState = ev.State;

            if (currentState != DynamicEventState.Inactive)
            {
                lastProgress[ev.DynamicEventId] = Math.Max(
                    lastProgress.GetValueOrDefault(ev.DynamicEventId),
                    ev.Progress);
            }

            if (currentState == DynamicEventState.Battle)
            {
                if (ev.Progress > 0)
                {
                    if (!Progress.TryGetValue(ev.DynamicEventId, out var current))
                    {
                        current = new EventProgress();
                        Progress[ev.DynamicEventId] = current;
                    }

                    if (current.samples.Count == 0 || current.samples[^1].Progress != ev.Progress)
                    {
                        current.Add(ev.Progress);
                    }

                    if (ev.Progress == 100)
                    {
                        Progress.Remove(ev.DynamicEventId);
                    }
                }
            }
            else
            {
                Progress.Remove(ev.DynamicEventId);
            }

            if (previousState == currentState)
            {
                continue;
            }

            lastStates[ev.DynamicEventId] = currentState;

            switch (currentState)
            {
                case DynamicEventState.Inactive:
                    if (previousState == DynamicEventState.Battle
                        && lastProgress.TryGetValue(ev.DynamicEventId, out var finalProgress)
                        && finalProgress >= 100)
                    {
                        OnCompletedState?.Invoke(ev);
                    }

                    OnInactiveState?.Invoke(ev);
                    lastProgress.Remove(ev.DynamicEventId);
                    break;

                case DynamicEventState.Register:
                    OnRegisterState?.Invoke(ev);
                    break;

                case DynamicEventState.Warmup:
                    OnWarmupState?.Invoke(ev);
                    break;

                case DynamicEventState.Battle:
                    OnBattleState?.Invoke(ev);
                    break;
            }
        }
    }
}
