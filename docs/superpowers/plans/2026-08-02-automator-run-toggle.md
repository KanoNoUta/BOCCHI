# Automator Single Run Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace BOCCHI's duplicated automation controls with one responsive run switch backed by a truthful, idempotent lifecycle.

**Architecture:** Add a pure `AutomatorRunStateMachine` that owns stopped/starting/running/stopping transitions while `AutomatorModule` owns game/plugin side effects. ImGui renders the state and sends requests only; cross-plugin preparation happens from module updates, never from the render path.

**Tech Stack:** C# 13, .NET 10, Dalamud CN SDK 15, Dalamud ImGui, existing `BOCCHI.DataSmoke` console assertions.

---

## File Map

- Create `BOCCHI/Modules/Automator/AutomatorRunStateMachine.cs`: pure lifecycle enum, request actions, state/detail transitions.
- Modify `tests/BOCCHI.DataSmoke/Program.cs`: lifecycle regression tests and compact-render side-effect guard.
- Modify `BOCCHI/Modules/Automator/AutomatorModule.cs`: single request API, startup polling, stop ordering, compatibility wrappers.
- Modify `BOCCHI/Windows/MainWindow.cs`: shared single switch and minimal compact layout; remove duplicate controls and render-time dependency mutation.
- Modify `BOCCHI/Windows/ConfigWindow.cs`: remove runtime Enabled checkbox from settings.
- Modify `BOCCHI/Modules/Automator/AutomatorWindow.cs`: remove the duplicate title-bar toggle.

### Task 1: Pure Automation Lifecycle

**Files:**
- Create: `BOCCHI/Modules/Automator/AutomatorRunStateMachine.cs`
- Modify: `tests/BOCCHI.DataSmoke/Program.cs`

- [ ] **Step 1: Write failing lifecycle assertions**

Add assertions covering start, repeated start, successful start, failed start with detail, stop during startup, repeated stop, and stop completion:

```csharp
var runState = new AutomatorRunStateMachine();
Assert(runState.State == AutomatorRunState.Stopped, "Automation must initialize stopped.");
Assert(runState.RequestEnabled(true) == AutomatorRunAction.BeginStart
       && runState.State == AutomatorRunState.Starting
       && runState.TargetEnabled,
    "An enable request must publish Starting immediately.");
Assert(runState.RequestEnabled(true) == AutomatorRunAction.None,
    "Repeated enable requests must be idempotent.");
runState.CompleteStart();
Assert(runState.State == AutomatorRunState.Running, "A ready start must become Running.");
Assert(runState.RequestEnabled(false) == AutomatorRunAction.BeginStop
       && runState.State == AutomatorRunState.Stopping
       && !runState.TargetEnabled,
    "A stop request must block new work immediately.");
runState.CompleteStop();
Assert(runState.State == AutomatorRunState.Stopped, "Cleanup completion must publish Stopped.");

var failedStart = new AutomatorRunStateMachine();
failedStart.RequestEnabled(true);
failedStart.FailStart("vnavmesh 未加载");
Assert(failedStart.State == AutomatorRunState.Stopped
       && failedStart.Detail == "vnavmesh 未加载",
    "Rejected starts must turn off and preserve a user-readable reason.");

var cancelledStart = new AutomatorRunStateMachine();
cancelledStart.RequestEnabled(true);
Assert(cancelledStart.RequestEnabled(false) == AutomatorRunAction.BeginStop
       && !cancelledStart.TargetEnabled,
    "Disable during startup must prevent a later running transition.");
cancelledStart.CompleteStart();
Assert(cancelledStart.State == AutomatorRunState.Stopping,
    "A stale completion callback must not revive a cancelled start.");
```

- [ ] **Step 2: Run smoke tests and verify RED**

Run: `dotnet run --project tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug`

Expected: compilation fails because `AutomatorRunStateMachine`, `AutomatorRunState`, and `AutomatorRunAction` do not exist.

