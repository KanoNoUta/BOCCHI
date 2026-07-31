using BOCCHI;
using BOCCHI.Chains;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.AggroRange;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.StateManager;
using BOCCHI.Modules.Treasure;
using BOCCHI.Pathfinding;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static AethernetData CreateShard(Aethernet aethernet, Vector3 position, Vector3 destination)
{
    return new AethernetData
    {
        Aethernet = aethernet,
        Position = position,
        Destination = destination,
    };
}

static Task<List<Vector3>> StraightPath(Vector3 start, Vector3 destination, CancellationToken _)
{
    return Task.FromResult(new List<Vector3> { start, destination });
}

var aggroZone = new AggroDangerZone(14857, Vector3.Zero, 5f);
var terminalAggroZones = AggroAvoidancePlanner.GetRelevantZones(
    [aggroZone, new AggroDangerZone(14858, new Vector3(20f, 0f, 0f), 5f)],
    new Vector3(1f, 0f, 0f));
Assert(terminalAggroZones.Length == 1 && terminalAggroZones[0].NameId == 14858,
    "A danger circle containing the requested destination must be excluded without hiding unrelated obstacles.");
var straightAggroPath = new List<Vector3>
{
    new(-10f, 0f, 0f),
    new(10f, 0f, 0f),
};
Assert(AggroAvoidancePlanner.TryAvoid(
           straightAggroPath,
           [aggroZone],
           verticalTolerance: 6f,
           point => point,
           out var avoidedAggroPath)
       && avoidedAggroPath.Count > straightAggroPath.Count
       && AggroAvoidancePlanner.IsPathClear(avoidedAggroPath, [aggroZone], 6f),
    "Aggro avoidance must insert a clear tangent/arc detour around a crossed ordinary-mob circle.");
Assert(AggroAvoidancePlanner.TryAvoid(
           [new Vector3(-10f, 0f, 8f), new Vector3(10f, 0f, 8f)],
           [aggroZone],
           6f,
           point => point,
           out var alreadyClearAggroPath)
       && alreadyClearAggroPath.Count == 2,
    "A clear route must remain unchanged instead of gaining unnecessary avoidance waypoints.");
Assert(!AggroAvoidancePlanner.SegmentEntersZone(
           new Vector3(-10f, 20f, 0f),
           new Vector3(10f, 20f, 0f),
           aggroZone,
           verticalTolerance: 6f),
    "Aggro avoidance must ignore circles on a different vertical floor.");
Assert(!AggroAvoidancePlanner.SegmentEntersZone(
           new Vector3(1f, 0f, 0f),
           new Vector3(10f, 0f, 0f),
           aggroZone,
           verticalTolerance: 6f),
    "A player already inside a circle must be allowed to leave directly without deadlocking navigation.");
Assert(AggroAvoidancePlanner.TryAvoid(
           [new Vector3(1f, 0f, 0f), new Vector3(-10f, 0f, 0f)],
           [aggroZone],
           6f,
           point => point,
           out var insideEscapePath)
       && insideEscapePath.Count > 2
       && AggroAvoidancePlanner.IsPathClear(insideEscapePath, [aggroZone], 6f),
    "A route starting inside and initially travelling deeper must first escape outward, then detour without deadlock.");

Assert(PostActivityReturnPolicy.ShouldQueue(EventType.Fate)
       && !PostActivityReturnPolicy.ShouldQueue(EventType.CriticalEncounter),
    "Completed FATEs must lock the automator into a base-camp return before selecting another activity.");
Assert(CombatAutomationPolicy.ShouldAcquireTarget(false, false)
       && CombatAutomationPolicy.ShouldAcquireTarget(true, true)
       && !CombatAutomationPolicy.ShouldAcquireTarget(false, true),
    "FATE arrival must acquire an initial target even when continuous force-target is disabled.");
Assert(CombatAutomationPolicy.ShouldRetryPromeRotation(true, false)
       && !CombatAutomationPolicy.ShouldRetryPromeRotation(false, false)
       && !CombatAutomationPolicy.ShouldRetryPromeRotation(true, true),
    "FATE combat maintenance must retry a loaded but stopped PromeRotation instance only.");
Assert(TreasureInteractionPolicy.CanAttempt(true, false, false)
       && TreasureInteractionPolicy.CanAttempt(false, false, false)
       && !TreasureInteractionPolicy.CanAttempt(true, true, false)
       && !TreasureInteractionPolicy.CanAttempt(true, false, true),
    "Treasure interaction must remain available while mounted and only wait for combat/casting to clear.");
Assert(TreasureSightRefreshPolicy.ShouldCast(true, false)
       && !TreasureSightRefreshPolicy.ShouldCast(true, true)
       && !TreasureSightRefreshPolicy.ShouldCast(false, false),
    "Treasuresight must run only when requested and the current territory count has not been initialized.");
Assert(TreasureHuntDataPolicy.ShouldReload(null, 1252, 60)
       && TreasureHuntDataPolicy.ShouldReload(1252, 1253, 60)
       && TreasureHuntDataPolicy.ShouldReload(1252, 1252, 0)
       && !TreasureHuntDataPolicy.ShouldReload(1252, 1252, 60),
    "Treasure layout data must be cached within one territory and reloaded only after a map change or empty load.");

var legacyMobConfig = new Config
{
    Version = 2,
    MobFarmerConfig = new BOCCHI.Modules.MobFarmer.MobFarmerConfig
    {
        Mobs = [Mob.Goobbue, Mob.CrescentCliffkite],
    },
};
Assert(legacyMobConfig.Migrate()
       && legacyMobConfig.Version == Config.CurrentVersion
       && legacyMobConfig.MobFarmerConfig.SouthHornMobs.SequenceEqual([Mob.Goobbue])
       && legacyMobConfig.MobFarmerConfig.NorthHornMobs.SequenceEqual([Mob.CrescentCliffkite])
       && legacyMobConfig.MobFarmerConfig.Mobs.Count == 0,
    "Legacy mixed monster selections must migrate into independent South/North Horn lists.");

var navigationEvent = new EventData
{
    Id = 9000,
    Type = EventType.Fate,
    InternalName = "Smart navigation smoke test",
};
var navigationPlayer = Vector3.Zero;
var navigationDestination = new Vector3(100f, 0f, 0f);
var navigationBaseCamp = CreateShard(
    Aethernet.NorthBaseCamp,
    new Vector3(1000f, 0f, 0f),
    new Vector3(1000f, 0f, 0f));
var navigationSource = CreateShard(
    Aethernet.WillOWispVillage,
    new Vector3(45f, 0f, 0f),
    new Vector3(1000f, 0f, 0f));
var navigationTarget = CreateShard(
    Aethernet.SunkenTempleFront,
    new Vector3(900f, 0f, 0f),
    new Vector3(60f, 0f, 0f));
var navigationShards = new[] { navigationBaseCamp, navigationSource, navigationTarget };

var detourPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    (start, destination, _) => Task.FromResult(
        start == navigationPlayer && destination == navigationDestination
            ? new List<Vector3> { start, new(0f, 0f, 300f), destination }
            : new List<Vector3> { start, destination }),
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
var straightFallbackPlan = SmartNavigation.DecideFallback(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    returnCost: 300f,
    teleportCost: 20f,
    "test comparison");
Assert(straightFallbackPlan.Type == NavigationType.Walk
       && detourPlan.Type == NavigationType.WalkTeleportWalk
       && detourPlan.SourceAethernet == navigationSource.Aethernet
       && detourPlan.DestinationAethernet == navigationTarget.Aethernet,
    "Measured vnavmesh detours must be able to change the route selected by straight-line costs.");

var huntFallbackPlan = SmartNavigation.DecideFallback(
    navigationPlayer,
    navigationDestination,
    (Aethernet?)null,
    navigationShards,
    navigationBaseCamp,
    returnCost: 300f,
    teleportCost: 5f,
    "North Horn hunt smoke test");
var huntTransitSteps = HuntNavigationPlanner.BuildTransitSteps(huntFallbackPlan);
Assert(huntFallbackPlan.Type == NavigationType.WalkTeleportWalk
       && huntTransitSteps.Count == 2
       && huntTransitSteps[0].Type == PathfinderStepType.WalkToAethernet
       && huntTransitSteps[0].Aethernet == navigationSource.Aethernet
       && huntTransitSteps[1].Type == PathfinderStepType.TeleportToAethernet
       && huntTransitSteps[1].Aethernet == navigationTarget.Aethernet,
    "Generic fallback routing must retain its source-shard/teleport option for callers that explicitly allow it.");
var safeHuntFallbackPlan = SmartNavigation.DecideFallback(
    navigationPlayer,
    navigationDestination,
    (Aethernet?)null,
    navigationShards,
    navigationBaseCamp,
    returnCost: 300f,
    teleportCost: 5f,
    "North Horn safe hunt fallback",
    includeWalkTeleportCandidate: false);
Assert(safeHuntFallbackPlan.Type != NavigationType.WalkTeleportWalk
       && safeHuntFallbackPlan.Candidates.All(candidate =>
           candidate.Type != NavigationType.WalkTeleportWalk),
    "An unmeasured hunt fallback must not walk toward an unverified source shard.");
var forcedHuntRecovery = HuntNavigationPlanner.BuildForcedRecoverySteps(
    navigationTarget.Aethernet,
    navigationBaseCamp.Aethernet);
Assert(forcedHuntRecovery.Count == 2
       && forcedHuntRecovery[0].Type == PathfinderStepType.ReturnToBaseCamp
       && forcedHuntRecovery[1].Type == PathfinderStepType.TeleportToAethernet
       && forcedHuntRecovery.All(step => step.Type != PathfinderStepType.WalkToAethernet),
    "An unreachable North Horn hunt node must recover through return and its closest aethernet.");
