using Dalamud.Game.Chat;
using ECommons.DalamudServices;
using System;
using System.Numerics;

namespace BOCCHI.Modules.Automator.Pots;

public enum PotLogEventKind
{
    CofferReveal,
    Hint,
    BonusOffer,
}

public readonly record struct PotLogEvent(
    PotLogEventKind Kind,
    int Revision,
    PotHintDirection Direction,
    PotHintDistance Distance,
    Vector3? Origin);

/// <summary>
/// Reads the pot's messages by their LogMessage row, so the Chinese client
/// needs no text matching. Row ids are from upstream BOCCHI 4.2.
/// </summary>
public sealed class PotHintTracker : IDisposable
{
    public const uint CofferRevealLogMessageId = 10985;

    public const uint HintImmediateLogMessageId = 10986;

    public const uint HintCloseLogMessageId = 10987;

    public const uint HintFarLogMessageId = 10988;

    public const uint HintBeyondFarLogMessageId = 10989;

    public const uint BonusOfferLogMessageId = 10994;

    private bool armed;

    private bool subscribed;

    private PotLogEvent? last;

    public int Revision { get; private set; }

    public void Start()
    {
        if (subscribed)
        {
            return;
        }

        Svc.Chat.LogMessage += OnLogMessage;
        subscribed = true;
    }

    public void Dispose()
    {
        if (!subscribed)
        {
            return;
        }

        Svc.Chat.LogMessage -= OnLogMessage;
        subscribed = false;
    }

    public void Arm()
    {
        armed = true;
        Revision = 0;
        last = null;
    }

    public void Disarm()
    {
        armed = false;
        last = null;
    }

    public bool TryGetEventSince(int baseline, out PotLogEvent value)
    {
        if (last is { } current && current.Revision > baseline)
        {
            value = current;
            return true;
        }

        value = default;
        return false;
    }

    public static PotLogEventKind? Classify(uint logMessageId, out PotHintDistance distance)
    {
        distance = logMessageId switch
        {
            HintImmediateLogMessageId => PotHintDistance.Immediate,
            HintCloseLogMessageId => PotHintDistance.Close,
            HintFarLogMessageId => PotHintDistance.Far,
            HintBeyondFarLogMessageId => PotHintDistance.BeyondFar,
            _ => PotHintDistance.Unknown,
        };

        return logMessageId switch
        {
            CofferRevealLogMessageId => PotLogEventKind.CofferReveal,
            BonusOfferLogMessageId => PotLogEventKind.BonusOffer,
            >= HintImmediateLogMessageId and <= HintBeyondFarLogMessageId => PotLogEventKind.Hint,
            _ => null,
        };
    }

    private void OnLogMessage(ILogMessage message)
    {
        if (!armed)
        {
            return;
        }

        try
        {
            if (Classify(message.LogMessageId, out var distance) is not { } kind)
            {
                return;
            }

            var direction = PotHintDirection.Unknown;
            if (kind == PotLogEventKind.Hint)
            {
                if (!message.TryGetIntParameter(0, out var value))
                {
                    return;
                }

                direction = PotTreasureFilter.DirectionFromParameter(value);
                if (direction == PotHintDirection.Unknown)
                {
                    return;
                }
            }

            Revision++;
            last = new PotLogEvent(kind, Revision, direction, distance, Svc.Objects.LocalPlayer?.Position);
            Svc.Log.Info($"[Pots] Log message {message.LogMessageId}: {kind} {direction} {distance}");
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, "[Pots] Could not read a pot log message.");
        }
    }
}
