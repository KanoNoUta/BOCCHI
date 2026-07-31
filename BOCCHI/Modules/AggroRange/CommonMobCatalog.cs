using BOCCHI.Data;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.AggroRange;

/// <summary>
/// Trigger distance is measured from the edge of the monster hitbox. The
/// runtime hitbox radius is added separately so model scale is never mistaken
/// for an aggro-distance measurement.
/// </summary>
public readonly record struct CommonMobProfile(uint NameId, float FallbackEdgeRange);

public static class CommonMobCatalog
{
    // BossMod's comparable open-world avoidance implementations use an
    // 8.5-10y edge distance. Use the maximum until this exact BNpcName has a
    // clean local observation; this keeps both sight and sound mobs safe.
    public const float DefaultFallbackEdgeRange = 10f;

    private static readonly HashSet<uint> NorthHornCommonNameIds =
        [.. MobData.NorthHornMobs.Select(mob => (uint)mob)];

    public static int Count => NorthHornCommonNameIds.Count;

    public static bool TryGet(uint nameId, out CommonMobProfile profile)
    {
        if (!NorthHornCommonNameIds.Contains(nameId))
        {
            profile = default;
            return false;
        }

        profile = new CommonMobProfile(nameId, DefaultFallbackEdgeRange);
        return true;
    }
}
