using Ocelot.Config.Attributes;
using Ocelot.Modules;
using System.Collections.Generic;

namespace BOCCHI.Modules.Automator;

public class AutomatorConfig : ModuleConfig
{
    [Checkbox]
    [Illegal]
    [RequiredPlugin("Lifestream", "vnavmesh")]
    [Label("generic.label.enabled")]
    [Tooltip("enabled")]
    public bool Enabled { get; set; } = false;

    [Enum(typeof(AiType), nameof(AiTypeProvider))]
    public AiType AiProvider { get; set; } = AiType.VBM;

    [Checkbox] public bool ToggleAiProvider { get; set; } = true;

    public bool ShouldToggleAiProvider
    {
        get => IsPropertyEnabled(nameof(ToggleAiProvider));
    }

    [Checkbox] public bool ChangeLowLevelJob { get; set; } = true;
    public bool ShouldChangeLowLevelJob
    {
        get => IsPropertyEnabled(nameof(ChangeLowLevelJob));
    }

    [Checkbox] public bool ForceTarget { get; set; } = true;

    public bool ShouldForceTarget
    {
        get => IsPropertyEnabled(nameof(ForceTarget));
    }

    [Checkbox]
    [DependsOn(nameof(ForceTarget))]

    public bool ForceTargetCentralEnemy { get; set; } = true;

    public bool ShouldForceTargetCentralEnemy
    {
        get => IsPropertyEnabled(nameof(ForceTargetCentralEnemy));
    }

    [FloatRange(5f, 30f)] public float EngagementRange { get; set; } = 5f;

    [Checkbox] public bool AutoReturnAfterDeath { get; set; } = true;

    public bool ShouldAutoReturnAfterDeath
    {
        get => IsPropertyEnabled(nameof(AutoReturnAfterDeath));
    }

    [IntRange(1, 60)]
    [DependsOn(nameof(AutoReturnAfterDeath))]
    [Indent]
    public int DeathReturnMinutes { get; set; } = 10;

    [Enum(typeof(InstanceEntryArea), nameof(InstanceEntryAreaProvider))]
    public InstanceEntryArea InitialInstanceArea { get; set; } = InstanceEntryArea.NorthHorn;

    [Checkbox]
    [RequiredPlugin("DailyRoutines")]
    public bool AutoRotateInstance { get; set; } = false;

    public bool ShouldAutoRotateInstance
    {
        get => IsPropertyEnabled(nameof(AutoRotateInstance));
    }

    [FloatRange(15f, 180f)]
    [DependsOn(nameof(AutoRotateInstance))]
    [Indent]
    public float InstanceStayMinutes { get; set; } = 90f;

    [Checkbox]
    [DependsOn(nameof(AutoRotateInstance))]
    [Indent]
    public bool RotateWhenPopulationLow { get; set; } = false;

    public bool ShouldRotateWhenPopulationLow
    {
        get => IsPropertyEnabled(nameof(RotateWhenPopulationLow));
    }

    [IntRange(1, 72)]
    [DependsOn(nameof(RotateWhenPopulationLow))]
    [Indent]
    public int MinimumInstancePopulation { get; set; } = 10;

    // Critical Encounters
    [Checkbox] public bool DoCriticalEncounters { get; set; } = true;

    public bool ShouldDoCriticalEncounters
    {
        get => IsPropertyEnabled(nameof(DoCriticalEncounters));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]

    public bool StanceOffBeforeCriticalEncounters { get; set; } = false;

    public bool ShouldStanceOffBeforeCriticalEncounters
    {
        get => IsPropertyEnabled(nameof(StanceOffBeforeCriticalEncounters));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]

    public bool DelayCriticalEncounters { get; set; } = false;

    private float _minDelay = 5f;
    [FloatRange(0f, 30f)]
    [DependsOn(nameof(DelayCriticalEncounters))]
    public float MinDelay
    {
        get => _minDelay;
        set
        {
            _minDelay = value;
            if (_minDelay >= MaxDelay)
            {
                MaxDelay = _minDelay + 0.1f;
            }
        }
    }

    private float _maxDelay = 15f;
    [FloatRange(0f, 30f)]
    [DependsOn(nameof(DelayCriticalEncounters))]
    public float MaxDelay
    {
        get => _maxDelay;
        set
        {
            _maxDelay = value;
            if (_maxDelay <= MinDelay)
            {
                MinDelay = _maxDelay - 0.1f;
            }
        }
    }

