# Blazor frontend architecture and contract ownership

This is the maintained guide for `TplQueue.Sample.BlazorSignalR` and its ETL
integration. The supplied JobMonitor instructions replace the former ScatterChart
direction. The current frontend is the reusable JavaScript `<job-queue-timeline>`
under [tools/TplQueue.JobMonitor](../../tools/TplQueue.JobMonitor/README.md).

## Current host and ownership

The implementation project and namespaces are `TplQueue.Sample.Simulation`
(formerly `TplQueue.Sample.Etl`). The contracts project and namespaces remain
`TplQueue.Sample.Etl.Contracts`; `IEtlWorkflow` and `AddSampleEtlWorkflow` retain
their names. Existing consumers of implementation namespaces must rebuild with
the new references. Cache payload type names follow the renamed assembly and
namespace; the sample uses process-local memory, with no persisted cache migration.

The sample is a passive .NET 8 Interactive Server application. C# owns queue
configuration, payloads, handlers, retries, graph topology and materialized state.
The hosted service attaches all observers before starting `ISimulationService`,
then adapts application shutdown to that service. The Simulation module owns
measurement collection, scenario orchestration and finite timer delivery.
Browser connections do not start workloads.
The built-in Blazor circuit carries updates; there is no application REST API,
OpenAPI document or custom dashboard SignalR hub.

### Finite scenario delivery

`AddSampleEtlWorkflow()` registers three scenarios, `etl-parallel`, `etl-fifo` and
`etl-cache`. Each submits one existing three-job ETL root per tick, after a
one-second startup offset and then at a three-second interval, for two ticks.
The per-scenario admission bound is two active roots, including queued roots.
With successful submissions this produces six roots and eighteen unique jobs;
Ingest -> Transform -> Load and handler delays 500/700/400 ms are unchanged.
The accepted 15/50-job graph defaults remain for later graph scenarios.

The overload accepting `SimulationScenarioSettings` configures scenario ID,
queue, interval, startup offset, roots per tick, finite repetitions and maximum
active runs. Settings are immutable and validated before timer creation; scenario
IDs must be unique. Interval is 1 through `Int32.MaxValue` milliseconds; offset
is zero through that maximum. Zero offset schedules an asynchronous first tick
at timer resolution. Roots per tick and repetitions are positive; the active-run
bound must fit a complete batch.

Each scenario owns one internal `System.Timers.Timer`. Its short elapsed callback
counts a tick and schedules at most one tracked submission worker. Busy ticks or
ticks without room for a full batch count as skipped; they consume repetitions
and create no catch-up backlog. Admission includes previously submitted roots
until the runtime observes their terminal outcome, including cache-rehydrated
roots. Delayed or lost terminal observations conservatively retain capacity.
Submission failures are observed in the worker, counted, and exposed as the last
error message in detached `ScenarioDeliverySnapshot` values. A partially submitted
batch retains its accepted roots; later ticks may continue. Runtime handler
failures continue through the existing queue observer path.

The lifecycle is single-use. `Start` rejects repeated starts and starts after
stop/disposal. `StopAsync` closes admission and waits for any already-admitted
submission; no submission remains after its returned task completes. It neither
drains nor cancels accepted graphs. The host passes its shutdown token to the
simulation and the graphs, preserving shutdown cancellation. `Completion` means
finite arrivals and submissions have finished, not that graph execution, observer
delivery or cache acknowledgment has finished. Stop/dispose ignore late callbacks;
disposal also waits for submission work before releasing timers. Dashboard Pause
still freezes only viewing time. Restart, explicit drain/cancel controls and
continuous retention are deferred to their later tasks.

Contracts remain in `TplQueue.Sample.Etl.Contracts`; timer types and live jobs
stay internal. Delivery snapshots expose scenario IDs and accepted root IDs, but
do not add running/failed/cancelled graph membership to the monitor (P03).

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

Keep `ExecutionChannel` on the optional `IJobExecutionEvent` extension for the
current compatible API. An execution event is already one `IJobEvent` object;
the channel does not need a separate event or observer stream. Adding a required
nullable member to `IJobEvent` would still break existing implementations.
For a future breaking contract version, moving `int? ExecutionChannel` onto
`IJobEvent` could simplify consumers if channel metadata becomes universal.
It must remain nullable before assignment or when unknown, queue-local, and
captured at publication time, including terminal events. This refresh change
does not require that migration.

Queued jobs and events without both channel metadata and a captured channel-bearing
Started event project with channel null. They appear in a collapsible Unassigned
strip outside the queue's numbered channels. The strip starts collapsed, reports
the total count, and can be expanded by mouse or keyboard. Its tooltip distinguishes
the total from the count in the visible time window. Search reveals older waiting
jobs and expands their strip automatically. Recorded enqueue markers remain in
the strip after assignment; its count includes this history. Only strips without
either unassigned jobs or retained enqueue markers disappear.
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

Assigned node time uses ChannelStartedAt, captured only from a channel-bearing
Started event. Running, retry and terminal observations do not substitute their
timestamps for channel acquisition. A late Started event enriches the position
without regressing lifecycle state. Until that event is known, the node stays
Unassigned at EnqueuedAt (or FirstObservedAt when enqueue is unknown). On assignment
the same job gains its channel/start position and retains its enqueue marker in U.
The additive nullable `enqueuedAt` transport field supplies the recorded enqueue
time, also retained in metadata. Layout draws a directed enqueue-to-start connector
when both positions (or their groups) are visible. The two positions share one
job identity, selection callback and job count; enqueue history is not another job.
Missing enqueue observations produce no historical marker. Duration still uses observer lifecycle timestamps and is labeled as
such. Root identity never places jobs into channels. Explicit dependencies create
job-to-job edges; enqueue-to-start connectors describe positions of the same job.
No scheduler or observer contract change is required.

The monitor provides dark theme tokens, search by name/ID/description, connected
graph focus, selection, bounded hover metadata, zoom, draggable/keyboard queue
dividers, horizontal overflow and vertical history navigation. No permanent
details panel reserves viewer space. State normalization belongs to the model;
unknown states remain visible.

The overview displays five seconds ending at the reference datetime at the bottom.
Live following advances to the browser clock minus `liveLagMs` only on accepted
snapshot arrival or an explicit Follow live/restore-live action. There is no
periodic refresh; an idle view stays still. Pause freezes the window while new
snapshots still update job state. History
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

The page loads one initial snapshot and then refreshes only for new accepted
`IJobEvent` observations. Duplicate/rejected events do not notify the view.
The wrapper tracks snapshot identity so selection callbacks and unrelated parent
renders do not resend data. Replace detached snapshots; never mutate a delivered
instance. Several observations may be coalesced into the latest snapshot: DTOs
represent accumulated job state, not a one-to-one event log or a CLR interface
serialization. The standalone component receives `setData` calls from its host;
it has no runtime subscription or polling timer. Its one-shot redraw scheduler
coalesces incoming data, user input and resize requests, and stops when idle.

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
