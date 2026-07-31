using BOCCHI.Data;
using Ocelot.Config.Handlers;

namespace BOCCHI.Modules.MobFarmer;

public class MobProvider : EnumProvider<Mob>
{
    public override string GetLabel(Mob mob)
    {
        return MobData.GetName(mob);
    }
}

public sealed class SouthHornMobProvider : MobProvider
{
    public override bool Filter(Mob mob)
    {
        return MobData.IsSouthHornMob(mob);
    }
}

public sealed class NorthHornMobProvider : MobProvider
{
    public override bool Filter(Mob mob)
    {
        return MobData.IsNorthHornMob(mob);
    }
}
