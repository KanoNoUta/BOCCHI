using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Ocelot.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BocchiActions = BOCCHI.ActionHelpers.Actions;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Modules.Automator.Pots;

public enum PotChestFarmPhase
{
    WaitingForBuff,
    ElixirAtCenter,
    SearchingCandidates,
    OpeningReveal,
    BlindSweep,
}

/// <summary>
/// Collects the coffer a magic pot reveals after its FATE. The pot gives
/// "Cache Me If You Can" (status 1531); the Magical Elixir key item makes it
/// point at the coffer, and each hint narrows the known coffer pads. Opening
/// the coffer can offer a second chance at one of the zone's reroll pads.
/// Ported from upstream BOCCHI 4.2's FarmingPotChestsHandler, without its
/// cloud pad sync, aethernet travel planning and Ninja Hide.
/// </summary>
public sealed class PotChestFarm
{
    public const uint CacheMeStatusId = 1531;

    public const uint MagicalElixirItemId = 2003296;

    public static readonly uint[] RevealCofferBaseIds = [2014741, 2014742, 2014743];

    private const float ChestSearchRadius = 18f;

    private const float RevealSearchRadius = 28f;

    private const float UntargetableRevealRadius = 12f;

    private const float LiveRevealDivertRadius = 80f;

    private const float CandidateProbeRadius = 5f;

    private const float ArrivalRadius = 3.5f;

    private const float InteractRadius = 2f;

    private const float MountDistance = 30f;

    private const int MaxElixirAttempts = 3;

    private const int MaxHintReadings = 10;

    private static readonly TimeSpan BuffWaitTimeout = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan ElixirInterval = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan RevealPhaseTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan PostBuffGrace = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RevealSpawnGrace = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan RerollWait = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan ChestSpawnWait = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan StuckWindow = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan IdleStuckWindow = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan FarmTimeout = TimeSpan.FromMinutes(10);

    private readonly AutomatorModule module;

    private readonly PotHintTracker hints;

    private readonly bool includeRerolls;

    private readonly List<Vector3> primaryPads;

    private readonly List<Vector3> rerollPads;

    private readonly List<Vector3> otherCofferSpots;

    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;

    private readonly HashSet<ulong> openedObjects = [];

    private readonly List<Vector3> openedPositions = [];

    private readonly List<IGameObject> reveals = [];

    private readonly List<IGameObject> chests = [];

    private Queue<Vector3> candidates = new();

    private List<Vector3> pool = [];

    private Queue<Vector3> blindChests = new();

    private DateTimeOffset phaseStartedAt = DateTimeOffset.UtcNow;

    private DateTimeOffset? lastElixirAt;

    private DateTimeOffset? settledAt;

    private DateTimeOffset? buffLostAt;

    private DateTimeOffset? waitingForSpawnSince;

    private Vector3? elixirHintOrigin;

    private int elixirAttempts;

    private int hintBaseline;

    private int hintsApplied;

    private bool holdingAfterBuffLoss;

    private bool rerollWaitStarted;

    private bool onRerollPool;

    private bool hasOpenedChest;

    private bool defending;

    private ulong? openTargetId;

    private DateTimeOffset openStartedAt;

    private bool interactAttempted;

    private Vector3? lastPathDestination;

    private DateTimeOffset lastPathIssuedAt = DateTimeOffset.MinValue;

    private Vector3? watchTarget;

    private float watchBestDistance = float.MaxValue;

    private DateTimeOffset watchImprovedAt;

    private DateTimeOffset? watchIdleSince;

    public PotChestFarm(AutomatorModule module, PotHintTracker hints, uint fateId, uint territoryId, bool includeRerolls)
    {
        this.module = module;
        this.hints = hints;
        this.includeRerolls = includeRerolls;
        FateId = fateId;
        TerritoryId = territoryId;
        primaryPads = PotZoneData.GetPrimaryPads(fateId).ToList();
        rerollPads = PotZoneData.GetRerollPads(territoryId).ToList();
        otherCofferSpots = TreasureLayoutData.Read(territoryId).Select(node => node.Position).ToList();
        Phase = primaryPads.Count > 0 ? PotChestFarmPhase.WaitingForBuff : PotChestFarmPhase.BlindSweep;
        if (Phase == PotChestFarmPhase.BlindSweep)
        {
            blindChests = new Queue<Vector3>(SortByDistance(ActiveBlindPads()));
        }

        Svc.Log.Info($"[Pots] Starting the coffer farm for pot FATE {fateId} ({Phase}).");
    }

    public uint FateId { get; }

    public uint TerritoryId { get; }

    public PotChestFarmPhase Phase { get; private set; }

