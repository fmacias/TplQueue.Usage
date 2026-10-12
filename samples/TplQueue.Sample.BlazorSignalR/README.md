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
UC03 adds a third continuous FIFO timer as described below. New workflows
are always included alongside existing workflows without configuration selectors.

The host attaches observers before starting delivery. **Stop arrivals** closes
admission across browser tabs and waits for pending submissions while accepted
jobs finish. Host shutdown cancels the token used by jobs. Refreshing the page
does not restart delivery; restart the server for another session. Pause freezes
the view only. History, graph membership and business data remain in memory for
the process lifetime; bounded retention is still future work.

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

After the initial snapshot, new accepted `IJobEvent` observations trigger projection
notifications and coalesced updates through the Blazor circuit. Duplicate events
do not trigger another update. The standalone monitor has no refresh interval:
live time advances on snapshot arrival and stays still when idle. Pause/history
keeps the chosen reference while incoming snapshots update job state. Selection,
zoom and resizing still redraw locally; selection callbacks do not resend the
unchanged snapshot. DTOs represent accumulated job state derived from events.

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
It continuously submits fresh independent Domain ingest roots to the existing FIFO
instance after a one-second startup offset, with 100 ms between arrival attempts.
The existing ingest handler takes 500 ms. There is no total root limit or active-root
admission cap, so FIFO accumulates observable waiting work and executes on channel zero.
Failed submissions are recorded; later ticks create fresh roots. **Stop arrivals**
ends delivery without cancelling accepted jobs; keep the host running to let them finish.
ETL and SingleJob continue submitting to all three queues. Their FIFO submissions
can interleave with UC03, whose roots keep their relative submission order.
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