var sameCampRecovery = HuntNavigationPlanner.BuildForcedRecoverySteps(
    navigationBaseCamp.Aethernet,
    navigationBaseCamp.Aethernet);
Assert(sameCampRecovery.Count == 1
       && sameCampRecovery[0].Type == PathfinderStepType.ReturnToBaseCamp,
    "Forced recovery to base camp must not add a redundant teleport.");
Assert(HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, navigationDestination],
           navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, new Vector3(50f, 0f, 0f)],
           navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination([], navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination([navigationDestination], navigationDestination)
       && HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, navigationDestination + new Vector3(5f, 0f, 0f)],
           navigationDestination)
       && !HuntNavigationPlanner.ReachesDestination(
           [Vector3.Zero, navigationDestination + new Vector3(5.01f, 0f, 0f)],
           navigationDestination),
    "Hunt routing must reject partial vnavmesh paths before following them across an unreachable boundary.");

var routeWithCurrentNode = new List<PathfinderStep>
{
    PathfinderStep.WalkToDestination(1),
    PathfinderStep.WalkToDestination(2),
    PathfinderStep.WalkToDestination(3),
};
HuntNavigationPlanner.InsertBeforeCurrentNode(routeWithCurrentNode, 1, forcedHuntRecovery);
Assert(routeWithCurrentNode.Count == 5
       && routeWithCurrentNode[0].NodeId == 1
       && routeWithCurrentNode[1].Type == PathfinderStepType.ReturnToBaseCamp
       && routeWithCurrentNode[2].Type == PathfinderStepType.TeleportToAethernet
       && routeWithCurrentNode[3].Type == PathfinderStepType.WalkToNode
       && routeWithCurrentNode[3].NodeId == 2
       && routeWithCurrentNode[4].NodeId == 3,
    "Transit insertion must preserve the current hunt node behind return/teleport without pre-advancing it.");

var partialSourcePlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    (start, destination, _) => Task.FromResult(
        start == navigationPlayer && destination == navigationSource.Position
            ? new List<Vector3> { start, start + Vector3.UnitX }
            : new List<Vector3> { start, destination }),
    returnCost: 300f,
    teleportCost: 5f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(partialSourcePlan.Candidates.All(candidate =>
        candidate.Type != NavigationType.WalkTeleportWalk
        || candidate.SourceAethernet != navigationSource.Aethernet),
    "A partial route to the source shard must never produce a WalkTeleportWalk candidate.");

var cheapTeleportPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    StraightPath,
    returnCost: 300f,
    teleportCost: 5f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
var expensiveTeleportPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    StraightPath,
    returnCost: 300f,
    teleportCost: 200f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(cheapTeleportPlan.Type == NavigationType.WalkTeleportWalk
       && expensiveTeleportPlan.Type == NavigationType.Walk,
    "TeleportCost must participate in smart-navigation route selection.");
var delayedCePlan = SmartNavigation.PreferBaseCampReturn(cheapTeleportPlan, navigationBaseCamp);
Assert(delayedCePlan.Type == NavigationType.ReturnTeleportWalk
       && delayedCePlan.SourceAethernet == navigationBaseCamp.Aethernet
       && delayedCePlan.DestinationAethernet == cheapTeleportPlan.DestinationAethernet,
    "A delayed CE must use Return from base camp instead of walking to an arbitrary source shard.");
Assert(SmartNavigation.PreferBaseCampReturn(expensiveTeleportPlan, navigationBaseCamp)
           == expensiveTeleportPlan,
    "Delayed-CE return preference must not replace a direct walking route.");

var preferredNavigationEvent = navigationEvent;
preferredNavigationEvent.Aethernet = navigationTarget.Aethernet;
var unreachableTargetPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    preferredNavigationEvent,
    navigationShards,
    navigationBaseCamp,
    (start, destination, _) => Task.FromResult(
        start == navigationTarget.Destination && destination == navigationDestination
            ? new List<Vector3> { start, start + Vector3.UnitX }
            : new List<Vector3> { start, destination }),
    returnCost: 300f,
    teleportCost: 5f,
    destinationCandidateCount: 1,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(unreachableTargetPlan.Candidates.All(candidate =>
        candidate.DestinationAethernet != navigationTarget.Aethernet),
    "Candidates with an unreachable aethernet-to-event segment must be skipped.");

var fallbackPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    (_, _, _) => Task.FromResult(new List<Vector3>()),
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(fallbackPlan.UsedFallback && fallbackPlan.FallbackReason != null,
    "Smart navigation must fall back to straight-line costs when every vnavmesh segment fails.");

var preferredPlan = await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    preferredNavigationEvent,
    navigationShards,
    navigationBaseCamp,
    StraightPath,
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 1,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(preferredPlan.Candidates.Any(candidate =>
        candidate.DestinationAethernet == navigationTarget.Aethernet),
    "An event's preferred aethernet must survive destination-candidate prefiltering.");

var activePathfindRequests = 0;
var maximumConcurrentPathfindRequests = 0;
var pathfindConcurrencyLock = new object();
await SmartNavigation.DecideAsync(
    navigationPlayer,
    navigationDestination,
    navigationEvent,
    navigationShards,
    navigationBaseCamp,
    async (start, destination, token) =>
    {
        lock (pathfindConcurrencyLock)
        {
            activePathfindRequests++;
            maximumConcurrentPathfindRequests = Math.Max(
                maximumConcurrentPathfindRequests,
                activePathfindRequests);
        }

        try
        {
            await Task.Delay(5, token);
            return new List<Vector3> { start, destination };
        }
        finally
        {
            lock (pathfindConcurrencyLock)
            {
                activePathfindRequests--;
            }
        }
    },
    returnCost: 300f,
    teleportCost: 20f,
    destinationCandidateCount: 2,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(maximumConcurrentPathfindRequests == 1,
    "Smart navigation must serialize candidate probes because vnavmesh supports only one pathfinding task.");

Assert(FateTravelTargetPolicy.ShouldPursue(2075, 2075),
    "FATE travel must allow targets that belong to the selected FATE.");
Assert(!FateTravelTargetPolicy.ShouldPursue(2075, 0)
       && !FateTravelTargetPolicy.ShouldPursue(2075, 2076),
    "Roadside enemies and enemies from another FATE must not override the selected activity route.");
Assert(!FateNavigationPolicy.ShouldRepath(navigationActive: true, targetMovement: 100f),
    "A moving FATE target must not replace a vnavmesh route that is still active.");
Assert(FateNavigationPolicy.ShouldRepath(navigationActive: false, targetMovement: 6f)
       && !FateNavigationPolicy.ShouldRepath(navigationActive: false, targetMovement: 5f),
    "FATE target repathing must wait for navigation to stop and enforce its movement threshold.");
Assert(FateNavigationPolicy.IsTargetlessArrival(distanceToCenter: 4.9f, fateRadius: 100f)
       && !FateNavigationPolicy.IsTargetlessArrival(distanceToCenter: 5.1f, fateRadius: 100f),
    "A broad FATE radius must not end travel until the player reaches the center approach.");
Assert(FateNavigationPolicy.IsTargetInEngagementRange(centerDistance: 25f, hitboxRadius: 20f, engagementRange: 5f)
       && !FateNavigationPolicy.IsTargetInEngagementRange(centerDistance: 100f, hitboxRadius: 500f, engagementRange: 5f),
    "FATE arrival must honor normal hitboxes without trusting corrupt or oversized radii.");
Assert(FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation: false, elapsedMs: 1000)
       && !FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation: true, elapsedMs: 1000)
       && !FateNavigationPolicy.IsInsideNavigationStartGrace(hasObservedNavigation: false, elapsedMs: 2000),
    "FATE travel must tolerate only the initial vnavmesh SimpleMove startup gap.");
var fateWatcherStartedAt = FateNavigationPolicy.StartAtFirstTick(null, 100000);
Assert(fateWatcherStartedAt == 100000
       && FateNavigationPolicy.StartAtFirstTick(fateWatcherStartedAt, 101999) == 100000,
    "FATE startup grace must begin when its watcher first executes, not while an earlier return/teleport chain is running.");

var ceFinalTarget = new Vector3(20f, 0f, 0f);
Assert(CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(false, 1999)
       && !CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(true, 1999)
       && !CriticalEncounterNavigationPolicy.IsInsideNavigationStartGrace(false, 2000),
    "CE initial travel must tolerate only the vnavmesh SimpleMove startup gap.");
var ceWatcherStartedAt = CriticalEncounterNavigationPolicy.StartAtFirstTick(null, 200000);
Assert(ceWatcherStartedAt == 200000
       && CriticalEncounterNavigationPolicy.StartAtFirstTick(ceWatcherStartedAt, 201999) == 200000,
    "CE startup grace must begin when its watcher first executes, not while an earlier return/teleport chain is running.");
Assert(CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, false, false, 1999) == FinalApproachDecision.Waiting
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, true, false, 2000) == FinalApproachDecision.Waiting
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, false, false, 2000) == FinalApproachDecision.StoppedBeforeArrival
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           Vector3.Zero, ceFinalTarget, false, true, 1) == FinalApproachDecision.StoppedBeforeArrival
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           new Vector3(15f, 0f, 0f), ceFinalTarget, false, true, 2000) == FinalApproachDecision.Arrived
       && CriticalEncounterNavigationPolicy.EvaluateFinalApproach(
           new Vector3(14.99f, 0f, 0f), ceFinalTarget, false, true, 2000) == FinalApproachDecision.StoppedBeforeArrival,
    "CE final approach must survive vnavmesh's startup gap and confirm the saved random target within five yalms.");