    public bool IsFinished { get; private set; }

    public string? FinishReason { get; private set; }

    public bool HasOpenedChest => hasOpenedChest;

    public int RemainingCandidates => Phase == PotChestFarmPhase.BlindSweep ? blindChests.Count : candidates.Count;

    public static bool HasCacheMe()
    {
        return Svc.Objects.LocalPlayer?.StatusList.Any(status => status.StatusId == CacheMeStatusId) == true;
    }

    public void Update(VNavmesh vnav)
    {
        if (IsFinished)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (Svc.ClientState.TerritoryType != TerritoryId || now - startedAt > FarmTimeout)
        {
            Finish(vnav, Svc.ClientState.TerritoryType != TerritoryId ? "left the zone" : "farm timed out");
            return;
        }

        var player = Svc.Objects.LocalPlayer;
        if (player == null || player.IsDead || Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return;
        }

        if (UpdateSelfDefense(vnav))
        {
            return;
        }

        RefreshObjects();

        if (Phase != PotChestFarmPhase.WaitingForBuff && !HasCacheMe())
        {
            if (!TryFinishRevealAfterBuff(vnav, now))
            {
                Finish(vnav, hasOpenedChest ? "coffer opened" : "Cache Me If You Can ended");
            }

            return;
        }

        buffLostAt = null;
        if (holdingAfterBuffLoss)
        {
            holdingAfterBuffLoss = false;
            rerollWaitStarted = false;
            Svc.Log.Info("[Pots] Cache Me If You Can came back after the coffer; looking for the second chance.");
            ResumeSearchOrBlind(vnav);
            return;
        }

        switch (Phase)
        {
            case PotChestFarmPhase.WaitingForBuff:
                HandleWaitingForBuff(vnav, now);
                break;
            case PotChestFarmPhase.ElixirAtCenter:
                HandleElixirAtCenter(vnav, now);
                break;
            case PotChestFarmPhase.SearchingCandidates:
                HandleSearchingCandidates(vnav, now);
                break;
            case PotChestFarmPhase.OpeningReveal:
                HandleOpeningReveal(vnav, now);
                break;
            case PotChestFarmPhase.BlindSweep:
                HandleBlindSweep(vnav, now);
                break;
        }
    }

    public void Stop(VNavmesh? vnav, string reason)
    {
        Finish(vnav, reason);
    }

    private void Finish(VNavmesh? vnav, string reason)
    {
        if (IsFinished)
        {
            return;
        }

        IsFinished = true;
        FinishReason = reason;
        hints.Disarm();
        StopMoving(vnav);
        ReleaseSelfDefense();
        Svc.Log.Info($"[Pots] Coffer farm for pot FATE {FateId} finished: {reason}.");
    }

    private void SetPhase(PotChestFarmPhase phase)
    {
        if (Phase != phase)
        {
            Svc.Log.Info($"[Pots] {Phase} -> {phase}");
        }

        Phase = phase;
        phaseStartedAt = DateTimeOffset.UtcNow;
    }

    // ---- Combat -------------------------------------------------------------

    private bool UpdateSelfDefense(VNavmesh vnav)
    {
        if (!Svc.Condition[ConditionFlag.InCombat])
        {
            ReleaseSelfDefense();
            return false;
        }

        var player = Svc.Objects.LocalPlayer!;
        if (!defending)
        {
            defending = true;
            StopMoving(vnav);
            module.SetAiProviderEnabled(true);
            Svc.Log.Info("[Pots] Attacked during the coffer farm; fighting back.");
        }

        if (EzThrottler.Throttle("Pots.SelfDefense", 1000))
        {
            var attacker = TargetHelper.Enemies.FirstOrDefault(enemy => enemy.TargetObjectId == player.GameObjectId);
            if (attacker != null
                && (Svc.Targets.Target is not IBattleNpc { IsDead: false } current
                    || current.TargetObjectId != player.GameObjectId))
            {
                Svc.Targets.Target = attacker;
            }

            if (!PromeRotationController.IsRunning())
            {
                PromeRotationController.StartWithoutAutoPull();
            }
        }

        return true;
    }

    private void ReleaseSelfDefense()
    {
        if (!defending)
        {
            return;
        }

        defending = false;
        PromeRotationController.Stop();
        module.SetAiProviderEnabled(false);
        ResetWatch();
    }

    // ---- Phases -------------------------------------------------------------

    private void HandleWaitingForBuff(VNavmesh vnav, DateTimeOffset now)
    {
        if (HasCacheMe())
        {
            hints.Arm();
            hintBaseline = 0;
            StopMoving(vnav);
            elixirAttempts = 0;
            lastElixirAt = null;
            SetPhase(PotChestFarmPhase.ElixirAtCenter);
            return;
        }

        if (now - phaseStartedAt >= BuffWaitTimeout)
        {
            Finish(vnav, "no Cache Me If You Can after the pot");
        }
    }

