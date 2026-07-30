using BOCCHI.Enums;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Data;

public readonly record struct SupportJobLevelCandidate(
    byte RowId,
    byte Level,
    byte LevelMax);

public static class SupportJobLevelingPolicy
{
    public static bool ShouldKeepCurrent(JobId job, byte level, byte levelMax)
    {
        return level > 0
               && level < levelMax;
    }

    public static byte? SelectLowestIncomplete(IEnumerable<SupportJobLevelCandidate> candidates)
    {
        return candidates
            .Where(candidate =>
                candidate.Level > 0
                && candidate.Level < candidate.LevelMax)
            .OrderBy(candidate => candidate.Level)
            .ThenBy(candidate => candidate.RowId)
            .Select(candidate => (byte?)candidate.RowId)
            .FirstOrDefault();
    }
}
