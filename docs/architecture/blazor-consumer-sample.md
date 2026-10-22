# Blazor frontend architecture and contract ownership

This guide describes the current source implementation of
`TplQueue.Sample.BlazorSignalR` and its Domain, Contracts and Simulation integration.
Source and supported public API contracts take precedence over historical plans.
The frontend is the reusable JavaScript `<job-queue-timeline>` under
[tools/TplQueue.JobMonitor](../../tools/TplQueue.JobMonitor/README.md).

The [PlantUML component diagram](blazor-sample-components.puml) distinguishes direct
project/package references from runtime and asset integration. Future scenarios
are specified in the [use-case catalog](../development/simulation-use-case-plan.md).

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

These relationships come from the four current project files:
[BlazorSignalR](../../samples/TplQueue.Sample.BlazorSignalR/TplQueue.Sample.BlazorSignalR.csproj),
[Domain](../../samples/TplQueue.Sample.Domain/TplQueue.Sample.Domain.csproj),
[Etl.Contracts](../../samples/TplQueue.Sample.Etl.Contracts/TplQueue.Sample.Etl.Contracts.csproj),
and [Simulation](../../samples/TplQueue.Sample.Simulation/TplQueue.Sample.Simulation.csproj).
Domain, Contracts and Simulation target `netstandard2.0`. Contracts and Simulation
explicitly select C# 9; Domain inherits C# 12 from `Directory.Build.props`.
Simulation also directly references Microsoft DI and Logging Abstractions 9.0.8.
All direct TplQueue package versions use the shared `TplQueuePackageVersion` property.

### Shared contract responsibility

`TplQueue.Sample.Etl.Contracts` is the shared contract boundary for Domain,
Simulation and their consumers. Alongside measurement and payload contracts it
contains `ISampleJobFactory`, queue/cache wrapper interfaces, `IEtlQueueRuntime`,
`ISimulationWorkflow`, `ISimulationService`, `ISimulationGraphCatalog` and delivery
DTOs. Its responsibility already extends beyond ETL. Browser-specific
`JobMonitorSnapshot` remains owned by the Blazor host and carries original
`JobEventSnapshot` occurrences plus queue and composition metadata.

`TplQueue.Sample.Contracts` would express this broader responsibility more clearly.
That is a future naming proposal, not an applied project/assembly/namespace rename
or a prerequisite for a use case. Keep the current dependency direction and avoid
moving Domain implementations into Contracts.

### Composition and queue identity

`Program.cs` calls `AddTplQueue`, `AddSampleDomain`, and `AddMeasurementEtlConsumer`.
It unconditionally registers `AddSampleEtlWorkflow`, `AddSampleSingleJobSimulation`
and `AddSampleMultipleFifoRootsSimulation` together. Every added workflow joins
the host without configuration flags or profile selection. After building the
host, it calls `RegisterSampleEtlPayloadHandlers`. These existing composition
entry points define the current application setup.

Domain registers transient queue/cache wrappers and singleton business data and
job-factory services. The singleton `IEtlQueueRuntime` captures one set of wrappers;
the dashboard catalog and observers obtain those same queues through the runtime.
They do not resolve another set of transient wrappers. Queue option names remain
`FifoQ`, `ParallelQ` and `CacheQ`. Payloads are internal implementations with public
constructors for System.Text.Json hydration and an explicit type allowlist.

The runtime exposes both `Enqueue` and `EnqueueFifo`. The latter delegates to
`IParallelQ.EnqueueFifo` or the generic `ICacheQ.EnqueueFifo` for those queues;
the inherently serial `IFifoQ` uses its existing `Enqueue` operation. Both paths
capture composed graph membership before dispatch and share linked cancellation
registration and terminal/failure cleanup. UC03 explicitly calls `EnqueueFifo`
on FIFO, ParallelQ and CacheQ; ETL and SingleJob continue using `Enqueue`.

Factory overloads copy caller-supplied measurements into immutable payloads. The
Simulation-owned `SampleMeasurementSource` supplies the workflow batches. Domain is sample code,
not a product package or a new documentation publishing source. Adapter's language
trees remain the public product-documentation source.

The hosted service subscribes before resuming polling and starting workflows.
Browser connections do not start workloads. The built-in Blazor circuit carries
updates; there is no application REST API, OpenAPI document or custom dashboard hub.

### Workflow structure

