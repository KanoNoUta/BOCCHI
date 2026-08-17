# BOCCHI Post-Activity Return Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make automatic post-FATE and post-CE return consistently honor the corresponding saved switches.

**Architecture:** Extend the pure `PostActivityReturnPolicy` to accept both switch values, then route the `Automator` lifecycle fallback and `Teleporter` state-exit notification through that same decision. Keep queueing idempotent and retain independent-navigation suppression.

**Tech Stack:** C# 14 / .NET 10, Dalamud CN SDK, BOCCHI.DataSmoke.

---

### Task 1: Add Regression Tests

**Files:**
- Modify: `tests/BOCCHI.DataSmoke/Program.cs`

- [ ] Replace the existing FATE-only policy assertions with calls that pass `returnAfterFate`, `returnAfterCriticalEncounter`, and `independentNavigationRunning`.
- [ ] Assert enabled FATE and CE switches queue their matching event, disabled switches do not, and independent navigation suppresses both.
- [ ] Add source assertions that both `Automator` completion paths read `TeleporterConfig.ReturnAfterFate` and `ReturnAfterCriticalEncounter`, and that automatic-mode FATE notification checks `ReturnAfterFate`.
- [ ] Run `dotnet build tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-restore`; expect compilation to fail because `PostActivityReturnPolicy.ShouldQueue` does not yet accept the two switch values.

### Task 2: Unify Runtime Policy

**Files:**
- Modify: `BOCCHI/Modules/Automator/Automator.cs`
- Modify: `BOCCHI/Modules/Teleporter/Teleporter.cs`

- [ ] Extend `PostActivityReturnPolicy.ShouldQueue` to select the matching FATE or CE switch and then apply independent-navigation suppression.
- [ ] Pass the two `TeleporterConfig` switch values from both `Automator` activity-completion paths.
- [ ] Use event-type-correct return reasons in lifecycle logs.
- [ ] Make automatic-mode `Teleporter.OnFateEnd` honor `ReturnAfterFate`, matching the existing CE behavior.
- [ ] Rebuild and run DataSmoke; expect all return-policy assertions to pass.

### Task 3: Verify and Release 3.3.38

**Files:**
- Modify: `BOCCHI/BOCCHI.csproj`
- Modify: `BOCCHI/BOCCHI.json`
- Modify: `CHANGELOG.md`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/BOCCHI.json`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/latest.zip`
- Modify: `DalamudPlugins-KanoNoUta/pluginmaster.json`

- [ ] Run the full DataSmoke runner and `--ce-crowdsource` specialization.
- [ ] Update project version to `3.3.38`, manifest version to `3.3.38.0`, and add the focused changelog.
- [ ] Run `dotnet build BOCCHI/BOCCHI.csproj -c Release_CN --no-restore` and verify `BOCCHI/bin/Release_CN/BOCCHI.dll` assembly version and SHA-256.
- [ ] Fetch `maintainer`, verify it is not ahead, precisely commit source files, and push `cn:main`.
- [ ] Package the complete `Release_CN` output at the zip root, update the plugin manifest, run `python generate_pluginmaster.py`, and verify zip layout plus DLL hash equality.
- [ ] Precisely commit and push the plugin repository, then verify the raw catalog and package show `3.3.38.0` and match local hashes.