    private void HandleElixirAtCenter(VNavmesh vnav, DateTimeOffset now)
    {
        if (hints.TryGetEventSince(hintBaseline, out var evt))
        {
            hintBaseline = evt.Revision;
            switch (evt.Kind)
            {
                case PotLogEventKind.BonusOffer:
                    SwitchToRerollPool(vnav);
                    return;
                case PotLogEventKind.CofferReveal:
                    SetPhase(PotChestFarmPhase.OpeningReveal);
                    return;
                case PotLogEventKind.Hint:
                    pool = ActivePool();
                    if (pool.Count == 0)
                    {
                        FallBackToBlind(vnav, "no pads for this pot");
                        return;
                    }

                    TryNarrowByHint(vnav, evt);
                    return;
            }
        }

        if (elixirAttempts >= MaxElixirAttempts)
        {
            if (lastElixirAt is { } used && now - used >= ElixirInterval)
            {
                FallBackToBlind(vnav, "no compass hint after the elixir");
            }

            return;
        }

        if (lastElixirAt is { } last && now - last < ElixirInterval)
        {
            return;
        }

        if (!HasElixir())
        {
            FallBackToBlind(vnav, "no Magical Elixir");
            return;
        }

        UseElixir(now);
    }

    private void HandleSearchingCandidates(VNavmesh vnav, DateTimeOffset now)
    {
        var player = Svc.Objects.LocalPlayer!;
        if (FindUnopenedRevealNear(player.Position, RevealSearchRadius, allowUntargetableWithin: UntargetableRevealRadius) is { } nearby)
        {
            BeginOpening(nearby);
            return;
        }

        var divert = reveals
            .Where(reveal => reveal.IsTargetable && !IsOpened(reveal))
            .Where(reveal => PotTreasureFilter.Distance2D(reveal.Position, player.Position) <= LiveRevealDivertRadius)
            .OrderBy(reveal => PotTreasureFilter.Distance2D(reveal.Position, player.Position))
            .FirstOrDefault();
        if (divert != null)
        {
            BeginOpening(divert);
            return;
        }

        if (hints.TryGetEventSince(hintBaseline, out var evt))
        {
            hintBaseline = evt.Revision;
            switch (evt.Kind)
            {
                case PotLogEventKind.BonusOffer:
                    SwitchToRerollPool(vnav);
                    return;
                case PotLogEventKind.CofferReveal:
                    SetPhase(PotChestFarmPhase.OpeningReveal);
                    return;
                case PotLogEventKind.Hint:
                    if (!TryNarrowByHint(vnav, evt))
                    {
                        return;
                    }

                    break;
            }
        }

        while (candidates.Count > 0 && IsPadOpened(candidates.Peek()))
        {
            candidates.Dequeue();
            elixirAttempts = 0;
        }

        if (candidates.Count == 0)
        {
            ResumeSearchOrBlind(vnav);
            return;
        }

        var target = candidates.Peek();
        var liveNearTarget = FindUnopenedRevealNear(target, ChestSearchRadius, allowUntargetableWithin: ChestSearchRadius)
                             ?? FindChestNear(target);
        var pathTarget = liveNearTarget?.Position ?? target;
        var distance = PotTreasureFilter.Distance2D(player.Position, pathTarget);
        if (distance > CandidateProbeRadius)
        {
            settledAt = null;
            if (IsStuck(pathTarget, distance, now))
            {
                SkipCandidate(vnav, "could not reach the pad");
                return;
            }

            MoveTo(vnav, pathTarget, now);
            return;
        }

        if (settledAt == null)
        {
            StopMoving(vnav);
            settledAt = now;
            return;
        }

        if (now - settledAt.Value < SettleDelay)
        {
            return;
        }

        if (liveNearTarget != null)
        {
            BeginOpening(liveNearTarget);
            return;
        }

        // Standing on the pad: the elixir either reveals the coffer here or
        // gives a closer hint.
        if (elixirAttempts < MaxElixirAttempts && (lastElixirAt == null || now - lastElixirAt.Value >= ElixirInterval))
        {
            if (HasElixir())
            {
                UseElixir(now);
            }
            else
            {
                elixirAttempts = MaxElixirAttempts;
            }

            return;
        }

        var lastAction = lastElixirAt is { } used && used > settledAt.Value ? used : settledAt.Value;
        if (now - lastAction >= ElixirInterval)
        {
            SkipCandidate(vnav, "no coffer at the pad");
        }
    }