ETL, SingleJob and MultipleFifoRoots implement `ISimulationWorkflow` through
`ScheduledWorkflow`. Each workflow owns one `System.Threading.Timer`. ETL and
SingleJob attempt a submission to each of the three queues per tick. UC03 adds
one timer that requests FIFO submission on all three queues, so the host starts
three continuous workflow timers. Each
Submit call requests fresh Domain jobs; no workflow replays cached job instances.
`ISimulationService` coordinates Start, StopAsync, Completion and detached snapshots.
Neither workflow nor session contract implements IDisposable; the host awaits
StopAsync during shutdown. The runtime separately owns queue and subscription cleanup.

| Simulation folder | Responsibility |
| --- | --- |
| `Workflows/Etl`, `Workflows/SingleJob`, `Workflows/MultipleFifoRoots` | Request Domain graphs through `ISampleJobFactory` and submit them |
| `Workflows/ScheduledWorkflow.cs` | Timer ownership, queue selection, stop coordination and delivery counters |
| `Measurements` | Supply measurement batches through `IMeasurementSource` |
| `Session` | Coordinate the registered workflows |
| `Execution` | Queue access, submission, subscriptions and root cancellation tracking |
| `Graphs` | Retain composed graph membership independently of execution outcomes |
| `Composition` | Register Simulation services and the chosen workflow types |

The old finite delivery profiles, scenario settings, `IEtlWorkflow` and scenario
classes have been removed from the current sample. They are not retained under a runtime
`Legacy/` folder. The test-only legacy ScatterChart mapper is a separate retained artifact.

The [scheduled workflow](../../samples/TplQueue.Sample.Simulation/Workflows/ScheduledWorkflow.cs)
is the extension point for scenarios. Its `Submit` hook returns one root ID. The
constructor accepts distinct target queues and a positive arrival interval.
Defaults remain all queues and three seconds; the startup offset remains one second.
Arrivals continue until StopAsync or host cancellation. There is no sample-side
active-root cap or total root limit. Stored root IDs are delivery history, never
cached jobs or a gate on new submissions. Execution capacity belongs to each queue.

### Continuous combined demo

The host registers both continuous workflows alongside UC03. Their fixed timing is:
one second before the first tick and three seconds between ticks. There is no
`Simulation:Profile`, `Simulation:IntervalSeconds` or `Simulation:StartupOffsetSeconds`
selection in the current host. Passing those arguments does not configure delivery.

Each full pair of ticks submits three ETL roots and three independent ingest roots:
six roots and twelve jobs. Earlier queued/running roots do not suppress new
submissions in any workflow. A busy submission callback still skips an overlapping
tick; this guards callback overlap, not queue execution capacity. Submission failures
are recorded without preventing attempts on remaining queues or later ticks.
Snapshots aggregate accepted root IDs and counters; SkippedTicks counts callback
overlap and FailedTicks counts affected ticks. No catch-up scheduler is added.
UC03 continuously submits a fresh root to each queue through `EnqueueFifo` every
100 ms, sharing the queues with ETL and SingleJob. Timer interleaving determines
enqueue order across workflows; UC03 ordering is queue-local. FIFO's serial execution can leave
many roots waiting, all represented through actual queue observations.

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
measurements/summaries accumulate for the process lifetime. Neither the sample's
arrival backlog nor graph/business-data retention is bounded. Monitor observation
retention is bounded independently. UC23 and UC25 are partially implemented:
broader lifecycle controls and coordinated retention remain to be added. The
[continuous-demo record](../development/simulation-continuous-demo.md) preserves
history separately from active specifications.

`EtlQueueRuntime` internally offers `Cancel(Guid)` and retains linked per-root
cancellation sources, but `IEtlQueueRuntime` currently exposes only enqueue,
queue access, polling, observation and active-root inspection. UC17 needs a small
Usage contract extension to reach cancellation through normal composition.
Neither the session nor the dashboard currently offers per-root cancellation,
pause-arrivals/resume or a drain operation.

### Finite scenario delivery

This anchor is retained for historical task links; all current workflows are continuous.
The old finite host profiles and configurable `SimulationScenarioSettings` remain
unavailable. Earlier P02/UC01 execution records are historical evidence, not launch
instructions for the current host. Completion still means arrivals/submissions
have stopped, not that accepted jobs or asynchronous observers have finished.
The dashboard's arrivals state follows session Completion after Stop or shutdown.

### Multiple FIFO roots (UC03)

