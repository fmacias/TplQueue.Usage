# Integration into `TplQueue.Sample.BlazorSignalR`

The September 20, 2026 approved viewer changes supersede earlier geometry guidance:
five-second overview ending at the bottom reference, exact square centers, count
markers instead of displacement, and temporary interval inspection. See the
[component README](README.md) for current configuration and interaction behavior.
The host continues to synchronize the shared assets; do not edit generated copies.

## 1. Goal

Integrate the standalone `TplQueue.JobMonitor` Web Component into the existing `.NET 8` Blazor sample while preserving TplQueue runtime boundaries.

The frontend is diagnostic/presentation code.

It must not become coupled directly to TplQueue runtime objects.

---

## 2. Verify the current repository first

Known historical assumptions include:

- `TplQueue.Sample.BlazorSignalR`
- `.Etl` and `.Etl.Contracts`
- `IFifoQ`, `IParallelQ`, `ICacheQ`
- `IJobEvent`, `IDataJobEvent`
- server-side observer/state updates
- Interactive Server rendering
- an earlier ScatterChart/ChartJS frontend

These are **not guaranteed to still be current**.

Before editing:

1. locate the actual Blazor host,
2. locate dashboard/state/projection services,
3. locate observer registration,
4. locate queue creation/configuration and `MaxParallelism`,
5. locate the current frontend integration,
6. inspect TplQueue Core for the actual execution-capacity acquisition/release path,
7. report material differences from this document.

---

## 3. Recommended component placement

Reusable source:

```text
tools/TplQueue.JobMonitor/
```

Blazor static assets:

```text
TplQueue.Sample.BlazorSignalR/wwwroot/job-monitor/
```

If the current repository already uses a Razor Class Library for reusable UI, preserve that convention and use:

```text
_content/{AssemblyName}/...
```

Do not create a second competing asset convention.

---

## 4. Blazor → JavaScript boundary

Use a small Razor wrapper around:

```razor
<job-queue-timeline @ref="_element"></job-queue-timeline>
```

The wrapper should:

- import the ES module once,
- initialize the custom element once,
- call `setData(snapshot)` when the presentation snapshot changes,
- subscribe to `job-select`,
- return selected job identity to Blazor through `EventCallback`,
- dispose JavaScript listeners,
- dispose `DotNetObjectReference` if used,
- dispose the module/reference correctly,
- tolerate navigation away/back.

Use:

- `ElementReference`,
- `IJSRuntime`,
- imported ES module,
- `IAsyncDisposable` where appropriate.

Do not call JavaScript directly from an observer/background thread.

---

# 5. Presentation DTO contract

JSON passed to JavaScript must be presentation-only and camel-cased.

Recommended snapshot:

```json
{
  "referenceTime": "2026-09-17T15:20:00.000Z",
  "queues": [
    {
      "id": "parallel",
      "name": "ParallelQ",
      "maxParallelism": 3
    }
  ],
  "jobs": [
    {
      "id": "job-42",
      "rootJobId": "root-7",
      "name": "Normalize measurements",
      "description": "Normalizes units and filters invalid samples.",
      "queueId": "parallel",
      "channel": 0,
      "observedAt": "2026-09-17T15:19:58.500Z",
      "state": "completed",
      "durationMs": 2380,
      "dependsOn": ["job-41"],
      "metadata": {
        "handler": "sample.etl.normalize.v2"
      },
      "isRoot": false
    }
  ]
}
```

A waiting/pre-execution job may use:

```json
"channel": null
```

only when no real execution slot has been acquired yet.

Do not replace `null` with a hash/family-derived lane.

---

# 6. Execution-channel contract

## 6.1 Definition

`channel` means:

> the logical execution slot owned by the job inside its queue while it holds queue execution capacity.

It is **not**:

- thread ID,
- Task ID,
- root/family index,
- arbitrary presentation lane,
- modulo/hash of job ID.

For a queue with `MaxParallelism = N`:

```text
channel ∈ [0, N-1]
```

## 6.2 Current known issue

`IJobEvent` does not necessarily expose this information.

Therefore the integration must not assume that observer events alone are sufficient.

## 6.3 Repository investigation

Find the Core code path where a job actually obtains/relinquishes execution capacity.

Typical concepts to inspect, using the repository's real names:

- semaphore/permit acquisition,
- dequeue + dispatch,
- worker/execution lease,
- start execution,
- terminal completion/failure/cancellation,
- observer emission.

## 6.4 Preferred implementation if Core has no channel identity

Add a minimal logical channel allocator adjacent to the existing execution-capacity mechanism.

Conceptually:

```text
queue has MaxParallelism = N
channel pool = 0..N-1

after execution capacity is acquired:
    acquire one channel id
    associate it with job id / execution
    emit or make it available to observer/projection

on every terminal path:
    release channel id
```

The channel allocator is observability instrumentation, not a new scheduler.

Do not change queue ordering, backpressure, retry, cancellation, or dependency behavior.

### Required safety rules

