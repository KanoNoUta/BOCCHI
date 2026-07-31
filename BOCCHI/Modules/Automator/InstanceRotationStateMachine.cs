using BOCCHI.Data;
using System;

namespace BOCCHI.Modules.Automator;

public enum InstanceRotationState
{
    Idle,
    Monitoring,
    WaitingForExit,
    Cooldown,
    WaitingForEntry,
    Failed,
}

public enum InstanceRotationAction
{
    None,
    RequestExit,
    EnterSouthHorn,
    EnterNorthHorn,
}

public enum InstanceRotationReason
{
    None,
    StayTimeElapsed,
    PopulationLow,
}

public readonly record struct InstanceRotationInput(
    bool Enabled,
    uint TerritoryId,
    bool CanStart,
    TimeSpan StayDuration,
    bool PopulationLow,
    TimeSpan? InstanceTimeRemaining = null);

/// <summary>
/// Pure command-once state machine for unattended Occult Crescent instance rotation.
/// Game APIs and command dispatch deliberately live in InstanceRotationController so
/// timeout and transition behavior can be smoke-tested without a running game.
/// </summary>
public sealed class InstanceRotationStateMachine
{
    public static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan ReentryCooldown = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan EntryTimeout = TimeSpan.FromMinutes(2);

    public InstanceRotationState State { get; private set; } = InstanceRotationState.Idle;

    public InstanceRotationReason Reason { get; private set; } = InstanceRotationReason.None;

    public uint OriginalTerritoryId { get; private set; }

    public DateTimeOffset? IslandEnteredAt { get; private set; }

    public DateTimeOffset? Deadline { get; private set; }

    public string? FailureReason { get; private set; }

    public bool IsBusy => State is InstanceRotationState.WaitingForExit
        or InstanceRotationState.Cooldown
        or InstanceRotationState.WaitingForEntry;

    public InstanceRotationAction BeginEntryFromOutside(DateTimeOffset now, uint targetTerritoryId)
    {
        if (State != InstanceRotationState.Idle)
        {
            return InstanceRotationAction.None;
        }

        if (!ZoneData.IsOccultCrescentTerritory(targetTerritoryId))
        {
            return FailWithNoAction("invalid_entry_territory");
        }

        State = InstanceRotationState.WaitingForEntry;
        Reason = InstanceRotationReason.None;
        OriginalTerritoryId = targetTerritoryId;
        IslandEnteredAt = null;
        Deadline = now + EntryTimeout;
        FailureReason = null;

        return targetTerritoryId switch
        {
            ZoneData.SOUTHHORN => InstanceRotationAction.EnterSouthHorn,
            ZoneData.NORTHHORN => InstanceRotationAction.EnterNorthHorn,
            _ => FailWithNoAction("invalid_entry_territory"),
        };
    }

    public InstanceRotationAction Update(DateTimeOffset now, InstanceRotationInput input)
    {
        if (!input.Enabled)
        {
            Reset();
            return InstanceRotationAction.None;
        }

        switch (State)
        {
            case InstanceRotationState.Idle:
                if (ZoneData.IsOccultCrescentTerritory(input.TerritoryId))
                {
                    BeginMonitoring(input.TerritoryId, now);
                }

                return InstanceRotationAction.None;

            case InstanceRotationState.Monitoring:
                if (!ZoneData.IsOccultCrescentTerritory(input.TerritoryId))
                {
                    Reset();
                    return InstanceRotationAction.None;
                }

                if (input.TerritoryId != OriginalTerritoryId)
                {
                    BeginMonitoring(input.TerritoryId, now);
                    return InstanceRotationAction.None;
                }

                var stayTimeElapsed = input.InstanceTimeRemaining is { } remaining
                                      && InstanceDutyTimerPolicy.HasStayDurationElapsed(remaining, input.StayDuration);
                if (!input.CanStart || (!stayTimeElapsed && !input.PopulationLow))
                {
                    return InstanceRotationAction.None;
                }

                Reason = input.PopulationLow
                    ? InstanceRotationReason.PopulationLow
                    : InstanceRotationReason.StayTimeElapsed;
                State = InstanceRotationState.WaitingForExit;
                Deadline = now + ExitTimeout;
                return InstanceRotationAction.RequestExit;

            case InstanceRotationState.WaitingForExit:
                if (input.TerritoryId == OriginalTerritoryId)
                {
                    if (now >= Deadline)
                    {
                        Fail("exit_timeout");
                    }

                    return InstanceRotationAction.None;
                }

                if (ZoneData.IsOccultCrescentTerritory(input.TerritoryId))
                {
                    Fail("unexpected_occult_territory");
                    return InstanceRotationAction.None;
                }

                State = InstanceRotationState.Cooldown;
                Deadline = now + ReentryCooldown;
                return InstanceRotationAction.None;

            case InstanceRotationState.Cooldown:
                if (ZoneData.IsOccultCrescentTerritory(input.TerritoryId))
                {
                    if (input.TerritoryId == OriginalTerritoryId)
                    {
                        BeginMonitoring(input.TerritoryId, now);
                    }
                    else
                    {
                        Fail("unexpected_occult_territory");
                    }

                    return InstanceRotationAction.None;
                }

                if (now < Deadline)
                {
                    return InstanceRotationAction.None;
                }

                State = InstanceRotationState.WaitingForEntry;
                Deadline = now + EntryTimeout;
                return OriginalTerritoryId switch
                {
                    ZoneData.SOUTHHORN => InstanceRotationAction.EnterSouthHorn,
                    ZoneData.NORTHHORN => InstanceRotationAction.EnterNorthHorn,
                    _ => FailWithNoAction("unknown_original_territory"),
                };

            case InstanceRotationState.WaitingForEntry:
                if (input.TerritoryId == OriginalTerritoryId)
                {
                    BeginMonitoring(input.TerritoryId, now);
                    return InstanceRotationAction.None;
                }

                if (ZoneData.IsOccultCrescentTerritory(input.TerritoryId))
                {
                    Fail("unexpected_occult_territory");
                    return InstanceRotationAction.None;
                }

                if (now >= Deadline)
                {
                    Fail("entry_timeout");
                }

                return InstanceRotationAction.None;

            case InstanceRotationState.Failed:
            default:
                return InstanceRotationAction.None;
        }
    }

    public void Fail(string reason)
    {
        State = InstanceRotationState.Failed;
        FailureReason = reason;
        Deadline = null;
    }

    public void Reset()
    {
        State = InstanceRotationState.Idle;
        Reason = InstanceRotationReason.None;
        OriginalTerritoryId = 0;
        IslandEnteredAt = null;
        Deadline = null;
        FailureReason = null;
    }

    private void BeginMonitoring(uint territoryId, DateTimeOffset now)
    {
        State = InstanceRotationState.Monitoring;
        Reason = InstanceRotationReason.None;
        OriginalTerritoryId = territoryId;
        IslandEnteredAt = now;
        Deadline = null;
        FailureReason = null;
    }

    private InstanceRotationAction FailWithNoAction(string reason)
    {
        Fail(reason);
        return InstanceRotationAction.None;
    }
}