    private void HandleOpeningReveal(VNavmesh vnav, DateTimeOffset now)
    {
        var player = Svc.Objects.LocalPlayer!;
        var reveal = GetOpenTarget()
                     ?? FindUnopenedRevealNear(player.Position, RevealSearchRadius, allowUntargetableWithin: UntargetableRevealRadius)
                     ?? (candidates.Count > 0
                         ? FindUnopenedRevealNear(candidates.Peek(), ChestSearchRadius, allowUntargetableWithin: ChestSearchRadius)
                         : null);
        if (reveal != null)
        {
            if (openTargetId != reveal.GameObjectId)
            {
                BeginOpening(reveal);
            }

            switch (TryOpen(vnav, reveal, now))
            {
                case OpenResult.Opened:
                    FinishReveal(vnav, opened: true);
                    break;
                case OpenResult.Failed:
                    FinishReveal(vnav, opened: false);
                    break;
            }

            return;
        }

        if (openTargetId != null && interactAttempted)
        {
            // The coffer vanished after we interacted with it: it was opened.
            FinishReveal(vnav, opened: true);
            return;
        }

        if (hints.TryGetEventSince(hintBaseline, out var evt))
        {
            hintBaseline = evt.Revision;
            if (evt.Kind == PotLogEventKind.Hint && TryNarrowByHint(vnav, evt))
            {
                return;
            }

            if (evt.Kind == PotLogEventKind.BonusOffer)
            {
                SwitchToRerollPool(vnav);
                return;
            }
        }

        if (now - phaseStartedAt >= RevealPhaseTimeout)
        {
            ResumeSearchOrBlind(vnav);
        }
    }

    private void HandleBlindSweep(VNavmesh vnav, DateTimeOffset now)
    {
        while (blindChests.Count > 0 && IsPadOpened(blindChests.Peek()))
        {
            blindChests.Dequeue();
            waitingForSpawnSince = null;
        }

        if (blindChests.Count == 0)
        {
            Finish(vnav, "no pads left to check");
            return;
        }

        var player = Svc.Objects.LocalPlayer!;
        var pad = blindChests.Peek();
        var live = FindUnopenedRevealNear(pad, ChestSearchRadius, allowUntargetableWithin: ChestSearchRadius)
                   ?? FindChestNear(pad);
        if (live != null)
        {
            waitingForSpawnSince = null;
            if (openTargetId != live.GameObjectId)
            {
                BeginOpening(live);
            }

            switch (TryOpen(vnav, live, now))
            {
                case OpenResult.Opened:
                    MarkOpened(live);
                    hasOpenedChest = true;
                    blindChests.Dequeue();
                    ClearOpenTarget();
                    break;
                case OpenResult.Failed:
                    blindChests.Dequeue();
                    ClearOpenTarget();
                    break;
            }

            return;
        }

        var distance = PotTreasureFilter.Distance2D(player.Position, pad);
        if (distance > ArrivalRadius)
        {
            if (IsStuck(pad, distance, now))
            {
                Svc.Log.Info($"[Pots] Skipping blind pad {pad}: could not reach it.");
                blindChests.Dequeue();
                ResetWatch();
                return;
            }

            MoveTo(vnav, pad, now);
            return;
        }

        StopMoving(vnav);
        waitingForSpawnSince ??= now;
        if (now - waitingForSpawnSince.Value >= ChestSpawnWait)
        {
            blindChests.Dequeue();
            waitingForSpawnSince = null;
        }
    }

    // ---- Hint narrowing and pools --------------------------------------------

    private bool TryNarrowByHint(VNavmesh vnav, PotLogEvent evt)
    {
        var from = elixirHintOrigin ?? evt.Origin ?? Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        if (pool.Count == 0)
        {
            pool = ActivePool();
        }

        var basis = candidates.Count > 0 ? candidates.ToList() : pool;
        var survivors = PotTreasureFilter.Narrow(basis, from, evt.Direction, evt.Distance, PotTreasureFilter.OctantTolerance);
        if (survivors.Count == 0)
        {
            survivors = PotTreasureFilter.Narrow(pool, from, evt.Direction, evt.Distance, PotTreasureFilter.OctantTolerance);
        }

        if (survivors.Count == 0)
        {
            survivors = PotTreasureFilter.Narrow(pool, from, evt.Direction, evt.Distance, PotTreasureFilter.WideTolerance);
        }

        if (survivors.Count == 0)
        {
            elixirHintOrigin = null;
            if (candidates.Count > 0)
            {
                return true;
            }

            FallBackToBlind(vnav, "no pad matches the hint");
            return false;
        }

        NarrowTo(survivors);
        Svc.Log.Info($"[Pots] Hint {evt.Direction} {evt.Distance}: {survivors.Count} pads remain.");
        return true;
    }