The standard host always includes `MultipleFifoRootsWorkflow`
(`multiple-fifo-roots` in delivery snapshots) alongside ETL and SingleJob.
Every arrival requests a fresh independent ingest root per queue from the existing
Domain factory through `ISampleJobFactory`. Each root has its own job ID; instances
are not shared between queues. Arrivals begin after one second and continue
at 100 ms intervals without a root-count limit or active-root cap. The existing
500 ms ingest handler lets queued work accumulate. UC03 uses `EnqueueFifo` on FIFO,
ParallelQ and CacheQ on every tick;
ETL and SingleJob continuously submit to FIFO, Parallel and Cache at their existing intervals.

Successful submissions are recorded in order in the delivery snapshot. Accepted
roots retain their host token and use each queue's public FIFO submission contract.
FIFO runs on channel zero; ParallelQ and CacheQ retain their actual observed channels.
There is no global ordering across queues. A failed submission is recorded without
preventing attempts on the other queues or fresh roots on later ticks.
Stop arrivals closes all workflow timers without cancelling accepted work. Keep
the host running to observe the accumulated backlog drain after stopping arrivals.

Graph membership is captured before enqueue, keeping each independent root in its
own composed run. FIFO ordering edges do not merge those memberships. The existing
projection shows waiting jobs in Unassigned and execution on the observed channel;
no queue scheduling or event data is synthesized by the scenario. Observer delivery
order does not establish handler order, and queue `WaitAsync` does not drain observers.
Host ownership, timer cleanup, runtime identity and browser lifecycle are unchanged.

The focused integration test holds handlers while at least eight roots per queue
are accepted, explicitly stops arrivals, then checks handler start/finish order per
queue separately from observer completion, execution channels and composed membership.
CacheQ uses the gated registered handler after hydration. A combined integration
case includes all three workflows and verifies they continue until explicit stop.
Runtime and browser
acceptance remain human-run; see the [sample launch guide](../../samples/TplQueue.Sample.BlazorSignalR/README.md).

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
survives terminal outcomes for the process session. It does not assign queues,
channels, timestamps, status or scenario execution facts. Membership remains on
submission exceptions because a queue may already have published events; it is
not proof that enqueue succeeded. Admission and accepted-root counters track
successful runtime submissions separately.

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

Selection highlights composed memberships and their immediate prerequisites and
dependents for all outcomes. Explicit navigation follows longer chains without
highlighting the entire FIFO history. FIFO ordering edges remain visible but do not merge composed run
memberships. Root lists never choose execution channels or synthesize edges.
Shared membership support does not mean a runnable shared/cross-queue workflow
already exists. The current factory supplies only independent ingest roots and
three-job ETL chains; UC06–UC18 specify additional graph/outcome scenarios.
This catalog is process-local and not restart-durable; UC25 covers retention.

```text
runtime IJobEvent
  -> JobMonitorObserver (synchronous Adapter snapshot capture)
  -> JobMonitorStore (original immutable JobEventSnapshot occurrences)
  -> JobMonitorSnapshot (retained events + queue capacity + composed memberships)
  -> Dashboard InvokeAsync / JobMonitor Razor wrapper
  -> JS bridge -> event-stream.js -> job-queue-timeline
```

`Presentation/Monitoring` separates observer capture, queue lookup, retention and
transport. There is no per-job C# state DTO or ETL mapper chain. The browser receives
detached metadata, including exception diagnostics, never raw events, live jobs,
payloads or exceptions. Diagnostics are omitted from renderer metadata. The
singleton store retains observations; each circuit owns selection, viewport,
reference time and component resources. The pure JavaScript projection derives
positions without changing the original stream.
`JobMonitorJson` serializes the envelope with maximum JSON depth 2048 and sends
the resulting string through interop. The bridge parses it before projection;
this preserves deep dependency metadata without relying on interop's default depth.

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

`JobMonitorSnapshot` contains ordered `JobEventSnapshot` occurrences, queue
runtime/display IDs and capacity, composed root memberships, an accepted-occurrence
revision and an expired-observation count. Repeated Running and RootSuccessed
observations remain separate entries, including equal-valued callbacks. There is
no producer event ID, so metadata fingerprint deduplication would lose occurrences.

`event-stream.js` derives one current renderer job per observed ID. It normalizes
states, accumulates direct dependency IDs, resolves lifecycle times and associates
channels with Started observations. Configured queue capacity and composed root
membership remain explicit metadata because events cannot supply them. The renderer
receives bounded event/retry/timestamp metadata. Exception diagnostics remain in
the original snapshots, outside the renderer model. Description remains empty.

