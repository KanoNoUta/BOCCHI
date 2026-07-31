using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BOCCHI.Modules.Automator;

public static partial class InstanceDutyTimerPolicy
{
    public static readonly TimeSpan OccultCrescentDuration = TimeSpan.FromMinutes(180);

    [GeneratedRegex(@"(?<!\d)(\d{1,3}):([0-5]\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TimerPattern();

    public static bool TryParse(string? text, out TimeSpan remaining)
    {
        remaining = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = TimerPattern().Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        remaining = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return remaining <= OccultCrescentDuration;
    }

    public static bool HasStayDurationElapsed(TimeSpan remaining, TimeSpan stayDuration)
    {
        if (remaining < TimeSpan.Zero || remaining > OccultCrescentDuration)
        {
            return false;
        }

        var elapsed = OccultCrescentDuration - remaining;
        return elapsed >= stayDuration;
    }
}

/// <summary>
/// Reads the duty countdown rendered by the game's actual Duty Information HUD.
/// AddonToDoList exposes the timer node directly, so this does not depend on a
/// fragile guessed node ID or on when BOCCHI itself was enabled.
/// </summary>
public sealed class InstanceDutyTimerProvider
{
    public const string AddonName = "_ToDoList";

    public TimeSpan? CurrentRemaining { get; private set; }

    public string? CurrentText { get; private set; }

    public unsafe void Update()
    {
        CurrentRemaining = null;
        CurrentText = null;

        var addon = Svc.GameGui.GetAddonByName<AddonToDoList>(AddonName);
        if (addon == null || addon->DutyTimerTextNode == null)
        {
            return;
        }

        CurrentText = addon->DutyTimerTextNode->NodeText.ToString();
        if (InstanceDutyTimerPolicy.TryParse(CurrentText, out var remaining))
        {
            CurrentRemaining = remaining;
        }
    }

    public void Reset()
    {
        CurrentRemaining = null;
        CurrentText = null;
    }
}
