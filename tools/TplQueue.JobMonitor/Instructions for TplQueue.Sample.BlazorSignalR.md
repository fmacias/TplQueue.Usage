# TplQueue Job Monitor — Frontend Design and Implementation Instructions

> Geometry update approved September 20, 2026: the five-second overview ends at
> the bottom reference and uses exact square centers, compact channels, a visible
> left UTC ruler, straight connectors and count markers for collisions. Temporary
> interval inspection replaces timestamp displacement. These decisions supersede
> the centered-reference, circle and collision-offset requirements below. See
> [README.md](README.md#timing-and-limits) for the maintained implementation rules.

## 1. Purpose

Design and integrate a standalone JavaScript/Web Component for visual monitoring of TplQueue jobs in the existing `TplQueue.Sample.BlazorSignalR` sample.

The component is a **queue execution timeline**, not a generic workflow editor and not a full orchestrator UI.

The first goal is a human-readable visualization of:

- queues,
- queue parallelism,
- actual logical execution channels,
- jobs observed over time,
- job state,
- dependencies,
- cross-queue dependencies,
- root relationships,
- and live progression.

At this stage, **do not add a job details panel**. The complete available area is reserved for the job viewer.

The frontend must remain reusable outside Blazor.

---

## 2. Sources of truth

Before editing code:

1. Read every applicable `AGENTS.md`.
2. Also read the `AGENTS.md` in the parent `fmacias` workspace if it applies to this repository.
3. Read `INTEGRATION.md`.
4. Read `FIXES_2026-09-17.md`.
5. Inspect the current repository. Do not assume that paths, DTO names, observer classes, or the old ScatterChart integration still match an earlier iteration.
6. Inspect the hand-drawn sketch below as a **UX intent**, not as pixel-perfect artwork.

![Hand-drawn Job Monitor sketch](Sketchbyhand.jpg)

If the repository contradicts these documents, report the mismatch first and adapt to the repository. Do not force obsolete names or paths.

---

## 3. Architectural principles

### 3.1 Maintain a strict boundary

Preserve this direction:

```text
TplQueue runtime/core
    ↓
observer/runtime facts
    ↓
stable dashboard DTOs
    ↓
thread-safe presentation projection
    ↓
Blazor wrapper / JS interop
    ↓
<job-queue-timeline>
```

Do **not** expose any of these directly to JavaScript:

- `IJobEvent`
- `IDataJobEvent`
- `DataJob`
- queue implementations
- internal scheduler/dispatcher types
- generated transport types
- runtime synchronization primitives

The browser receives only stable presentation DTOs.

### 3.2 Keep rendering replaceable

Keep configuration, layout rules, channel semantics, DTO normalization, state mapping, collision handling, graph traversal, and theme tokens **outside the low-level renderer**.

The renderer should be mechanical.

The human should be able to review the project quickly without reading SVG implementation details.

Avoid changing `svg-renderer.js` unless the required behavior cannot reasonably be expressed in controller/layout/configuration code.

### 3.3 Human-readable structure

Prefer small modules with clear names over large files.

A reasonable separation is:

```text
src/
  component/
  controller/
  model/
  projection/
  layout/
  graph/
  interaction/
  theme/
  renderer/
```

The exact structure may differ, but responsibilities must remain obvious.

---

# 4. Visual language

## 4.1 Default appearance: dark developer IDE style

The default theme must be **dark**, compact, technical, and appropriate for a developer/diagnostic tool.

Use a Visual Studio / VS Code-like visual language without copying branding.

Recommended characteristics:

- charcoal/dark-gray application background,
- slightly lighter queue headers/panels,
- subtle grid and separators,
- high-contrast but not pure-white text,
- muted secondary text,
- state colors with sufficient contrast on dark backgrounds,
- thin dependency vectors,
- stronger focus/selection outline,
- monospace font for IDs, timestamps, channels and technical metadata,
- normal UI sans-serif font for queue/job labels.

Theme values must be CSS variables or configuration tokens. Do not hard-code the theme throughout the renderer.

Example token direction:

```css
--jm-bg: #1e1e1e;
--jm-panel: #252526;
--jm-header: #2d2d30;
--jm-border: #3f3f46;
--jm-grid: #333337;
--jm-fg: #d4d4d4;
--jm-muted: #9da0a6;
```

State/focus colors should also be tokens.

A light theme is not required now, but the architecture must not make one difficult later.

---

# 5. Sketch interpretation

The letters in the sketch describe behavior, not implementation classes.

## A — Search/filter

Provide a job search box.

Search by:

- job name,
- job ID,
- description.

Display matching jobs below the control.

When a match is selected:

1. find the selected job,
2. traverse its dependency graph in both directions,
3. focus the viewer on the connected graph in which that job participates,
4. visually emphasize the selected job.

Search must not mutate runtime state.

## B — Scale / zoom

Zoom changes the visual time scale and spacing while maintaining readability.

Required invariants:

- job nodes never become smaller than the configured minimum readable size,
- same-channel nodes must never visually overlap,
- dependency vectors remain visible,
- minimum channel spacing remains readable,
- minimum vector length remains visible,
- timestamps remain semantically correct even when collision offsets are used.

Scaling must not change queue/channel identity.

If a collision offset is needed for events with nearly identical timestamps, preserve their true timestamp in data/tooltip and use only a small deterministic visual displacement.

## C — Queue header

Each queue has a header containing its name and enough structural information to understand its channels.

Keep it visually compact.

## D — Jobs executed sequentially in one channel

Jobs assigned to the same execution channel are drawn in that same channel.

They are ordered by observed/execution time.

Maintain enough vertical space between nodes to display a visible connecting/dependency vector.

## E — Channel spacing

Each channel has a minimum horizontal slot width and minimum margin.

These values are scalable/configurable, but must never collapse below human-readable minimums.

## F — Vertical time axis and history navigation

Time is vertical.

The viewer must allow vertical history navigation.

A native scrollbar, a dedicated history scrollbar, or equivalent accessible mechanism is acceptable, but the interaction must:

- move the reference datetime,
- preserve temporal ordering,
- not resize or reassign channels.

Mouse wheel support is useful but must not be the only accessible mechanism.

## G — Reference datetime and live-follow mode

The reference datetime is displayed around the vertical center of the viewport.

Coordinate rule:

```text
observedAt < referenceTime  → above the reference line
observedAt = referenceTime  → on the reference line
observedAt > referenceTime  → below the reference line
```

In live-follow mode, `referenceTime` advances with the clock, optionally with a small configurable lead offset.

As the reference time advances, existing jobs naturally move upward.

The component must also support a fixed historical reference time so that a production version can inspect previous activity.

Do not hard-wire `Date.now()` inside the renderer. Time behavior belongs in controller/configuration code.

## H — Queue label

Queue width is primarily determined by its channel count and channel width.

When a queue becomes too narrow to show its complete name:

- show the first letter as the minimal label,
- expose the complete queue name on hover and via an accessibility label.

## I — Queue section and `MaxParallelism`

`MaxParallelism` defines the number of **logical execution channels** displayed for that queue.

Examples:

```text
MaxParallelism = 1   → channel 0
MaxParallelism = 3   → channels 0, 1, 2
MaxParallelism = 15  → channels 0..14
```

A channel is **not an OS thread ID**.

A channel is a logical concurrency slot of the queue while a job owns execution capacity.

The frontend must never fabricate a normal channel merely to make the layout look correct.

See `INTEGRATION.md` for how channel information must be obtained.

## J — Queue separator / width adjustment

Queue sections are separated by draggable separators.

On hover, show an appropriate resize indication such as `↔`.

Minimum channel widths still apply.

If the viewport cannot contain all queue/channel columns at their minimum readable width, use horizontal scrolling instead of overlapping or compressing them below the minimum.

## K — Cross-queue dependency

Dependencies may connect jobs in different queues.

Cross-queue vectors must remain visible and should be routed so they do not hide queue labels or job nodes unnecessarily.

## M — Root job

A root job may depend on several jobs.

Root identity is graph information.

**Root/family identity must never override actual channel placement.**

---

# 6. Runtime channel semantics — important correction from the previous iteration

The previous frontend iteration exposed a problem: `IJobEvent` does not necessarily contain the actual execution channel.

Do not solve this by assigning a fake presentation lane from `rootJobId`, hash values, array order, or another frontend-only heuristic.

The desired semantic is:

```text
channel = logical queue execution slot owned by the job while it is executing
```

For a queue with `MaxParallelism = N`, valid channels are:

```text
0 .. N-1
```

### Required repository investigation

Before changing the DTO contract, inspect TplQueue Core and identify where concurrency capacity is actually acquired and released.

Look for the execution path around:

- dequeue / dispatch,
- semaphore or permit acquisition,
- worker/execution slot acquisition,
- job start,
- job completion/failure/cancellation,
- observer notification.

The exact current type/class names must come from the repository.

### Preferred solution

If the core already has a stable concept that identifies a logical slot, expose/project that value.

If it does not, add the **smallest backward-compatible instrumentation** needed to allocate a logical channel index when execution capacity is acquired:

```text
queue MaxParallelism = N
available logical channels = 0..N-1

job acquires execution capacity
    → acquire one logical channel
    → keep that channel stable for the execution
    → include/correlate it with observer/presentation data
job execution ends/cancels/fails
    → release the logical channel
```

Required invariants:

- no two concurrently executing jobs in the same queue own the same channel,
- channel is always `< MaxParallelism`,
- channel remains stable for the job's execution span,
- channel is released on every terminal path,
- `MaxParallelism = 1` always means channel `0`,
- channel is not derived from thread ID,
- channel assignment must not change execution ordering or scheduling semantics.

Do not introduce a broad scheduler redesign just to expose this diagnostic fact.

### Jobs observed before a runtime channel exists

A queued/waiting job may be observed before it owns an execution slot.

Do not invent a channel for that state.

Choose one of these approaches after inspecting the current observer model:

1. allow `channel: null` until execution begins and render it in an explicit unassigned/waiting presentation area, or
2. omit the pre-execution point from the channel timeline and show the job once a real channel is known.

Prefer the smallest solution consistent with the current sample behavior.

Document the decision.

The renderer must never convert `null` into a fake channel.

---

# 7. Job node behavior

A job is represented by a compact circular node.

Minimum hover information:

- name,
- ID,
- queue,
- channel when known,
- state,
- observed timestamp,
- duration when known,
- description,
- metadata.

Metadata should be rendered safely and compactly; do not dump an unbounded object into the viewport.

Use a tooltip/popover appropriate for a diagnostic UI.

Do not add a permanent details panel at this stage.

The component may emit `job-select` so Blazor can remember the selected job for future use, but **selection must not consume viewer space with a details panel in this iteration**.

---

# 8. State normalization

Normalize state names before rendering.

Expected presentation states include:

```text
waiting
running
completed
retried
failed
cancelled
```

Accept `canceled` as an input alias if current runtime code emits it and normalize it to `cancelled`.

State styling belongs in theme/configuration, not inside graph/domain logic.

---

# 9. Standalone component contract

Create or maintain the reusable component under:

```text
./tools/TplQueue.JobMonitor
```

Primary custom element:

```html
<job-queue-timeline></job-queue-timeline>
```

Start with snapshot updates:

```javascript
timeline.setData(snapshot)
```

Do not add `appendJob` until the repository has a stable, ordered incremental contract and snapshot behavior is already correct.

The standalone demo must use the **same component code** as Blazor. Do not fork the renderer or layout logic for the demo.

---

# 10. Standalone demo

Provide a small HTTP-served demo with deterministic dummy data.

The demo must include at least:

- `FifoQ` with `MaxParallelism = 1`,
- `ParallelQ` with multiple channels,
- `CacheQ`,
- a dense queue with `MaxParallelism = 15`,
- several jobs in one channel,
- simultaneous jobs in different channels,
- a cross-queue dependency,
- one root depending on at least three jobs,
- failed/retried/completed/running/waiting examples as supported,
- metadata examples,
- a search/focus example.

The demo must not require Blazor or ASP.NET.

Document how to run it through a local HTTP server. Do not assume opening `index.html` through `file://` will work with ES modules.

---

# 11. Blazor integration requirements

Follow `INTEGRATION.md`.

Important points:

- target the existing .NET 8 Blazor Web App with Interactive Server rendering unless the repository proves otherwise,
- preserve the observer → projection → UI boundary,
- use a small JS interop wrapper,
- do not call JS from observer/background threads,
- marshal UI updates via the existing presentation/state service and `InvokeAsync`,
- use correct async disposal,
- remove the old ChartJS/ScatterChart frontend only after the new monitor is integrated and compiling; if it is already gone, do not reintroduce it,
- preserve unrelated user changes and current public APIs.

---

# 12. Acceptance criteria

## JavaScript/component

- syntax checks pass,
- unit tests pass,
- standalone demo runs over HTTP,
- browser acceptance harness passes in current Edge/Chrome,
- dark IDE theme is the default,
- theme values are centralized,
- queues display exactly `MaxParallelism` logical channels,
- real channel placement is respected,
- `channel: null` is never silently converted to a normal channel,
- same-channel nodes do not overlap,
- dependency edges remain visible,
- cross-queue edges remain visible,
- search focuses the connected graph,
- queue labels degrade to one letter when necessary,
- horizontal overflow scrolls instead of collapsing channels,
- history/reference time navigation works,
- no job details panel is displayed.

## Core/channel instrumentation, if required

Add focused tests proving:

- channel range is valid,
- concurrently running jobs cannot share a channel in one queue,
- channel is stable during execution,
- channels are released after complete/fail/cancel,
- FifoQ uses channel 0,
- instrumentation does not change execution semantics.

## Blazor

- affected projects build,
- complete solution builds if the full workspace dependencies are available,
- existing TplQueue tests still pass,
- observer-driven updates do not duplicate subscriptions,
- navigation away/back does not cause disposed-object or JS interop errors,
- no TplQueue runtime/domain object crosses the JS boundary.

---

# 13. Required Codex report after implementation

At the end, provide a concise implementation report containing:

1. repository facts discovered,
2. where the execution channel comes from,
3. whether Core required instrumentation,
4. DTO/projection changes,
5. JS/Web Component changes,
6. Blazor integration changes,
7. old frontend files removed,
8. tests and build commands executed,
9. exact results,
10. remaining limitations.

Do not hide assumptions.

---

# 14. Scope control

Work incrementally.

Do not redesign TplQueue scheduling, dependency semantics, retry behavior, or persistence as part of this frontend task.

The visualization must observe execution; it must not become the owner of execution semantics.
