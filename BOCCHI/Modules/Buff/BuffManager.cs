using BOCCHI.Data;
using BOCCHI.Modules.Buff.Chains;
using ECommons.GameHelpers;
using Ocelot.Chain;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Buff;

public class BuffManager
{
    private bool applyBuffsOnNextTick = false;

    public void QueueBuffs()
    {
        applyBuffsOnNextTick = true;
    }

    public bool IsQueued()
    {
        return applyBuffsOnNextTick;
    }

    public void Update(BuffModule module)
    {
        if (applyBuffsOnNextTick)
        {
            applyBuffsOnNextTick = false;
            ApplyBuffs(module);
        }
    }

    public void CancelPending()
    {
        applyBuffsOnNextTick = false;
    }

    public void ApplyBuffs(BuffModule module)
    {
        var manager = ChainManager.Get("OCH##BuffManager");
        if (manager.IsRunning)
        {
            return;
        }

        manager.Submit(new AllBuffsChain(module));
    }

    private static IEnumerable<PlayerStatus> GetRequestedBuffs(BuffModule module)
    {
        List<PlayerStatus> buffs = [];

        if (module.Config.ApplyEnduringFortitude)
        {
            buffs.Add(PlayerStatus.EnduringFortitude);
        }

        if (module.Config.ApplyFleetfooted)
        {
            buffs.Add(PlayerStatus.Fleetfooted);
        }

        if (module.Config.ApplyRomeosBallad)
        {
            buffs.Add(PlayerStatus.RomeosBallad);
        }

        if (module.Config.ApplyQuickerStep)
        {
            buffs.Add(PlayerStatus.QuickerStep);
        }
        if (module.Config.UseInquiringMind && !module.Config.ApplyQuickerStep)
        {
            buffs.Add(PlayerStatus.QuickerStep);
        }

        return buffs.Distinct();
    }

    public bool NeedsRefresh(BuffModule module, PlayerStatus buff)
    {
        var status = Player.Status.Get(buff);
        return status == null
               || status.RemainingTime <= module.Config.ReapplyThreshold * 60;
    }

    public bool ShouldRefresh(BuffModule module)
    {
        if (!module.IsEnabled)
        {
            return false;
        }

        return GetRequestedBuffs(module).Any(buff => NeedsRefresh(module, buff));
    }
}
