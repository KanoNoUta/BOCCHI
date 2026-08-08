using BOCCHI.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Data.Traps;

public static partial class TrapData
{
    // CN 7.55 ARR ObjectSpawn union from three Grand Magic Tower clears.
    // These are potential positions, not a claim that every point is active in
    // the current layout. Runtime EventObj discovery remains the source of
    // truth for the 7m/30m danger ranges.
    private static List<TrapGroup> GrandMagic { get; } =
    [
        ..CreateSingletonGroups(OccultObjectType.Trap,
        [
            // Entry and first split.
            new(596.500f, -700.000f, 957.000f),
            new(603.500f, -700.000f, 957.000f),
            new(631.500f, -699.941f, 929.500f),
            new(645.500f, -699.901f, 929.500f),
            new(638.500f, -700.001f, 922.500f),

            // Lower corridors and side rooms.
            new(730.500f, -680.000f, 728.500f),
            new(717.500f, -680.000f, 732.000f),
            new(723.500f, -680.000f, 735.500f),
            new(835.000f, -698.000f, 758.500f),
            new(603.500f, -684.000f, 776.000f),
            new(639.000f, -680.000f, 832.500f),
            new(678.500f, -680.000f, 861.000f),

            // Middle-floor cross and adjoining corridors.
            new(530.500f, -700.000f, 88.500f),
            new(537.500f, -700.000f, 88.500f),
            new(593.000f, -700.000f, 109.000f),
            new(607.000f, -700.000f, 109.000f),
            new(600.000f, -699.950f, 113.000f),
            new(592.000f, -699.950f, 116.000f),
            new(608.000f, -699.950f, 116.000f),
            new(615.000f, -700.000f, 117.000f),
            new(491.500f, -700.000f, 120.500f),
            new(498.500f, -700.000f, 120.500f),
            new(582.000f, -700.000f, 124.000f),
            new(589.000f, -699.950f, 124.000f),
            new(611.000f, -699.950f, 124.000f),
            new(618.000f, -700.000f, 124.000f),
            new(491.500f, -700.000f, 127.500f),
            new(560.000f, -700.000f, 127.500f),
            new(568.000f, -700.000f, 127.500f),
            new(615.000f, -700.000f, 131.000f),
            new(608.000f, -699.950f, 132.000f),
            new(593.000f, -700.000f, 139.000f),
            new(600.000f, -699.956f, 141.000f),
            new(530.500f, -700.000f, 159.500f),
            new(537.500f, -700.000f, 166.500f),

            // Upper locked-door area.
            new(-9.000f, -707.950f, -430.000f),
            new(4.000f, -707.950f, -430.000f),
            new(36.000f, -715.950f, -397.000f),
            new(27.000f, -715.950f, -394.000f),
            new(32.000f, -715.950f, -394.000f),
            new(36.000f, -715.950f, -385.000f),
        ]),

        ..CreateSingletonGroups(OccultObjectType.BigTrap,
        [
            // Lower corridors and side rooms.
            new(800.000f, -700.000f, 772.000f),
            new(807.000f, -700.000f, 772.000f),
            new(596.500f, -684.000f, 776.000f),
            new(723.500f, -680.000f, 780.500f),
            new(807.000f, -700.000f, 782.000f),
            new(482.500f, -680.000f, 784.000f),
            new(736.500f, -680.000f, 784.000f),

            // Middle-floor cross and adjoining corridors.
            new(634.500f, -700.000f, 117.000f),
            new(669.500f, -700.000f, 117.000f),
            new(677.500f, -700.000f, 117.000f),
            new(528.343f, -700.000f, 118.343f),
            new(634.500f, -700.000f, 124.000f),
            new(669.500f, -700.000f, 124.000f),
            new(528.343f, -700.000f, 129.657f),
            new(634.500f, -700.000f, 131.000f),
            new(677.500f, -700.000f, 131.000f),
        ]),
    ];

    private static IEnumerable<TrapGroup> CreateSingletonGroups(
        OccultObjectType type,
        IEnumerable<Vector3> positions)
    {
        return positions.Select(position => new TrapGroup([new TrapDatum(position, type)]));
    }
}
