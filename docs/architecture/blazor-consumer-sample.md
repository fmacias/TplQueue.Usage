# Blazor frontend architecture and contract ownership

This is the maintained guide for `TplQueue.Sample.BlazorSignalR` and its ETL
integration. The supplied JobMonitor instructions replace the former ScatterChart
direction. The current frontend is the reusable JavaScript `<job-queue-timeline>`
under [tools/TplQueue.JobMonitor](../../tools/TplQueue.JobMonitor/README.md).

## Current host and ownership

The standalone Usage solution consumes TplQueue `0.2.0-preview.2` NuGet packages.
Domain now lives in `samples/TplQueue.Sample.Domain` inside this repository; a
sibling product or WorkspaceTplQueue checkout is not required to build the sample.
See [local development](../development/local-development.md) for feed setup.

| Project | Responsibility and dependencies |
| --- | --- |
| `TplQueue.Sample.Etl.Contracts` | Sample interfaces and immutable DTOs; references the Abstractions package |
| `TplQueue.Sample.Domain` | Job factories, payloads, handlers, queue/cache wrappers and business data; references Contracts and the MemCache package |
| `TplQueue.Sample.Simulation` | Measurements, scheduling, admission, graph membership and runtime access; references Contracts, without a Domain source dependency |
| `TplQueue.Sample.BlazorSignalR` | .NET 8 Interactive Server host, composition, observer projection and presentation; references the three local sample projects plus Core and DI packages |

`Program.cs` calls `AddTplQueue`, `AddSampleDomain`, `AddSampleEtlWorkflow`,
`AddSampleSingleJobSimulation` and `AddMeasurementEtlConsumer`. After building the
host, it calls `RegisterSampleEtlPayloadHandlers`. These existing composition
entry points define the current application setup.

Domain registers transient queue/cache wrappers and singleton business data and
job-factory services. The singleton `IEtlQueueRuntime` captures one set of wrappers;
the dashboard catalog and observers obtain those same queues through the runtime.
They do not resolve another set of transient wrappers. Queue option names remain
`FifoQ`, `ParallelQ` and `CacheQ`. Payloads are internal implementations with public
constructors for System.Text.Json hydration and an explicit type allowlist.

Factory overloads copy caller-supplied measurements into immutable payloads. The
host's measurement source supplies the workflow batches. Domain is sample code,
not a product package or a new documentation publishing source. Adapter's language
trees remain the public product-documentation source.

The hosted service subscribes before resuming polling and starting workflows.
Browser connections do not start workloads. The built-in Blazor circuit carries
updates; there is no application REST API, OpenAPI document or custom dashboard hub.

### Workflow structure

ETL and SingleJob implement `ISimulationWorkflow` through `ScheduledWorkflow`.
Each workflow owns one `System.Threading.Timer` and attempts a submission to each
of the three queues per tick. The normal host therefore owns two workflow timers.
`ISimulationService` coordinates Start, StopAsync, Completion and detached snapshots.
Neither workflow nor session contract implements IDisposable; the host awaits
StopAsync during shutdown. The runtime separately owns queue and subscription cleanup.

| Simulation folder | Responsibility |
| --- | --- |
| `Workflows/Etl`, `Workflows/SingleJob` | Request Domain graphs through `ISampleJobFactory` and submit them |
| `Workflows/ScheduledWorkflow.cs` | Timer ownership, per-queue admission and aggregate delivery counters |
| `Measurements` | Supply measurement batches through `IMeasurementSource` |
| `Session` | Coordinate the registered workflows |
| `Execution` | Queue access, submission, subscriptions and root cancellation tracking |
| `Graphs` | Retain composed graph membership independently of execution outcomes |
| `Composition` | Register Simulation services and the chosen workflow types |

The old finite delivery, scenario settings, `IEtlWorkflow` and scenario classes
have been removed from the current sample. They are not retained under a runtime
`Legacy/` folder. The test-only legacy ScatterChart mapper is a separate retained artifact.

### Continuous combined demo

The host registers both workflows directly. Timing is fixed in ScheduledWorkflow:
one second before the first tick and three seconds between ticks. There is no
`Simulation:Profile`, `Simulation:IntervalSeconds` or `Simulation:StartupOffsetSeconds`
selection in the current host. Passing those arguments does not configure delivery.

Each full pair of ticks submits three ETL roots and three independent ingest roots:
six roots and twelve jobs. ETL permits two active roots per queue; SingleJob permits
one. Admission includes queued roots until the runtime observes a terminal event.
A busy callback skips its tick. A queue at capacity is skipped while other queues
can proceed. A submission failure is recorded without preventing attempts on the
remaining queues. Snapshots named `etl` and `single` aggregate counters and accepted
root IDs across the three queues. SkippedTicks and FailedTicks count affected ticks,
not individual queue attempts. There is no catch-up backlog.

**Stop arrivals** calls the singleton session's StopAsync, closes admission and
waits for any admitted submission. It does not drain or cancel accepted jobs.
Accepted jobs retain the host shutdown token. Completion reports that arrivals and
submissions have stopped, not that execution, observer delivery or cache acknowledgment
has finished. Stop is idempotent; Start is single-use. A workflow observes host
cancellation in its timer callback, and host shutdown also explicitly awaits StopAsync.

Every circuit observes Completion without polling. Navigating away or disconnecting
cancels only that circuit's completion wait. Opening another browser cannot restart
delivery; restart the server for a fresh session. Pause freezes only viewing time.