Assert(CriticalEncounterNavigationPolicy.CanSubmitApproach(registrationOpen: true, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitApproach(registrationOpen: false, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitApproach(registrationOpen: true, playerInEncounter: true),
    "CE approach navigation must stop as soon as registration closes or participation begins.");
Assert(CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
           registrationOpen: true,
           finalDestinationSubmitted: false,
           isCloseToZone: true,
           pathfindingInProgress: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
           registrationOpen: false,
           finalDestinationSubmitted: false,
           isCloseToZone: true,
           pathfindingInProgress: false)
       && !CriticalEncounterNavigationPolicy.CanSubmitFinalApproach(
           registrationOpen: true,
           finalDestinationSubmitted: true,
           isCloseToZone: true,
           pathfindingInProgress: false),
    "CE final random approach must be submitted exactly once while registration remains open.");
var ceCenter = new Vector3(100f, 20f, 200f);
var ceMinFinalTarget = CriticalEncounterNavigationPolicy.CreateFinalTarget(ceCenter, 0f, 0f);
var ceMaxFinalTarget = CriticalEncounterNavigationPolicy.CreateFinalTarget(ceCenter, MathF.PI, 100f);
Assert(MathF.Abs(Vector3.Distance(ceCenter, ceMinFinalTarget)
                 - CriticalEncounterNavigationPolicy.MinFinalOffset) < 0.001f
       && MathF.Abs(Vector3.Distance(ceCenter, ceMaxFinalTarget)
                    - CriticalEncounterNavigationPolicy.MaxFinalOffset) < 0.001f,
    "CE final random targets must remain in the configured inner annulus instead of landing at the center or edge.");
Assert(CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: false, isInZone: false, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: true, isInZone: false, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: false, isInZone: true, playerInEncounter: false)
       && !CriticalEncounterNavigationPolicy.ShouldAbandon(
           registrationOpen: false, isInZone: false, playerInEncounter: true),
    "A started CE must be abandoned only while the player remains outside and has not joined it.");

Assert(TransitCompletionPolicy.HasVerifiedArrival(true, true)
       && !TransitCompletionPolicy.HasVerifiedArrival(true, false)
       && !TransitCompletionPolicy.HasVerifiedArrival(false, true)
       && !TransitCompletionPolicy.HasVerifiedArrival(false, false)
       && TransitCompletionPolicy.CanContinueAfterReturn(true)
       && !TransitCompletionPolicy.CanContinueAfterReturn(false),
    "Return/teleport chains must not advance until both child success and destination validation are true.");

var observedNorthReturnLanding = new Vector3(906.7626f, 259.99268f, 907.2258f);
var karnakLanding = new Vector3(454.3429f, 69.99997f, 530.9988f);
Assert(TransitCompletionPolicy.IsVerifiedReturnLanding(
           observedNorthReturnLanding,
           ZoneData.StartingLocations[ZoneData.NORTHHORN],
           Aethernet.NorthBaseCamp,
           Aethernet.NorthBaseCamp)
       && !TransitCompletionPolicy.IsVerifiedReturnLanding(
           karnakLanding,
           ZoneData.StartingLocations[ZoneData.NORTHHORN],
           Aethernet.KarnakCitadel,
           Aethernet.NorthBaseCamp),
    "Return must verify the base-camp landing instead of accepting another aethernet's BetweenAreas cycle.");

long? inactiveSince = null;
inactiveSince = NavigationStopPolicy.UpdateInactiveSince(false, 100, inactiveSince);
Assert(!NavigationStopPolicy.HasStopped(100, inactiveSince, 900),
    "Navigation must retain its startup grace before reporting a stop.");
inactiveSince = NavigationStopPolicy.UpdateInactiveSince(true, 1000, inactiveSince);
inactiveSince = NavigationStopPolicy.UpdateInactiveSince(false, 1100, inactiveSince);
Assert(!NavigationStopPolicy.HasStopped(100, inactiveSince, 2500)
       && NavigationStopPolicy.HasStopped(100, inactiveSince, 2600),
    "A transient vnavmesh status gap must not be treated as a stable stop.");

Assert(ActivityParticipationState.GetCombatStartupDecision(false, 0) == CombatStartupDecision.Ready
       && ActivityParticipationState.GetCombatStartupDecision(true, 0) == CombatStartupDecision.WaitingForUnmount
       && ActivityParticipationState.GetCombatStartupDecision(true, 7999) == CombatStartupDecision.WaitingForUnmount
       && ActivityParticipationState.GetCombatStartupDecision(true, 8000) == CombatStartupDecision.TimedOut,
    "Combat automation must wait for a confirmed dismount and fail closed after the bounded retry window.");

Assert(SupportJobLevelingPolicy.ShouldKeepCurrent(JobId.Freelancer, 1, 24)
       && SupportJobLevelingPolicy.ShouldKeepCurrent(JobId.Ninja, 1, 10)
       && !SupportJobLevelingPolicy.ShouldKeepCurrent(JobId.Ninja, 10, 10),
    "An unfinished Freelancer must remain eligible for automatic leveling like every other unlocked support job.");
var lowestSupportJob = SupportJobLevelingPolicy.SelectLowestIncomplete([
    new SupportJobLevelCandidate((byte)JobId.Freelancer, 5, 24),
    new SupportJobLevelCandidate((byte)JobId.Ninja, 3, 10),
    new SupportJobLevelCandidate((byte)JobId.WhiteMage, 2, 10),
    new SupportJobLevelCandidate((byte)JobId.BlackMage, 0, 10),
    new SupportJobLevelCandidate((byte)JobId.Dragoon, 10, 10),
]);
Assert(lowestSupportJob == (byte)JobId.WhiteMage,
    "Automatic leveling must compare actual levels instead of treating row-zero Freelancer as automatically lowest.");
var unfinishedFreelancer = SupportJobLevelingPolicy.SelectLowestIncomplete([
    new SupportJobLevelCandidate((byte)JobId.Freelancer, 1, 24),
    new SupportJobLevelCandidate((byte)JobId.WhiteMage, 2, 10),
]);
Assert(unfinishedFreelancer == (byte)JobId.Freelancer,
    "Freelancer must still be selected when its real level is the lowest unfinished level.");
var northCampShard = Aethernet.NorthBaseCamp.GetData();
Assert(ZoneData.IsWithinKnownAethernetRange(
           new Vector3(881.836f, 258.5f, 881.894f),
           northCampShard.Position,
           AethernetData.DISTANCE + 1f)
       && !ZoneData.IsWithinKnownAethernetRange(
           new Vector3(900f, 258.5f, 900f),
           northCampShard.Position,
           AethernetData.DISTANCE + 1f),
    "North Horn teleport validation must fall back to maintained shard coordinates when EventObj lookup is unavailable.");
var ruinedStreetsShard = Aethernet.RuinedStreetsFront.GetData();
var failedRuinedStreetsApproach = new Vector3(-384.575f, 39.15826f, -439.2537f);
Assert(!ZoneData.IsWithinKnownAethernetRange(
           failedRuinedStreetsApproach,
           ruinedStreetsShard.Position,
           AethernetData.DISTANCE)
       && ZoneData.IsWithinKnownAethernetRange(
           failedRuinedStreetsApproach,
           ruinedStreetsShard.Position,
           AethernetData.DISTANCE + 1f),
    "Source aethernet approach must reject the former 5.2-yalm allowance so Lifestream can actually interact.");
var maintainedAethernets = ZoneData.GetAethernets(ZoneData.SOUTHHORN)
    .Concat(ZoneData.GetAethernets(ZoneData.NORTHHORN))
    .Select(aethernet => aethernet.GetData())
    .ToArray();
Assert(maintainedAethernets.All(shard =>
           shard.IsWithinLandingRange(shard.Destination, AethernetData.DISTANCE))
       && maintainedAethernets.All(shard =>
           !shard.IsWithinLandingRange(
               shard.Destination + new Vector3(AethernetData.DISTANCE + 0.01f, 0f, 0f),
               AethernetData.DISTANCE)),
    "Post-teleport validation must use the maintained landing point and reject positions outside 4.2 yalms.");
var northHornLayerTargets = ZoneData.GetAethernets(ZoneData.NORTHHORN)
    .Select(aethernet => aethernet.GetData())
    .ToArray();
Assert(northHornLayerTargets.All(shard => shard.NavigationPositionOverride.HasValue)
       && northHornLayerTargets.All(shard =>
           Vector3.Distance(shard.NavigationPosition, shard.Position) <= AethernetData.DISTANCE)
       && northHornLayerTargets.All(shard => shard.NavigationPosition != shard.Position),
    "North Horn LAYERS navigation targets must remain on the reachable mesh and inside aethernet interaction range.");
Assert(Aethernet.BaseCamp.GetData().NavigationPosition == Aethernet.BaseCamp.GetData().Position,
    "Territories without a mesh-specific override must keep using the interaction coordinate for navigation.");
Assert(!AutomatorChainPolicy.IsActive([])
       && !AutomatorChainPolicy.IsActive([(false, 0), (false, 0)])
       && AutomatorChainPolicy.IsActive([(true, 0)])
       && AutomatorChainPolicy.IsActive([(false, 1)]),
    "Only running or queued chains may block the automator; retained empty queues must not stall navigation.");
