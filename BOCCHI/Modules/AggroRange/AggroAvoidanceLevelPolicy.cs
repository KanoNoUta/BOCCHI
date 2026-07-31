namespace BOCCHI.Modules.AggroRange;

/// <summary>
/// Filters ordinary-mob avoidance by the Occult Crescent level stored in
/// BattleChara.ForayInfo. Unknown levels stay conservative until game data is
/// available; a confirmed lower-level mob can no longer affect route planning.
/// </summary>
public static class AggroAvoidanceLevelPolicy
{
    public static bool ShouldAvoid(uint playerLevel, uint mobLevel)
    {
        if (playerLevel == 0 || mobLevel == 0)
        {
            return true;
        }

        return mobLevel >= playerLevel;
    }
}