Assigned node time uses a channel start captured only from a channel-bearing
Started event. Running, retry and terminal observations do not substitute their
timestamps for channel acquisition. A late Started event enriches the position
only for the captured channel once a job is terminal. Later observations of that
terminal identity on another channel do not relocate its completed execution.
Active jobs may still change channel and require a matching Started timestamp.
Late start enrichment continues
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

The monitor provides dark theme tokens, search by name/ID/description, composed-run
and immediate-dependency focus, selection, bounded hover metadata, zoom, draggable/keyboard queue
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
a count opens a shorter interval and a temporary docked inspector for individual
selection. The time axis expands enough to separate distinct same-lane timestamps;
large groups scroll vertically with an aligned UTC ruler. Selecting a member or
resizing retains the full group's interval. Identical timestamps remain count
squares with edges and individually selectable list entries. Counts are centered
without a small dependency label; internal dependencies remain in tooltips and
inspection. Group squares are white until every member completes, then green,
using current job state even for retained enqueue positions.
Before advancing live time would scroll any visible group member out of the
interval, the controller holds the current reference. Snapshots continue updating
state, so a completed group remains a green numbered square in the same timeline.
Follow live explicitly catches up to the latest five seconds. Clicking a group or
zooming resolves its members; further group zoom adds vertical space without
cropping siblings. Explicit snapshot removal of jobs still applies.
Queue summary buttons retain access to all observed jobs when they leave the live
interval, including CacheQ history. Root groups list actual composed
memberships, independently of time-based collision groups. Inspection holds one
captured snapshot; subsequent snapshots replace a pending latest snapshot until
Refresh inspection, close, Back to 5 seconds or Follow live. Selection retains job
identity across refreshes. Back to 5 seconds restores the overview. Straight arrows
point from prerequisite to dependent, including root-to-root ordering. Relationships
between the same visible groups share a counted connector; internal dependencies
are counted on groups and individually navigable in inspection. U displays current
waiting count separately from retained enqueue history in its tooltip.
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

The page subscribes before loading its initial retained window, then refreshes on
each accepted observation. Rejected input does not notify; identical observations
do notify and remain separate. Coalescing sends a complete retained window, so
intermediate occurrences survive even if several notifications become one render.
A late circuit receives that same window. Interop snapshot identity checks avoid
resends on unrelated renders. The bridge projects before calling `setData`; it has
no polling timer. Selection, resize and user navigation can redraw locally.

MSBuild synchronizes the single component source tree into the ignored
`wwwroot/job-monitor` directory before static asset discovery, then copies assets
for build/publish. This supports source-directory launches and published hosts.
Stale generated files are removed; Debug browser tests are excluded from publish.
Do not manually edit generated copies.
ChartJS, ScatterChart, Bootstrap package/assets and the former details panel are
removed from the sample. The obsolete ScatterChart mapper and its retired-contract
tests have been removed. Store/capture tests cover the observation boundary;
JavaScript event-stream tests cover timeline semantics alongside renderer/browser tests.

## Validation

The human developer owns runtime validation. Relevant entry points are documented
in the [local development reference](../development/local-development.md),
[sample README](../../samples/TplQueue.Sample.BlazorSignalR/README.md) and
[JobMonitor guide](../../tools/TplQueue.JobMonitor/README.md). Existing source
fixtures and historical results do not constitute fresh runtime acceptance.
HTTP/prerender checks do not establish interactive circuit behavior.

## Remaining boundaries and future work

CacheQ here uses process-local memory, hydration and registered handlers; it is
not a durable spool or replayable event log. `JobMonitor:MaximumEvents` bounds
retained observation count (default 20,000). The store evicts entire jobs in
first-observed order, including active jobs and a single oversized job. Later
observations may introduce an evicted ID again; missing earlier facts remain unknown.
Surviving histories retain all captured positions. The page reports expired events;
new windows remove evicted jobs from the live model. An inspection already holding
a snapshot retains it until refresh under the existing UI rules. No unbounded
fingerprint index remains. Snapshot graph sizes are not byte-bounded; Simulation
membership and business-data retention remain separate future work. Windows are
currently replaced in full rather than transmitted incrementally.
Observer delivery remains asynchronous and
best-effort. WaitAsync does not wait for observers or cache acknowledgment.

Future implementations stay in Usage. Product requirements belong in the sample's
[README Roadmap](../../README.md#roadmap), with motivation, affected public API or
behavior and the blocked use-case ID. Existing sample gaps, including retention
and cancellation interface exposure, do not by themselves justify product changes.

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
