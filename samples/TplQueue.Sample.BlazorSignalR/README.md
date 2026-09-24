# TplQueue Blazor ETL job monitor

Passive .NET 8 Interactive Server dashboard for `TplQueue.Sample.Simulation`. The
backend uses finite timers owned by the Simulation module: one three-job root
per queue after one second, then one more three seconds later. This produces six
roots and eighteen jobs independently of browsers. Each scenario admits at most
two active roots; busy or capacity-limited ticks are counted and skipped.
The host attaches observers before starting delivery and closes admission during
shutdown, waiting for pending submissions. Its shutdown token cancels jobs.
See the [finite delivery contract](../../docs/architecture/blazor-consumer-sample.md#finite-scenario-delivery)
for configurable settings, completion semantics and failure counters.

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
and navigates to its execution time. The finite workload may finish before the
browser opens; search or history navigation reveals its recorded markers.

See the maintained [architecture and contract guide](../../docs/architecture/blazor-consumer-sample.md)
and [standalone component guide](../../tools/TplQueue.JobMonitor/README.md).

During coordinated source development, run from `WorkspaceTplQueue`:

```powershell
.\build.ps1 -Configuration Debug
dotnet run --no-build --project ..\TplQueue.Usage\samples\TplQueue.Sample.BlazorSignalR
```

Open `/` at the address printed by ASP.NET Core. The Debug build additionally
provides `/job-monitor/tests/blazor.html` for interactive circuit acceptance.
The test harness is excluded from publishing. No application API or custom
SignalR hub is added.

The build synchronizes shared JavaScript/CSS from `tools/TplQueue.JobMonitor`
into the ignored `wwwroot/job-monitor` directory before static asset discovery;
there is no Node build or duplicated maintained renderer.
The sample retains sibling source references during coordinated preview work.
Build it through `WorkspaceTplQueue.sln` for the full source reference switch.
Package-only validation is a separate maintained workflow. A standalone rebuild
must select a coordinated package version containing `IJobExecutionEvent`;
the older default preview package does not contain that new optional contract.
