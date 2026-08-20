# Death Return Confirmation Design

## Problem

The timed death-return flow casts Return successfully, but the confirmation window can remain open. The listener currently runs only on `SelectYesno` `PostSetup`; at that point the addon can exist before it or its Yes button is visible and enabled. Returning early loses the only callback opportunity.

## Design

Listen for `SelectYesno` `PostDraw`, which repeats while the dialog is rendered. Confirmation remains strictly scoped to a pending Return cast initiated by BOCCHI, while the player is dead, automation and death return are enabled, and the player remains in Occult Crescent.

Before confirming, require `AddonSelectYesno`, its Yes button, and the button resource node to be present, visible, and enabled. Fire callback `0` once, then clear the pending flag immediately so later `PostDraw` events cannot click another dialog. Keep the pending state until the next bounded Return retry rather than expiring after three seconds; resurrection, leaving the island, stopping automation, or a failed cast still clears it.

## Verification

The data smoke test will assert the lifecycle event, typed addon/button readiness checks, bounded pending lifetime, and removal of the one-shot `PostSetup` handler. Then run the full smoke suite, CE crowdsource smoke, and the `Release_CN` build before packaging version `3.3.40`.
