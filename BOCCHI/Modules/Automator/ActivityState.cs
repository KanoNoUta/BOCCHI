namespace BOCCHI.Modules.Automator;

public enum ActivityState
{
    Idle,
    Pathfinding,
    WaitingToStartCriticalEncounter,
    Participating,
    WaitingForPot,
    Done,
}

public static class ActivityStateExtensions
{
    public static string ToTranslationKey(this ActivityState state)
    {
        return state switch
        {
            ActivityState.Idle => "idle",
            ActivityState.Pathfinding => "pathfinding",
            ActivityState.WaitingToStartCriticalEncounter => "waiting_to_start_ce",
            ActivityState.Participating => "participating",
            ActivityState.WaitingForPot => "waiting_for_pot",
            ActivityState.Done => "done",
            _ => "unknown",
        };
    }
}