- [ ] **Step 3: Implement the minimal pure state machine**

Create enums and methods with guarded transitions:

```csharp
public enum AutomatorRunState { Stopped, Starting, Running, Stopping }
public enum AutomatorRunAction { None, BeginStart, BeginStop }

public sealed class AutomatorRunStateMachine
{
    public AutomatorRunState State { get; private set; } = AutomatorRunState.Stopped;
    public string? Detail { get; private set; }
    public bool TargetEnabled => State is AutomatorRunState.Starting or AutomatorRunState.Running;
    public bool CanRunWork => State == AutomatorRunState.Running;

    public AutomatorRunAction RequestEnabled(bool enabled)
    {
        if (enabled)
        {
            if (State is AutomatorRunState.Starting
                or AutomatorRunState.Running
                or AutomatorRunState.Stopping)
            {
                return AutomatorRunAction.None;
            }

            State = AutomatorRunState.Starting;
            Detail = null;
            return AutomatorRunAction.BeginStart;
        }

        if (State is AutomatorRunState.Stopped or AutomatorRunState.Stopping)
        {
            return AutomatorRunAction.None;
        }

        State = AutomatorRunState.Stopping;
        Detail = null;
        return AutomatorRunAction.BeginStop;
    }

    public void SetStartingDetail(string? detail)
    {
        if (State == AutomatorRunState.Starting)
        {
            Detail = detail;
        }
    }

    public void CompleteStart()
    {
        if (State == AutomatorRunState.Starting)
        {
            State = AutomatorRunState.Running;
            Detail = null;
        }
    }

    public void FailStart(string detail)
    {
        if (State == AutomatorRunState.Starting)
        {
            State = AutomatorRunState.Stopped;
            Detail = detail;
        }
    }

    public void CompleteStop()
    {
        if (State == AutomatorRunState.Stopping)
        {
            State = AutomatorRunState.Stopped;
            Detail = null;
        }
    }
}
```

- [ ] **Step 4: Run smoke tests and verify GREEN**

Run: `dotnet run --project tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug`

Expected: existing smoke suite prints its success line and exits 0.

### Task 2: Wire the Module to One Request API

**Files:**
- Modify: `BOCCHI/Modules/Automator/AutomatorModule.cs`
- Modify: `tests/BOCCHI.DataSmoke/Program.cs`

- [ ] **Step 1: Add failing policy assertions**

Add a pure preflight policy that distinguishes ready, waiting, and failed dependencies without invoking IPC:

```csharp
Assert(AutomatorStartPolicy.Evaluate(true, true) == AutomatorStartReadiness.Ready,
    "Loaded navigation dependencies must permit startup.");
Assert(AutomatorStartPolicy.Evaluate(false, true) == AutomatorStartReadiness.VnavmeshUnavailable,
    "Missing vnavmesh must reject startup immediately.");
Assert(AutomatorStartPolicy.Evaluate(true, false) == AutomatorStartReadiness.LifestreamUnavailable,
    "Missing Lifestream must reject startup immediately.");
```

- [ ] **Step 2: Run smoke tests and verify RED**

Run the DataSmoke command and expect compilation failure for the missing policy types.

- [ ] **Step 3: Add module lifecycle integration**

Implement:

```csharp
public AutomatorRunState RunState => runState.State;
public string? RunStateDetail => runState.Detail;
public bool RequestedEnabled => runState.TargetEnabled;

public void RequestEnabled(bool enabled)
{
    var action = runState.RequestEnabled(enabled);
    if (action == AutomatorRunAction.BeginStart)
    {
        BeginStartRequest();
    }
    else if (action == AutomatorRunAction.BeginStop)
    {
        CompleteStopRequest();
    }
}
```

`BeginStartRequest` performs only local installed/loaded checks, sets `Config.Enabled = true`, saves once, and leaves the state at `Starting`. `PostUpdate` performs IPC readiness polling; it completes startup immediately in-zone and drives DailyRoutines entry preparation only while outside or instance rotation requires it. `CompleteStopRequest` sets `Config.Enabled = false` before cleanup, executes every cleanup action with guarded exception logging, saves once, and completes `Stopping -> Stopped` in `finally`.

