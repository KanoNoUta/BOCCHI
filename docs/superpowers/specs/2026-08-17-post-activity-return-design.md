# BOCCHI Post-Activity Return Design

## Goal

Make alive-player return-to-base behavior deterministic after FATE and critical-encounter completion, while preserving independent treasure, carrot, and mob-farming navigation ownership.

## Current Problem

Post-activity return is split between `Automator` activity completion and `Teleporter` state-exit callbacks. `Automator` always queues FATE returns but explicitly excludes critical encounters, while `Teleporter` ignores the FATE switch in automatic mode and is the only automatic-mode fallback for critical encounters. A missed state-exit callback therefore loses a CE return even when `ReturnAfterCriticalEncounter` is enabled. The split also makes the two visible switches behave differently.

## Design

`PostActivityReturnPolicy` remains the single pure decision point. It receives the completed event type, both configured return switches, and independent-navigation state. It returns true only for the matching enabled switch and when independent navigation does not own movement.

`Automator` evaluates that policy in both completion paths: an activity that reaches `Done` and an activity that disappears from the client table. This is the reliable lifecycle fallback for both FATE and CE. `Teleporter` keeps its state-exit callbacks as early notifications, but automatic-mode callbacks use the same policy inputs and idempotent queue method, so either observation produces the same result without duplicate work.

Independent treasure, carrot, and mob-farming navigation continues to suppress a post-activity return. The idle no-event return remains unchanged because it is automatic-mode positioning behavior, not the immediate post-event lock controlled by these two switches.

## Configuration

No new setting is added. Existing `TeleporterConfig.ReturnAfterFate` and `ReturnAfterCriticalEncounter` values are authoritative in automatic and manual modes. Existing saved values are preserved.

## Diagnostics

Return reasons identify the actual event type rather than labeling every completion as a FATE. Existing queued, started, completed, failed, and independent-navigation cancellation logs remain available.

## Tests

DataSmoke covers FATE and CE with their switch enabled and disabled, plus independent-navigation suppression. Source checks verify both `Automator` completion paths pass the two configuration values and the `Teleporter` callbacks no longer bypass the FATE switch. Full DataSmoke, CE crowdsource smoke, and the `Release_CN` build are required before publishing.
