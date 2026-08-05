using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.Teleporter;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Commands;
using Ocelot.IPC;
using Ocelot.Modules;

namespace BOCCHI.Commands;

[OcelotCommand]
public class TeleportCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/bocchitp";
    }

    protected override string Description
    {
        get => @"
自动传送到最近的活动相关以太之光。
 - /bocchitp : 传送到最近的活动传送点
 - /bocchitp ce : 传送到最近的 CE 传送点
 - /bocchitp fate : 传送到最近的 FATE 传送点
 - /bocchitp pot : 传送到最近的 POT FATE 传送点
--------------------------------
".Trim();
    }


    public override void Execute(string command, string arguments)
    {
        var module = plugin.Modules.GetModule<TeleporterModule>();

        if (!ZoneData.IsNearAnyAethernetShard())
        {
            Svc.Chat.Print(module.T("messages.not_near_shard"));
            return;
        }

        var lifestream = plugin.IPC.GetSubscriber<Lifestream>();
        if (!lifestream.IsReady() || lifestream.IsBusy())
        {
            Svc.Chat.Print(module.T("messages.lifestream_busy"));
            return;
        }

        Aethernet? shard = null;
        if (arguments.Length <= 0)
        {
            shard ??= GetCriticalEncounterAethernet();
            shard ??= GetFateAethernet();
            shard ??= GetPotFateAethernet();
        }
        else
        {
            switch (arguments)
            {
                case "fate":
                    shard = GetFateAethernet();
                    break;
                case "ce":
                    shard = GetCriticalEncounterAethernet();
                    break;
                case "pot":
                    shard = GetPotFateAethernet();
                    break;
            }
        }

        if (shard == null)
        {
            Svc.Chat.Print(module.T("messages.no_shard_found"));
            return;
        }

        if (ZoneData.IsNearAethernetShard((Aethernet)shard))
        {
            Svc.Chat.Print(module.T("messages.already_at_closest_shard"));
            return;
        }

        Plugin.Chain.Submit(ChainHelper.TeleportChain((Aethernet)shard));
    }

    private Aethernet? GetFateAethernet()
    {
        var source = plugin.Modules.GetModule<FatesModule>();
        foreach (var fate in source.fates.Values)
        {
            if (fate.IsPotFate())
            {
                continue;
            }

            return fate.GetAethernet();
        }

        return null;
    }

    private Aethernet? GetPotFateAethernet()
    {
        var source = plugin.Modules.GetModule<FatesModule>();
        foreach (var fate in source.fates.Values)
        {
            if (!fate.IsPotFate())
            {
                continue;
            }

            return fate.GetAethernet();
        }

        return null;
    }

    private Aethernet? GetCriticalEncounterAethernet()
    {
        var source = plugin.Modules.GetModule<CriticalEncountersModule>();
        foreach (var encounter in source.CriticalEncounters.Values)
        {
            if (encounter.EventType >= 4 || encounter.State != DynamicEventState.Register)
            {
                continue;
            }

            var data = EventData.GetCriticalEncounter(encounter.DynamicEventId, Svc.ClientState.TerritoryType);

            return data.Aethernet ?? ZoneData.GetClosestAethernetShard(data.StartPosition ?? encounter.MapMarker.Position);
        }

        return null;
    }
}
