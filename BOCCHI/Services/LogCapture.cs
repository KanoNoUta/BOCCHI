using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BOCCHI.Services;

public readonly record struct CapturedLog(DateTime Time, LogEventLevel Level, string Message);

/// <summary>The last few thousand BOCCHI log lines, kept in memory for the log window.</summary>
public static class LogBuffer
{
    public const int Capacity = 3000;

    private static readonly LinkedList<CapturedLog> Entries = new();

    private static readonly object Gate = new();

    public static void Append(LogEventLevel level, string message)
    {
        lock (Gate)
        {
            Entries.AddLast(new CapturedLog(DateTime.Now, level, message));
            while (Entries.Count > Capacity)
            {
                Entries.RemoveFirst();
            }
        }
    }

    public static IReadOnlyList<CapturedLog> Snapshot()
    {
        lock (Gate)
        {
            return Entries.ToList();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
        }
    }

    public static string Format(CapturedLog entry)
    {
        return $"{entry.Time:HH:mm:ss.fff} [{entry.Level}] {entry.Message}";
    }

    /// <summary>Fills "{Name}" holes the way Serilog would, for display only.</summary>
    public static string Render(string template, object?[] values)
    {
        if (values.Length == 0)
        {
            return template;
        }

        try
        {
            var index = 0;
            var converted = Regex.Replace(template, @"\{[^{}]+\}", _ => "{" + index++ + "}");
            return string.Format(CultureInfo.InvariantCulture, converted, values);
        }
        catch (FormatException)
        {
            return template + " | " + string.Join(", ", values.Select(value => value?.ToString() ?? "null"));
        }
    }
}

/// <summary>
/// Wraps Dalamud's plugin log so everything BOCCHI writes through
/// <see cref="Svc.Log"/> is also kept for the log window. Ported from upstream
/// BOCCHI 4.2's CapturingPluginLog.
/// </summary>
public sealed class CapturingPluginLog(IPluginLog inner) : IPluginLog
{
    public IPluginLog Inner => inner;

    /// <summary>Swaps <see cref="Svc.Log"/> for a capturing wrapper; does nothing if that is not possible.</summary>
    public static void Install()
    {
        try
        {
            if (Svc.Log is CapturingPluginLog || Svc.Log == null)
            {
                return;
            }

            typeof(Svc).GetProperty(nameof(Svc.Log))!.SetValue(null, new CapturingPluginLog(Svc.Log));
        }
        catch (Exception exception)
        {
            Svc.Log?.Warning($"[Logs] Could not capture logs for the log window: {exception.Message}");
        }
    }

    public static void Uninstall()
    {
        try
        {
            if (Svc.Log is CapturingPluginLog capturing)
            {
                typeof(Svc).GetProperty(nameof(Svc.Log))!.SetValue(null, capturing.Inner);
            }
        }
        catch (Exception)
        {
            // Unloading anyway.
        }
    }

    public ILogger Logger => inner.Logger;

    public LogEventLevel MinimumLogLevel
    {
        get => inner.MinimumLogLevel;
        set => inner.MinimumLogLevel = value;
    }

    private static void Capture(LogEventLevel level, Exception? exception, string template, object[] values)
    {
        var message = LogBuffer.Render(template, values);
        if (exception != null)
        {
            message = $"{message} :: {exception.GetType().Name}: {exception.Message}";
        }

        LogBuffer.Append(level, message);
    }

    public void Fatal(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Fatal, null, messageTemplate, values);
        inner.Fatal(messageTemplate, values);
    }

    public void Fatal(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Fatal, exception, messageTemplate, values);
        inner.Fatal(exception, messageTemplate, values);
    }

    public void Error(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Error, null, messageTemplate, values);
        inner.Error(messageTemplate, values);
    }

    public void Error(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Error, exception, messageTemplate, values);
        inner.Error(exception, messageTemplate, values);
    }

    public void Warning(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Warning, null, messageTemplate, values);
        inner.Warning(messageTemplate, values);
    }

    public void Warning(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Warning, exception, messageTemplate, values);
        inner.Warning(exception, messageTemplate, values);
    }

    public void Information(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Information, null, messageTemplate, values);
        inner.Information(messageTemplate, values);
    }

    public void Information(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Information, exception, messageTemplate, values);
        inner.Information(exception, messageTemplate, values);
    }

    public void Info(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Information, null, messageTemplate, values);
        inner.Info(messageTemplate, values);
    }

    public void Info(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Information, exception, messageTemplate, values);
        inner.Info(exception, messageTemplate, values);
    }

    public void Debug(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Debug, null, messageTemplate, values);
        inner.Debug(messageTemplate, values);
    }

    public void Debug(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Debug, exception, messageTemplate, values);
        inner.Debug(exception, messageTemplate, values);
    }

    public void Verbose(string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Verbose, null, messageTemplate, values);
        inner.Verbose(messageTemplate, values);
    }

    public void Verbose(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(LogEventLevel.Verbose, exception, messageTemplate, values);
        inner.Verbose(exception, messageTemplate, values);
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(level, exception, messageTemplate, values);
        inner.Write(level, exception, messageTemplate, values);
    }
}
