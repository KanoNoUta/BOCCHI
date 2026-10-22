using BOCCHI.Data;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator.Pots;

/// <summary>
/// Magic pot FATE centres and the coffer pads each pot can reveal. The data is
/// from upstream BOCCHI 4.2.0.18 (OhKannaDuh/BOCCHI); the pads are the spots a
/// pot coffer was seen at, not live objects.
/// </summary>
public static class PotZoneData
{
    public sealed record PotFate(uint Id, uint TerritoryId, Vector3 Center);

    public static readonly IReadOnlyList<PotFate> PotFates =
    [
        new(1976, ZoneData.SOUTHHORN, new(200f, 111.73f, -215f)),
        new(1977, ZoneData.SOUTHHORN, new(-481f, 75f, 528f)),
        new(2072, ZoneData.NORTHHORN, new(233f, 7.729229f, -470f)),
        new(2073, ZoneData.NORTHHORN, new(-505.2822f, 53.14409f, 244.041f)),
    ];

    private static readonly Dictionary<uint, Vector3[]> PrimaryPads = new()
    {
        [1976] =
        [
            new(571.5841f, 51.451305f, -813.1642f),
            new(662.4388f, 120f, 161.1339f),
            new(606.4641f, 108.07402f, 184.8517f),
            new(-312.2778f, 103.19944f, -35.25348f),
            new(587.7039f, 78.8956f, -545.8168f),
            new(891.2597f, 120f, -20.672f),
            new(878.1131f, 108.28959f, -91.1057f),
            new(803.6609f, 95.99998f, -354.1809f),
            new(341.4413f, 95.99999f, 194.7507f),
            new(570.2421f, 64.66201f, 272.1734f),
            new(-216.372f, 5.4469404f, -510.1361f),
            new(684.4223f, 96.10129f, -165.4811f),
            new(-188.1745f, 2.999999f, -717.2005f),
            new(-476.3011f, 101.44228f, -86.69939f),
            new(80.19762f, 101.27949f, 391.2263f),
            new(-534.6993f, 2.999998f, -651.6244f),
            new(-165.2374f, 95.33837f, 437.4505f),
            new(330.8659f, 6.7168036f, -654.5339f),
            new(-333.3444f, 2.9999998f, -861.1722f),
            new(-313.2906f, 108.10962f, 70.76207f),
            new(-459.1735f, 93.57443f, 5.054043f),
            new(-54.69518f, 99.40573f, 405.0261f),
            new(-382.4396f, 109.30187f, -378.3482f),
            new(263.2559f, 100.38499f, 326.6834f),
            new(224.7233f, 68.7328f, 518.668f),
            new(19.73968f, 26.045855f, -420.977f),
            new(705.2716f, 68.143616f, 358.6714f),
            new(-660.5336f, 98f, -216.7666f),
            new(-324.2736f, 121f, 203.2017f),
            new(-386.5904f, -0.13994062f, -461.0976f),
        ],
        [1977] =
        [
            new(-195.4419f, 110.15342f, -287.8911f),
            new(74.73397f, 110.494316f, -394.1289f),
            new(-386.437f, 98.60658f, -221.7847f),
            new(-554.6146f, 99.01769f, -309.1231f),
            new(107.0611f, 105.699875f, 146.7059f),
            new(825.9521f, 70f, 772.4054f),
            new(-836.7586f, 106.999985f, 597.2944f),
            new(67.45271f, 69.477974f, 745.8658f),
            new(45.0f, 111.56f, -225.0f),
            new(301.8741f, 103.784424f, 70.59854f),
            new(-38.97946f, 102.073296f, -175.4589f),
            new(-60.72729f, 69.687035f, 828.4997f),
            new(17.60418f, 65.93209f, 674.6207f),
            new(393.2685f, 57.545956f, 844.6924f),
            new(393.0191f, 104f, -124.1651f),
            new(-798.7886f, 84.22545f, -4.822005f),
            new(440.8355f, 70.3f, 876.4097f),
            new(-734.1434f, 170.99998f, 683.7238f),
            new(423.3505f, 70.3f, 578.9013f),
            new(200.1241f, 56f, 624.2285f),
            new(-603.3457f, 139f, 858.6771f),
            new(-829.598f, 62.66814f, 66.82948f),
            new(-645.3027f, 135.69208f, -73.54771f),
            new(-836.1612f, 107f, 770.2822f),
            new(-676.6202f, 128.57442f, 1.531581f),
            new(-713.6796f, 203f, 710.08f),
            new(781.2514f, 70f, 560.0701f),
            new(-746.1318f, 172.00023f, 828.8809f),
            new(-730.5441f, 107.694275f, -371.4776f),
            new(-810.8279f, 114.053925f, -226.8324f),
        ],
        [2072] =
        [
            new(927.0178f, 54f, -155.2175f),
            new(929.4178f, 54f, -1.817501f),
            new(939.2178f, 80.269966f, -273.1175f),
            new(912.2978f, 61.18964f, -461.5099f),
            new(385f, 33f, -177f),
            new(-536.1014f, 87.01824f, 149.8447f),
            new(830.0979f, 77.75924f, -148.9099f),
            new(-530f, 67.77658f, -58f),
            new(-251.781f, 65.949005f, -864.3828f),
            new(889.2178f, 53.999996f, 155.9825f),
            new(-596f, 41.869873f, -285f),
            new(-223.8233f, 10.891144f, -353.9438f),
            new(-190f, 61.75258f, -763f),
            new(-498.7f, 11.051006f, 128.9f),
            new(-86f, 60.596237f, -737f),
            new(32.4f, 56.835186f, -777.3f),
            new(948.5978f, 63.594563f, -567.0099f),
            new(-252.1626f, 66.55432f, -879.5855f),
            new(546.56f, 36.120197f, 143.3104f),
            new(321.198f, 59.85f, -889.8872f),
            new(928.8978f, 74.0003f, -332.8099f),
            new(593f, 39.622505f, 34f),
            new(782.4979f, 70.34123f, -56.4099f),
            new(810.8979f, 78.39757f, -278.8099f),
            new(1.768392f, 71.555756f, -872.2798f),
            new(440.298f, 60.615795f, -926.5872f),
            new(452.6f, 57.10005f, -310.3f),
            new(151.9998f, 61.106945f, -842.0175f),
            new(714.698f, 69.24771f, 262.6901f),
            new(-455.989f, 39.688915f, -365.5418f),
        ],
        [2073] =
        [
            new(-113.4943f, 5.0879984f, -74.15943f),
            new(-960f, 48f, -425.8f),
            new(-834f, 18.913685f, -569f),
            new(-853.493f, 58f, -323.8983f),
            new(-586.3f, 47.81013f, -715.2f),
            new(71.10001f, 81.074875f, 942.3f),
            new(93.4f, 3.7155468f, -114.3f),
            new(210f, 98.400055f, 916f),
            new(28.10088f, 3.9999995f, -16.69861f),
            new(0.9425046f, 41.80327f, 623.2599f),
            new(-628.4385f, 49.07533f, -449.5009f),
            new(11.98766f, 68.15505f, 795.707f),
            new(-339.8588f, 85.47024f, 861.5197f),
            new(-88.43135f, 2.400001f, 4.891054f),
            new(-127f, 71.47446f, 808.4f),
            new(-184.5137f, 71.1816f, 667.8036f),
            new(52f, 25.316154f, 552f),
            new(-109.5452f, 8.047999f, -210.1855f),
            new(194.2296f, -0.3000001f, 352.9844f),
            new(-330f, 42f, -628f),
            new(190.3622f, 3.880325f, -204.7095f),
            new(237.9156f, -0.29999995f, 309.4334f),
            new(-512f, 41.999996f, -389f),
            new(-975.4507f, 17.57744f, -526.2878f),
            new(47.6f, 3.8843424f, -218.3f),
            new(-269.6122f, 107.93719f, 875.6997f),
            new(-15.89468f, 4.0000005f, -20.29277f),
            new(-769.25f, 28.97f, -504.51f),
            new(-259.6f, 3.6823246f, 56.9f),
            new(-184f, 53.15f, 91f),
            new(-172.6f, 53.15f, 103.2f),
            new(-779f, 160f, 686f),
        ],
    };

