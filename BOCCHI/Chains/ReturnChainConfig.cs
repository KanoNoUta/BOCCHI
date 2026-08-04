namespace BOCCHI.Chains;

using System;

public struct ReturnChainConfig()
{
    public bool ApproachAetheryte { get; init; } = true;

    public bool ForceReturn { get; init; } = false;

    /// <summary>
    /// Optional predicate evaluated by long-running chain steps. When it
    /// returns true (for example the automation mode was stopped), the step
    /// ends immediately so a nested return chain cannot keep walking after an
    /// emergency stop. Independent navigation callers leave it null and are
    /// unaffected.
    /// </summary>
    public Func<bool>? StopCheck { get; init; } = null;
}