    private void NarrowTo(IEnumerable<Vector3> survivors)
    {
        candidates = new Queue<Vector3>(survivors);
        hintsApplied++;
        elixirAttempts = 0;
        lastElixirAt = null;
        elixirHintOrigin = null;
        settledAt = null;
        ResetWatch();
        SetPhase(PotChestFarmPhase.SearchingCandidates);
    }

    private void ResumeSearchOrBlind(VNavmesh vnav)
    {
        ClearOpenTarget();
        if (candidates.Count > 0)
        {
            SetPhase(PotChestFarmPhase.SearchingCandidates);
            return;
        }

        if (hasOpenedChest && !EnsureSecondChancePool(vnav))
        {
            return;
        }

        if (pool.Count > 0 && hintsApplied < MaxHintReadings && HasCacheMe())
        {
            NarrowTo(pool);
            return;
        }

        FallBackToBlind(vnav, "no candidates left");
    }

    private void SwitchToRerollPool(VNavmesh vnav)
    {
        if (!includeRerolls || onRerollPool)
        {
            return;
        }

        if (!TryActivateRerollPool(markOpened: true))
        {
            Finish(vnav, "no second-chance pads");
            return;
        }

        NarrowTo(pool);
    }

    private bool EnsureSecondChancePool(VNavmesh vnav)
    {
        if (onRerollPool && pool.Count > 0)
        {
            return true;
        }

        if (!includeRerolls)
        {
            Finish(vnav, "coffer opened; second-chance coffers are off");
            return false;
        }

        if (!TryActivateRerollPool(markOpened: false))
        {
            Finish(vnav, "coffer opened; no second-chance pads");
            return false;
        }

        return true;
    }

    private bool TryActivateRerollPool(bool markOpened)
    {
        if (rerollPads.Count == 0)
        {
            return false;
        }

        onRerollPool = true;
        hasOpenedChest |= markOpened;
        pool = rerollPads.ToList();
        candidates.Clear();
        ResetWatch();
        Svc.Log.Info($"[Pots] Switched to {pool.Count} second-chance pads.");
        return true;
    }

    private void FallBackToBlind(VNavmesh vnav, string reason)
    {
        hints.Disarm();
        List<Vector3> positions;
        if (hasOpenedChest || onRerollPool)
        {
            if (!includeRerolls || rerollPads.Count == 0)
            {
                Finish(vnav, $"{reason}; no second-chance search");
                return;
            }

            onRerollPool = true;
            positions = rerollPads.ToList();
        }
        else
        {
            positions = ActiveBlindPads();
        }

        positions = SortByDistance(positions);
        if (positions.Count == 0)
        {
            Finish(vnav, $"{reason}; no pads");
            return;
        }

        Svc.Log.Info($"[Pots] Falling back to checking {positions.Count} pads in order: {reason}.");
        blindChests = new Queue<Vector3>(positions);
        candidates.Clear();
        waitingForSpawnSince = null;
        ResetWatch();
        SetPhase(PotChestFarmPhase.BlindSweep);
    }

    private List<Vector3> ActivePool()
    {
        return hasOpenedChest || onRerollPool ? rerollPads.ToList() : primaryPads.ToList();
    }

    private List<Vector3> ActiveBlindPads()
    {
        return includeRerolls ? primaryPads.Concat(rerollPads).ToList() : primaryPads.ToList();
    }

    private static List<Vector3> SortByDistance(IEnumerable<Vector3> positions)
    {
        var from = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        return positions.OrderBy(position => PotTreasureFilter.Distance2D(position, from)).ToList();
    }

    private void SkipCandidate(VNavmesh vnav, string reason)
    {
        if (candidates.Count > 0)
        {
            Svc.Log.Info($"[Pots] Skipping pad {candidates.Peek()}: {reason}.");
            candidates.Dequeue();
        }

        elixirAttempts = 0;
        lastElixirAt = null;
        settledAt = null;
        StopMoving(vnav);
        ResetWatch();
    }

    // ---- After the buff ends ---------------------------------------------------

