using BOCCHI.Modules.CeCrowdsource;
using BOCCHI.Modules.Fates;
using ECommons.DalamudServices;
using System;
using System.Linq;

namespace BOCCHI.Modules.Automator.Pots;

/// <summary>What the automator needs to know about this zone's pots right now.</summary>
public readonly record struct PotContext(
    PotCycleSnapshot Cycle,
    DateTimeOffset Now,
    uint LivePot,
    uint? StartableLivePot,
    bool PotWorkEnabled)
{
    public bool HasStartableLivePot => StartableLivePot != null;

    public static PotContext Capture(AutomatorModule module)
    {
        var config = module.Config;
        var now = DateTimeOffset.UtcNow;
        var cycle = module.potCycles.Get(Svc.ClientState.TerritoryType);
        var liveFate = Svc.Fates.FirstOrDefault(fate => PotZoneData.IsPotFate(fate.FateId));
        var livePot = liveFate == null ? 0u : liveFate.FateId;

        var potsOnly = module.RunMode == AutomatorRunMode.PotsAndTreasure;
        uint? startable = null;
        if (liveFate != null && (potsOnly || (config.ShouldDoFates && IsPotWanted(config, livePot))))
        {
            // Some North Horn FATEs report ~1s for their whole life, so a value
            // that low means "unknown", not "about to end".
            var remaining = liveFate.TimeRemaining;
            if (remaining < 10 || !PotTimingPolicy.ShouldSkipLivePot(config.MinPotFateMinutesRemaining, remaining))
            {
                startable = livePot;
            }
        }

        // Pots & Treasure always waits for the next pot.
        var workEnabled = cycle.HasPredictedNextPot
                          && (potsOnly
                              || (config.ShouldDoFates
                                  && (config.ShouldFarmPotChests || config.ShouldPrepositionToPots)
                                  && IsPotWanted(config, cycle.PredictedNextPotFateId)));

        return new PotContext(cycle, now, livePot, startable, workEnabled);
    }

    public static bool IsPotWanted(AutomatorConfig config, uint potFateId)
    {
        return config.ShouldPreferPotFates
               || (config.FatesMap.TryGetValue(potFateId, out var enabled) && enabled);
    }

    /// <summary>Feeds the zone's cycle from the live pot and the crowdsource server.</summary>
    public static void UpdateCycles(AutomatorModule module)
    {
        var territory = Svc.ClientState.TerritoryType;
        if (!PotZoneData.GetPotFates(territory).Any())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var liveFate = Svc.Fates.FirstOrDefault(fate => PotZoneData.IsPotFate(fate.FateId));
        if (liveFate != null)
        {
            var firstSeen = module.TryGetModule<FatesModule>(out var fates)
                            && fates != null
                            && fates.fates.TryGetValue(liveFate.FateId, out var tracked)
                ? tracked.SpawnedAt
                : 0;
            var spawnAt = PotTimingPolicy.ResolveSpawnTime(liveFate.StartTimeEpoch, firstSeen, now);
            module.potCycles.Update(territory, liveFate.FateId, spawnAt, now);
            return;
        }

        module.potCycles.Update(territory, 0, now, now);
        if (!module.TryGetModule<CeCrowdsourceModule>(out var crowd) || crowd == null)
        {
            return;
        }

        // The server already filters to this island; the zone server check
        // only guards against a record left over from the previous island.
        var zoneServer = CeZoneServerId.Current;
        var newest = crowd.Records
            .Where(record => record.EventType == "FATE"
                             && record.TerritoryID == territory
                             && PotZoneData.IsPotFate(record.EventID)
                             && record.LastSpawnedAt > 0
                             && (zoneServer == 0 || record.ZoneServerID == (int)zoneServer))
            .OrderByDescending(record => record.LastSpawnedAt)
            .FirstOrDefault();
        if (newest != null)
        {
            module.potCycles.TryApplyRemoteAnchor(
                territory,
                newest.EventID,
                DateTimeOffset.FromUnixTimeSeconds(newest.LastSpawnedAt),
                now);
        }
    }
}
