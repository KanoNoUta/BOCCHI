using BOCCHI.Data;
using BOCCHI.Modules.MobFarmer.States;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Reflection;
using Ocelot.Modules;
using Ocelot.States;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.MobFarmer;

public class Farmer : IDisposable
{
    public bool Running { get; private set; } = false;

    public Vector3 StartingPoint { get; private set; } = Vector3.Zero;

    public readonly IRotationPlugin RotationPlugin;

    private readonly Dictionary<string, Func<IModule, IRotationPlugin>> rotationPlugins = new()
    {
        { "WrathCombo", m => new Wrath(m) },
    };

    public readonly StateMachine<FarmerPhase, MobFarmerModule> StateMachine;

    public Farmer(MobFarmerModule module)
    {
        StateMachine = new StateMachine<FarmerPhase, MobFarmerModule>(FarmerPhase.Waiting, module);

        RotationPlugin = new BlankRotationPlugin();
        foreach (var (plugin, factory) in rotationPlugins)
        {
            if (!DalamudReflector.TryGetDalamudPlugin(plugin, out _, false, true))
            {
                continue;
            }

            RotationPlugin = factory(module);
            break;
        }
    }

    public void Update(UpdateContext context)
    {
        if (!context.IsForModule<MobFarmerModule>(out var module))
        {
            return;
        }


        if (!Running)
        {
            return;
        }

        StateMachine.Update();
    }

    public void Draw(RenderContext context)
    {
        if (!context.IsForModule<MobFarmerModule>(out var module))
        {
            return;
        }

        if (!module.Scanner.Mobs.Any())
        {
            return;
        }

        if (!Running && !module.Config.ShouldRenderDebugLinesWhileNotRunning)
        {
            return;
        }

        if (!module.Config.RenderDebugLines)
        {
            return;
        }

        foreach (var mob in module.Scanner.NotInCombat)
        {
            var color = new Vector4(0.9f, 0.1f, 0.1f, 1f);
            if (module.Config.IsSelectedForTerritory((Mob)mob.NameId, Svc.ClientState.TerritoryType))
            {
                color = new Vector4(0.9f, 0.1f, 0.9f, 1f);
            }

            context.DrawLine(mob.Position, color);
        }

        foreach (var mob in module.Scanner.InCombat)
        {
            context.DrawLine(mob.Position, new Vector4(0.1f, 0.9f, 0.1f, 1f));
        }
    }

    public void EnableFarmerMode()
    {
        if (Running)
            return;

        Running = true;
        StateMachine.Reset();

        StartingPoint = Player.Position;
        RotationPlugin.PhantomJobOff();
        PromeRotationController.Stop();
        if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
        {
            Chat.ExecuteCommand("/aepull off");
            Chat.ExecuteCommand("/aeTargetSelector off");
            Chat.ExecuteCommand("/occs 炮击 on");
            Chat.ExecuteCommand("/occs 神圣炮 off");
            Chat.ExecuteCommand("/occs 暗黑炮 on");
            Chat.ExecuteCommand("/occs 冲击炮 off");
            Chat.ExecuteCommand("/occs 老化炮 on");
        }
    }

    public void DisableFarmerMode()
    {
        PromeRotationController.Stop();
        BOCCHI.Pathfinding.AggroAvoidanceNavigation.Stop();
        Running = false;
        StartingPoint = Vector3.Zero;
        StateMachine.Reset();
    }

    public void Toggle()
    {
        if (!Running)
        {
            EnableFarmerMode();
        }
        else
        {
            DisableFarmerMode();
        }
    }

    public void Dispose()
    {
        PromeRotationController.Stop();
        RotationPlugin.Dispose();
    }
}
