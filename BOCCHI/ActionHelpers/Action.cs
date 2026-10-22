using BOCCHI.Chains;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Chain;
using System;

namespace BOCCHI.ActionHelpers;

public unsafe class Action(ActionType type, uint id)
{
    public ActionType Type => type;

    public uint Id => id;

    public float GetRecastTime()
    {
        var recast = ActionManager.Instance()->GetRecastTime(type, id);
        var elapsed = ActionManager.Instance()->GetRecastTimeElapsed(type, id);

        return recast - elapsed;
    }

    public bool CanCast()
    {
        return GetRecastTime() <= 0f;
    }

    public void Cast()
    {
        TryCast();
    }

    public bool TryCast()
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(type, id);
    }

    public void Cast(uint arg)
    {
        ActionManager.Instance()->UseAction(type, id, arg);
    }

    public bool TryCastOn(ulong targetId)
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(type, id, targetId);
    }

    // Callers abandon a cast chain after at most 15 seconds; a recast that
    // never clears must end the chain instead of leaving it polling forever.
    private const int CanCastTimeoutMs = 15_000;

    public Func<Chain> GetCastChain()
    {
        return () => CastOnChain(Chain.Create($"Action({type}, {id})").DisposeWhenFinished());
    }

    public Chain CastOnChain(Chain chain)
    {
        return chain
            .Then(new TaskManagerTask(() => CanCast(), new TaskManagerConfiguration
            {
                TimeLimitMS = CanCastTimeoutMs,
                AbortOnTimeout = true,
                ShowError = false,
            }))
            .Then(_ => Cast());
    }
}