    private bool TryFinishRevealAfterBuff(VNavmesh vnav, DateTimeOffset now)
    {
        buffLostAt ??= now;
        if (now - buffLostAt.Value >= PostBuffGrace)
        {
            return false;
        }

        if (hints.TryGetEventSince(hintBaseline, out var evt))
        {
            hintBaseline = evt.Revision;
            if (evt.Kind == PotLogEventKind.CofferReveal)
            {
                holdingAfterBuffLoss = true;
                hasOpenedChest = true;
            }
        }

        var player = Svc.Objects.LocalPlayer!;
        var reveal = GetOpenTarget()
                     ?? FindUnopenedRevealNear(player.Position, RevealSearchRadius, allowUntargetableWithin: UntargetableRevealRadius);
        if (reveal == null)
        {
            if (openTargetId != null && interactAttempted)
            {
                MarkOpenedTarget();
                holdingAfterBuffLoss = true;
                hasOpenedChest = true;
                ClearOpenTarget();
            }

            if (!holdingAfterBuffLoss)
            {
                return now - buffLostAt.Value < RevealSpawnGrace;
            }

            // Opening the coffer consumes the buff. A second chance gives it
            // back shortly afterwards.
            if (!rerollWaitStarted)
            {
                rerollWaitStarted = true;
                buffLostAt = now;
            }

            return includeRerolls && now - buffLostAt.Value < RerollWait;
        }

        holdingAfterBuffLoss = true;
        hasOpenedChest = true;
        if (openTargetId != reveal.GameObjectId)
        {
            BeginOpening(reveal);
        }

        Phase = PotChestFarmPhase.OpeningReveal;
        if (TryOpen(vnav, reveal, now) == OpenResult.Opened)
        {
            MarkOpened(reveal);
            ClearOpenTarget();
        }

        return true;
    }

    private void FinishReveal(VNavmesh vnav, bool opened)
    {
        if (opened)
        {
            MarkOpenedTarget();
            hasOpenedChest = true;
        }

        if (candidates.Count > 0)
        {
            candidates.Dequeue();
        }

        elixirAttempts = 0;
        lastElixirAt = null;
        settledAt = null;
        ClearOpenTarget();
        StopMoving(vnav);
        ResumeSearchOrBlind(vnav);
    }

    // ---- Objects -------------------------------------------------------------

    private void RefreshObjects()
    {
        reveals.Clear();
        chests.Clear();
        var potPads = primaryPads.Concat(rerollPads).ToList();
        foreach (var obj in Svc.Objects)
        {
            if (obj == null || !obj.IsValid() || obj.IsDead)
            {
                continue;
            }

            if (RevealCofferBaseIds.Contains(obj.BaseId))
            {
                if (PotTreasureFilter.IsOnPotPad(obj.Position, potPads, otherCofferSpots))
                {
                    reveals.Add(obj);
                }
            }
            else if (obj.ObjectKind == ObjectKind.Treasure)
            {
                chests.Add(obj);
            }
        }
    }

    private IGameObject? FindUnopenedRevealNear(Vector3 origin, float radius, float allowUntargetableWithin)
    {
        return reveals
            .Where(reveal => !IsOpened(reveal))
            .Select(reveal => (reveal, distance: PotTreasureFilter.Distance2D(reveal.Position, origin)))
            .Where(pair => pair.distance <= radius
                           && (pair.reveal.IsTargetable || pair.distance <= allowUntargetableWithin))
            .OrderBy(pair => pair.distance)
            .Select(pair => pair.reveal)
            .FirstOrDefault();
    }

    private IGameObject? FindChestNear(Vector3 position)
    {
        return chests
            .Where(chest => !IsOpened(chest))
            .Select(chest => (chest, distance: PotTreasureFilter.Distance2D(chest.Position, position)))
            .Where(pair => pair.distance <= ChestSearchRadius)
            .OrderBy(pair => pair.distance)
            .Select(pair => pair.chest)
            .FirstOrDefault();
    }

    private IGameObject? GetOpenTarget()
    {
        if (openTargetId is not { } id)
        {
            return null;
        }

        return reveals.Concat(chests).FirstOrDefault(obj => obj.GameObjectId == id);
    }

    private bool IsPadOpened(Vector3 pad)
    {
        return openedPositions.Any(position => PotTreasureFilter.Distance2D(position, pad) <= ChestSearchRadius);
    }

    private bool IsOpened(IGameObject obj)
    {
        return openedObjects.Contains(obj.GameObjectId) || IsLooted(obj);
    }