    // Second-chance coffers. Both pots of a zone share one list.
    private static readonly Dictionary<uint, Vector3[]> RerollPads = new()
    {
        [ZoneData.SOUTHHORN] =
        [
            new(-676.4631f, 5f, -769.7955f),
            new(-823.9183f, 140.00032f, 677.6934f),
            new(-886.4718f, 107f, 712.4964f),
            new(-625.7809f, 171f, 810.8691f),
            new(-813.9943f, 5f, -663.3634f),
            new(-842.8967f, 75.76903f, -125.0559f),
            new(-680.0345f, 201f, 739.9117f),
            new(-793.0552f, 5f, -777.3126f),
            new(-708.6777f, 171f, 669.5714f),
            new(-718.0424f, 5f, -633.8791f),
            new(-868.8489f, 67.5054f, -59.44909f),
            new(-803.5182f, 3f, -602.7497f),
            new(-732.2048f, 139f, 828.8491f),
            new(-659.1158f, 12.198493f, -508.7968f),
            new(-785.997f, 162.39513f, 790.5948f),
            new(-840.8771f, 107.26465f, -250.273f),
            new(-708.687f, 141.16982f, -139.3283f),
            new(-796.66f, 114.15647f, -228.9318f),
            new(-776.6315f, 5f, -486.978f),
            new(-758.8058f, 127.66496f, -183.164f),
        ],
        [ZoneData.NORTHHORN] =
        [
            new(782.8808f, 60.390976f, -611.7695f),
            new(925.6533f, 70.21527f, -906.2195f),
            new(909f, 97.05797f, -961.8f),
            new(-661f, 160f, 937f),
            new(-527f, 160.1012f, 834f),
            new(-631.9453f, 160f, 808.8979f),
            new(-809f, 6.3495464f, -879f),
            new(671.2f, 60.99496f, -550.1f),
            new(701f, 59.999992f, -945f),
            new(-623f, 160f, 883f),
            new(-585f, 160f, 842f),
            new(-656.9f, 23.036425f, -799.3f),
            new(-839.9977f, 160f, 740f),
            new(-779f, 160f, 686f),
            new(-487.8f, 48.000015f, -953.2f),
            new(-603f, 32f, -869f),
            new(-637.2283f, 32f, -950.4841f),
            new(-866f, -41.01304f, -775f),
            new(626.3f, 61.119125f, -844.9f),
            new(943.4631f, 70.21487f, -879.5159f),
            new(-449.6f, 45.6567f, -967.0001f),
        ],
    };

    public static bool IsPotFate(uint fateId)
    {
        return PotFates.Any(pot => pot.Id == fateId);
    }

    public static IEnumerable<PotFate> GetPotFates(uint territoryId)
    {
        return PotFates.Where(pot => pot.TerritoryId == territoryId);
    }

    public static bool TryGetPotFate(uint fateId, out PotFate pot)
    {
        pot = PotFates.FirstOrDefault(candidate => candidate.Id == fateId)!;
        return pot != null;
    }

    /// <summary>The zone's other pot, which spawns 30 minutes after this one.</summary>
    public static uint GetOppositePot(uint fateId)
    {
        if (!TryGetPotFate(fateId, out var pot))
        {
            return 0;
        }

        return GetPotFates(pot.TerritoryId).FirstOrDefault(other => other.Id != fateId)?.Id ?? 0;
    }

    public static IReadOnlyList<Vector3> GetPrimaryPads(uint fateId)
    {
        return PrimaryPads.TryGetValue(fateId, out var pads) ? pads : [];
    }

    public static IReadOnlyList<Vector3> GetRerollPads(uint territoryId)
    {
        return RerollPads.TryGetValue(territoryId, out var pads) ? pads : [];
    }
}
