namespace BOCCHI.Modules.Automator;

public enum AutomatorRunState
{
    Stopped,
    Starting,
    Running,
    Stopping,
}

public enum AutomatorRunAction
{
    None,
    BeginStart,
    BeginStop,
}

public enum AutomatorStartReadiness
{
    Ready,
    VnavmeshUnavailable,
    LifestreamUnavailable,
}

public static class AutomatorStartPolicy
{
    public static AutomatorStartReadiness Evaluate(bool vnavmeshAvailable, bool lifestreamLoaded)
    {
        if (!vnavmeshAvailable)
        {
            return AutomatorStartReadiness.VnavmeshUnavailable;
        }

        return lifestreamLoaded
            ? AutomatorStartReadiness.Ready
            : AutomatorStartReadiness.LifestreamUnavailable;
    }
}

public static class AutomatorStopPolicy
{
    // At 60 FPS this covers roughly 15 seconds, including a normal territory
    // transition where vnavmesh or Lifestream may briefly unregister IPC.
    public const int MaxAttempts = 900;

    public static bool ShouldRetry(bool providersStopped, int completedAttempts)
    {
        return !providersStopped && completedAttempts < MaxAttempts;
    }
}

public sealed class AutomatorRunStateMachine
{
    public AutomatorRunState State { get; private set; } = AutomatorRunState.Stopped;

    public string? Detail { get; private set; }

    public bool TargetEnabled => State is AutomatorRunState.Starting or AutomatorRunState.Running;

    public bool CanRunWork => State == AutomatorRunState.Running;

    public AutomatorRunAction RequestEnabled(bool enabled)
    {
        if (enabled)
        {
            if (State is AutomatorRunState.Starting
                or AutomatorRunState.Running
                or AutomatorRunState.Stopping)
            {
                return AutomatorRunAction.None;
            }

            State = AutomatorRunState.Starting;
            Detail = null;
            return AutomatorRunAction.BeginStart;
        }

        if (State is AutomatorRunState.Stopped or AutomatorRunState.Stopping)
        {
            return AutomatorRunAction.None;
        }

        State = AutomatorRunState.Stopping;
        Detail = null;
        return AutomatorRunAction.BeginStop;
    }

    public AutomatorRunAction RequestStopAll()
    {
        if (State == AutomatorRunState.Stopping)
        {
            return AutomatorRunAction.None;
        }

        State = AutomatorRunState.Stopping;
        Detail = null;
        return AutomatorRunAction.BeginStop;
    }

    public void SetStartingDetail(string? detail)
    {
        if (State == AutomatorRunState.Starting)
        {
            Detail = detail;
        }
    }

    public void CompleteStart()
    {
        if (State == AutomatorRunState.Starting)
        {
            State = AutomatorRunState.Running;
            Detail = null;
        }
    }

    public void FailStart(string detail)
    {
        if (State == AutomatorRunState.Starting)
        {
            State = AutomatorRunState.Stopped;
            Detail = detail;
        }
    }

    public void CompleteStop()
    {
        if (State == AutomatorRunState.Stopping)
        {
            State = AutomatorRunState.Stopped;
            Detail = null;
        }
    }
}
