using Ocelot.Modules;
using Ocelot.Windows;
using System.Collections.Generic;
using System.Numerics;
using BOCCHI.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.IPC;
using System;

namespace BOCCHI.Modules.Treasure;

[OcelotModule(1003, 1)]
public class TreasureModule(Plugin _plugin, Config config) : Module(_plugin, config)
{
    public override TreasureConfig Config
    {
        get => PluginConfig.TreasureConfig;
    }

    public override bool ShouldInitialize
    {
        get => true;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public readonly static Vector4 Bronze = new(0.804f, 0.498f, 0.196f, 1f);

    public readonly static Vector4 Silver = new(0.753f, 0.753f, 0.753f, 1f);

    public readonly static Vector4 Unknown = new(0.6f, 0.2f, 0.8f, 1f);

    public readonly TreasureTracker Tracker = new();

    private TreasureHunt hunter = null!;

    private Job? pendingTreasureSightJobRestore;

    private long nextTreasureSightJobRestoreAt;

    public List<Treasure> Treasures
    {
        get => Tracker.Treasures;
    }

    public bool IsHuntRunning => hunter?.IsRunning == true;

    private readonly Panel panel = new();

    private readonly Radar radar = new();

    public override void PostInitialize()
    {
        hunter = new TreasureHunt(this);
    }

    public override void Update(UpdateContext context)
    {
        Tracker.Tick(Plugin);
        if (BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            TryRestoreTreasureSightJob();
            hunter.Update();
        }
    }

    public override void Render(RenderContext context)
    {
        radar.Draw(context.ForModule(this));
    }

    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);

        if (Config.ShouldEnableTreasureHunt && BOCCHI.Data.ZoneData.IsInOccultCrescent())
        {
            hunter.Draw(this);
        }

        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        StopHunt();
        Tracker.Reset();
        if (!ZoneData.IsOccultCrescentTerritory(id))
        {
            pendingTreasureSightJobRestore = null;
            nextTreasureSightJobRestoreAt = 0;
        }
    }

    public void QueueTreasureSightJobRestore(Job startingJob)
    {
        if (startingJob.id == BOCCHI.Enums.JobId.Freelancer)
        {
            return;
        }

        pendingTreasureSightJobRestore = startingJob;
        nextTreasureSightJobRestoreAt = 0;
    }

    private void TryRestoreTreasureSightJob()
    {
        var restoreJob = pendingTreasureSightJobRestore;
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
            pendingTreasureSightJobRestore = null;
            nextTreasureSightJobRestoreAt = 0;
            return;
        }

        // Do not overwrite a deliberate manual job change. The fallback owns
        // only the temporary Freelancer state created by Treasuresight.
        if (currentJob.id != BOCCHI.Enums.JobId.Freelancer)
        {
            pendingTreasureSightJobRestore = null;
            nextTreasureSightJobRestoreAt = 0;
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextTreasureSightJobRestoreAt)
        {
            return;
        }

        restoreJob.ChangeTo();
        nextTreasureSightJobRestoreAt = now + 1000;
        Svc.Log.Info($"Restoring support job {restoreJob.id} after Treasuresight.");
    }

    public void StopHunt()
    {
        hunter?.Stop();
    }

    public bool TryStartHunt(out string error)
    {
        error = string.Empty;
        if (!ZoneData.IsInOccultCrescent())
        {
            error = "宝箱猎人只能在南部或北部新月岛内启动。";
            return false;
        }

        if (!TryGetIPCSubscriber<VNavmesh>(out var navigation)
            || navigation == null
            || !navigation.IsReady())
        {
            error = "vnavmesh 尚未准备完成。";
            return false;
        }

        if (!TryGetIPCSubscriber<Lifestream>(out var lifestream)
            || lifestream == null
            || !lifestream.IsReady())
        {
            error = "Lifestream 尚未准备完成。";
            return false;
        }

        Config.Enabled = true;
        Config.EnableTreasureHunt = true;
        PluginConfig.Save();
        hunter.Start();
        return true;
    }

    public override void Dispose()
    {
        StopHunt();
        pendingTreasureSightJobRestore = null;
        Tracker.Dispose();
        base.Dispose();
    }
}