Assert(typeof(CarrotHunt).GetMethod(
           "Teardown",
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null,
    "Repeating carrot routes must use Hunter's centralized teardown so route-generation and interaction state are reset.");
Assert(typeof(CarrotHunt).GetMethod(
           "ShouldStopEarly",
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null,
    "Carrot hunting must stop immediately when Fortune Carrots run out instead of repeating an empty route.");

var southFates = EventData.GetFatesForTerritory(ZoneData.SOUTHHORN).ToList();
var northFates = EventData.GetFatesForTerritory(ZoneData.NORTHHORN).ToList();
var southCriticalEncounters = EventData.GetCriticalEncountersForTerritory(ZoneData.SOUTHHORN).ToList();
var northCriticalEncounters = EventData.GetCriticalEncountersForTerritory(ZoneData.NORTHHORN).ToList();

var fate2075 = EventData.Fates[2075];
var wispLanding = Aethernet.WillOWispVillage.GetData().Destination;
Assert(fate2075.Aethernet == Aethernet.SunkenTempleFront,
    "FATE 2075 must teleport to the north-shore aethernet instead of Will-o'-the-Wisp Village.");
Assert(NorthHornSouthCrossingRoute.TryCreate(fate2075, wispLanding, out var southCrossingRoute),
    "FATE 2075 must use the South Crossing transit profile from Will-o'-the-Wisp Village.");
Assert(southCrossingRoute.Count == 92,
    "FATE 2075 South Crossing route must retain every verified land waypoint.");
Assert(Vector3.Distance(southCrossingRoute[^1], new Vector3(510f, 15.65f, -30f)) < 0.01f,
    "FATE 2075 South Crossing route must end on the east-bank approach.");
Assert(Vector3.Distance(southCrossingRoute[^1], fate2075.StartPosition!.Value) <= NorthHornSouthCrossingRoute.ArrivalDistance,
    "FATE 2075 South Crossing route must finish inside the event-arrival radius without a fallback pathfind.");
Assert(Vector3.Distance(wispLanding, southCrossingRoute[0]) <= 15.01f
       && southCrossingRoute.Zip(southCrossingRoute.Skip(1), Vector3.Distance).All(distance => distance <= 15.01f),
    "Every FATE 2075 South Crossing leg must remain inside the verified direct-follow radius.");
Assert(southCrossingRoute.All(point => point.Y > 1f),
    "FATE 2075 South Crossing route must not include the river-water shortcut.");
Assert(southCrossingRoute.Any(point => point.X is > 175f and < 256f && point.Z is > -470f and < -420f),
    "FATE 2075 South Crossing route is missing the verified southern lowland transit segment.");
Assert(!NorthHornSouthCrossingRoute.ShouldUse(fate2075, fate2075.StartPosition!.Value),
    "The FATE 2075 South Crossing profile must not override an already-east-bank route.");
Assert(!NorthHornSouthCrossingRoute.ShouldUse(EventData.Fates[2076], wispLanding),
    "The South Crossing profile must not affect another North Horn FATE.");

var southCrossingPathfindCalls = new List<(Vector3 Start, Vector3 Destination)>();
var southCrossingPlan = await SmartNavigation.DecideAsync(
    wispLanding,
    fate2075.StartPosition!.Value,
    fate2075,
    new[] { Aethernet.WillOWispVillage.GetData() },
    Aethernet.NorthBaseCamp.GetData(),
    (start, destination, _) =>
    {
        southCrossingPathfindCalls.Add((start, destination));
        throw new InvalidOperationException("No generic route");
    },
    returnCost: 300f,
    teleportCost: 50f,
    destinationCandidateCount: 1,
    sourceCandidateCount: 1,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(!southCrossingPlan.UsedFallback
       && southCrossingPlan.Candidates.Any(candidate => candidate.Type == NavigationType.Walk)
       && southCrossingPathfindCalls.All(call =>
           call.Start != wispLanding || call.Destination != fate2075.StartPosition!.Value),
    "FATE 2075 must price its fixed South Crossing route without requesting the known-bad generic vnavmesh segment.");

Assert(southFates.Count == 13, $"Expected 13 South Horn FATEs, got {southFates.Count}.");
Assert(northFates.Count == 13, $"Expected 13 North Horn FATEs, got {northFates.Count}.");
Assert(southCriticalEncounters.Count == 16, $"Expected 16 South Horn dynamic events, got {southCriticalEncounters.Count}.");
Assert(northCriticalEncounters.Count == 17, $"Expected 17 North Horn dynamic events, got {northCriticalEncounters.Count}.");
Assert(northFates.Count(fate => fate.IsPot) == 2, "North Horn must contain exactly two pot FATEs.");
Assert(northCriticalEncounters.Count(encounter => encounter.Id is >= 49 and <= 63) == 15,
    "North Horn must contain CE IDs 49 through 63.");
Assert(northCriticalEncounters.Any(encounter => encounter.Id == 64 && encounter.InternalName == "两岐塔 魔之塔"),
    "North Horn normal tower event 64 is missing.");
Assert(northCriticalEncounters.Any(encounter => encounter.Id == 65 && encounter.InternalName == "两歧塔 超魔之塔"),
    "North Horn high-difficulty tower event 65 is missing.");

var northTowerDefinitions = TowerHelper.GetDefinitionsForTerritory(ZoneData.NORTHHORN);
Assert(northTowerDefinitions.Count == 2 &&
       northTowerDefinitions.Select(definition => definition.DynamicEventId).SequenceEqual(new uint[] { 64, 65 }),
    "North Horn tower definitions must independently map dynamic events 64 and 65.");
Assert(northTowerDefinitions.All(definition => !definition.HasPlatformGeometry),
    "North Horn platform geometry must remain unset until captured from the live client.");

var towerClock = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);
var magicTowerCycle = new TowerCycleState(towerClock);
var grandMagicTowerCycle = new TowerCycleState(towerClock);
magicTowerCycle.RecordFate();
magicTowerCycle.RecordCriticalEncounter();
Assert(magicTowerCycle.FatesCompleted == 1 && magicTowerCycle.CriticalEncountersCompleted == 1,
    "Magic Tower cycle did not record its modifiers.");
Assert(grandMagicTowerCycle.FatesCompleted == 0 && grandMagicTowerCycle.CriticalEncountersCompleted == 0,
    "North Horn tower cycle counters must not share mutable state.");
magicTowerCycle.MarkEnded(towerClock.AddMinutes(10));
Assert(magicTowerCycle.FatesCompleted == 0 && magicTowerCycle.CriticalEncountersCompleted == 0 &&
       grandMagicTowerCycle.LastTowerEnd == towerClock,
    "Ending one North Horn tower must reset only that tower cycle.");

var northAethernets = ZoneData.GetAethernets(ZoneData.NORTHHORN);
Assert(northAethernets.Count == 6, $"Expected 6 North Horn aethernet entries, got {northAethernets.Count}.");
Assert(northAethernets.Distinct().Count() == northAethernets.Count, "North Horn aethernet IDs must be unique.");
Assert(Aethernet.NorthBaseCamp.GetData().BaseId == 2015429, "North Horn base-camp EObj ID is incorrect.");
Assert(Aethernet.KarnakCitadel.GetData().BaseId == 2015434, "Karnak Citadel EObj ID is incorrect.");
Assert(EventData.CriticalEncounters[63].Aethernet == Aethernet.SunkenTempleFront,
    "Morphing Mage must teleport to the north-shore aethernet instead of Will-o'-the-Wisp Village.");

Assert((int)JobId.Ninja == 16 && (int)JobId.Necromancer == 23, "North Horn support-job IDs are incorrect.");
Assert((uint)PlayerStatus.PhantomNinja == 5328 && (uint)PlayerStatus.PhantomNecromancer == 5335,
    "North Horn support-job status IDs are incorrect.");
Assert((uint)PlayerStatus.MagicEvasion == 5316 &&
       (uint)PlayerStatus.DragonSword == 5319 &&
       (uint)PlayerStatus.EarthenWall == 5320 &&
       (uint)PlayerStatus.MagicMightyGuard == 5321 &&
       (uint)PlayerStatus.SmokeBomb == 5327,
    "North Horn support-action status IDs are not aligned with the CN 7.55 Status sheet.");
var northSupportActions = NorthHornSupportActionData.All.OrderBy(set => set.SupportJobRowId).ToArray();
Assert(northSupportActions.Length == 8 &&
       northSupportActions.Select(set => set.SupportJobRowId).SequenceEqual(Enumerable.Range(16, 8).Select(id => (uint)id)),
    "North Horn MKDSupportJob rows 16-23 are incomplete.");
Assert(northSupportActions.All(set => set.Actions.Select(action => action.GeneralActionId)
        .SequenceEqual(Enumerable.Range(31, set.Actions.Count).Select(id => (uint)id))),
    "North Horn support-job GeneralAction slots must remain contiguous from slot 31.");
Assert(NorthHornSupportActionData.Get(JobId.BlueMage).Actions.Last().ActionRowId == 49090 &&
       NorthHornSupportActionData.Get(JobId.RedMage).Actions.First().ActionRowId == 49092 &&
       NorthHornSupportActionData.Get(JobId.Necromancer).Actions.Last().ActionRowId == 49101,
    "North Horn Action-sheet rows are not aligned with the CN 7.55 MKDSupportJob sheet.");
Assert((uint)MonsterNote.AncientGrimoire == 51979 && (uint)MonsterNote.CalofisteriDoppelganger == 51988,
    "North Horn survey-record item IDs are incorrect.");
Assert(Enum.GetValues<SoulShard>()
        .Where(soulShard => (uint)soulShard is >= 51967 and <= 51974)
        .Select(soulShard => (uint)soulShard)
        .SequenceEqual(Enumerable.Range(51967, 8).Select(id => (uint)id)),
    "North Horn Soul Shard item IDs 51967-51974 are incomplete or out of order.");

var confirmedNorthHornNoteDrops = new Dictionary<uint, MonsterNote>
{
    [50] = MonsterNote.CalofisteriDoppelganger,
    [51] = MonsterNote.AlabasterBlade,
    [52] = MonsterNote.AncientGrimoire,
    [53] = MonsterNote.RedDragon,
    [54] = MonsterNote.Algol,
    [57] = MonsterNote.PhantomNecromancer,
    [59] = MonsterNote.PallidDemon,
    [60] = MonsterNote.LittleMage,
    [61] = MonsterNote.KidnapperDemon,
    [63] = MonsterNote.MorphingMage,
};
var actualNorthHornNoteDrops = northCriticalEncounters
    .Where(encounter => encounter.Id is >= 49 and <= 63 && encounter.Note is not null)
    .ToDictionary(encounter => encounter.Id, encounter => encounter.Note!.Value);
Assert(actualNorthHornNoteDrops.Count == confirmedNorthHornNoteDrops.Count,
    $"Expected {confirmedNorthHornNoteDrops.Count} confirmed North Horn CE record drops, got {actualNorthHornNoteDrops.Count}.");
foreach (var (eventId, expectedNote) in confirmedNorthHornNoteDrops)
{
    Assert(actualNorthHornNoteDrops.TryGetValue(eventId, out var actualNote) && actualNote == expectedNote,
        $"North Horn CE {eventId} has an incorrect Investigation Record mapping.");
}

var towerOnlyRecords = new[]
{
    MonsterNote.Amphiptere,
    MonsterNote.SwordDancer,
    MonsterNote.DeathDefier,
    MonsterNote.Catalog,
};
Assert(!northCriticalEncounters.Any(encounter =>
        encounter.Id is >= 49 and <= 63 &&
        encounter.Note is { } note &&
        towerOnlyRecords.Contains(note)),
    "Tower-only Investigation Records 51989-51992 must not be assigned to an ordinary CE.");

var recordAlertConfig = new CriticalEncountersConfig { AlertInvestigationRecords = true };
Assert(confirmedNorthHornNoteDrops.Keys.All(eventId =>
        recordAlertConfig.ShouldAlertForRewards(EventData.CriticalEncounters[eventId])),
    "The Investigation Record alert option must cover every confirmed North Horn CE record drop.");

foreach (var soulShard in Enum.GetValues<SoulShard>())
{
    var soulShardAlertConfig = new CriticalEncountersConfig();
    var property = typeof(CriticalEncountersConfig).GetProperty($"Alert{soulShard}");
    Assert(property is { CanWrite: true, PropertyType: { } propertyType } && propertyType == typeof(bool),
        $"Soul Shard alert configuration is missing the Alert{soulShard} switch.");
    property!.SetValue(soulShardAlertConfig, true);

    Assert(soulShardAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = soulShard }),
        $"The dedicated Soul Shard switch does not enable {soulShard} ({(uint)soulShard}).");
    Assert(Enum.GetValues<SoulShard>()
            .Where(other => other != soulShard)
            .All(other => !soulShardAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = other })),
        $"The Alert{soulShard} switch incorrectly enables another Soul Shard reward.");
}
Assert(!northCriticalEncounters.Any(encounter => encounter.Soulshard is not null),
    "North Horn Soul Shard sources are unconfirmed and must not be guessed in CE reward data.");
