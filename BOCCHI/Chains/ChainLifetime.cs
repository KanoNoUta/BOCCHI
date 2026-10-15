using Ocelot.Chain;
using System;

namespace BOCCHI.Chains;

/// <summary>
/// Ocelot only disposes the chains a ChainQueue runs directly. A chain nested
/// through Then(...) keeps its own and its task manager's Framework.Update
/// handlers registered for the rest of the session, so every navigation,
/// Return and buff step used to leave two idle per-frame handlers behind.
/// Disposing a chain from its final callback keeps long automation sessions
/// from accumulating thousands of them. A disposed chain still reports
/// IsComplete(), so the parent waiting on it continues normally.
/// </summary>
public static class ChainLifetime
{
    public static Chain DisposeWhenFinished(this Chain chain)
    {
        return chain.OnFinally(chain.Dispose);
    }
}

/// <summary>A chain factory whose chains release their frame handlers when they finish.</summary>
public abstract class OwnedChainFactory : ChainFactory
{
    public override Func<Chain> Factory()
    {
        var name = GetType().Name;
        return () => Create(Chain.Create(name)).DisposeWhenFinished();
    }
}
