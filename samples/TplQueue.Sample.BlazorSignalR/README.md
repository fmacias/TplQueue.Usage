# TplQueue Blazor ETL job monitor

The .NET 8 Interactive Server host consumes TplQueue `0.2.0-preview.2` packages
and the repository-local Domain, Contracts and Simulation projects. It no longer
requires sibling product source or the temporary WorkspaceTplQueue Domain project.

The host always registers ETL, SingleJob and MultipleFifoRoots together. ETL and
SingleJob submit to FIFO, Parallel and Cache. Their timers start after one second and tick every
three seconds. Each full pair of ticks adds six roots and twelve jobs. There are
no sample active-root caps: each arrival creates fresh jobs even while earlier
roots wait. Overlapping submission callbacks are still skipped. The former
Simulation:Profile and timing configuration keys are not used by this host.
UC03 adds a third timer that submits with FIFO ordering to all three queues. New workflows
are always included alongside existing workflows without configuration selectors.

The host attaches observers before starting delivery. **Stop arrivals** closes
admission across browser tabs and waits for pending submissions while accepted
jobs finish. Host shutdown cancels the token used by jobs. Refreshing the page
does not restart delivery; restart the server for another session. Pause freezes
the view only. Monitor observations have configurable bounded retention; graph
membership and business data still remain in memory for the process lifetime.

See the [delivery contract](../../docs/architecture/blazor-consumer-sample.md#continuous-combined-demo).

The full-area `<job-queue-timeline>` Web Component displays actual logical
execution channels, vertical time, explicit dependency edges, search, graph focus,
zoom and history navigation. Waiting jobs without a runtime channel are explicitly
unassigned in a collapsible strip. Assigned positions use channel-bearing Started
event timestamps. Recorded enqueue markers remain in Unassigned after assignment,
with directed connectors to their Started positions when both are visible. Both
positions select the same job; enqueue time also remains in metadata. Jobs missing that start
event remain Unassigned until it arrives. Hover shows bounded metadata; there is
no permanent details panel.

Root membership is available from the first observation, including running,
failed and cancelled graphs. Shared prerequisites retain one job ID and all root
memberships. Selection emphasizes the connected dependency graph; root identity
never supplies a channel or changes observed lifecycle facts.

`JobMonitorObserver` captures each `IJobEvent` synchronously through the Adapter
factory and forwards only `JobEventSnapshot` to `JobMonitorStore`. The store retains
each occurrence, including repeated statuses or identical metadata. After the
initial retained window, observations trigger coalesced updates through the Blazor
circuit. The standalone monitor has no refresh interval:
live time advances on snapshot arrival and stays still when idle. Pause/history
keeps the chosen reference while incoming snapshots update job state. Selection,
zoom and resizing still redraw locally; selection callbacks do not resend the
unchanged snapshot. The bridge passes the original snapshots to `event-stream.js`,
which derives current positions separately from event history. Queue capacity and
composed memberships are supplementary metadata; no runtime objects are retained.

Configure `JobMonitor:MaximumEvents` in `appsettings.json` (default 20,000).
When exceeded, the store removes entire jobs in first-observed order, including
active jobs if necessary. An individual job exceeding the limit is also evicted;
a later occurrence can introduce it again with only then-retained facts. The page
reports expired observations. Surviving histories keep their original enqueue/start
events, and late browsers receive the same retained window. This bounds event
count, not the size of nested metadata or the Simulation catalog/business stores.
Updates currently replace the bounded window rather than using a delta protocol.

Expand **U** to inspect enqueue history. Searching for a job also expands its strip
and navigates to its execution time. Search or history navigation reveals older
recorded markers, including work accepted before the browser connected.

See the maintained [architecture and contract guide](../../docs/architecture/blazor-consumer-sample.md)
and [standalone component guide](../../tools/TplQueue.JobMonitor/README.md).

From the TplQueue.Usage repository root:

```powershell
.\build.ps1 -Configuration Debug
dotnet run --no-build --project .\samples\TplQueue.Sample.BlazorSignalR
```

**UC03: multiple FIFO roots** runs automatically alongside ETL and SingleJob.
It continuously submits fresh independent Domain ingest roots through
`IEtlQueueRuntime.EnqueueFifo` to FIFO, ParallelQ and CacheQ after a one-second
startup offset, with 100 ms between arrival attempts. Each tick creates one root
with its own ID for each queue.
The existing ingest handler takes 500 ms. There is no total root limit or active-root
admission cap, so waiting work accumulates. FIFO executes on channel zero;
ParallelQ and CacheQ show their actual backend execution channels.
Failed submissions are recorded; later ticks create fresh roots. **Stop arrivals**
ends delivery without cancelling accepted jobs; keep the host running to let them finish.
ETL and SingleJob continue using normal enqueue on all three queues. Their submissions
can interleave with UC03; FIFO ordering is local to each queue, not global across queues.
The dashboard keeps reporting arrivals active until **Stop arrivals** stops the
whole session. Backlog and drain time grow with the duration of this workload.

Expand **U** to inspect the backlog and retained enqueue markers; each root keeps
its own composed membership even when FIFO ordering edges connect roots. Observer
callback arrival order is not handler execution order. The scenario starts with
the host, so a later browser connection sees existing history and ongoing arrivals.
After Stop arrivals, restart the server to begin a fresh session.

The configured feed must contain the pinned package version. See
[local development](../../docs/development/local-development.md) for feed setup.

Open `/` at the address printed by ASP.NET Core. The Debug build additionally
provides `/job-monitor/tests/blazor.html` for interactive circuit acceptance.
The test harness is excluded from publishing. No application API or custom
SignalR hub is added.

The build synchronizes shared JavaScript/CSS from `tools/TplQueue.JobMonitor`
into the ignored `wwwroot/job-monitor` directory before static asset discovery;
there is no Node build or duplicated maintained renderer.

The Debug browser harness is intended for the continuous combined workload. Open
`/job-monitor/tests/blazor.html?profile=combined`: it stops arrivals and checks
drain, selection and multiple circuits. Its drain timeout scales with the observed
job count for the continuous FIFO backlog. The query selects harness expectations,
not a host profile. Historical finite `etl`/`single-job` host launch instructions
no longer apply. HTTP prerender tests do not replace interactive circuit checks.
