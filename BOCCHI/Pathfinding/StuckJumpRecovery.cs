using Dalamud.Game.ClientState.Conditions;
using BOCCHI.ActionHelpers;
using BOCCHI.Modules.Pathfinder;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.IPC;
using System;
using System.Numerics;

namespace BOCCHI.Pathfinding;

/// <summary>
/// Detects a stuck vnavmesh route (the player barely moves while navigation
/// claims to be active) and performs a short burst of jumps. In the Occult
/// Crescent the navmesh often pins the player against a rock, stair edge or a
/// monster-collision seam; a few jumps snap the actor out of the stuck state
/// so the existing repath/watcher logic can continue.
/// </summary>
public static class StuckJumpRecovery
{
    private const long StuckTimeoutMs = 4000;
    private const float StuckDistanceSquared = 0.25f;
    private const int JumpCount = 3;
    private const long JumpIntervalMs = 650;
    private const long PostJumpCooldownMs = 6000;

    private static Vector3? lastPosition;
    private static long lastMoveAt;
    private static int jumpsRemaining;
    private static long nextJumpAt;
    private static long recoveryCooldownUntil;
    private static Func<PathfinderConfig>? configProvider;

    public static void Configure(Func<PathfinderConfig> provider)
    {
        configProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static void Update(VNavmesh? vnav, bool hasActiveRoute)
    {
        if (configProvider?.Invoke() is { ShouldUseStuckJumpRecovery: false })
        {
            Reset();
            return;
        }

        var now = Environment.TickCount64;
        if (vnav == null || !vnav.IsReady() || Svc.Objects.LocalPlayer is not { } player)
        {
            Reset();
            return;
        }

        if (player.IsDead || IsBusy())
        {
            Reset();
            return;
        }

        var navigationActive = IsNavigationActive(vnav) || hasActiveRoute;
        if (!navigationActive)
        {
            Reset();
            return;
        }

        // Mid-jump burst: keep issuing jumps on the fixed cadence.
        if (jumpsRemaining > 0)
        {
            if (now >= nextJumpAt)
            {
                TryJump();
                jumpsRemaining--;
                nextJumpAt = now + JumpIntervalMs;
            }

            return;
        }

        var position = player.Position;
        if (lastPosition is { } last)
        {
            if (DistanceSquaredXZ(position, last) >= StuckDistanceSquared)
            {
                // The player is still making progress; keep the reference fresh.
                lastPosition = position;
                lastMoveAt = now;
                return;
            }
        }
        else
        {
            lastPosition = position;
            lastMoveAt = now;
            return;
        }

        if (now - lastMoveAt < StuckTimeoutMs || now < recoveryCooldownUntil)
        {
            return;
        }

        // Stuck. If the player is mounted, dismount first: jumping is not
        // available while riding, and the unmount itself often frees the actor
        // from the collision seam. Re-arm the progress clock and evaluate
        // again after the dismount.
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Actions.TryUnmount();
            lastMoveAt = now;
            Svc.Log.Warning(
                $"Navigation made no progress for {(now - lastMoveAt) / 1000d:F1}s while mounted; " +
                "dismounting to unstick the route.");
            return;
        }

        // Start the jump burst. A fresh burst refreshes the cooldown so
        // repeated stalls are handled, while a route that recovers keeps the
        // next burst at least one cooldown away.
        jumpsRemaining = JumpCount;
        nextJumpAt = now;
        recoveryCooldownUntil = now + PostJumpCooldownMs;
        Svc.Log.Warning(
            $"Navigation made no progress for {(now - lastMoveAt) / 1000d:F1}s; " +
            $"jumping {JumpCount} times to unstick the route.");
        TryJump();
        jumpsRemaining--;
        nextJumpAt = now + JumpIntervalMs;
    }

    private static unsafe void TryJump()
    {
        try
        {
            if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 2) == 0)
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
            }
        }
        catch (Exception exception)
        {
            Svc.Log.Verbose(exception, "Could not issue a stuck-recovery jump.");
        }
    }

    private static bool IsNavigationActive(VNavmesh vnav)
    {
        try
        {
            return vnav.IsRunning() || vnav.IsSimpleMoveInProgress() || vnav.IsPathfinding();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBusy()
    {
        return Svc.Condition[ConditionFlag.InCombat]
               || Svc.Condition[ConditionFlag.BetweenAreas]
               || Svc.Condition[ConditionFlag.BetweenAreas51]
               || Svc.Condition[ConditionFlag.OccupiedInEvent]
               || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
               || Svc.Condition[ConditionFlag.Casting];
    }

    private static float DistanceSquaredXZ(Vector3 left, Vector3 right)
    {
        var dx = left.X - right.X;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dz * dz);
    }

    private static void Reset()
    {
        lastPosition = null;
        lastMoveAt = 0;
        jumpsRemaining = 0;
        nextJumpAt = 0;
    }
}