    public bool ShouldDelayCriticalEncounters
    {
        get => IsPropertyEnabled(nameof(DelayCriticalEncounters));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoScourgeOfTheMind { get; set; } = true;

    public bool ShouldDoScourgeOfTheMind
    {
        get => IsPropertyEnabled(nameof(DoScourgeOfTheMind));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTheBlackRegiment { get; set; } = true;

    public bool ShouldDoTheBlackRegiment
    {
        get => IsPropertyEnabled(nameof(DoTheBlackRegiment));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTheUnbridled { get; set; } = true;

    public bool ShouldDoTheUnbridled
    {
        get => IsPropertyEnabled(nameof(DoTheUnbridled));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCrawlingDeath { get; set; } = true;

    public bool ShouldDoCrawlingDeath
    {
        get => IsPropertyEnabled(nameof(DoCrawlingDeath));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCalamityBound { get; set; } = true;

    public bool ShouldDoCalamityBound
    {
        get => IsPropertyEnabled(nameof(DoCalamityBound));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTrialByClaw { get; set; } = true;

    public bool ShouldDoTrialByClaw
    {
        get => IsPropertyEnabled(nameof(DoTrialByClaw));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoFromTimesBygone { get; set; } = true;

    public bool ShouldDoFromTimesBygone
    {
        get => IsPropertyEnabled(nameof(DoFromTimesBygone));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCompanyOfStone { get; set; } = true;

    public bool ShouldDoCompanyOfStone
    {
        get => IsPropertyEnabled(nameof(DoCompanyOfStone));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoSharkAttack { get; set; } = true;

    public bool ShouldDoSharkAttack
    {
        get => IsPropertyEnabled(nameof(DoSharkAttack));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoCriticalEncounters))]

    public bool DoOnTheHunt { get; set; } = true;

    public bool ShouldDoOnTheHunt
    {
        get => IsPropertyEnabled(nameof(DoOnTheHunt));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoWithExtremePrejudice { get; set; } = true;

    public bool ShouldDoWithExtremePrejudice
    {
        get => IsPropertyEnabled(nameof(DoWithExtremePrejudice));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoNoiseComplaint { get; set; } = true;

    public bool ShouldDoNoiseComplaint
    {
        get => IsPropertyEnabled(nameof(DoNoiseComplaint));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCursedConcern { get; set; } = true;

    public bool ShouldDoCursedConcern
    {
        get => IsPropertyEnabled(nameof(DoCursedConcern));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoEternalWatch { get; set; } = true;

    public bool ShouldDoEternalWatch
    {
        get => IsPropertyEnabled(nameof(DoEternalWatch));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoFlameOfDusk { get; set; } = true;

    public bool ShouldDoFlameOfDusk
    {
        get => IsPropertyEnabled(nameof(DoFlameOfDusk));
    }

    // North Horn Critical Encounters
    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe49 { get; set; } = true;

    public bool ShouldDoNorthHornCe49
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe49));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe50 { get; set; } = true;

    public bool ShouldDoNorthHornCe50
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe50));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe51 { get; set; } = true;

    public bool ShouldDoNorthHornCe51
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe51));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe52 { get; set; } = true;

    public bool ShouldDoNorthHornCe52
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe52));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe53 { get; set; } = true;

    public bool ShouldDoNorthHornCe53
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe53));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe54 { get; set; } = true;

    public bool ShouldDoNorthHornCe54
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe54));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe55 { get; set; } = true;