- no duplicate channel ownership among concurrent jobs of one queue,
- always release in `finally` or equivalent guaranteed cleanup,
- never exceed `MaxParallelism`,
- never change channel during one execution,
- FifoQ uses `0`,
- queue instances must not leak channel state into each other.

## 6.5 How to expose it

Prefer the smallest backward-compatible path supported by the current architecture:

1. existing observer metadata/DTO field if already available,
2. additive observer contract field if compatible,
3. internal execution correlation store consumed by the sample projection,
4. another minimal additive mechanism justified by repository structure.

Do not break public interfaces merely to satisfy the sample if a smaller compatible solution exists.

Document the chosen path.

---

# 7. Observer integration

Required direction:

```text
observer callback
    ↓
map runtime facts to dashboard DTO
    ↓
update thread-safe projection
    ↓
notify presentation state
    ↓
Razor InvokeAsync(...)
    ↓
JS setData(snapshot)
```

Rules:

- no JS interop from observer threads,
- no Razor component owns runtime synchronization,
- no runtime object is serialized to the browser,
- projection must be safe under observer timing,
- do not assume asynchronous observer callback order unless the current Core contract guarantees it.

Prefer full snapshots for this integration.

Only add incremental `appendJob` after a stable ordered event contract exists and is tested.

---

# 8. Projection responsibilities

The presentation projection should own/derive:

### Queue data

- queue ID,
- queue display name,
- `MaxParallelism`.

### Job data

- job ID,
- root job ID if available,
- name,
- description,
- queue ID,
- real logical channel when known,
- observed timestamp,
- normalized state,
- duration,
- dependency IDs,
- bounded presentation metadata,
- root flag.

The projection may normalize data.

It must not invent execution facts.

---

# 9. State normalization

Normalize to:

```text
waiting
running
completed
retried
failed
cancelled
```

If runtime code uses `canceled`, convert it to `cancelled`.

Unknown states should be handled explicitly and visibly rather than silently treated as completed.

---

# 10. Selection behavior

The Web Component emits `job-select`.

Blazor may store the selected job ID or update existing selected-job state.

However:

**do not display the old job detail panel in this iteration.**

The complete available dashboard area belongs to the monitor.

Selection is retained for:

- visual focus,
- future details behavior,
- diagnostics,
- compatibility with existing presentation state.

---

# 11. Replacing the old ScatterChart frontend

If the old `BlazorExpress.ChartJS` / ScatterChart implementation still exists:

1. integrate and compile the new wrapper first,
2. verify snapshot rendering,
3. then remove old frontend-specific ChartJS/ScatterChart files/references,
4. remove unused JS/CSS/package references,
5. do not remove unrelated backend/projection behavior.

If the old frontend is already removed, do not reintroduce it.

---

# 12. Dark IDE theme

The component's default styling is dark developer/diagnostic UI.

The theme is owned by the Web Component through centralized CSS variables/tokens.

Blazor should not duplicate the component's internal styling.

The host may provide outer layout sizing only.

---

# 13. Asset synchronization

Do not manually maintain two divergent copies of the component.

Choose one clear source of truth under:

```text
tools/TplQueue.JobMonitor
```

and define how assets reach `wwwroot/job-monitor`.

Acceptable approaches include:

- explicit copy step/script,
- MSBuild target,
- documented manual packaging step for the sample.

The selected approach must be obvious and deterministic.

---

# 14. Verification

## Build

Run the affected project builds and, where workspace dependencies are available, the complete solution build.

Record exact commands and results.

## Existing tests

Run all relevant existing TplQueue tests.

## Core/channel tests if instrumentation is added

Verify:

- channel range,
- uniqueness among concurrent executions,
- release on completion,
- release on failure,
- release on cancellation,
- stability during execution,
- FifoQ = channel 0,
- independent channel pools per queue instance.

## Frontend behavior

Verify:

- `ParallelQ`, `FifoQ`, `CacheQ` show correct `MaxParallelism`,
- jobs use actual channels,
- waiting/unassigned behavior is explicit,
- same-channel nodes do not overlap,
- dependency lines render,
- cross-queue dependencies render,
- search by name/ID/description works,
- graph focus works,
- hover displays required information including metadata,
- queue label collapses to first letter when necessary,
- 15-channel queue remains usable through horizontal scrolling,
- history scrollbar/reference datetime works,
- dark IDE theme is the default,
- no details panel is present.

## Lifecycle

Verify:

- no duplicate observer subscriptions,
- no duplicate DOM event subscriptions,
- navigation away/back works,
- disposal produces no `ObjectDisposedException`,
- no JS interop occurs after disposal.

---

# 15. Final implementation report

Codex must finish with:

- architecture discovered,
- channel source and semantics,
- whether Core changed,
- all changed files grouped by project,
- removed legacy frontend files,
- build commands/results,
- test commands/results,
- browser/demo verification,
- remaining limitations or assumptions.

Do not report a requirement as satisfied unless it was actually verified.
