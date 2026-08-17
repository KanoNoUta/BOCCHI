# BOCCHI Death Return and BMR Target Arbitration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically return dead unattended characters after a configurable timeout and preserve BMR's Tiny Terror mechanic target.

**Architecture:** Add a pure death timeout tracker and extend the existing pure combat target policy. Keep Dalamud action/addon integration inside `AutomatorModule`, and expose only the two new settings in the existing automation common settings UI.

**Tech Stack:** C# 14 / .NET 10, Dalamud CN SDK, FFXIVClientStructs, ImGui, BOCCHI.DataSmoke.

---

### Task 1: Regression Tests

**Files:**
- Modify: `tests/BOCCHI.DataSmoke/Program.cs`

- [ ] Add assertions for a 10-minute death timeout boundary, reset after resurrection, retry spacing, and disabled/outside-island reset behavior using the desired `DeathReturnTracker` API.
- [ ] Add assertions for `CombatAutomationPolicy.ShouldAcquireTarget` showing that BMR preserves a valid Little Mage target, still acquires when invalid, and keeps existing force-target behavior elsewhere.
- [ ] Run `dotnet build tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-restore` and confirm compilation fails because the new tracker/API do not exist yet.

### Task 2: Pure Policies

**Files:**
- Create: `BOCCHI/Modules/Automator/DeathReturnTracker.cs`
- Modify: `BOCCHI/Modules/Automator/Activity.cs`

- [ ] Implement monotonic death timing with `Wait`, `Trigger`, and reset behavior plus bounded retry scheduling.
- [ ] Extend `CombatAutomationPolicy.ShouldAcquireTarget` with `AiType` and `MonsterNote?`, special-casing only BMR Little Mage with a valid current target.
- [ ] Route both target-acquisition call sites through the extended policy.
- [ ] Re-run the DataSmoke build and runner; confirm the policy tests pass.

### Task 3: Runtime Integration and Settings

**Files:**
- Modify: `BOCCHI/Modules/Automator/AutomatorConfig.cs`
- Modify: `BOCCHI/Modules/Automator/AutomatorModule.cs`
- Modify: `BOCCHI/Windows/ConfigWindow.cs`
- Modify: `Translations/zh/modules.automator.json`
- Modify: `Translations/en/modules.automator.json`
- Modify: `Translations/jp/modules.automator.json`
- Modify: `Translations/fr/modules.automator.json`

- [ ] Add enabled-by-default auto-return and a 1-60 minute setting defaulting to 10.
- [ ] Evaluate death recovery before normal work, stop runtime movement/rotation while dead, invoke Return only on tracker triggers, and reset on stop/territory change.
- [ ] Register a `SelectYesno` callback that confirms only a pending automatic death return while the player is dead and automation remains eligible.
- [ ] Add the switch and dependent minutes slider to the common settings section with localized labels/tooltips.
- [ ] Run the full DataSmoke runner and CE crowdsource specialization.

### Task 4: Release Verification and Publication

**Files:**
- Modify: `BOCCHI/BOCCHI.csproj`
- Modify: `BOCCHI/BOCCHI.json`
- Modify: `CHANGELOG.md`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/BOCCHI.json`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/latest.zip`
- Modify: `DalamudPlugins-KanoNoUta/pluginmaster.json`

- [ ] Run all required smoke commands and `dotnet build BOCCHI/BOCCHI.csproj -c Release_CN --no-restore`.
- [ ] Increment the patch version consistently and update changelog text.
- [ ] Verify `cn...maintainer/main`, precisely stage release files, commit, and push `cn:main`.
- [ ] Package the complete `Release_CN` directory with files at the zip root, regenerate `pluginmaster.json`, verify manifest and DLL hashes, then precisely commit and push the plugin repository.
- [ ] Download the raw catalog and package, verify HTTP success, version/changelog/download URL, zip layout, and DLL SHA-256 equality.

