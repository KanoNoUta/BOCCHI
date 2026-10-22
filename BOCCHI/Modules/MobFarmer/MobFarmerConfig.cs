using BOCCHI.Data;
using Ocelot.Config.Attributes;
using Ocelot.Modules;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer;

public class MobFarmerConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    // Kept for one-way migration from the pre-3.3.14 mixed monster selector.
    // It intentionally has no config UI attribute.
    public List<Mob> Mobs { get; set; } = [];

    [MultiEnum(typeof(Mob), nameof(SouthHornMobProvider))]
    [Searchable]
    public List<Mob> SouthHornMobs { get; set; } = [];

    [MultiEnum(typeof(Mob), nameof(NorthHornMobProvider))]
    [Searchable]
    public List<Mob> NorthHornMobs { get; set; } = [];

    [Checkbox] public bool ConsiderSpecialMobs { get; set; } = false;

    [IntRange(1, 40)] public int MaxMobLevel { get; set; } = 40;

    [FloatRange(10f, 1000f)]
    [RangeIndicator(0.9f, 0.1f, 0.6f)]
    public float MaxEuclideanDistance { get; set; } = 75f;

    [Checkbox] public bool ReturnToStartInWaitingPhase { get; set; } = false;

    [FloatRange(10f, 1000f)]
    [RangeIndicator(0.9f, 0.1f, 0.6f)]
    [DependsOn(nameof(ReturnToStartInWaitingPhase))]
    public float MinEuclideanDistanceToReturnHome { get; set; } = 200f;

    [Checkbox] public bool RenderDebugLines { get; set; } = false;

    [Checkbox]
    [DependsOn(nameof(RenderDebugLines))]
    public bool RenderDebugLinesWhileNotRunning { get; set; } = false;

    public bool ShouldRenderDebugLinesWhileNotRunning
    {
        get => IsPropertyEnabled(nameof(RenderDebugLinesWhileNotRunning));
    }

    [Checkbox] public bool ApplyBattleBell { get; set; } = false;

    [FloatRange(0f, 30f)]
    [DependsOn(nameof(ApplyBattleBell))]
    public float MaximumBattleBellWaitTime { get; set; } = 10f;

    [IntRange(0, 20)] public int MinimumMobsToStartLoop { get; set; } = 0;

    [IntRange(1, 20)] public int MinimumMobsToStartFight { get; set; } = 5;

    [IntRange(0, 20)] public int ExtraTimeToWait { get; set; } = 0;

    // Named farm spots, edited on their own config tab. Empty: farm where
    // the player stands when the farmer starts.
    public List<FarmSpot> Spots { get; set; } = [];

    [FloatRange(3f, 30f)] public float ClaimedSpotSeconds { get; set; } = 8f;

    [FloatRange(3f, 15f)] public float ClaimedPlayerRadius { get; set; } = 5f;

    // Yields: the farmer pauses itself and hands the player to another mode,
    // then walks back to its spot. Ported from upstream BOCCHI 4.2.
    [Checkbox] public bool YieldToPots { get; set; } = false;

    // On by default: the farmer has always cast Treasuresight after each fight.
    [Checkbox] public bool CastTreasureSightAtFarm { get; set; } = true;

    // 0 keeps the old behaviour of casting after every fight.
    [IntRange(0, 60)]
    [DependsOn(nameof(CastTreasureSightAtFarm))]
    public int TreasureSightIntervalMinutes { get; set; } = 0;

    [Checkbox] public bool YieldToTreasureHunt { get; set; } = false;

    [IntRange(5, 60)]
    [DependsOn(nameof(YieldToTreasureHunt))]
    public int TreasureHuntIntervalMinutes { get; set; } = 15;

    // On by default: the farmer has always gone back for crystal buffs when
    // the buff module is enabled and a buff runs low.
    [Checkbox] public bool YieldToCrystalBuffs { get; set; } = true;

    public IReadOnlyList<Mob> GetMobsForTerritory(uint territoryId)
    {
        return territoryId switch
        {
            ZoneData.SOUTHHORN => SouthHornMobs,
            ZoneData.NORTHHORN => NorthHornMobs,
            _ => [],
        };
    }

    public bool IsSelectedForTerritory(Mob mob, uint territoryId)
    {
        return GetMobsForTerritory(territoryId).Contains(mob);
    }
}
