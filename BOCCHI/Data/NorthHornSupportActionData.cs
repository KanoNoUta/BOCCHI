using BOCCHI.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Data;

/// <summary>
/// CN 7.55 MKDSupportJob rows 16-23.  ActionRowId is the Action sheet row;
/// GeneralActionId is the slot passed to ActionManager for the active support
/// job.  Keeping both prevents Action-sheet IDs from being confused with the
/// support hotbar slots.
/// </summary>
public sealed record SupportJobActionDatum(
    uint ActionRowId,
    uint GeneralActionId,
    byte UnlockLevel,
    string ChineseName);

public sealed record SupportJobActionSet(
    uint SupportJobRowId,
    JobId Job,
    IReadOnlyList<SupportJobActionDatum> Actions);

public static class NorthHornSupportActionData
{
    private static readonly IReadOnlyDictionary<JobId, SupportJobActionSet> Sets =
        new[]
        {
            Set(16, JobId.Ninja,
                A(49062, 31, 1, "风魔手里剑"), A(49063, 32, 2, "烟雾弹"),
                A(49064, 33, 3, "雷遁之术"), A(49065, 34, 4, "火遁之术"), A(49066, 35, 6, "分身")),
            Set(17, JobId.WhiteMage,
                A(49067, 31, 1, "魔救疗"), A(49068, 32, 2, "魔愈疗"),
                A(49069, 33, 3, "魔闪躲"), A(49070, 34, 4, "魔复活"), A(49071, 35, 5, "魔神圣")),
            Set(18, JobId.BlackMage,
                A(49072, 31, 1, "魔爆炎"), A(49073, 32, 2, "魔冰封"),
                A(49074, 33, 3, "魔暴雷"), A(49075, 34, 4, "魔蛙变"), A(49076, 35, 5, "魔核爆")),
            Set(19, JobId.Dragoon,
                A(49077, 31, 1, "魔跳跃"), A(49078, 32, 2, "前踏步"), A(49079, 33, 3, "龙剑")),
            Set(20, JobId.Summoner,
                A(49080, 31, 1, "地狱之火炎"), A(49081, 32, 2, "制裁之雷"),
                A(49082, 33, 3, "大地之壁"), A(49083, 34, 4, "雷暴"), A(49084, 35, 5, "百万核爆")),
            Set(21, JobId.BlueMage,
                A(49085, 31, 1, "魔疾风"), A(49086, 32, 1, "魔导弹"),
                A(49087, 33, 1, "魔水流吐息"), A(49088, 34, 2, "魔强力守护"), A(49090, 35, 3, "魔白风")),
            Set(22, JobId.RedMage,
                A(49092, 31, 1, "魔烈炎"), A(49093, 32, 2, "魔救疗"),
                A(49094, 33, 3, "魔侦测"), A(49095, 34, 4, "魔冰冻"), A(49096, 35, 5, "魔震雷")),
            Set(23, JobId.Necromancer,
                A(49097, 31, 1, "吸血触"), A(49098, 32, 2, "深度冻结"),
                A(49099, 33, 3, "地狱之风"), A(49100, 34, 4, "混沌疾雷"), A(49101, 35, 5, "惩戒之光")),
        }.ToDictionary(set => set.Job);

    public static IReadOnlyCollection<SupportJobActionSet> All => Sets.Values.ToArray();

    public static SupportJobActionSet Get(JobId job)
    {
        return Sets.TryGetValue(job, out var set)
            ? set
            : throw new ArgumentOutOfRangeException(nameof(job), job, "Not a North Horn support job.");
    }

    public static uint GetGeneralActionId(JobId job, uint actionRowId)
    {
        var action = Get(job).Actions.SingleOrDefault(candidate => candidate.ActionRowId == actionRowId);
        return action?.GeneralActionId
               ?? throw new ArgumentOutOfRangeException(nameof(actionRowId), actionRowId,
                   $"Action row is not registered for {job}.");
    }

    private static SupportJobActionDatum A(uint row, uint slot, byte level, string name)
    {
        return new SupportJobActionDatum(row, slot, level, name);
    }

    private static SupportJobActionSet Set(uint row, JobId job, params SupportJobActionDatum[] actions)
    {
        return new SupportJobActionSet(row, job, actions);
    }
}
