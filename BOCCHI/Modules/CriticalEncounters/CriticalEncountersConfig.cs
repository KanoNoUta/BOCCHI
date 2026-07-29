using BOCCHI.Data;
using BOCCHI.Enums;
using Ocelot.Config.Attributes;
using Ocelot.Modules;
using System;
using System.Collections.Generic;

namespace BOCCHI.Modules.CriticalEncounters;

public class CriticalEncountersConfig : ModuleConfig
{
    private static readonly IReadOnlyDictionary<Demiatma, Func<CriticalEncountersConfig, bool>> DemiatmaAlertRules =
        new Dictionary<Demiatma, Func<CriticalEncountersConfig, bool>>
        {
            [Demiatma.Azurite] = config => config.AlertAzurite,
            [Demiatma.Verdigris] = config => config.AlertVerdigris,
            [Demiatma.Malachite] = config => config.AlertMalachite,
            [Demiatma.Realgar] = config => config.AlertRealgar,
            [Demiatma.CaputMortuum] = config => config.AlertCaputMortuum,
            [Demiatma.Orpiment] = config => config.AlertOrpiment,
        };

    private static readonly IReadOnlyDictionary<SoulShard, Func<CriticalEncountersConfig, bool>> SoulShardAlertRules =
        new Dictionary<SoulShard, Func<CriticalEncountersConfig, bool>>
        {
            // These legacy properties intentionally remain the source of truth so
            // existing configuration files continue to work without migration.
            [SoulShard.Oracle] = config => config.AlertOracle,
            [SoulShard.Berserker] = config => config.AlertBerserker,
            [SoulShard.Ranger] = config => config.AlertRanger,
            [SoulShard.Ninja] = config => config.AlertNinja,
            [SoulShard.BlackMage] = config => config.AlertBlackMage,
            [SoulShard.WhiteMage] = config => config.AlertWhiteMage,
            [SoulShard.Dragoon] = config => config.AlertDragoon,
            [SoulShard.Summoner] = config => config.AlertSummoner,
            [SoulShard.BlueMage] = config => config.AlertBlueMage,
            [SoulShard.RedMage] = config => config.AlertRedMage,
            [SoulShard.Necromancer] = config => config.AlertNecromancer,
        };

    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [Checkbox] public bool TrackForkedTower { get; set; } = true;

    [Checkbox] public bool LogSpawn { get; set; } = false;

    [Checkbox] public bool AlertAll { get; set; } = false;

    [Checkbox] public bool AlertAzurite { get; set; } = false;

    [Checkbox] public bool AlertVerdigris { get; set; } = false;

    [Checkbox] public bool AlertMalachite { get; set; } = false;

    [Checkbox] public bool AlertRealgar { get; set; } = false;

    [Checkbox] public bool AlertCaputMortuum { get; set; } = false;

    [Checkbox] public bool AlertOrpiment { get; set; } = false;

    [Checkbox] public bool AlertInvestigationRecords { get; set; } = false;

    [Checkbox] public bool AlertOracle { get; set; } = false;

    [Checkbox] public bool AlertBerserker { get; set; } = false;

    [Checkbox] public bool AlertRanger { get; set; } = false;

    [Checkbox] public bool AlertNinja { get; set; } = false;

    [Checkbox] public bool AlertBlackMage { get; set; } = false;

    [Checkbox] public bool AlertWhiteMage { get; set; } = false;

    [Checkbox] public bool AlertDragoon { get; set; } = false;

    [Checkbox] public bool AlertSummoner { get; set; } = false;

    [Checkbox] public bool AlertBlueMage { get; set; } = false;

    [Checkbox] public bool AlertRedMage { get; set; } = false;

    [Checkbox] public bool AlertNecromancer { get; set; } = false;

    public bool ShouldAlertForRewards(EventData data)
    {
        if (data.Demiatma is { } demiatma &&
            DemiatmaAlertRules.TryGetValue(demiatma, out var demiatmaRule) &&
            demiatmaRule(this))
        {
            return true;
        }

        if (data.Note is not null && AlertInvestigationRecords)
        {
            return true;
        }

        return data.Soulshard is { } soulShard &&
               SoulShardAlertRules.TryGetValue(soulShard, out var soulShardRule) &&
               soulShardRule(this);
    }
}