Assert(!recordAlertConfig.ShouldAlertForRewards(EventData.CriticalEncounters[49]),
    "A North Horn CE without a confirmed Investigation Record must not trigger the record alert.");

var legacyAlertPropertyNames = new[] { "AlertOracle", "AlertBerserker", "AlertRanger" };
Assert(legacyAlertPropertyNames.All(name =>
        typeof(CriticalEncountersConfig).GetProperty(name) is { CanRead: true, CanWrite: true, PropertyType: { } type } &&
        type == typeof(bool)),
    "Legacy Oracle/Berserker/Ranger configuration properties must remain public writable booleans.");
var legacyAlertConfig = new CriticalEncountersConfig
{
    AlertOracle = true,
    AlertBerserker = true,
    AlertRanger = true,
};
Assert(legacyAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = SoulShard.Oracle }) &&
       legacyAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = SoulShard.Berserker }) &&
       legacyAlertConfig.ShouldAlertForRewards(new EventData { Soulshard = SoulShard.Ranger }),
    "Legacy Oracle/Berserker/Ranger alert properties must remain configuration-compatible.");

var northHornMobs = Enum.GetValues<Mob>()
    .Where(mob => (uint)mob is >= 14857 and <= 14923)
    .ToArray();
Assert(northHornMobs.Length == 67, $"Expected 67 North Horn BNpcName rows, got {northHornMobs.Length}.");
Assert((uint)Mob.CrescentCliffkite == 14857 && (uint)Mob.CrescentFlame == 14923,
    "North Horn field-monster ID range is incorrect.");
Assert((uint)Mob.CrescentBibliotaph == 14860 && (uint)Mob.CrescentOiseauRare == 14910,
    "North Horn field-monster semantic names are not aligned with the 7.55 BNpcName sheet.");

foreach (var mob in northHornMobs)
{
    Assert(CommonMobCatalog.TryGet((uint)mob, out var profile),
        $"North Horn ordinary mob {(uint)mob} is missing from the aggro catalog.");
    Assert(MathF.Abs(profile.FallbackEdgeRange - 10f) < 0.001f,
        $"North Horn ordinary mob {(uint)mob} still uses model scale as a fake aggro range.");
}
Assert(!CommonMobCatalog.TryGet(14856, out _) && !CommonMobCatalog.TryGet(14924, out _),
    "Aggro catalog must not include actors outside the 67 ordinary North Horn BNpcName rows.");

var calibration = new AggroRangeCalibration();
Assert(!calibration.AddSample(3.9f) && !calibration.AddSample(20.1f),
    "Aggro calibration must reject implausible passive-pull distances.");
Assert(calibration.AddSample(8f) && calibration.AddSample(10f) && calibration.AddSample(9f),
    "Aggro calibration rejected valid passive-pull samples.");
Assert(MathF.Abs(calibration.ResolveEdgeRange(10f) - 10.5f) < 0.001f,
    "Aggro calibration must use the conservative upper quartile plus frame-latency allowance.");
var calibratedConfig = new AggroRangeConfig
{
    Calibrations = new Dictionary<uint, AggroRangeCalibration> { [14857] = calibration },
};
CommonMobCatalog.TryGet(14857, out var calibratedProfile);
Assert(MathF.Abs(AggroRangeResolver.ResolveTriggerRadius(calibratedProfile, 2f, calibratedConfig) - 12.5f) < 0.001f,
    "Resolved aggro radius must add the live monster hitbox exactly once.");

var crystal = new Vector3(10f, 2f, 10f);
var crystalApproach = KnowledgeCrystalApproachPolicy.GetDesiredApproachPosition(
    new Vector3(20f, 2f, 10f),
    crystal);
Assert(MathF.Abs(Vector3.Distance(crystalApproach, crystal) - KnowledgeCrystalApproachPolicy.DesiredOffset) < 0.001f,
    "Knowledge-crystal approach point must stay inside the verified cast range.");
Assert(KnowledgeCrystalApproachPolicy.HasArrived(new Vector3(13f, 2f, 10f), crystal)
       && !KnowledgeCrystalApproachPolicy.HasArrived(new Vector3(13.01f, 2f, 10f), crystal),
    "Knowledge-crystal arrival must be based on real distance, not vnavmesh stopping.");

var treasurePattern = LogMessageHelper.BuildPattern(
    "在当前区域中感知到了<num(lnum1)>个银宝箱、<num(lnum2)>个铜宝箱……！");
var treasureMatch = Regex.Match("在当前区域中感知到了3个银宝箱、17个铜宝箱……！", treasurePattern);
Assert(treasureMatch.Success, "CN treasure-count LogMessage pattern did not match rendered text.");
Assert(treasureMatch.Groups["lnum1"].Value == "3" && treasureMatch.Groups["lnum2"].Value == "17",
    "CN treasure-count LogMessage captures are incorrect.");

var retainedFateHandles = typeof(BOCCHI.Modules.Fates.Fate)
    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    .Where(field => field.FieldType.FullName == "Dalamud.Game.ClientState.Fates.IFate")
    .ToArray();
Assert(retainedFateHandles.Length == 0,
    "Fate snapshots must not retain an IFate backed by game memory after despawn.");
var retainedDynamicEvents = typeof(CriticalEncounterTracker)
    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    .Where(field => field.FieldType.FullName ==
                    "FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent")
    .ToArray();
Assert(retainedDynamicEvents.Length == 0,
    "Critical Encounter tracking must not retain a native DynamicEvent across frames.");
Assert(typeof(CriticalEncounterSnapshot).GetProperties().All(property =>
        property.PropertyType.FullName is not
            "FFXIVClientStructs.FFXIV.Client.System.String.Utf8String" and not
            "FFXIVClientStructs.FFXIV.Client.Game.UI.MapMarkerData"),
    "Critical Encounter snapshots must copy native strings and map markers into managed values.");
var clientStructsAssembly = Assembly.Load(
    typeof(CriticalEncounterTracker).Assembly.GetReferencedAssemblies()
        .Single(reference => reference.Name == "FFXIVClientStructs"));
var dynamicEventType = clientStructsAssembly.GetType(
    "FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent",
    throwOnError: true)!;
Assert(Marshal.OffsetOf(dynamicEventType, "SecondsRegistrationTime").ToInt64() == 0x6C
       && Marshal.OffsetOf(dynamicEventType, "SecondsWarmupTime").ToInt64() == 0x70,
    "CN 7.55 DynamicEvent timing offsets changed; update the managed snapshot before release.");

