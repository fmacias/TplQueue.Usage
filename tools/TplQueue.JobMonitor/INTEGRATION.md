# Integration into `TplQueue.Sample.BlazorSignalR`

This runbook describes the current observation-stream integration. Earlier UI
decisions originate in Usage commits `ac680c2` and `931d6f0`; snapshot transport and
retention were refactored for Usage issue #1. Preserve the current channel,
frontend and asset-copy semantics when modifying this integration.

The [sample architecture guide](../../docs/architecture/blazor-consumer-sample.md)
owns overall architecture and repository boundaries. The [adopted frontend instructions](<Instructions for TplQueue.Sample.BlazorSignalR.md>)
record geometry, UX decisions and reasons; [README.md](README.md) is the component
API reference. Read applicable repository/workspace instructions before editing.
Verify source names if a later revision changes them.

## 1. Current host and source map

The host is a passive .NET 8 Blazor Web App using Interactive Server rendering.
The hosted ETL workload runs independently of browsers. The Blazor circuit carries
presentation updates; there is no application REST API, OpenAPI contract or custom
dashboard SignalR hub. `QueueObserverSignalRDashboard` is a separate sample with
its own transport and must not be confused with this host.

Paths below are relative to Usage:

| Path | Responsibility |
| --- | --- |
| `samples/TplQueue.Sample.BlazorSignalR/Presentation/Monitoring/JobMonitorObserver.cs` | Synchronous one-to-one capture through Adapter's snapshot factory. |
| `samples/TplQueue.Sample.BlazorSignalR/Presentation/Monitoring/JobMonitorQueueCatalog.cs` | Detached queue identity/capacity from configured queue instances. |
| `samples/TplQueue.Sample.BlazorSignalR/Presentation/Monitoring/JobMonitorStore.cs` | Thread-safe occurrence retention and whole-job eviction. |
| `samples/TplQueue.Sample.BlazorSignalR/Presentation/Monitoring/JobMonitorSnapshot.cs` | Retained observations with queue and membership metadata. |
| `samples/TplQueue.Sample.BlazorSignalR/Presentation/Monitoring/JobMonitorJson.cs` | JSON encoding with depth sufficient for nested job graphs. |
| `tools/TplQueue.JobMonitor/src/event-stream.js` | Pure current-state/timeline projection from retained snapshots. |
| `samples/TplQueue.Sample.BlazorSignalR/Components/Pages/Dashboard.razor` | Store subscription, InvokeAsync dispatch and accessible totals. |
| `samples/TplQueue.Sample.BlazorSignalR/Components/JobMonitor.razor` | Serialized JS initialization, updates and disposal. |
| `tools/TplQueue.JobMonitor/integrations/blazor/job-monitor.js` | Idempotent selection subscription and snapshot bridge. |
| `samples/TplQueue.Sample.BlazorSignalR/TplQueue.Sample.BlazorSignalR.csproj` | Shared asset synchronization for build/publish. |
| `test/integration/Fmacias.TplQueue.Usage.Integration.Test/Samples/JobMonitor*Tests.cs` | Capture, retention and transport regressions. |
| `tools/TplQueue.JobMonitor/tests/event-stream.test.js` | Lifecycle/channel/graph projection regressions. |

The actual direction is:

```text
IJobEvent -> JobMonitorObserver -> JobEventSnapshot
  -> JobMonitorStore -> JobMonitorSnapshot retained window
  -> Dashboard / JobMonitor Razor wrapper
  -> JS bridge -> projectEventStream -> job-queue-timeline.setData
```

Do not send live jobs, IJobEvent, payload graphs, queue instances, schedulers,
exceptions or synchronization objects across JS interop. The store accepts immutable
snapshots and preserves each callback occurrence; equal metadata is not evidence of
a duplicate. `JobMonitorJob`, `EtlDashboardSnapshot` and `JobMonitorMapper` are removed.
Original snapshot diagnostics travel as metadata but are omitted from the renderer
model. C# owns runtime behavior; the browser owns view state and timeline projection.