History, event fingerprints, graph membership, accepted-root lists and business
measurements/summaries accumulate for the process lifetime. Admission is bounded;
retention is not. UC23's broader lifecycle work and UC25's bounded retention remain
pending. The [continuous-demo record](../development/simulation-continuous-demo.md)
preserves historical validation separately from the current package migration.

### Finite scenario delivery

This anchor is retained for earlier task links. Finite host profiles and configurable
`SimulationScenarioSettings` belonged to the earlier implementation and are no longer
available. Current tests stop continuous workflows explicitly after the required
submissions. Earlier P02/UC01 execution records remain historical evidence, not
instructions for launching the current host.

### Single-job scenario (UC01)

The SingleJob workflow submits one independent ingest root per available queue on
each admitted tick. Each root has fresh job/operation identity and no composed
prerequisites. It uses the same measurement source, 500 ms ingest handler and Cache
hydration path as ETL. ETL adds Transform and Load with 700/400 ms delays.

Both workflows run in the standard host; `Simulation:Profile=single-job` is no
longer a supported selector. Search for `Single job:` to inspect independent roots
alongside ETL. FIFO can add ordering edges without merging composed root memberships.
The earlier UC01 capacity-one Cache observation is historical; it does not establish
the behavior of a newly rebuilt package. Use current test results for acceptance.

### Simulation graph identity

`ISimulationGraphCatalog` exposes detached root ID lists by job ID. The module
captures all reachable job IDs before enqueue can publish observations, hydrate
cache objects or add FIFO ordering edges. Each composed root ID identifies one
run in the current one-root-per-run model; no second run GUID is necessary.
The catalog retains IDs only, uses a lock for atomic registration and reads, and
survives terminal outcomes for the finite host lifetime. It does not assign queues,
channels, timestamps, status or scenario execution facts. Membership remains on
submission exceptions because a queue may already have published events; it is
not proof that enqueue succeeded. Admission and accepted-root counters retain
their separate P02 meaning.

The projection joins event-observed jobs to that catalog when making a detached
snapshot. It creates no jobs, enqueue timestamps or lifecycle events from catalog
membership. A job without its own observation remains absent; explicit dependency
IDs can therefore refer to unresolved endpoints. Missing observations are not
invented. Cache hydration retains the same IDs. Registered root success never
backfills or overwrites another job's lifecycle or membership.

`rootJobIds` contains every known composed root membership, sorted and detached.
Shared prerequisites have one global job ID and several root IDs; each root counts
that prerequisite once, while global job counts count it once. The legacy singular
`rootJobId` is the sole root when unambiguous, the job's own ID when it is itself a
root, and null for a shared non-root. `isRoot` is independent of terminal status.
Producers without a catalog retain the existing success-based fallback; their
early membership remains unknown. JavaScript accepts older singular-only producers
and emits both fields in `job-select`. The Blazor callback still selects by job ID.

Selection follows explicit dependency edges in both directions for all outcomes.
Selecting a shared prerequisite or either connected root highlights the whole
connected graph. FIFO ordering edges remain visible but do not merge composed run
memberships. Root lists never choose execution channels or synthesize edges.
Future multi-root scenario IDs and bounded continuous retention require their
own acceptance tasks; this catalog is process-local and not restart-durable.

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
all known root memberships (plus the compatible singular root), name, description, channel, observation time, normalized
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

From the TplQueue.Usage repository root, validate package consumption with:

```powershell
.\build.ps1 -Configuration Debug
.\test.ps1 -Configuration Debug
```

From `tools/TplQueue.JobMonitor`:

```powershell
node scripts/check.mjs
node --test tests/layout.test.js
node demo/server.mjs
```

The standalone browser harness is `/tests/browser.html` on the demo server.
The Debug Blazor host serves `/job-monitor/tests/blazor.html?profile=combined` for actual circuit,
channel/selection and navigation/disposal acceptance; it is excluded from publish.
HTTP prerender smoke tests alone do not establish interactive browser behavior.
Report exact commands, results and any unverified acceptance separately.

Channel tests cover concurrent uniqueness, range, stability, release after
completion/failure/cancellation, FIFO zero and independent queues. Projection
tests cover nullable legacy channels, late-event enrichment, invalid channels,
detached snapshots and presentation-only serialization. Tests follow the current
Domain/Simulation interfaces; historical finite-profile tests are not current
acceptance evidence.

## Remaining boundaries and future work

CacheQ here uses process-local memory, hydration and registered handlers; it is
not a durable spool or replayable event log. Projection retention is currently
unbounded for the process session; a continuous production stream needs explicit
retention and snapshot sizing. Observer delivery remains asynchronous and
best-effort. WaitAsync does not wait for observers or cache acknowledgment.

Contract generation is a future option, not an existing provenance claim. A
future TypeScript/Angular consumer should use reviewed transport contracts and
reproducible generation, with handwritten semantic mappers. Do not introduce
OpenAPI, a custom SignalR hub, dynamic schema forms or an industrial ingestion
architecture solely because earlier sketches mentioned them.

The sample guide belongs to public Usage. Public product documentation and site
synchronization continue to use `TplQueue.Adapter/docs/<lang>/`; private Core
implementation documentation stays in Core. Standalone Usage builds use package
references. Loading selected projects through WorkspaceTplQueue can still activate
its development-time source-reference switch; that does not prove package consumption.
Local preview packages belong in `../TplQueue.NugetLocal`, never `_local-packages`.

## ScatterChart frontend intent

This historical anchor is retained for old links. The ScatterChart intent is
superseded by the JobMonitor implementation and rules above.
