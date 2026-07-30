using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Pathfinding;
using BOCCHI.Modules.StateManager;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using Ocelot.Ui;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Teleporter;

public class Teleporter(TeleporterModule module)
{
    public void Button(Aethernet? aethernet, Vector3 destination, string name, string id, EventData ev)
    {
        if (!module.TryGetIPCSubscriber<VNavmesh>(out var vnav) || vnav == null || !vnav.IsReady())
        {
            return;
        }

        if (aethernet == null)
        {
            aethernet = ZoneData.GetClosestAethernetShard(destination);
        }

        OcelotUi.Indent(() =>
        {
            PathfindingButton(destination, name, id, ev);
            TeleportButton((Aethernet)aethernet, destination, name, id, ev);
        });
    }

    private void PathfindingButton(Vector3 destination, string name, string id, EventData ev)
    {
        if (!module.TryGetIPCSubscriber<VNavmesh>(out var vnav) || vnav == null || !vnav.IsReady())
        {
            return;
        }

        if (ImGuiEx.IconButton(FontAwesomeIcon.Running, $"{name}##{id}"))
        {
            Svc.Log.Info($"寻路前往位于 {destination} 的 {name}");

            Plugin.Chain.Submit(() =>
            {
                var useSouthCrossing = false;
                var pathfinding = new PathfindingChain(vnav, destination, ev, 20f);
                return Chain.Create("寻路中")
                    .ConditionalThen(_ =>
                    {
                        useSouthCrossing = NorthHornSouthCrossingRoute.ShouldUse(ev, ECommons.GameHelpers.Player.Position);
                        return useSouthCrossing && module.Config.ShouldMount;
                    }, ChainHelper.MountChain())
                    .Then(pathfinding)
                    .BreakIf(() => pathfinding.TransitAttempted && !pathfinding.TransitReachedEnd)
                    .ConditionalThen(_ => !useSouthCrossing && module.Config.ShouldMount, ChainHelper.MountChain())
                    .WaitUntilNear(vnav, destination, 205f);
            });
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"寻路前往 {name}");
        }

        if (!module.TryGetIPCSubscriber<Lifestream>(out var lifestream) || lifestream == null || !lifestream.IsReady())
        {
            return;
        }

        ImGui.SameLine();
    }

    private void TeleportButton(Aethernet aethernet, Vector3 destination, string name, string id, EventData ev)
    {
        if (!module.TryGetIPCSubscriber<Lifestream>(out var lifestream) || lifestream == null || !lifestream.IsReady())
        {
            return;
        }

        var isNearShards = ZoneData.IsNearAnyAethernetShard();
        var isNearCurrentShard = ZoneData.IsNearAethernetShard(aethernet);

        if (ImGuiEx.IconButton(FontAwesomeIcon.LocationArrow, $"{name}##{id}", enabled: isNearShards && !isNearCurrentShard))
        {
            Chain Factory()
            {
                var teleport = ChainHelper.TeleportChain(aethernet);
                var chain = Chain.Create("Teleport Sequence")
                    .Then(teleport)
                    .Then(_ =>
                    {
                        if (!teleport.Succeeded)
                        {
                            Svc.Log.Error(
                                $"Stopping manual teleport route because teleport to {aethernet} did not complete; " +
                                $"reason={teleport.FailureReason ?? "teleport chain timed out or was aborted"}");
                        }
                    })
                    .BreakIf(() => !teleport.Succeeded)
                    .Debug("等待Lifestream不再“忙碌”")
                    .Then(new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));

                if (module.TryGetIPCSubscriber<VNavmesh>(out var vnav) && vnav != null && vnav.IsReady())
                {
                    var useSouthCrossing = false;
                    var pathfinding = new PathfindingChain(vnav, destination, ev, 20f);
                    chain.RunIf(() => module.Config.PathToDestination)
                        .ConditionalThen(_ =>
                        {
                            useSouthCrossing = NorthHornSouthCrossingRoute.ShouldUse(ev, ECommons.GameHelpers.Player.Position);
                            return useSouthCrossing && module.Config.ShouldMount;
                        }, ChainHelper.MountChain())
                        .Then(pathfinding)
                        .BreakIf(() => pathfinding.TransitAttempted && !pathfinding.TransitReachedEnd)
                        .ConditionalThen(_ => !useSouthCrossing && module.Config.ShouldMount, ChainHelper.MountChain())
                        .WaitUntilNear(vnav, destination, 20f);
                }

                return chain;
            }

            Plugin.Chain.Submit(Factory);
        }

        if (!ImGui.IsItemHovered())
        {
            return;
        }

        if (!isNearShards)
        {
            ImGui.SetTooltip($"你必须靠近一个水晶才能传送");
        }
        else if (isNearCurrentShard)
        {
            ImGui.SetTooltip($"你已经在 {aethernet.ToFriendlyString()} 附近了");
        }
        else
        {
            ImGui.SetTooltip($"传送至 {aethernet.ToFriendlyString()}");
        }
    }

    public void OnFateEnd(StateManagerModule states)
    {
        var automator = module.GetModule<AutomatorModule>();
        if (automator.IsEnabled)
        {
            // Automatic mode has its own deterministic post-FATE policy: get
            // back to base camp before selecting the next activity. Do not
            // inherit the manual teleporter toggle here, otherwise the route
            // planner can walk to a nearby shard and chain-teleport instead.
            automator.automator.QueuePostActivityReturn("FATE completion");
            return;
        }

        if (!module.Config.ReturnAfterFate)
        {
            return;
        }

        Return();
    }

    public void OnCriticalEncounterEnd(StateManagerModule states)
    {
        var automator = module.GetModule<AutomatorModule>();
        if (automator.IsEnabled)
        {
            if (module.Config.ReturnAfterCriticalEncounter)
            {
                automator.automator.QueuePostActivityReturn("critical encounter completion");
            }

            return;
        }

        if (!module.Config.ReturnAfterCriticalEncounter)
        {
            return;
        }

        Return();
    }

    public void Return()
    {
        if (ZoneData.IsInForkedTower())
        {
            return;
        }

        Plugin.Chain.Submit(ChainHelper.ReturnChain());
    }

    public bool IsReady()
    {
        return module.TryGetIPCSubscriber<Lifestream>(out _);
    }
}
