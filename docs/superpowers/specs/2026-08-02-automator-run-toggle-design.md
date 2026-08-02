# Automator Single Run Toggle Design

## Goal

Reduce BOCCHI's runtime controls to one visible automation switch and make start/stop feedback immediate and truthful. Full and compact main-window layouts may place the switch differently, but both render the same component and invoke the same state transition API.

## Scope

- Replace duplicate start, pause, emergency-stop, title-bar, and settings-page automation controls with one reusable run toggle.
- Keep `/bocchiillegal on|off|toggle` as a command surface using the same transition API.
- Make compact-mode status checks read-only and non-blocking.
- Correct delayed start, delayed visual feedback, repeated clicks, and displayed-state/runtime-state disagreement.
- Preserve current full-mode sections, compact-mode summaries, island selection, event configuration, and hunter controls unless they directly duplicate the global automation switch.

## Interaction

The main window presents one control labeled `自动运行` with the current state beside it:

- `已停止`: switch off and interactive.
- `启动中`: switch on, temporarily disabled, with dependency or entry preparation shown as secondary status.
- `运行中`: switch on and interactive; switching it off requests a complete stop.
- `停止中`: switch off and temporarily disabled while owned work is released.
- `启动失败`: switch off and interactive, with the concrete failure reason shown nearby or in a tooltip.

Full and compact layouts are mutually exclusive, so only one instance is visible. Both call the same renderer and controller API. The compact/full-layout selector remains a separate display preference because it does not control automation.

The stop side of the single switch has emergency-stop semantics: it prevents new work first, then clears owned activity and navigation state. A second stop button is therefore unnecessary.

## Control Ownership

`AutomatorModule` is the single owner of requested and effective runtime state. UI code does not write `Config.Enabled` directly and does not infer effective state from dependency attributes.

The controller exposes a small surface equivalent to:

```csharp
AutomatorRunState RunState { get; }
string? RunStateDetail { get; }
void RequestEnabled(bool enabled);
```

`RequestEnabled` is idempotent. Repeated requests for the current or transitional target do nothing. The command handler, main window, compact window, and optional Automator window all use this API.

## State Flow

```text
Stopped --enable--> Starting --ready/in-zone--> Running
                         |--failure-----------> Stopped + detail

Running --disable--> Stopping --cleanup------> Stopped
Starting --disable----------------------------> Stopping
```

The requested state changes immediately on click. Potentially slow cross-plugin preparation is performed from framework updates in bounded steps, never from ImGui rendering. Starting inside Occult Crescent does not wait for DailyRoutines. DailyRoutines preparation occurs only when outside entry or instance rotation actually requires it.

## Dependency Handling

- The render path reads cached status only.
- vnavmesh and Lifestream readiness are refreshed by the module update path at a bounded interval.
- DailyRoutines module discovery/enabling is not called merely because the compact window is open.
- Missing required dependencies fail the start request with a concrete reason and return the switch to off.
- Transitional readiness must not be displayed as `运行中`.

## Stop Ordering

Stopping follows this order:

1. Latch the requested state to off so no new activity can be submitted.
2. Clear the active Automator activity and instance-rotation state.
3. Stop treasure, carrot, mob-farmer, buff, and rotation work owned by automation.
4. Abort BOCCHI/Ocelot chains.
5. Stop vnavmesh and abort Lifestream when their IPC is ready.
6. Release AI-provider and external targeting state.
7. Save configuration once and publish `Stopped`.

Cleanup operations remain guarded and idempotent so repeated stop requests and disappearing IPC providers cannot leave a half-enabled state.

## UI Changes

- Remove both automation title-bar buttons from `MainWindow`.
- Remove the separate emergency-stop buttons from full and compact layouts.
- Replace full and compact start/pause buttons with a shared binary switch renderer.
- Remove the runtime `Enabled` checkbox from Automator settings; settings configure behavior, while the main window controls execution.
- Remove or replace the Automator lens title-bar toggle so it cannot become another independent control.
- Keep color restrained: green/teal for running, neutral gray for stopped, amber for transition, and red only for a reported failure.
- Use stable dimensions so status text changes do not move adjacent controls.

## Error Handling

- Every failed start records a user-readable reason and logs the underlying exception or dependency state.
- Exceptions during one cleanup step are logged and do not prevent the remaining cleanup steps.
- The switch never remains visually on after a rejected start.
- The switch never reports stopped until new work has been blocked, even if external cleanup continues briefly.

## Tests

Add tests before production changes for:

1. `Stopped -> Starting -> Running` on a ready in-zone start.
2. Missing vnavmesh rejects start and returns to `Stopped` with a reason.
3. Starting in-zone does not invoke DailyRoutines preparation.
4. Outside entry remains `Starting` while DailyRoutines modules are enabling.
5. Repeated enable clicks are idempotent.
6. Disable during `Starting` prevents later transition to `Running`.
7. Disable latches off before cleanup callbacks and reaches `Stopped` even if one cleanup action throws.
8. Compact status rendering performs no plugin-enabling IPC.
9. All UI and command entry points route through the same request method.

Build the existing solution and run its current smoke tests after the focused state-machine tests. A local Dalamud smoke check must verify one visible switch, immediate state feedback, no frame stall while compact mode is open, and complete cleanup on stop.

## Non-Goals

- Redesigning event, FATE, CE, monster-selection, or navigation algorithms.
- Changing automatic activity priority.
- Removing command-line start/stop support.
- Persisting an enabled automation state across plugin reloads.
