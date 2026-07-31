using BOCCHI.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot;
using Ocelot.Chain;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.Windows;
using System;

namespace BOCCHI.Modules.MobFarmer;

[OcelotModule(int.MaxValue - 2)]
public class MobFarmerModule : Module
{
    public override MobFarmerConfig Config
    {
        get => PluginConfig.MobFarmerConfig;
    }

    public override bool IsEnabled
    {
        get => Config.Enabled;
    }

    private readonly Panel panel = new();

    public readonly Scanner Scanner;

    public readonly Farmer Farmer;

    private Job? pendingTreasureFindingJobRestore;

    private long nextTreasureFindingJobRestoreAt;

    public MobFarmerModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        Scanner = new Scanner(this);
        Farmer = new Farmer(this);
    }

    public override void Update(UpdateContext context)
    {
        TryRestoreTreasureFindingJob();
        Scanner.Tick(context.Framework);
        Farmer.Update(context.ForModule(this));
    }

    public override void Render(RenderContext context)
    {
        Farmer.Draw(context.ForModule(this));
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        pendingTreasureFindingJobRestore = null;
        nextTreasureFindingJobRestoreAt = 0;

        if (!Farmer.Running)
        {
            return;
        }

        if (TryGetIPCSubscriber<VNavmesh>(out var navigation) && navigation != null && navigation.IsReady())
        {
            BOCCHI.Pathfinding.AggroAvoidanceNavigation.Stop(navigation);
        }

        Plugin.Chain.Abort();
        ChainManager.Get("MobFarmer+Farmer").Abort();
        Farmer.DisableFarmerMode();
    }

    public void QueueTreasureFindingJobRestore(Job startingJob)
    {
        if (startingJob.id == BOCCHI.Enums.JobId.Freelancer)
        {
            return;
        }

        pendingTreasureFindingJobRestore = startingJob;
        nextTreasureFindingJobRestoreAt = 0;
    }

    private void TryRestoreTreasureFindingJob()
    {
        var restoreJob = pendingTreasureFindingJobRestore;
        if (restoreJob == null
            || Svc.Condition[ConditionFlag.InCombat]
            || Svc.Condition[ConditionFlag.BetweenAreas]
            || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Player.IsCasting)
        {
            return;
        }

        var currentJob = Job.Current;
        if (currentJob.id == restoreJob.id)
        {
            ClearPendingTreasureFindingJobRestore();
            return;
        }

        // The fallback owns only the temporary Freelancer state created by
        // Treasuresight. Never overwrite a support-job change made by the user.
        if (currentJob.id != BOCCHI.Enums.JobId.Freelancer)
        {
            ClearPendingTreasureFindingJobRestore();
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextTreasureFindingJobRestoreAt)
        {
            return;
        }

        restoreJob.ChangeTo();
        nextTreasureFindingJobRestoreAt = now + 1000;
        Svc.Log.Info($"Restoring support job {restoreJob.id} after mob-farmer Treasuresight.");
    }

    private void ClearPendingTreasureFindingJobRestore()
    {
        pendingTreasureFindingJobRestore = null;
        nextTreasureFindingJobRestoreAt = 0;
    }

    public override void Dispose()
    {
        ClearPendingTreasureFindingJobRestore();
        Farmer.Dispose();
    }
}