    public bool ShouldDoNorthHornCe55
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe55));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe56 { get; set; } = true;

    public bool ShouldDoNorthHornCe56
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe56));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe57 { get; set; } = true;

    public bool ShouldDoNorthHornCe57
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe57));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe58 { get; set; } = true;

    public bool ShouldDoNorthHornCe58
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe58));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe59 { get; set; } = true;

    public bool ShouldDoNorthHornCe59
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe59));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe60 { get; set; } = true;

    public bool ShouldDoNorthHornCe60
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe60));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe61 { get; set; } = true;

    public bool ShouldDoNorthHornCe61
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe61));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe62 { get; set; } = true;

    public bool ShouldDoNorthHornCe62
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe62));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]
    public bool DoNorthHornCe63 { get; set; } = true;

    public bool ShouldDoNorthHornCe63
    {
        get => IsPropertyEnabled(nameof(DoNorthHornCe63));
    }

    // Fates
    [Checkbox] public bool DoFates { get; set; } = true;

    public bool ShouldDoFates
    {
        get => IsPropertyEnabled(nameof(DoFates));
    }

    [Checkbox]
    [DependsOn(nameof(DoFates))]

    public bool StanceOnBeforeDoFates { get; set; } = false;

    public bool ShouldStanceOnBeforeDoFates
    {
        get => IsPropertyEnabled(nameof(StanceOnBeforeDoFates));
    }

    [Checkbox]
    [DependsOn(nameof(DoFates))]
    public bool DelayFates { get; set; } = false;

    private float _minFateDelay = 5f;

    [FloatRange(0f, 30f)]
    [DependsOn(nameof(DelayFates))]
    public float MinFateDelay
    {
        get => _minFateDelay;
        set
        {
            _minFateDelay = value;
            if (_minFateDelay >= MaxFateDelay)
            {
                MaxFateDelay = _minFateDelay + 0.1f;
            }
        }
    }

    private float _maxFateDelay = 15f;

    [FloatRange(0f, 30f)]
    [DependsOn(nameof(DelayFates))]
    public float MaxFateDelay
    {
        get => _maxFateDelay;
        set
        {
            _maxFateDelay = value;
            if (_maxFateDelay <= MinFateDelay)
            {
                MinFateDelay = _maxFateDelay - 0.1f;
            }
        }
    }

    public bool ShouldDelayFates
    {
        get => IsPropertyEnabled(nameof(DelayFates));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoRoughWaters { get; set; } = true;

    public bool ShouldDoRoughWaters
    {
        get => IsPropertyEnabled(nameof(DoRoughWaters));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoTheGoldenGuardian { get; set; } = true;

    public bool ShouldDoTheGoldenGuardian
    {
        get => IsPropertyEnabled(nameof(DoTheGoldenGuardian));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoKingOfTheCrescent { get; set; } = true;

    public bool ShouldDoKingOfTheCrescent
    {
        get => IsPropertyEnabled(nameof(DoKingOfTheCrescent));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    [Experimental]

    public bool DoTheWingedTerror { get; set; } = false;

    public bool ShouldDoTheWingedTerror
    {
        get => IsPropertyEnabled(nameof(DoTheWingedTerror));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoAnUnendingDuty { get; set; } = true;

    public bool ShouldDoAnUnendingDuty
    {
        get => IsPropertyEnabled(nameof(DoAnUnendingDuty));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoBrainDrain { get; set; } = true;

    public bool ShouldDoBrainDrain
    {
        get => IsPropertyEnabled(nameof(DoBrainDrain));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoADelicateBalance { get; set; } = true;

    public bool ShouldDoADelicateBalance
    {
        get => IsPropertyEnabled(nameof(DoADelicateBalance));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoSwornToSoil { get; set; } = true;

    public bool ShouldDoSwornToSoil
    {
        get => IsPropertyEnabled(nameof(DoSwornToSoil));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoAPryingEye { get; set; } = true;

    public bool ShouldDoAPryingEye
    {
        get => IsPropertyEnabled(nameof(DoAPryingEye));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoFatalAllure { get; set; } = true;

    public bool ShouldDoFatalAllure
    {
        get => IsPropertyEnabled(nameof(DoFatalAllure));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoServingDarkness { get; set; } = true;

    public bool ShouldDoServingDarkness
    {
        get => IsPropertyEnabled(nameof(DoServingDarkness));
    }

    [Checkbox]
    [Experimental]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoPersistentPots { get; set; } = false;

    public bool ShouldDoPersistentPots
    {
        get => IsPropertyEnabled(nameof(DoPersistentPots));
    }

    [Checkbox]
    [Experimental]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoPleadingPots { get; set; } = false;

    public bool ShouldDoPleadingPots
    {
        get => IsPropertyEnabled(nameof(DoPleadingPots));
    }

    // North Horn FATEs
    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    // 2072/2073 are ordinary North Horn FATEs and default to enabled, exactly
    // like every other North Horn FATE. A saved configuration keeps whatever
    // value it stored, so an existing user who explicitly disabled them is
    // never silently re-enabled.
    public bool DoNorthHornFate2072 { get; set; } = true;

    public bool ShouldDoNorthHornFate2072
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2072));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2073 { get; set; } = true;

    public bool ShouldDoNorthHornFate2073
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2073));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2074 { get; set; } = true;

    public bool ShouldDoNorthHornFate2074
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2074));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2075 { get; set; } = true;

    public bool ShouldDoNorthHornFate2075
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2075));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2076 { get; set; } = true;

    public bool ShouldDoNorthHornFate2076
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2076));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2077 { get; set; } = true;

    public bool ShouldDoNorthHornFate2077
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2077));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2078 { get; set; } = true;

    public bool ShouldDoNorthHornFate2078
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2078));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2079 { get; set; } = true;

    public bool ShouldDoNorthHornFate2079
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2079));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2080 { get; set; } = true;

    public bool ShouldDoNorthHornFate2080
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2080));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2081 { get; set; } = true;

    public bool ShouldDoNorthHornFate2081
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2081));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2082 { get; set; } = true;

    public bool ShouldDoNorthHornFate2082
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2082));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2083 { get; set; } = true;

    public bool ShouldDoNorthHornFate2083
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2083));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    public bool DoNorthHornFate2084 { get; set; } = true;

    public bool ShouldDoNorthHornFate2084
    {
        get => IsPropertyEnabled(nameof(DoNorthHornFate2084));
    }

    public IReadOnlyDictionary<uint, bool> CriticalEncountersMap
    {
        get => new Dictionary<uint, bool>
        {
            { 33, ShouldDoScourgeOfTheMind },
            { 34, ShouldDoTheBlackRegiment },
            { 35, ShouldDoTheUnbridled },
            { 36, ShouldDoCrawlingDeath },
            { 37, ShouldDoCalamityBound },
            { 38, ShouldDoTrialByClaw },
            { 39, ShouldDoFromTimesBygone },
            { 40, ShouldDoCompanyOfStone },
            { 41, ShouldDoSharkAttack },
            { 42, ShouldDoOnTheHunt },
            { 43, ShouldDoWithExtremePrejudice },
            { 44, ShouldDoNoiseComplaint },
            { 45, ShouldDoCursedConcern },
            { 46, ShouldDoEternalWatch },
            { 47, ShouldDoFlameOfDusk },
            { 49, ShouldDoNorthHornCe49 },
            { 50, ShouldDoNorthHornCe50 },
            { 51, ShouldDoNorthHornCe51 },
            { 52, ShouldDoNorthHornCe52 },
            { 53, ShouldDoNorthHornCe53 },
            { 54, ShouldDoNorthHornCe54 },
            { 55, ShouldDoNorthHornCe55 },
            { 56, ShouldDoNorthHornCe56 },
            { 57, ShouldDoNorthHornCe57 },
            { 58, ShouldDoNorthHornCe58 },
            { 59, ShouldDoNorthHornCe59 },
            { 60, ShouldDoNorthHornCe60 },
            { 61, ShouldDoNorthHornCe61 },
            { 62, ShouldDoNorthHornCe62 },
            { 63, ShouldDoNorthHornCe63 },
        };
    }

    public IReadOnlyDictionary<uint, bool> FatesMap
    {
        get => new Dictionary<uint, bool>
        {
            { 1962, ShouldDoRoughWaters },
            { 1963, ShouldDoTheGoldenGuardian },
            { 1964, ShouldDoKingOfTheCrescent },
            { 1965, ShouldDoTheWingedTerror },
            { 1966, ShouldDoAnUnendingDuty },
            { 1967, ShouldDoBrainDrain },
            { 1968, ShouldDoADelicateBalance },
            { 1969, ShouldDoSwornToSoil },
            { 1970, ShouldDoAPryingEye },
            { 1971, ShouldDoFatalAllure },
            { 1972, ShouldDoServingDarkness },
            { 1976, ShouldDoPersistentPots },
            { 1977, ShouldDoPleadingPots },
            { 2072, ShouldDoNorthHornFate2072 },
            { 2073, ShouldDoNorthHornFate2073 },
            { 2074, ShouldDoNorthHornFate2074 },
            { 2075, ShouldDoNorthHornFate2075 },
            { 2076, ShouldDoNorthHornFate2076 },
            { 2077, ShouldDoNorthHornFate2077 },
            { 2078, ShouldDoNorthHornFate2078 },
            { 2079, ShouldDoNorthHornFate2079 },
            { 2080, ShouldDoNorthHornFate2080 },
            { 2081, ShouldDoNorthHornFate2081 },
            { 2082, ShouldDoNorthHornFate2082 },
            { 2083, ShouldDoNorthHornFate2083 },
            { 2084, ShouldDoNorthHornFate2084 },
        };
    }
}
