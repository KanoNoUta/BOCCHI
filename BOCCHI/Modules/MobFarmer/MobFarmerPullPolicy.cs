using BOCCHI.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer;

/// <summary>
/// Pull and pull-buff decisions, kept free of game types so the smoke tests
/// can check them. Ported from upstream BOCCHI 4.2's FarmerPullAssist and
/// Buffing handler.
/// </summary>
public static class MobFarmerPullPolicy
{
    /// <summary>Tanks open with a pull action within this range (yalms).</summary>
    public const float PullRange = 20f;

    public const uint ProvokeActionId = 7533;

    // Support job levels the pull buffs unlock at (upstream's defaults).
    public const byte QuickstepUnlockLevel = 2;

    public const byte RingingRespiteUnlockLevel = 3;

    public const byte CounterstanceUnlockLevel = 3;

    public static bool IsSpecialMob(uint nameId)
    {
        return MobData.MobsWithSpawnCondition.Contains((Mob)nameId);
    }

    public static int CountTowardMinimum(IEnumerable<uint> nameIds, bool countSpecials)
    {
        return countSpecials ? nameIds.Count() : nameIds.Count(id => !IsSpecialMob(id));
    }

    /// <summary>Gladiator, Marauder, Paladin, Warrior, Dark Knight and Gunbreaker.</summary>
    public static bool IsTank(uint classJobId)
    {
        return classJobId is 1 or 3 or 19 or 21 or 32 or 37;
    }

    public static uint? GetRangedPullActionId(uint classJobId)
    {
        return classJobId switch
        {
            1 or 19 => 24, // Shield Lob
            3 or 21 => 46, // Tomahawk
            32 => 3624, // Unmend
            37 => 16143, // Lightning Shot
            _ => null,
        };
    }

    public static uint? GetGapCloserActionId(uint classJobId)
    {
        return classJobId switch
        {
            19 => 16461, // Intervene
            21 => 7386, // Onslaught
            32 => 36926, // Shadowstride
            37 => 36934, // Trajectory
            _ => null,
        };
    }

    public static bool ShouldSkipQuickstep(int skipIfRemainingMinutes, float quickerStepRemainingSeconds)
    {
        return skipIfRemainingMinutes > 0
               && quickerStepRemainingSeconds >= skipIfRemainingMinutes * 60f;
    }

    /// <summary>0 means gathering and stacking have no time limit.</summary>
    public static bool IsStackingTimedOut(float timeoutSeconds, TimeSpan elapsed)
    {
        return timeoutSeconds > 0f && elapsed >= TimeSpan.FromSeconds(timeoutSeconds);
    }
}