The Simulation module registers graph membership before enqueue and exposes it
through `ISimulationGraphCatalog`. The projection joins that composition metadata
to event-observed jobs. `rootJobIds` and `job-select` retain all shared memberships
for running, failed and cancelled graphs; the compatible singular `rootJobId` is
null for shared non-roots. Membership never substitutes for lifecycle observations
or channel acquisition. See the maintained [graph identity contract](../../docs/architecture/blazor-consumer-sample.md#simulation-graph-identity).

## 2. Channel observation: the chosen contract

The additive public `IJobExecutionEvent : IJobEvent` interface in Abstractions
exposes `int? ExecutionChannel`. Existing IJobEvent implementations need not
implement it. The compatible Abstractions contract was committed as `2c4e4f0`.

This is one event object on the existing `IJobEvent` stream, with optional channel
metadata, not a separate channel event. Retain this compatible extension for now.
Moving a required nullable property onto `IJobEvent` would break existing
implementations; reserve that simplification for a deliberate contract-version
change. See the [contract decision](../../docs/architecture/blazor-consumer-sample.md#logical-execution-channels).

A non-null channel is queue-local execution capacity in `0..MaxParallelism-1`.
The runtime captures it while capacity is held and retains it in terminal events
before release. It stays stable across retries in that execution. FifoQ uses 0.
Independent queues may each own channel 0; concurrent executions within one queue
cannot share it. Capacity can include dependency waiting and retry delays, so it
is neither a thread identifier nor a CPU occupancy measurement.

Private allocation details belong in Core documentation. Do not add another
allocator or change execution semantics for this integration. The initial viewer
required additive runtime observation support; the later Started/Unassigned
correction changed Usage projection and UI only, with no additional scheduler
or public observer contract change.

## 3. Projection rules: pair the channel with its start time

Separate first observation from channel acquisition. Mapping an eventual channel
onto enqueue time made sequential jobs appear simultaneous in one lane.

| Value | Meaning |
| --- | --- |
| `FirstObservedAt` | Earliest observation known to the projection. |
| `EnqueuedAt` | Earliest Cache/Enqueueing/Enqueued observation, if known. |
| `StartedAt` | Existing duration baseline, enriched from Dequeued/Started/Running observations. |
| `ExecutionChannel` | Captured channel metadata, if known. |
| `ChannelObservedAt` | Internal ordering timestamp for channel metadata. |
| `ChannelStartedAt` | Earliest matching channel-bearing Started time; the assigned position. |
| `EndedAt` | Latest known terminal observation. |

`StartedAt` and `ChannelStartedAt` intentionally differ. Do not use the broader
lifecycle baseline as channel-acquisition time.

The store validates queue/channel identity before appending. The JavaScript
projection independently validates transport input and applies these rules:

1. Validate channel range against the queue before mutating state.
2. Replay each retained occurrence in arrival order without changing or deduplicating the input.
3. Accept non-null channel metadata at least as recent as the stored channel
   observation until a terminal job has a captured channel. Later observations
   cannot relocate that terminal identity to another channel. Late null-channel
   events cannot erase captured identity.
4. If an active job's accepted channel changes, clear the old ChannelStartedAt.
5. A channel-bearing Started event matching the stored channel enriches
   ChannelStartedAt, even when delivered after Running or terminal events.
6. Preserve lifecycle precedence: delayed Started corrects placement without
   regressing terminal state to running. Same-rank events use timestamps; retry
   count retains the maximum observed value.
7. Return renderer state separately from event history. The C# store notifies outside
   its lock and isolates subscriber failures; no observer notification calls JavaScript.

Mapper rule:

```text
assigned = ExecutionChannel exists AND ChannelStartedAt exists

assigned:
    channel = ExecutionChannel
    observedAt = ChannelStartedAt
    metadata.timestampSource = Started
otherwise:
    channel = null
    observedAt = EnqueuedAt ?? FirstObservedAt
    metadata.timestampSource = Enqueued or FirstObserved

if EnqueuedAt exists:
    enqueuedAt = round-trip timestamp
    metadata.enqueuedAt = round-trip timestamp
```

For example, two jobs enqueued at 12:00:00 but started in channel 0 at 12:00:01
and 12:00:02 appear at distinct start times. Before each matching Started event
is known, that job stays in U. A terminal event arriving first can leave a completed
job in U temporarily: its state is known, but its placement is not.

Update the same job ID on assignment. The optional nullable `enqueuedAt` field
retains the observed enqueue marker in U, including for browsers connecting after
completion. Layout adds a dashed directed connector to the channel/start position
when both endpoints are visible and U is expanded. One logical job has two position
identities for keyboard focus, hover and grouped inspection; callbacks, graph
traversal and job counts retain the original job ID. Unknown enqueue time produces
no historical point. Terminal snapshots retain both known positions. JavaScript cannot
infer a Started timestamp from an arbitrary observedAt; other producers must
supply the same semantics.

## 4. Presentation DTO contract

The C# `JobMonitorSnapshot` has `Queues`, `Events`, `RootJobIds`, `Revision` and
`DroppedEventCount`, serialized to camelCase by Blazor. Events are the original
`JobEventSnapshot` objects, with numeric `JobEventStatus` values under default JSON
settings. The event projection also accepts enum names. Queues add `queueId` for
the runtime GUID alongside their stable display ID. Root membership is a dictionary
from job GUID to root GUID array. No intermediate per-job C# DTO is serialized.
`JobMonitorJson` encodes this envelope as a JSON string before interop, with a
maximum JSON depth of 2048. This accommodates nested job dependency metadata beyond
the default interop serialization depth without flattening it. The JS bridge parses
the string and also accepts an already-decoded object from another transport.

`projectEventStream` derives the existing renderer contract. A representative
assigned renderer job (not the C# transport) is:

```json
{
  "queues": [
    { "id": "parallel", "name": "ParallelQ", "maxParallelism": 3 }
  ],
  "jobs": [
    {
      "id": "job-42",
      "rootJobId": null,
      "name": "Normalize measurements",
      "description": "",
      "queueId": "parallel",
      "channel": 0,
      "observedAt": "2026-09-20T12:00:01.000Z",
      "enqueuedAt": "2026-09-20T12:00:00.000Z",
      "state": "completed",
      "durationMs": 380,
      "dependsOn": [],
      "metadata": {
        "event": "Successed",
        "retryCount": "0",
        "durationSource": "observer lifecycle timestamps",
        "timestampSource": "Started",
        "enqueuedAt": "2026-09-20T12:00:00.0000000+00:00"
      },
      "isRoot": false
    }
  ]
}
```

The example uses readable IDs; the sample emits Guid strings and its Razor callback
accepts Guids. Description is empty because the observer contract supplies none.
Duration is nonnegative EndedAt minus lifecycle StartedAt if both exist, otherwise
null. It is not measured handler CPU duration. Snapshot diagnostics remain available
on the transport but are omitted from the renderer model. Raw errors/payloads are
never retained.

JS also accepts optional top-level `referenceTime`, an ISO timestamp with timezone.
Supplying it to setData calls setReferenceTime and enters history mode. The C# DTO
intentionally omits it, so observer updates do not reset each browser's live/history
choice. The deterministic demo supplies it; synthetic live arrivals remove it.

Validation requires queues/jobs arrays, unique nonempty string IDs, known queues,
and integer maxParallelism in 1..1024. Every job explicitly supplies channel null
or an integer within the queue's range; omitted channel is invalid. Timestamps
require an explicit timezone. Invalid input leaves the previous model intact.
Dependencies must be an array when supplied; duplicates/self references are removed,
while unknown endpoint IDs remain unresolved.

The mapper converts queued to waiting, canceled to cancelled, and running with
positive retry count to retried. JS also handles queued/canceled aliases and
shows unsupported states as unknown. Metadata is bounded to 12 entries, keys to
48 characters and values to 160; structured values become a summary. Render
presentation values through safe DOM textContent.

## 5. Viewer behavior the host must preserve

The frontend instructions give detailed geometry. Integration must retain:

Inspection now docks beside/below the timeline and captures one snapshot while
the bridge continues delivering updates. The component retains only the latest
pending snapshot and exposes Refresh inspection; Follow live, close or overview
also applies it. The host must continue calling `setData` normally. No DTO or
Blazor callback changes are required. Group summaries distinguish current state
from enqueue history; root groups use supplied memberships, while arrows use only
actual dependencies. Selecting a group expands the time axis to separate distinct
timestamps, with vertical scrolling when the full group needs more height. Selecting
a member retains that interval and scrolls to its position. Equal-time collisions
stay in count squares with edges. Group counts are centered without the small
dependency label and turn green only when all members complete. The current view
holds before live advancement would scroll a visible group away; state continues
updating. Follow live explicitly catches up again. Click or zoom resolves grouped
members on the same timeline. Queue summary buttons also open recorded history;
no host filtering or retention change is needed.
Bundled arrows count explicit relationships, including root-to-root ordering;
internal group dependencies remain accessible through inspector navigation.

The existing `SynchronizeJobMonitorAssets` target includes the new inspection
modules through its source glob. Run the normal build/synchronization workflow
and reload the browser to replace loaded ES modules; never edit generated copies.

- A five-second overview ending at the bottom reference, fixed left UTC ruler,
  exact square centers and time-only zoom down to a one-millisecond interval.
- Numbered execution channels and a separate collapsible U strip for null channels.
  U stays visible in both states; Unassigned appears in hover/accessibility text.
  Default extra width is 36 pixels collapsed and 40 expanded.
- Search revealing the selected job's Unassigned strip, including retained enqueue
  history. Its channel/start position and enqueue position share the same job ID.
  Draw a directed enqueue-to-start relation when both endpoints are visible.
- Count markers for resolution collisions, temporary interval inspection and a
  list for selecting equal-time jobs. No timestamp displacement.
- Straight dependency segments and root identity independent of channel placement.
- Per-browser selection/view state, keyboard access, horizontal overflow, and no
  permanent details panel.

Unassigned is a presentation classification, not a runtime channel or lifecycle
state. Legacy/completed jobs missing Started data can remain there. Correct start
times reduce grouping but cannot give every execution a separate point at every zoom.

## 6. Razor and JS lifecycle

Dashboard subscribes to MonitorStore.Changed, builds its initial retained window,
and uses InvokeAsync for refreshes. An interlocked pending flag coalesces updates.
Disposal marks the page disposed and unsubscribes. The singleton store owns runtime
facts; each circuit owns selection and presentation state.

After that initial load, only new accepted observer events request a data update.
Rejected observations do not notify the view. Equal-valued observations do notify
and remain distinct. Coalescing sends a complete retained window for several
notifications, preserving each occurrence still within retention. Subscribing before
bootstrap avoids a snapshot/subscribe gap. Late circuits receive the same history.

JobMonitor.razor renders an ElementReference-backed custom element. OnAfterRenderAsync
imports `./job-monitor/integrations/blazor/job-monitor.js`, creates one
DotNetObjectReference and attaches it. A SemaphoreSlim gate serializes initialization,
snapshot updates and disposal. Revision counters send the latest pending snapshot
without competing update loops. Revisions advance only when the snapshot instance
changes, so selection callbacks and unrelated parent renders do not resend it.
Treat delivered snapshots as immutable and replace the instance for changed data.

The standalone monitor advances live time on `setData`, using the browser clock
minus `liveLagMs`, and has no periodic refresh. Pause/history keeps the reference
while new snapshots update state. Follow live and restoring a live overview catch
up once. Its one-shot redraw scheduler coalesces data, user input and resize
requests; it does not schedule itself again when idle. The demo's optional timer
generates synthetic arrivals and is not a component refresh timer.

The bridge's WeakMap stores one active connection per element. Attach first detaches
a prior listener; update forwards only for attached elements; detach marks the
connection inactive and removes its listener. Job-select calls Razor SelectJob,
then EventCallback<Guid>. The page stores the selection without a details panel.

Async disposal marks the wrapper disposed, waits for the gate, detaches JS, and
disposes the module/callback while tolerating circuit disconnection. Keep the gate
alive for render work already queued on the circuit. Do not invoke JS from observer
threads or add runtime subscriptions to the Web Component. The component separately
cleans up DOM listeners, ResizeObserver and any pending one-shot redraw on disconnection. Navigation
away/back must not duplicate subscriptions.

## 7. Asset synchronization and host sizing

The maintained source is tools/TplQueue.JobMonitor. The host project implements
`SynchronizeJobMonitorAssets` before `ResolveProjectStaticWebAssets;AssignTargetPaths`.
It copies src/**, integrations/blazor/** and LICENSE into the ignored directory
`samples/TplQueue.Sample.BlazorSignalR/wwwroot/job-monitor/`, removes stale generated
files, and includes assets in build/publish output.

Debug additionally copies tests/blazor.html and tests/blazor.js with
CopyToPublishDirectory=Never. There is no manually maintained second asset copy,
Razor Class Library convention, npm build or CDN dependency. Edit shared source,
then rebuild the host to refresh generated files.

The host supplies outer layout and an explicit custom-element height. Internal
theme values remain component CSS variables. Host and viewport ResizeObservers
handle changes after the external shadow stylesheet loads.

ChartJS/ScatterChart, vis-timeline, Bootstrap assets and the old details panel are
retired from the host. The old C# mapper remains only in integration-test
Legacy/ScatterTimeline.cs to preserve tests, not as an alternate production contract.

## 8. Build and verification procedure

Choose the correct dependency mode first. During coordinated preview development,
the sample/ETL path intentionally references sibling source. Building through
WorkspaceTplQueue applies the matching source-reference switch. An older default
Abstractions package lacks IJobExecutionEvent; do not remove the channel contract
to work around a dependency mismatch.

From the sibling WorkspaceTplQueue directory:

```powershell
.\build.ps1 -Configuration Debug
dotnet run --no-build --configuration Debug --project ..\TplQueue.Usage\samples\TplQueue.Sample.BlazorSignalR
```

Open the address printed by ASP.NET Core. The workspace and Usage root build scripts
are distinct entry points. Usage build/test/coverage scripts serve its documented
package-consumption workflow; package versions must be coordinated. When local
packing is needed, use the documented sibling TplQueue.NugetLocal feed.

To reproduce the focused source validation used here, from Usage after restoring
the matching workspace:

```powershell
$workspaceRoot = ((Resolve-Path ..\WorkspaceTplQueue).Path -replace '\\', '/') + '/'
$sourceArgs = @('-p:SolutionFileName=WorkspaceTplQueue.sln', "-p:SolutionDir=$workspaceRoot", '-p:SkipPackLocal=true')
dotnet build samples/TplQueue.Sample.BlazorSignalR/TplQueue.Sample.BlazorSignalR.csproj -c Debug --no-restore @sourceArgs
dotnet test test/integration/Fmacias.TplQueue.Usage.Integration.Test/Fmacias.TplQueue.Usage.Integration.Test.csproj -c Debug --no-restore @sourceArgs
```

No-restore assumes matching restore outputs exist. These commands validate source
composition, not package-only consumption or a coverage gate. For runtime changes,
run the applicable repository build, unit, local pack and integration steps;
record omitted steps and dependency mode explicitly.

From tools/TplQueue.JobMonitor:

```powershell
node scripts/check.mjs
node --test tests/*.test.js
node demo/server.mjs
```

Node.js 20+ serves the demo at http://127.0.0.1:4178/. No install/bundle step is
needed. Open /tests/browser.html for standalone acceptance. On the running Debug
host, open /job-monitor/tests/blazor.html for circuit, channel/selection and
navigation/disposal checks. HTTP prerender success alone is not interactive coverage.

For headless CLI runs append `?automation=1`. Both harnesses use the demo server
on 4178 for a test-only page-load barrier, even when Blazor runs on another port.
Keep that server running and use the real clock; premature DOM dumps can still
say Running. Completion sets `html[data-result="passed"]` and a passed/failed
summary. The barrier times out after 45 seconds. Overriding demo PORT does not
change the harnesses' hardcoded automation port. This is not a production dependency.

| Area | Acceptance to preserve |
| --- | --- |
| Projection/mapper | Legacy/null channels, range before mutation, late Started enrichment, lifecycle non-regression, enqueue/start distinction, channel change clearing start, detached snapshots and presentation-only serialization. |
| JS model/layout | Invalid input, exact centers, compact pitch, grouping without displacement, null lanes, retained enqueue positions and directed assignment edges with unchanged dependency endpoints. |
| Browser | No idle redraw or reference drift, snapshot-driven live advance, paused updates, idle reconnect, zoom/overview restoration, fixed ruler, overflow, U label/tooltip, keyboard focus/expansion, search reveal, selection and disposal. |
| Blazor | Observer snapshots through a real circuit, idle view, no snapshot resend after selection, channel values, retained enqueue strips/connectors after completion, selection bridge and safe navigation away/back. |
| Runtime contract, when changed in its repository | Channel range, concurrent uniqueness, retry stability, terminal release, FIFO zero and independent queues. |

Recorded implementation validation: host build passed with zero warnings/errors;
25 focused projection tests and 111 total Usage integration tests passed; syntax
checks covered 15 JS modules; 19 layout/model tests, 20 standalone browser checks
and 3 Blazor circuit checks passed. The compact-U follow-up reran all 20 standalone
checks successfully. The integration test project had seven existing nullable/
unused-field warnings. Coverage was collected; no baseline-gate result was claimed.
These results are historical and were not newly executed for this documentation
update. Future reports must state exact commands and actual verification.

Event-driven refresh validation (September 22, 2026): the focused source-mode
host build above passed with zero warnings/errors. The source-mode `dotnet test`
command above, with `--collect:"XPlat Code Coverage" --results-directory
artifacts/refresh-validation`, passed all 115 NUnit tests and produced a Cobertura
report; the test project retained seven existing nullable/unused-field warnings.
`node scripts/check.mjs` checked 15 modules and `node --test tests/layout.test.js`
passed 24 tests. Headless Edge passed all 24 standalone checks at
`/tests/browser.html?automation=1` and all five actual circuit checks at
`/job-monitor/tests/blazor.html?automation=1`. The new idle/reconnect browser
regressions reproduced the timer-driven failure before implementation.
No local pack was needed: Usage has no pack-local script and no library/package
contract changed. This validates the documented sibling-source preview path;
package-only build/test scripts and the repository coverage baseline gate were
not run. Collected coverage is not a claim that the baseline gate passed.

## 9. Troubleshooting and limits

| Symptom | What to check |
| --- | --- |
| Completed job remains in U | Was a matching channel-bearing Started event delivered? Terminal metadata alone is insufficient. |
| Two jobs share a count marker | Check start times and pixel resolution. Sequential starts can share a parsed millisecond; inspect the list. |
| U has a waiting count but no visible jobs | W counts current waiting jobs across the snapshot; the tooltip separately reports enqueue history and visible positions. Search navigates to older jobs. |
| Source changes missing in Blazor | Rebuild to synchronize generated assets; do not patch the generated copy. |
| Live mode stops on every update | Remove optional referenceTime from live snapshots. The C# DTO omits it. |
| Empty or wrongly sized viewport | Check explicit height, module/CSS loading and viewport ResizeObserver behavior. |
| Headless output remains Running | Check the 4178 barrier server and wait for data-result. |
| IJobExecutionEvent does not resolve | Use coordinated source references or a compatible package version. |

This is bounded observation history with a separate current-state projection, not
durable replay. `JobMonitor:MaximumEvents` (default 20,000) evicts whole jobs in
first-observed order, even if active. A single oversized job is also evicted;
later observations can introduce it again with only retained facts. This keeps
enqueue/start anchors together for surviving jobs and bounds every monitor index.
Expired counts are visible on the page. There is no fingerprint set. Full windows
are still sent on updates; incremental transport is a future optimization.
Individual nested snapshot graphs are not byte-bounded, and Simulation graph/business
retention remains separate work. Observer
delivery is asynchronous/best-effort; WaitAsync does not wait for observers or
cache acknowledgment. The sample memory cache is not a durable spool. Missing
observations, millisecond resolution, large graphs and connector crossings remain
limits.

The reusable component accepts other producers with the same semantics. Contract
generation, another framework, custom hubs, durable history, touch popovers and
intermediate edge-routing elements require separate decisions. Public product
docs/site publishing remain owned by Adapter's language trees; this runbook does
not publish private Core implementation details or change that boundary.