var automatorConfig = new AutomatorConfig();
var northFateIds = Enumerable.Range(2072, 13).Select(id => (uint)id).ToArray();
var northCriticalEncounterIds = Enumerable.Range(49, 15).Select(id => (uint)id).ToArray();

Assert(PromeRotationController.PluginInternalName == "PromeRotation"
       && PromeRotationController.StartIpcName == "PromeRotation.IPC.Start"
       && PromeRotationController.StopIpcName == "PromeRotation.IPC.Stop"
       && PromeRotationController.IsRunningIpcName == "PromeRotation.IPC.IsRunning"
       && PromeRotationController.AutoPullOnCommand == "/pr autopull on"
       && PromeRotationController.AutoPullOffCommand == "/pr autopull off",
    "PromeRotation integration must retain its official IPC names and AutoPull commands.");
Assert(typeof(PromeRotationController).GetMethod(nameof(PromeRotationController.Start))?.ReturnType == typeof(void)
       && typeof(PromeRotationController).GetMethod(nameof(PromeRotationController.Stop))?.ReturnType == typeof(void),
    "PromeRotation start/stop must be fire-and-forget so IPC false cannot block an Ocelot chain.");

Assert(NavigationActivityState.IsActive(false, true, false)
       && NavigationActivityState.IsActive(false, false, true),
    "Automator must keep one activity chain alive while vnavmesh SimpleMove/Nav is calculating a route.");
Assert(!NavigationActivityState.IsActive(false, false, false),
    "Automator must still detect a genuinely stopped vnavmesh route.");
Assert(!ActivityParticipationState.HasEnded(false, State.Idle),
    "Automator must not reselect the same activity while waiting for initial FATE/CE participation.");
Assert(ActivityParticipationState.IsInsideActivity(State.InFate)
       && ActivityParticipationState.IsInsideActivity(State.InCriticalEncounter)
       && !ActivityParticipationState.IsInsideActivity(State.InCombat),
    "Only an observed FATE/CE state may arm activity-completion detection.");
Assert(ActivityParticipationState.HasEnded(true, State.Idle),
    "Automator must finish an activity after an observed FATE/CE returns to Idle.");

Assert(automatorConfig.FatesMap.Keys.Where(northFateIds.Contains).Order().SequenceEqual(northFateIds),
    "Automator must expose one mapping for every North Horn FATE ID 2072 through 2084.");
Assert(automatorConfig.CriticalEncountersMap.Keys.Where(northCriticalEncounterIds.Contains).Order()
        .SequenceEqual(northCriticalEncounterIds),
    "Automator must expose one mapping for every ordinary North Horn CE ID 49 through 63.");
Assert(!automatorConfig.CriticalEncountersMap.ContainsKey(64)
       && !automatorConfig.CriticalEncountersMap.ContainsKey(65),
    "Forked Tower events 64/65 must remain under the tower module, not Automator participation.");
Assert(!automatorConfig.FatesMap[2072] && !automatorConfig.FatesMap[2073],
    "North Horn pot FATE replacements 2072/2073 must preserve the legacy opt-in default.");
Assert(northFateIds.Where(id => id >= 2074).All(id => automatorConfig.FatesMap[id]),
    "Ordinary North Horn Automator FATE switches must be enabled by default.");
Assert(northCriticalEncounterIds.All(id => automatorConfig.CriticalEncountersMap[id]),
    "North Horn Automator CE switches must retain the previous enabled-by-default behavior.");

foreach (var id in northFateIds)
{
    var isolatedConfig = new AutomatorConfig();
    var property = typeof(AutomatorConfig).GetProperty($"DoNorthHornFate{id}");
    Assert(property != null, $"North Horn FATE {id} is missing its dedicated Automator setting.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "CheckboxAttribute"),
        $"North Horn FATE {id} setting is not rendered as a checkbox.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "DependsOnAttribute"),
        $"North Horn FATE {id} setting is not tied to the master FATE switch.");
    property!.SetValue(isolatedConfig, true);
    Assert(isolatedConfig.FatesMap[id], $"North Horn FATE {id} switch is not bound to its map entry.");
    property!.SetValue(isolatedConfig, false);
    Assert(!isolatedConfig.FatesMap[id], $"Disabling North Horn FATE {id} does not disable its map entry.");
}

foreach (var id in northCriticalEncounterIds)
{
    var isolatedConfig = new AutomatorConfig();
    var property = typeof(AutomatorConfig).GetProperty($"DoNorthHornCe{id}");
    Assert(property != null, $"North Horn CE {id} is missing its dedicated Automator setting.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "CheckboxAttribute"),
        $"North Horn CE {id} setting is not rendered as a checkbox.");
    Assert(property!.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "DependsOnAttribute"),
        $"North Horn CE {id} setting is not tied to the master CE switch.");
    property!.SetValue(isolatedConfig, false);
    Assert(!isolatedConfig.CriticalEncountersMap[id],
        $"Disabling North Horn CE {id} does not disable its map entry.");
    Assert(northCriticalEncounterIds.Where(other => other != id)
            .All(other => isolatedConfig.CriticalEncountersMap[other]),
        $"Disabling North Horn CE {id} incorrectly changed another CE switch.");
}

Assert(typeof(AutomatorConfig).GetProperty("DoNorthHornCe64") == null
       && typeof(AutomatorConfig).GetProperty("DoNorthHornCe65") == null,
    "Forked Tower Automator checkboxes must not be rendered as dead controls.");

var legacyConfig = new Config
{
    Version = 1,
    AutomatorConfig = new AutomatorConfig
    {
        DoPersistentPots = true,
        DoPleadingPots = false,
    },
};
Assert(legacyConfig.Migrate(), "Legacy configuration version 1 was not migrated.");
Assert(legacyConfig.Version == Config.CurrentVersion
       && legacyConfig.AutomatorConfig.DoNorthHornFate2072
       && !legacyConfig.AutomatorConfig.DoNorthHornFate2073,
    "Legacy pot FATE selections were not copied to North Horn FATE 2072/2073.");
Assert(!legacyConfig.Migrate(), "Configuration migration must be idempotent.");

automatorConfig.DoFates = false;
automatorConfig.DoCriticalEncounters = false;
Assert(northFateIds.All(id => !automatorConfig.FatesMap[id]),
    "The master FATE switch must suppress all North Horn FATE settings.");
Assert(northCriticalEncounterIds.All(id => !automatorConfig.CriticalEncountersMap[id]),
    "The master CE switch must suppress all North Horn CE settings.");

Assert(automatorConfig.InitialInstanceArea == InstanceEntryArea.NorthHorn
       && !automatorConfig.AutoRotateInstance && automatorConfig.InstanceStayMinutes == 90f
       && !automatorConfig.RotateWhenPopulationLow && automatorConfig.MinimumInstancePopulation == 10,
    "Unattended instance rotation must remain opt-in with conservative defaults.");
foreach (var propertyName in new[]
         {
             nameof(AutomatorConfig.InitialInstanceArea),
             nameof(AutomatorConfig.AutoRotateInstance), nameof(AutomatorConfig.InstanceStayMinutes),
             nameof(AutomatorConfig.RotateWhenPopulationLow), nameof(AutomatorConfig.MinimumInstancePopulation),
         })
{
    Assert(typeof(AutomatorConfig).GetProperty(propertyName)?.GetCustomAttributes().Any() == true,
        $"Instance rotation setting {propertyName} must be rendered by the configuration UI.");
}

var rotationStart = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
var initialNorthEntry = new InstanceRotationStateMachine();
Assert(initialNorthEntry.BeginEntryFromOutside(rotationStart, ZoneData.NORTHHORN)
       == InstanceRotationAction.EnterNorthHorn
       && initialNorthEntry.State == InstanceRotationState.WaitingForEntry
       && initialNorthEntry.OriginalTerritoryId == ZoneData.NORTHHORN,
    "Starting automation outside must request North Horn exactly once when configured.");
Assert(initialNorthEntry.BeginEntryFromOutside(rotationStart.AddSeconds(1), ZoneData.NORTHHORN)
       == InstanceRotationAction.None,
    "An in-progress outside entry must not emit the entry command again.");
var initialOutsideInput = new InstanceRotationInput(
    true,
    0,
    false,
    TimeSpan.MaxValue,
    false);
Assert(initialNorthEntry.Update(rotationStart.AddSeconds(2), initialOutsideInput)
       == InstanceRotationAction.None,
    "Waiting outside must remain command-silent after the initial North Horn request.");
Assert(initialNorthEntry.Update(
           rotationStart.AddSeconds(3),
           initialOutsideInput with { TerritoryId = ZoneData.NORTHHORN }) == InstanceRotationAction.None
       && initialNorthEntry.State == InstanceRotationState.Monitoring,
    "Arriving in the configured North Horn territory must complete initial entry.");

var initialSouthEntry = new InstanceRotationStateMachine();
Assert(initialSouthEntry.BeginEntryFromOutside(rotationStart, ZoneData.SOUTHHORN)
       == InstanceRotationAction.EnterSouthHorn
       && initialSouthEntry.OriginalTerritoryId == ZoneData.SOUTHHORN,
    "Starting automation outside must support the configured South Horn entry command.");
var invalidInitialEntry = new InstanceRotationStateMachine();
Assert(invalidInitialEntry.BeginEntryFromOutside(rotationStart, 0) == InstanceRotationAction.None
       && invalidInitialEntry.State == InstanceRotationState.Failed
       && invalidInitialEntry.FailureReason == "invalid_entry_territory",
    "Outside entry must reject an unknown territory without emitting a command.");

