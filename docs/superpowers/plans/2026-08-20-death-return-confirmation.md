# Death Return Confirmation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reliably click the Return confirmation button after BOCCHI's configured death timeout without touching unrelated confirmation dialogs.

**Architecture:** Replace the one-shot `PostSetup` listener with a `PostDraw` readiness poll. Preserve the existing pending-return ownership flag and clear it immediately after one successful callback or whenever death-return eligibility resets.

**Tech Stack:** C#, Dalamud addon lifecycle, FFXIVClientStructs, BOCCHI.DataSmoke.

---

### Task 1: Lock the regression behavior

**Files:**
- Modify: `tests/BOCCHI.DataSmoke/Program.cs`

- [ ] Change the source assertions to require `AddonEvent.PostDraw`, `AddonSelectYesno`, Yes-button visibility/enabled checks, and the new handler name.
- [ ] Run `dotnet build tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-restore` and `dotnet run --project tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-build`.
- [ ] Confirm the smoke run fails specifically because production still uses the old `PostSetup` handler.

### Task 2: Implement the readiness-based confirmation

**Files:**
- Modify: `BOCCHI/Modules/Automator/AutomatorModule.cs`

- [ ] Register and unregister `OnDeathReturnSelectYesnoPostDraw` for `AddonEvent.PostDraw`.
- [ ] Keep `deathReturnPending` active between a successful Return cast and the next 10-second retry boundary.
- [ ] Cast the addon to `AddonSelectYesno*`; require the addon, Yes button, and its resource node to be visible and enabled before firing callback `0`.
- [ ] Clear `deathReturnPending` immediately after the callback to prevent duplicate clicks.
- [ ] Re-run the smoke suite and CE crowdsource smoke.

### Task 3: Build and publish version 3.3.40

**Files:**
- Modify: `BOCCHI/BOCCHI.csproj`
- Modify: `BOCCHI/BOCCHI.json`
- Modify: `CHANGELOG.md`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/BOCCHI.json`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/latest.zip`
- Modify: `DalamudPlugins-KanoNoUta/plugins/BOCCHI/icon.png`
- Modify: `DalamudPlugins-KanoNoUta/pluginmaster.json`

- [ ] Set project version `3.3.40` and manifest version `3.3.40.0`, documenting the confirmation fix.
- [ ] Build `dotnet build BOCCHI/BOCCHI.csproj -c Release_CN --no-restore` and record the DLL SHA-256.
- [ ] Fetch `maintainer`, verify it is not ahead of `cn`, commit only release files, and push `cn:main`.
- [ ] Package the complete `BOCCHI/bin/Release_CN` output at the zip root, regenerate `pluginmaster.json`, and verify manifest/DLL hashes.
- [ ] Commit only BOCCHI catalog artifacts and push the plugin repository.
- [ ] Download the raw catalog and package, verify HTTP success, `3.3.40.0`, and matching local/remote DLL hashes.
