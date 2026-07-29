namespace BOCCHI.Modules.Automator;

public enum ActivityState
{
    Idle,
    PlanningRoute,
    Pathfinding,
    WaitingToStartCriticalEncounter,
    Participating,
    Done,
}

public static class ActivityStateExtensions
{
    public static string ToLabel(this ActivityState state)
    {
        return state switch
        {
            ActivityState.Idle => "Idle",
            ActivityState.PlanningRoute => "Planning Route",
            ActivityState.Pathfinding => "Pathfinding",
            ActivityState.WaitingToStartCriticalEncounter => "Waiting to Start (CE)",
            ActivityState.Participating => "Participating",
            ActivityState.Done => "Done",
            _ => "Unknown",
        };
    }
}
