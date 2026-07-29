using BOCCHI.Data;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;

namespace BOCCHI.Modules.CriticalEncounters;

public class Alerter : IDisposable
{
    private readonly CriticalEncountersModule module;

    public Alerter(CriticalEncountersModule module)
    {
        this.module = module;

        this.module.Tracker.OnRegisterState += OnCriticalEncounterSpawned;
        this.module.Tracker.OnInactiveState += OnCriticalEncounterDepawned;
    }

    private unsafe void OnCriticalEncounterSpawned(CriticalEncounterSnapshot ev)
    {
        if (module.Config.LogSpawn)
        {
            Svc.Chat.Print($"[CE] {ev.Name} 已出现");
        }

        if (!ShouldAlertForCriticalEncounter(ev))
        {
            return;
        }

        unsafe
        { UIGlobals.PlaySoundEffect(66); }
    }

    private unsafe void OnCriticalEncounterDepawned(CriticalEncounterSnapshot ev)
    {
        if (module.Config.LogSpawn)
        {
            Svc.Chat.Print($"[CE] {ev.Name} 已消失");
        }

        if (!ShouldAlertForCriticalEncounter(ev))
        {
            return;
        }

        unsafe
        { UIGlobals.PlaySoundEffect(68); }
    }

    private bool ShouldAlertForCriticalEncounter(CriticalEncounterSnapshot ev)
    {
        if (module.Config.AlertAll)
        {
            return true;
        }

        if (!EventData.CriticalEncounters.TryGetValue(ev.DynamicEventId, out var data))
        {
            return false;
        }

        return module.Config.ShouldAlertForRewards(data);
    }

    public void Dispose()
    {
        module.Tracker.OnRegisterState -= OnCriticalEncounterSpawned;
        module.Tracker.OnInactiveState -= OnCriticalEncounterDepawned;
    }
}
