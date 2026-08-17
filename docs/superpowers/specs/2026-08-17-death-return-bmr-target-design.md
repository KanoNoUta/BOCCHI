# BOCCHI Death Return and BMR Target Arbitration Design

## Goal

Add an unattended death recovery timeout, defaulting to 10 minutes, and stop BOCCHI from overriding BossMod Reborn's mechanic-specific target during the North Horn "Tiny Terror" critical encounter.

## Scope

- Death recovery is enabled by default and configurable from 1 to 60 minutes.
- It is active only while BOCCHI automation is requested, the run state can execute work, and the player is inside an Occult Crescent territory.
- Becoming alive, stopping automation, or leaving/changing territory resets the timer and any pending retry.
- At the timeout BOCCHI invokes the game's Return general action. If the dead-player confirmation dialog opens, BOCCHI confirms it only while a timed death return is pending.
- A failed/no-op Return attempt is retried at a bounded interval while the player remains dead and all eligibility conditions still hold.
- In Tiny Terror only, when the configured AI provider is BMR and the current target is a valid encounter enemy, BOCCHI does not replace that target even when Force Target is enabled.
- When no valid encounter target exists, BOCCHI still acquires its normal fallback target so combat can start and recover from a dead/despawned mechanic target.

## Design

`DeathReturnTracker` is a pure monotonic state machine. Each update receives eligibility, death state, current monotonic time, timeout, and retry interval. It returns `Wait`, `Trigger`, or `Reset`. This isolates elapsed-time and retry behavior from Dalamud UI/game APIs and makes clock rollback irrelevant.

`AutomatorModule` owns the tracker. Its update loop evaluates death recovery before ordinary automation work, stops local movement/rotation when a death is first observed, invokes the Return general action on `Trigger`, and marks the request pending. A `SelectYesno` lifecycle callback confirms only when the request is pending and the player is still dead; it cannot consume unrelated dialogs while alive or while automation is disabled.

`CombatAutomationPolicy.ShouldAcquireTarget` receives the force-target flag, current-target validity, selected AI provider, and encounter note. It preserves the existing rule everywhere except `AiType.BMR + MonsterNote.LittleMage`, where a valid current encounter target is authoritative. Both initial acquisition and combat maintenance use the same policy.

## Configuration and UI

`AutomatorConfig` gains `AutoReturnAfterDeath` (default `true`) and `DeathReturnMinutes` (default `10`). The common automation settings show the switch and a disabled minutes slider when the switch is off. All maintained locale files receive labels and tooltips.

## Error Handling

- Return invocation is wrapped so a transient client/API failure cannot escape the framework update.
- Attempts are throttled by the tracker rather than issued every frame.
- Confirmation requires all of: a pending timed return, automation eligibility, player still dead, and a visible `SelectYesno` addon.
- Territory change and module stop clear the tracker before normal automation resumes.

## Tests

The DataSmoke runner covers timeout boundary, reset, retry spacing, disabled/outside-island behavior, Tiny Terror BMR arbitration, and unchanged behavior for other providers/encounters. Source-level smoke assertions verify both target assignment sites use the shared policy and the death confirmation guard remains scoped to pending timed returns. Full DataSmoke, CE crowdsource smoke, and `Release_CN` build are required before publishing.