    private static unsafe bool IsLooted(IGameObject obj)
    {
        if (obj.ObjectKind == ObjectKind.Treasure)
        {
            var treasure = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)obj.Address;
            if (treasure != null
                && (treasure->Flags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags.Opened)
                    || treasure->Flags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags.FadedOut)))
            {
                return true;
            }
        }

        // Revealed pot coffers are event objects; only the loot window says
        // they were opened.
        var loot = Loot.Instance();
        if (loot == null)
        {
            return false;
        }

        foreach (var item in loot->Items)
        {
            if (item.ChestObjectId == obj.GameObjectId)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkOpened(IGameObject obj)
    {
        openedObjects.Add(obj.GameObjectId);
        openedPositions.Add(obj.Position);
    }

    private void MarkOpenedTarget()
    {
        if (GetOpenTarget() is { } target)
        {
            MarkOpened(target);
        }
        else if (watchTarget is { } position)
        {
            openedPositions.Add(position);
        }
    }

    // ---- Opening ---------------------------------------------------------------

    private enum OpenResult
    {
        Pending,
        Opened,
        Failed,
    }

    private void BeginOpening(IGameObject target)
    {
        openTargetId = target.GameObjectId;
        openStartedAt = DateTimeOffset.UtcNow;
        interactAttempted = false;
        ResetWatch();
        if (Phase is not PotChestFarmPhase.BlindSweep)
        {
            SetPhase(PotChestFarmPhase.OpeningReveal);
        }
    }

    private void ClearOpenTarget()
    {
        openTargetId = null;
        interactAttempted = false;
    }

    private OpenResult TryOpen(VNavmesh vnav, IGameObject target, DateTimeOffset now)
    {
        if (IsOpened(target))
        {
            StopMoving(vnav);
            MarkOpened(target);
            return OpenResult.Opened;
        }

        if (now - openStartedAt >= OpenTimeout)
        {
            Svc.Log.Info($"[Pots] Gave up opening the coffer at {target.Position}.");
            return OpenResult.Failed;
        }

        var player = Svc.Objects.LocalPlayer!;
        var distance = PotTreasureFilter.Distance2D(player.Position, target.Position);
        if (distance > InteractRadius)
        {
            MoveTo(vnav, target.Position, now, mount: distance > MountDistance, directWithin: 12f);
            if (distance > ArrivalRadius)
            {
                return OpenResult.Pending;
            }
        }
        else
        {
            StopMoving(vnav);
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            if (EzThrottler.Throttle("Pots.Dismount", 750))
            {
                BocchiActions.TryUnmount();
            }

            return OpenResult.Pending;
        }

        // Opening is a cast; interacting again would cancel it.
        if (player.IsCasting || !target.IsTargetable || !EzThrottler.Throttle("Pots.Interact", 500))
        {
            return OpenResult.Pending;
        }

        Interact(target);
        interactAttempted = true;
        return IsOpened(target) ? OpenResult.Opened : OpenResult.Pending;
    }

    private static unsafe void Interact(IGameObject target)
    {
        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null || target.Address == nint.Zero)
        {
            return;
        }

        targetSystem->InteractWithObject((GameObjectStruct*)target.Address, false);
    }

    // ---- Elixir --------------------------------------------------------------

    private void UseElixir(DateTimeOffset now)
    {
        if (!TryUseElixirItem())
        {
            return;
        }

        elixirAttempts++;
        lastElixirAt = now;
        settledAt = now;
        hintBaseline = hints.Revision;
        elixirHintOrigin = Svc.Objects.LocalPlayer?.Position;
        Svc.Log.Info($"[Pots] Used the Magical Elixir ({elixirAttempts}/{MaxElixirAttempts}).");
    }

    public static unsafe bool HasElixir()
    {
        return FindKeyItemSlot(out _) || GetInventoryCount() > 0;
    }

    private static unsafe int GetInventoryCount()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetInventoryItemCount(MagicalElixirItemId);
    }

    private static unsafe bool FindKeyItemSlot(out int slot)
    {
        slot = -1;
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return false;
        }

        var container = inventory->GetInventoryContainer(InventoryType.KeyItems);
        if (container == null)
        {
            return false;
        }

        for (var index = 0; index < container->Size; index++)
        {
            var item = container->GetInventorySlot(index);
            if (item != null && item->ItemId == MagicalElixirItemId && item->Quantity > 0)
            {
                slot = index;
                return true;
            }
        }

        return false;
    }

    private static unsafe bool TryUseElixirItem()
    {
        if (Svc.Condition[ConditionFlag.Mounted] || Player.IsCasting || !EzThrottler.Throttle("Pots.Elixir", 5500))
        {
            if (Svc.Condition[ConditionFlag.Mounted] && EzThrottler.Throttle("Pots.ElixirDismount", 750))
            {
                BocchiActions.TryUnmount();
            }

            return false;
        }

        var agent = AgentInventoryContext.Instance();
        if (agent == null)
        {
            return false;
        }

        if (FindKeyItemSlot(out var slot))
        {
            var result = agent->UseItem(MagicalElixirItemId, InventoryType.KeyItems, (uint)slot);
            if (result is 0 or 1)
            {
                return true;
            }
        }

        if (GetInventoryCount() > 0)
        {
            agent->UseItem(MagicalElixirItemId);
            return true;
        }

        return false;
    }

    // ---- Movement --------------------------------------------------------------

    private void MoveTo(VNavmesh vnav, Vector3 destination, DateTimeOffset now, bool mount = true, float directWithin = 0f)
    {
        var player = Svc.Objects.LocalPlayer!;
        var target = PathablePosition(destination, player.Position.Y);
        var distance = PotTreasureFilter.Distance2D(player.Position, target);
        if (distance <= InteractRadius)
        {
            StopMoving(vnav);
            return;
        }

        if (mount
            && distance > MountDistance
            && module.PluginConfig.TeleporterConfig.ShouldMount
            && !Svc.Condition[ConditionFlag.Mounted]
            && !Svc.Condition[ConditionFlag.InCombat]
            && !player.IsCasting)
        {
            if (Automator.IsChainActive)
            {
                return;
            }

            if (EzThrottler.Throttle("Pots.Mount", 3000))
            {
                StopMoving(vnav);
                Plugin.Chain.Submit(ChainHelper.MountChain(() => IsFinished || !module.IsEnabled));
                return;
            }
        }

        var moving = vnav.IsRunning() || vnav.IsPathfinding() || vnav.IsSimpleMoveInProgress();
        var sameDestination = lastPathDestination is { } previous && Vector3.Distance(previous, target) <= 2f;
        if (sameDestination && (moving || now - lastPathIssuedAt < TimeSpan.FromSeconds(2.5)))
        {
            return;
        }

        if (!EzThrottler.Throttle("Pots.Path", 750))
        {
            return;
        }

        lastPathDestination = target;
        lastPathIssuedAt = now;

        // Coffers can sit just off the navmesh; walk the last stretch straight.
        if (directWithin > 0f && distance <= directWithin && sameDestination && !moving)
        {
            vnav.FollowPath([player.Position, target], false);
            return;
        }

        var snapped = SnapToMesh(vnav, target);
        AggroAvoidanceNavigation.PathfindAndMoveTo(vnav, snapped, false);
    }

    private static Vector3 PathablePosition(Vector3 position, float playerY)
    {
        // Revealed coffers can report a bogus altitude around -500.
        return position.Y < -250f ? position with { Y = playerY } : position;
    }

    private static Vector3 SnapToMesh(VNavmesh vnav, Vector3 seed)
    {
        try
        {
            var onMesh = vnav.FindNearestPointOnMesh(seed, 8f, 30f);
            var floor = vnav.FindPointOnFloor(onMesh ?? seed, false, 8f) ?? onMesh;
            if (floor is { } point
                && PotTreasureFilter.Distance2D(point, seed) <= 12f
                && MathF.Abs(point.Y - seed.Y) <= 25f)
            {
                return point;
            }
        }
        catch (Exception exception)
        {
            Svc.Log.Debug($"[Pots] Navmesh snap failed: {exception.Message}");
        }

        return seed;
    }

    private void StopMoving(VNavmesh? vnav)
    {
        lastPathDestination = null;
        try
        {
            if (vnav != null && (vnav.IsRunning() || vnav.IsPathfinding() || vnav.IsSimpleMoveInProgress()))
            {
                AggroAvoidanceNavigation.Stop(vnav);
            }
        }
        catch (Exception exception)
        {
            Svc.Log.Debug($"[Pots] Could not stop vnavmesh: {exception.Message}");
        }
    }

    private bool IsStuck(Vector3 target, float distance, DateTimeOffset now)
    {
        if (watchTarget is not { } watched || Vector3.Distance(watched, target) > 2f)
        {
            watchTarget = target;
            watchBestDistance = distance;
            watchImprovedAt = now;
            watchIdleSince = null;
            return false;
        }

        if (distance < watchBestDistance - 1.5f)
        {
            watchBestDistance = distance;
            watchImprovedAt = now;
        }

        if (Automator.IsChainActive || Svc.Condition[ConditionFlag.Mounted] && Player.IsCasting)
        {
            watchImprovedAt = now;
            watchIdleSince = null;
            return false;
        }

        if (!Player.IsMoving)
        {
            watchIdleSince ??= now;
            if (now - watchIdleSince.Value >= IdleStuckWindow && now - watchImprovedAt >= IdleStuckWindow)
            {
                return true;
            }
        }
        else
        {
            watchIdleSince = null;
        }

        return now - watchImprovedAt >= StuckWindow;
    }

    private void ResetWatch()
    {
        watchTarget = null;
        watchBestDistance = float.MaxValue;
        watchIdleSince = null;
    }
}