var rotation = new InstanceRotationStateMachine();
Assert(InstanceDutyTimerProvider.AddonName == "_ToDoList"
       && InstanceDutyTimerPolicy.TryParse("\uE0BB 153:02", out var parsedDutyTime)
       && parsedDutyTime == TimeSpan.FromMinutes(153) + TimeSpan.FromSeconds(2)
       && !InstanceDutyTimerPolicy.TryParse("153:60", out _)
       && !InstanceDutyTimerPolicy.TryParse("181:00", out _),
    "Instance rotation must parse the actual _ToDoList duty timer and reject invalid values.");
Assert(!InstanceDutyTimerPolicy.HasStayDurationElapsed(TimeSpan.FromMinutes(165).Add(TimeSpan.FromSeconds(1)), TimeSpan.FromMinutes(15))
       && InstanceDutyTimerPolicy.HasStayDurationElapsed(TimeSpan.FromMinutes(165), TimeSpan.FromMinutes(15)),
    "Actual duty time must drive the configured stay-duration boundary.");

var monitoringInput = new InstanceRotationInput(
    true,
    ZoneData.SOUTHHORN,
    true,
    TimeSpan.FromMinutes(90),
    false,
    TimeSpan.FromMinutes(153).Add(TimeSpan.FromSeconds(2)));
Assert(rotation.Update(rotationStart, monitoringInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Monitoring,
    "Instance rotation must begin by monitoring the current Occult Crescent territory.");

var missingTimerRotation = new InstanceRotationStateMachine();
var missingTimerInput = monitoringInput with { InstanceTimeRemaining = null };
missingTimerRotation.Update(rotationStart, missingTimerInput);
Assert(missingTimerRotation.Update(rotationStart.AddHours(3), missingTimerInput) == InstanceRotationAction.None
       && missingTimerRotation.State == InstanceRotationState.Monitoring,
    "A missing Duty Information addon timer must fail closed instead of using BOCCHI's local uptime.");

var unsafeLowPopulationInput = monitoringInput with { CanStart = false, PopulationLow = true };
Assert(rotation.Update(rotationStart.AddMinutes(1), unsafeLowPopulationInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Monitoring,
    "Low population must not force an exit during a FATE, CE, combat, or active automation task.");

var safeLowPopulationInput = monitoringInput with { PopulationLow = true };
Assert(rotation.Update(rotationStart.AddMinutes(1).AddSeconds(1), safeLowPopulationInput)
       == InstanceRotationAction.RequestExit,
    "A confirmed low-population condition must request one safe exit.");
Assert(rotation.Reason == InstanceRotationReason.PopulationLow
       && rotation.Update(rotationStart.AddMinutes(1).AddSeconds(2), safeLowPopulationInput)
       == InstanceRotationAction.None,
    "The exit command must not be emitted again while waiting to leave.");

var outsideInput = monitoringInput with { TerritoryId = 0, CanStart = false, PopulationLow = false };
var leftAt = rotationStart.AddMinutes(1).AddSeconds(3);
Assert(rotation.Update(leftAt, outsideInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Cooldown,
    "The 15-second re-entry delay must begin only after the original territory has been left.");
Assert(rotation.Update(leftAt.AddSeconds(14.999), outsideInput) == InstanceRotationAction.None,
    "Instance rotation must wait the full 15 seconds before requesting re-entry.");
Assert(rotation.Update(leftAt.AddSeconds(15), outsideInput) == InstanceRotationAction.EnterSouthHorn
       && rotation.State == InstanceRotationState.WaitingForEntry,
    "South Horn rotation must select the ocs entry action after the full cooldown.");
Assert(rotation.Update(leftAt.AddSeconds(16), outsideInput) == InstanceRotationAction.None,
    "The entry command must not be emitted again while waiting for territory confirmation.");
Assert(rotation.Update(leftAt.AddSeconds(20), monitoringInput) == InstanceRotationAction.None
       && rotation.State == InstanceRotationState.Monitoring
       && rotation.IslandEnteredAt == leftAt.AddSeconds(20),
    "Returning to the original territory must reset the stay timer for the next cycle.");

var northRotation = new InstanceRotationStateMachine();
var northInput = new InstanceRotationInput(
    true,
    ZoneData.NORTHHORN,
    true,
    TimeSpan.FromMinutes(15),
    false,
    InstanceDutyTimerPolicy.OccultCrescentDuration);
northRotation.Update(rotationStart, northInput);
Assert(northRotation.Update(rotationStart.AddMinutes(15), northInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(165) })
       == InstanceRotationAction.RequestExit
       && northRotation.Reason == InstanceRotationReason.StayTimeElapsed,
    "The configured stay duration must use the actual duty timer rather than time since BOCCHI was enabled.");
northRotation.Update(rotationStart.AddMinutes(15).AddSeconds(1), northInput with { TerritoryId = 0 });
Assert(northRotation.Update(rotationStart.AddMinutes(15).AddSeconds(16), northInput with { TerritoryId = 0 })
       == InstanceRotationAction.EnterNorthHorn,
    "North Horn rotation must select the ocn entry action.");
Assert(InstanceRotationController.LeaveCommand == "/pdr leaveduty"
       && InstanceRotationController.SouthHornEntryCommand == "/pdrfe ocs"
       && InstanceRotationController.NorthHornEntryCommand == "/pdrfe ocn",
    "DailyRoutines rotation commands must remain aligned with the verified local command modules.");
Assert(DailyRoutinesModuleBridge.PluginInternalName == "DailyRoutines"
       && DailyRoutinesModuleBridge.IsModuleEnabledIpcName == "DailyRoutines.IsModuleEnabled"
       && DailyRoutinesModuleBridge.LoadModuleIpcName == "DailyRoutines.LoadModule"
       && DailyRoutinesModuleBridge.RequiredModuleNames.SequenceEqual(
           new[] { "AutoTalkSkip", "FieldEntryCommand" }),
    "pdrfe startup must enable its verified DailyRoutines prerequisite before the command module.");
Assert(!CriticalEncounterTracker.CanReadOccultCrescentEvents(false, true)
       && !CriticalEncounterTracker.CanReadOccultCrescentEvents(true, false)
       && CriticalEncounterTracker.CanReadOccultCrescentEvents(true, true),
    "CE tracking must stop reading PublicContentOccultCrescent after leaving the island.");

var cancelledRotation = new InstanceRotationStateMachine();
cancelledRotation.Update(rotationStart, monitoringInput);
cancelledRotation.Update(rotationStart.AddMinutes(90), monitoringInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(90) });
Assert(cancelledRotation.Update(rotationStart.AddMinutes(90).AddSeconds(1), monitoringInput with { Enabled = false })
       == InstanceRotationAction.None
       && cancelledRotation.State == InstanceRotationState.Idle,
    "Disabling automation must cancel an in-progress instance rotation.");

var timedOutRotation = new InstanceRotationStateMachine();
timedOutRotation.Update(rotationStart, monitoringInput);
timedOutRotation.Update(rotationStart.AddMinutes(90), monitoringInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(90) });
Assert(timedOutRotation.Update(rotationStart.AddMinutes(90) + InstanceRotationStateMachine.ExitTimeout,
           monitoringInput) == InstanceRotationAction.None
       && timedOutRotation.State == InstanceRotationState.Failed,
    "An unconfirmed exit must stop after its timeout instead of retrying every frame.");
Assert(timedOutRotation.Update(rotationStart.AddHours(3), monitoringInput) == InstanceRotationAction.None,
    "A failed rotation must remain command-silent until explicitly reset.");

var entryTimedOutRotation = new InstanceRotationStateMachine();
entryTimedOutRotation.Update(rotationStart, monitoringInput);
entryTimedOutRotation.Update(rotationStart.AddMinutes(90), monitoringInput with { InstanceTimeRemaining = TimeSpan.FromMinutes(90) });
var entryTimeoutLeftAt = rotationStart.AddMinutes(90).AddSeconds(1);
entryTimedOutRotation.Update(entryTimeoutLeftAt, outsideInput);
entryTimedOutRotation.Update(entryTimeoutLeftAt + InstanceRotationStateMachine.ReentryCooldown, outsideInput);
Assert(entryTimedOutRotation.Update(
           entryTimeoutLeftAt + InstanceRotationStateMachine.ReentryCooldown + InstanceRotationStateMachine.EntryTimeout,
           outsideInput) == InstanceRotationAction.None
       && entryTimedOutRotation.State == InstanceRotationState.Failed,
    "An unconfirmed re-entry must stop after its timeout without re-sending the entry command.");

var translationRoot = Path.Combine(Directory.GetCurrentDirectory(), "Translations");
Assert(Directory.Exists(translationRoot), $"Translation directory was not found: {translationRoot}");
var translationFiles = Directory.EnumerateFiles(translationRoot, "*.json", SearchOption.AllDirectories).ToArray();
Assert(translationFiles.Length >= 80, $"Expected the full translation tree, found only {translationFiles.Length} JSON files.");
foreach (var file in translationFiles)
{
    using var _ = JsonDocument.Parse(File.ReadAllText(file));
}

