using BOCCHI.Modules.Treasure;
using ECommons.DalamudServices;
using System;

namespace BOCCHI.Modules.Automator;

public enum AutomatorRunMode
{
    /// <summary>FATEs, CEs and pots as configured.</summary>
    Full,

    /// <summary>Only magic pots; treasure hunting fills the time between them.</summary>
    PotsAndTreasure,
}

/// <summary>
/// A treasure hunt the automator starts and pauses itself, for Pots &amp;
/// Treasure and for the optional hunt between activities. Ported from
/// upstream BOCCHI 4.2.
/// </summary>
public sealed class ManagedTreasureHunt
{
    public static readonly TimeSpan RestartDelay = TimeSpan.FromMinutes(3);

    private bool managing;

    private bool wasRunning;

    private DateTimeOffset nextStartAt = DateTimeOffset.MinValue;

    /// <summary>Between activities a finished hunt only starts again after another activity.</summary>
    private bool waitingForActivity;

    /// <summary>The player pressed Stop on our hunt: no more hunts until the automator restarts.</summary>
    private bool stoppedByPlayer;

    public bool IsManaging => managing;

    public string? PauseReason { get; private set; }

    public bool IsHunting(AutomatorModule module)
    {
        return managing && TryGetTreasure(module, out var treasure) && treasure.IsHuntRunning;
    }

    public bool IsPaused(AutomatorModule module)
    {
        return managing && TryGetTreasure(module, out var treasure) && treasure.IsHuntPaused;
    }

    /// <summary>Notes that the hunt ended by itself, which starts the restart wait.</summary>
    public void Observe(AutomatorModule module)
    {
        if (!managing || !TryGetTreasure(module, out var treasure))
        {
            return;
        }

        var running = treasure.IsHuntRunning;
        if (!running && !treasure.IsHuntPaused && treasure.HuntStoppedByPlayer)
        {
            Svc.Log.Info("[Automator] The player stopped the managed treasure hunt.");
            managing = false;
            stoppedByPlayer = true;
            wasRunning = false;
            return;
        }

        if (wasRunning && !running && !treasure.IsHuntPaused)
        {
            Svc.Log.Info("[Automator] Managed treasure hunt finished.");
            managing = false;
            nextStartAt = DateTimeOffset.UtcNow + RestartDelay;
            waitingForActivity = true;
        }

        wasRunning = running;
    }

    public void OnActivityCompleted()
    {
        waitingForActivity = false;
    }

    /// <summary>Starts or resumes the hunt. Returns true while the hunt owns the player.</summary>
    public bool Ensure(AutomatorModule module, AutomatorRunMode mode)
    {
        if (stoppedByPlayer || !TryGetTreasure(module, out var treasure))
        {
            return false;
        }

        if (treasure.IsHuntPaused)
        {
            // A pause the player made, or a hunt they started by hand, stays theirs.
            if (!managing || treasure.IsHuntPausedByPlayer)
            {
                return false;
            }

            Svc.Log.Info("[Automator] Resuming managed treasure hunt.");
            PauseReason = null;
            treasure.ResumeHunt();
            wasRunning = true;
            return true;
        }

        if (treasure.IsHuntRunning)
        {
            // Our own hunt, or one the player started by hand: leave it alone.
            return managing;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < nextStartAt)
        {
            return false;
        }

        if (mode == AutomatorRunMode.Full
            && (waitingForActivity || !TreasureHuntFillGate.MeetsMinimumFill(treasure.Tracker, treasure.Config)))
        {
            return false;
        }

        if (treasure.Tracker.CountInitialised
            && treasure.Tracker.BronzeChests + treasure.Tracker.SilverChests <= 0)
        {
            return false;
        }

        if (!treasure.TryStartManagedHunt(out var error))
        {
            Svc.Log.Info($"[Automator] Managed treasure hunt could not start: {error}");
            nextStartAt = now + TimeSpan.FromSeconds(30);
            return false;
        }

        Svc.Log.Info($"[Automator] Started managed treasure hunt ({mode}).");
        managing = true;
        wasRunning = true;
        PauseReason = null;
        return true;
    }

    public void Pause(AutomatorModule module, string reason)
    {
        if (!managing || !TryGetTreasure(module, out var treasure) || !treasure.IsHuntRunning)
        {
            return;
        }

        Svc.Log.Info($"[Automator] Pausing managed treasure hunt: {reason}.");
        PauseReason = reason;
        treasure.PauseHunt(reason);
        wasRunning = false;
    }

    public void Stop(AutomatorModule module)
    {
        if (managing && TryGetTreasure(module, out var treasure))
        {
            treasure.StopHunt();
        }

        managing = false;
        wasRunning = false;
        PauseReason = null;
        nextStartAt = DateTimeOffset.MinValue;
        waitingForActivity = false;
        stoppedByPlayer = false;
    }

    private static bool TryGetTreasure(AutomatorModule module, out TreasureModule treasure)
    {
        treasure = null!;
        if (!module.TryGetModule<TreasureModule>(out var found) || found == null)
        {
            return false;
        }

        treasure = found;
        return true;
    }
}

public static class TreasureHuntFillGate
{
    public const int BronzeCap = 30;

    public const int SilverCap = 8;

    public static bool MeetsMinimumFill(TreasureTracker tracker, TreasureConfig config)
    {
        return tracker.CountInitialised
               && MeetsMinimumFill(tracker.BronzeChests, tracker.SilverChests, config.HuntMinBronzePercent, config.HuntMinSilverPercent, config.ShouldHuntSilverChestsOnly);
    }

    public static bool MeetsMinimumFill(int bronze, int silver, int minBronzePercent, int minSilverPercent, bool silverOnly)
    {
        var bronzeOk = bronze / (float)BronzeCap * 100f >= minBronzePercent;
        var silverOk = silver / (float)SilverCap * 100f >= minSilverPercent;
        return silverOnly ? silverOk : bronzeOk || silverOk;
    }
}
