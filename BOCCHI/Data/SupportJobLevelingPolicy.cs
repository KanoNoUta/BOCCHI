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
    private const byte FreelancerRowId = (byte)JobId.Freelancer;

    public static bool ShouldKeepCurrent(JobId job, byte level, byte levelMax)
    {
        return job != JobId.Freelancer
               && level > 0
               && level < levelMax;
    }

    public static byte? SelectLowestIncomplete(IEnumerable<SupportJobLevelCandidate> candidates)
    {
        return candidates
            .Where(candidate =>
                candidate.RowId != FreelancerRowId
                && candidate.Level > 0
                && candidate.Level < candidate.LevelMax)
            .OrderBy(candidate => candidate.Level)
            .ThenBy(candidate => candidate.RowId)
            .Select(candidate => (byte?)candidate.RowId)
            .FirstOrDefault();
    }
}
