namespace BOCCHI.Chains;

using System;

public struct ReturnChainConfig()
{
    public bool ApproachAetheryte { get; init; } = true;

    public bool ForceReturn { get; init; } = false;

    /// <summary>
    /// Automation returns only need to reach the base-camp aethernet. When the
    /// player already stands in the Return landing area, casting Return again
    /// costs a cast and a loading screen for nothing, so walk to the aethernet
    /// instead. Explicit user Returns leave this off and always cast.
    /// </summary>
    public bool SkipReturnAtLanding { get; init; } = false;

    /// <summary>
    /// Optional predicate evaluated by long-running chain steps. When it
    /// returns true (for example the automation mode was stopped), the step
    /// ends immediately so a nested return chain cannot keep walking after an
    /// emergency stop. Independent navigation callers leave it null and are
    /// unaffected.
    /// </summary>
    public Func<bool>? StopCheck { get; init; } = null;
}