foreach (var language in new[] { "en", "fr", "jp", "zh" })
{
    var file = Path.Combine(translationRoot, language, "modules.automator.json");
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    var configKeys = document.RootElement
        .GetProperty("modules")
        .GetProperty("automator")
        .GetProperty("config");
    Assert(northFateIds.All(id => configKeys.TryGetProperty($"do_north_horn_fate{id}", out _)),
        $"{language} Automator translation is missing a North Horn FATE key.");
    Assert(northCriticalEncounterIds.All(id => configKeys.TryGetProperty($"do_north_horn_ce{id}", out _)),
        $"{language} Automator translation is missing a North Horn CE key.");
    Assert(!configKeys.TryGetProperty("do_north_horn_ce64", out _)
           && !configKeys.TryGetProperty("do_north_horn_ce65", out _),
        $"{language} Automator translation still exposes dead Forked Tower controls.");
    foreach (var key in new[]
             {
                 "initial_instance_area", "auto_rotate_instance", "instance_stay_minutes", "rotate_when_population_low",
                 "minimum_instance_population",
             })
    {
        Assert(configKeys.TryGetProperty(key, out _),
            $"{language} Automator translation is missing instance rotation key {key}.");
    }

    var rotationMessages = document.RootElement
        .GetProperty("modules")
        .GetProperty("automator")
        .GetProperty("messages")
        .GetProperty("rotation");
    Assert(rotationMessages.TryGetProperty("modules_enabling", out _),
        $"{language} Automator translation is missing the DailyRoutines module-enable message.");

    var criticalFile = Path.Combine(translationRoot, language, "modules.critical_encounters.json");
    using var criticalDocument = JsonDocument.Parse(File.ReadAllText(criticalFile));
    var criticalConfigKeys = criticalDocument.RootElement
        .GetProperty("modules")
        .GetProperty("critical_encounters")
        .GetProperty("config");
    foreach (var key in new[]
             {
                 "alert_ninja", "alert_black_mage", "alert_white_mage", "alert_dragoon",
                 "alert_summoner", "alert_blue_mage", "alert_red_mage", "alert_necromancer",
             })
    {
        Assert(criticalConfigKeys.TryGetProperty(key, out var entry)
               && entry.GetProperty("label").ValueKind == JsonValueKind.String
               && entry.GetProperty("tooltip").ValueKind == JsonValueKind.String,
            $"{language} {key} must explicitly state that its event source is pending confirmation.");
    }
}

var fallbackPositions = new Dictionary<uint, Vector3>
{
    [1] = new Vector3(1, 0, 0),
    [2] = new Vector3(10, 0, 0),
    [3] = new Vector3(5, 0, 0),
};
Assert(FallbackRoutePlanner.OrderByEuclideanCost(Vector3.Zero, new uint[] { 2, 99, 3, 1 }, fallbackPositions)
        .SequenceEqual(new uint[] { 1, 3, 2 }),
    "Euclidean fallback ordering must be deterministic and skip nodes without trusted positions.");

var partialGraph = new Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>>
{
    [1] = new() { [2] = (1f, [PathfinderStep.WalkToDestination(2)]) },
    [2] = new() { [3] = (1f, [PathfinderStep.WalkToDestination(3)]) },
    [3] = new(),
};
Assert(!HybridRoutePlanner.HasCompleteFiniteGraph(new uint[] { 1, 2, 3 }, partialGraph),
    "A partial hunt graph must not be classified as complete.");
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, new uint[] { 1, 2, 3 }, partialGraph)
        .SequenceEqual(new uint[] { 1, 2, 3 }),
    "Hybrid routing must consume every verified reachable edge before fallback.");
var deadEndGraph = new Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>>
{
    [1] = new()
    {
        [2] = (1f, [PathfinderStep.WalkToDestination(2)]),
        [3] = (2f, [PathfinderStep.WalkToDestination(3)]),
        [5] = (2f, [PathfinderStep.WalkToDestination(5)]),
    },
    [2] = new(),
    [3] = new() { [4] = (1f, [PathfinderStep.WalkToDestination(4)]) },
    [4] = new(),
    [5] = new() { [4] = (1f, [PathfinderStep.WalkToDestination(4)]) },
};
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, new uint[] { 5, 4, 3, 2, 1 }, deadEndGraph)
        .SequenceEqual(new uint[] { 1, 3, 4 }),
    "Hybrid routing must maximize verified-node coverage before cost and use node IDs as a deterministic tie-break.");
var unreachableGraph = new Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>>
{
    [1] = new() { [2] = (float.MaxValue, []) },
    [2] = new(),
};
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, new uint[] { 1, 2 }, unreachableGraph)
        .SequenceEqual(new uint[] { 1 }),
    "Hybrid routing must stop before an unreachable edge so fallback can append that node.");
Assert(HybridRoutePlanner.BuildReachableGreedyRoute(1, Array.Empty<uint>(), partialGraph).Count == 0,
    "Hybrid routing must return an empty prefix for an empty node set.");

var precomputeNodes = new[]
{
    new HuntNode(1, new Vector3(1, 2, 3)),
    new HuntNode(2, new Vector3(4, 5, 6)),
};
var precomputeAethernets = new[]
{
    new HuntAethernet(Aethernet.BaseCamp, new Vector3(10, 0, 10), new Vector3(11, 0, 11)),
};
var completedSegments = 0;
var computedData = await NodeDataPrecomputer.ComputeAsync(
    precomputeNodes,
    precomputeAethernets,
    (start, destination) => Task.FromResult(new List<Vector3> { start, destination }),
    _ => completedSegments++,
    segmentTimeout: TimeSpan.FromSeconds(1));
Assert(completedSegments == NodeDataPrecomputer.GetTaskCount(2, 1),
    "Node precomputation progress did not count every route segment.");
Assert(computedData.NodeToNodeDistances.Values.Sum(routes => routes.Count) == 2
       && computedData.NodeToAethernetDistances.Values.Sum(routes => routes.Count) == 2
       && computedData.AethernetToNodeDistances.Values.Sum(routes => routes.Count) == 2,
    "Node precomputation did not persist every successful route direction.");
Assert(computedData.NodeToAethernetDistances[1].Single().Path.First().X == precomputeNodes[0].Position.X
       && computedData.NodeToAethernetDistances[1].Single().Path.Last().X == precomputeAethernets[0].ShardPosition.X,
    "Node-to-aethernet precomputation direction is reversed.");
Assert(computedData.AethernetToNodeDistances[Aethernet.BaseCamp].Single(route => route.Id == 1).Path.First().X
       == precomputeAethernets[0].ArrivalPosition.X,
    "Aethernet-to-node precomputation must start at the arrival position.");

var timedOutSegments = 0;
var timeoutFailures = 0;
var neverCompletes = new TaskCompletionSource<List<Vector3>>(TaskCreationOptions.RunContinuationsAsynchronously);
var timedOutData = await NodeDataPrecomputer.ComputeAsync(
    precomputeNodes.Take(1).ToArray(),
    precomputeAethernets,
    (_, _) => neverCompletes.Task,
    _ => timedOutSegments++,
    _ => timeoutFailures++,
    TimeSpan.FromMilliseconds(20));
Assert(timedOutSegments == 2 && timeoutFailures == 2
       && timedOutData.NodeToAethernetDistances[1].Count == 0
       && timedOutData.AethernetToNodeDistances[Aethernet.BaseCamp].Count == 0,
    "Timed-out precomputation segments must be counted, reported, and omitted.");

using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
{
    var cancelled = false;
    try
    {
        await NodeDataPrecomputer.ComputeAsync(
            precomputeNodes.Take(1).ToArray(),
            precomputeAethernets,
            (_, _) => neverCompletes.Task,
            segmentTimeout: TimeSpan.FromSeconds(1),
            cancellationToken: cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }

    Assert(cancelled, "Node precomputation did not honor cancellation while an IPC segment was pending.");
}

var atomicDirectory = Path.Combine(Path.GetTempPath(), $"BOCCHI.DataSmoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(atomicDirectory);
try
{
    var atomicFile = Path.Combine(atomicDirectory, "nodes.json");
    await NodeDataPrecomputer.WriteAtomicAsync(atomicFile, computedData);
    var roundTrip = JsonSerializer.Deserialize<NodeDataSchema>(await File.ReadAllTextAsync(atomicFile));
    Assert(roundTrip.NodePositions.Count == computedData.NodePositions.Count,
        "Atomic node-data output did not deserialize with the expected positions.");
    Assert(!Directory.EnumerateFiles(atomicDirectory, "*.tmp").Any(),
        "Atomic node-data output left a temporary file after success.");

    await File.WriteAllTextAsync(atomicFile, "preserve-existing-output");
    using var cancelledWrite = new CancellationTokenSource();
    cancelledWrite.Cancel();
    var writeCancelled = false;
    try
    {
        await NodeDataPrecomputer.WriteAtomicAsync(atomicFile, computedData, cancelledWrite.Token);
    }
    catch (OperationCanceledException)
    {
        writeCancelled = true;
    }

    Assert(writeCancelled && await File.ReadAllTextAsync(atomicFile) == "preserve-existing-output",
        "Cancelled atomic output must leave the previous file untouched.");
    Assert(!Directory.EnumerateFiles(atomicDirectory, "*.tmp").Any(),
        "Cancelled atomic node-data output left a temporary file.");
}
finally
{
    Directory.Delete(atomicDirectory, true);
}

Assert(VnavmeshAvailabilityPolicy.Evaluate(true, true, new Version(0, 7, 6, 0)).IsAvailable
       && VnavmeshAvailabilityPolicy.Evaluate(true, true, new Version(0, 7, 7, 0)).IsAvailable
       && VnavmeshAvailabilityPolicy.Evaluate(true, true, null).IsAvailable,
    "Every loaded vnavmesh version must be accepted without an exact-version gate.");
Assert(VnavmeshAvailabilityPolicy.Evaluate(false, false, null).Status == VnavmeshAvailabilityStatus.Missing
       && VnavmeshAvailabilityPolicy.Evaluate(true, false, new Version(0, 7, 7, 0)).Status == VnavmeshAvailabilityStatus.NotLoaded,
    "Missing and unloaded vnavmesh installations must still be reported.");

Console.WriteLine("BOCCHI 7.55 North Horn data, lifecycle, config, translation, routing, precompute, and vnavmesh availability smoke tests passed.");
