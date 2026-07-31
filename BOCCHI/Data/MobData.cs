using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BOCCHI.Data;

public static class MobData
{
    private static Dictionary<Mob, string> NameCache = [];

    public static IReadOnlyList<Mob> SouthHornMobs { get; } =
        System.Enum.GetValues<Mob>().Where(IsSouthHornMob).ToArray();

    public static IReadOnlyList<Mob> NorthHornMobs { get; } =
        System.Enum.GetValues<Mob>().Where(IsNorthHornMob).ToArray();

    public static List<Mob> MobsWithSpawnCondition { get; private set; } =
    [
        Mob.Armor,
        Mob.Bomb,
        Mob.Caoineag,
        Mob.Dhruva,
        Mob.Dullahan,
        Mob.Fool,
        Mob.Geshunpest,
        Mob.Ghost,
        Mob.Gourmand,
        Mob.Mimic,
        Mob.Mousse,
        Mob.Troubadour,
    ];

    public static string GetName(Mob mob)
    {
        if (NameCache.TryGetValue(mob, out var name))
        {
            return name;
        }

        if (Svc.Data.GetExcelSheet<BNpcName>().TryGetRow((uint)mob, out var row))
        {
            var titleCase = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(row.Singular.ToString().ToLower());
            NameCache[mob] = titleCase;
            return titleCase;
        }

        return mob.ToString();
    }

    public static bool IsSouthHornMob(Mob mob)
    {
        return !IsNorthHornMob(mob);
    }

    public static bool IsNorthHornMob(Mob mob)
    {
        return (uint)mob >= (uint)Mob.CrescentCliffkite;
    }
}