Keep `EnableIllegalMode`, `DisableIllegalMode`, and `ToggleIllegalMode` as compatibility wrappers around `RequestEnabled`; toggle uses `RequestedEnabled`, not raw config.

- [ ] **Step 4: Run smoke tests and verify GREEN**

Run the DataSmoke command and expect exit 0.

### Task 3: Replace Duplicate UI Controls

**Files:**
- Modify: `BOCCHI/Windows/MainWindow.cs`
- Modify: `BOCCHI/Windows/ConfigWindow.cs`
- Modify: `BOCCHI/Modules/Automator/AutomatorWindow.cs`
- Modify: `tests/BOCCHI.DataSmoke/Program.cs`

- [ ] **Step 1: Add a failing source-level UI guard**

Read the main-window source and assert the compact renderer no longer calls DailyRoutines module enablement and that settings no longer call module enable/disable:

```csharp
var mainWindowSource = File.ReadAllText(Path.Combine("BOCCHI", "Windows", "MainWindow.cs"));
Assert(!mainWindowSource.Contains("EnsureDailyRoutinesCommandModules", StringComparison.Ordinal),
    "Rendering the compact window must never enable DailyRoutines modules.");
var configWindowSource = File.ReadAllText(Path.Combine("BOCCHI", "Windows", "ConfigWindow.cs"));
Assert(!configWindowSource.Contains("##AutomatorEnabled", StringComparison.Ordinal),
    "Runtime start/stop must not be duplicated in settings.");
```

- [ ] **Step 2: Run smoke tests and verify RED**

Run the DataSmoke command and expect the compact render-side-effect assertion to fail.

- [ ] **Step 3: Implement the shared switch**

Create a single `DrawAutomatorRunToggle(AutomatorModule automator, string id)` helper using `ImGui.Checkbox`. It derives checked state from `RequestedEnabled`, disables interaction only during `Stopping`, calls `RequestEnabled` on change, and prints a fixed-width status label from `RunState` plus `RunStateDetail`.

Use that helper once in full mode and once in compact mode; because the layouts are mutually exclusive, only one is visible. Remove MainWindow title-bar automation buttons, full/compact emergency buttons, compact dependency dots, compact quick actions, and all calls from rendering to `EnsureDailyRoutinesCommandModules`.

Compact mode keeps only the shared switch, compact-layout checkbox, current zone, and current activity/state. Remove the Automator settings Enabled checkbox and AutomatorWindow title-bar toggle.

- [ ] **Step 4: Run smoke tests and verify GREEN**

Run the DataSmoke command and expect exit 0.

### Task 4: Full Verification and DLL Output

**Files:**
- Verify all modified production and test files.

- [ ] **Step 1: Format and diff-check**

Run: `dotnet format BOCCHI.sln --no-restore`

Run: `git diff --check`

Expected: both commands exit 0; unrelated pre-existing diffs remain untouched.

- [ ] **Step 2: Run the complete smoke suite**

Run: `dotnet run --project tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug`

Expected: exit 0 with the BOCCHI smoke success line.

- [ ] **Step 3: Build the local test DLL**

Run: `dotnet build BOCCHI/BOCCHI.csproj -c Debug --no-restore`

Expected: exit 0 and a fresh `BOCCHI.dll` under `BOCCHI/bin/x64/Debug/net10.0-windows7.0/` or the SDK-selected Debug output directory.

- [ ] **Step 4: Verify artifact metadata**

Record the DLL full path, length, last-write timestamp, and SHA-256 with PowerShell `Get-Item` and `Get-FileHash`.

- [ ] **Step 5: Review the final diff**

Confirm the diff contains no unrelated rewrites, retains the user's existing MainWindow/ConfigWindow changes, and satisfies every requirement in the design spec.
