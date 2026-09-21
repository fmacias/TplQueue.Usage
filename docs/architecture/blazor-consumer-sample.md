# Blazor frontend architecture and contract ownership

This is the maintained guide for `TplQueue.Sample.BlazorSignalR` and its ETL
integration. The supplied JobMonitor instructions replace the former ScatterChart
direction. The current frontend is the reusable JavaScript `<job-queue-timeline>`
under [tools/TplQueue.JobMonitor](../../tools/TplQueue.JobMonitor/README.md).

## Current host and ownership

The sample is a passive .NET 8 Interactive Server application. C# owns queue
configuration, payloads, handlers, retries, graph topology and materialized state.
The hosted service attaches observers before submitting two three-job ETL roots
to each of ParallelQ, FifoQ and CacheQ. Browser connections do not start workloads.
The built-in Blazor circuit carries updates; there is no application REST API,
OpenAPI document or custom dashboard SignalR hub.

```text
queue execution capacity and lifecycle snapshots
  -> EtlQueueObserver
  -> thread-safe EtlExecutionProjectionStore
  -> detached JobMonitorSnapshot via JobMonitorMapper
  -> Dashboard InvokeAsync / JobMonitor Razor wrapper
  -> JS snapshot bridge -> job-queue-timeline
```

The browser sees stable presentation DTOs, never IJobEvent, live jobs, payload
graphs, schedulers or synchronization objects. The singleton store owns execution
facts; selection, viewport, reference time and component resources belong to each
circuit. Snapshots remain handwritten contracts requiring normal semantic review.

## Logical execution channels

A channel names queue capacity owned by one execution. It is not a thread, Task,
root family, job hash or frontend lane heuristic. A queue configured with
MaxParallelism N has channels 0 through N-1. Capacity ownership can include
dependency waiting and retry-policy delays; a channel does not assert that a
handler is using CPU throughout the interval.

The additive public `IJobExecutionEvent : IJobEvent` interface carries nullable
ExecutionChannel metadata. Existing event implementations need not implement it.
The runtime captures channel identity in immutable lifecycle events, including
terminal events, so asynchronous delivery remains correct after capacity reuse.
No scheduling, retry, cancellation, dependency or cache-acknowledgment semantics
are assigned to the frontend. Implementation details stay in private Core docs.

Queued jobs and legacy events without that metadata project with channel null.
They appear in an explicit Unassigned area outside the queue's numbered channels.
A late pre-execution event cannot erase a known channel. The projection rejects
out-of-range channels before mutating state and keeps detached snapshots.
Queue MaxParallelism is read from the configured queue instances.

## Monitor contract and behavior

`JobMonitorSnapshot` contains queue ID/name/capacity and individual job ID,
root ID when known, name, description, channel, observation time, normalized
state, duration when known, explicit dependency IDs, bounded metadata and root flag.
The runtime currently supplies no description; the mapper leaves it empty.
Metadata includes observer event type, retry count and duration provenance.
Raw exceptions and payloads are not passed to JavaScript. The projection's
existing error summary remains available to C# consumers.

Node time uses FirstObservedAt; lifecycle changes do not replace it with receipt
time. Duration is derived from observer start/end timestamps and is labeled as
such. Root identity can be backfilled by a root-success event; it never places
jobs into channels. Only explicit dependencies create edges.

The monitor provides dark theme tokens, search by name/ID/description, connected
graph focus, selection, bounded hover metadata, zoom, draggable/keyboard queue
dividers, horizontal overflow and vertical history navigation. No permanent
details panel reserves viewer space. State normalization belongs to the model;
unknown states remain visible.

The overview displays five seconds ending at the reference datetime at the bottom.
Live following moves older jobs upward; Pause freezes the window. History
navigation preserves its duration. A fixed left UTC ruler aligns with exact job
centers. The controller owns time; the renderer never reads the clock.

Square position markers grow from 2 to 12 pixels during closer inspection, with
readable outlines and state symbols. Channel pitch defaults to 40 pixels and does
not grow with time zoom. Same-channel collisions produce count markers spanning
the actual grouped interval; individual timestamps are never displaced. Selecting
a count opens a shorter interval and a temporary list for individual selection,
including identical timestamps. Back to 5 seconds restores the overview. Straight
connectors join visible markers; internal group edges appear when jobs separate.
See the component README for geometry, configuration and pixel-resolution limits.

## Rendering and lifecycle

Keep validation, normalization, graph traversal, channel meaning, layout, timing,
collision policy and theme configuration outside `svg-renderer.js`. The renderer
consumes prepared coordinates, paths and labels and writes safe SVG text.
The reusable source has no Blazor, ASP.NET, package-manager or CDN dependency.

`JobMonitor.razor` serializes initialization, snapshot updates and async disposal
with a per-component gate. It creates one callback reference and JS subscription,
coalesces pending snapshots, and tolerates circuit disconnection during cleanup.
The page dispatches observer notifications through InvokeAsync, coalesces queued
refreshes and unsubscribes on disposal. No observer thread calls JavaScript.

MSBuild synchronizes the single component source tree into the ignored
`wwwroot/job-monitor` directory before static asset discovery, then copies assets
for build/publish. This supports source-directory launches and published hosts.
Stale generated files are removed; Debug browser tests are excluded from publish.
Do not manually edit generated copies.
ChartJS, ScatterChart, Bootstrap package/assets and the former details panel are
removed from the sample. The previous pure C# mapper remains only under the
integration test project's `Legacy/` directory to preserve existing tests;
it is not part of the host or its browser contract.

## Validation

Use repository build/test scripts; workspace source validation is:

```powershell
.\build.ps1 -Configuration Debug -RunTests
```

From `tools/TplQueue.JobMonitor`:

```powershell
node scripts/check.mjs
node --test tests/layout.test.js
node demo/server.mjs
```

The standalone browser harness is `/tests/browser.html` on the demo server.
The Debug Blazor host serves `/job-monitor/tests/blazor.html` for actual circuit,
channel/selection and navigation/disposal acceptance; it is excluded from publish.
HTTP prerender smoke tests alone do not establish interactive browser behavior.
Report exact commands, results and any unverified acceptance separately.

Channel tests cover concurrent uniqueness, range, stability, release after
completion/failure/cancellation, FIFO zero and independent queues. Projection
tests cover nullable legacy channels, late-event enrichment, invalid channels,
detached snapshots and presentation-only serialization. Existing tests are retained.

## Remaining boundaries and future work

CacheQ here uses process-local memory, hydration and registered handlers; it is
not a durable spool or replayable event log. Projection retention is currently
unbounded for the finite demo; a continuous production stream needs explicit
retention and snapshot sizing. Observer delivery remains asynchronous and
best-effort. WaitAsync does not wait for observers or cache acknowledgment.

Contract generation is a future option, not an existing provenance claim. A
future TypeScript/Angular consumer should use reviewed transport contracts and
reproducible generation, with handwritten semantic mappers. Do not introduce
OpenAPI, a custom SignalR hub, dynamic schema forms or an industrial ingestion
architecture solely because earlier sketches mentioned them.

The sample guide belongs to public Usage. Public product documentation and site
synchronization continue to use `TplQueue.Adapter/docs/<lang>/`; private Core
implementation documentation stays in Core. Workspace project-reference switching
is intentional. The Blazor/ETL path retains its documented sibling-source preview
exception. Package consumption is validated separately through coordinated
workspace packing into `../TplQueue.NugetLocal`; never use `_local-packages`.

## ScatterChart frontend intent

This historical anchor is retained for old links. The ScatterChart intent is
superseded by the JobMonitor implementation and rules above.
