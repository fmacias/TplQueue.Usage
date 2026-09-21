# TplQueue Blazor ETL job monitor

Passive .NET 8 Interactive Server dashboard for `TplQueue.Sample.Etl`. The
backend starts two three-job roots on each queue independently of browsers.

The full-area `<job-queue-timeline>` Web Component displays actual logical
execution channels, vertical time, explicit dependency edges, search, graph focus,
zoom and history navigation. Waiting jobs without a runtime channel are explicitly
unassigned. Hover shows bounded metadata; there is no permanent details panel.

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
