# TplQueue Blazor ETL execution dashboard

This .NET 8 Blazor Interactive Server sample is a passive operational
dashboard for the reusable `TplQueue.Sample.Etl` backend module. Backend-hosted
code starts a deterministic demonstration; the browser only reads the
materialized execution state and renders it with one `vis-timeline`.

The ETL module remains host-independent and targets `netstandard2.0`.

## Architecture

```text
SampleEtlDemoHostedService
  -> existing IEtlWorkflow
  -> predefined Ingest -> Transform -> Load IDataJobRoot graph
  -> ParallelQ / FifoQ / CacheQ
  -> real IObservable<IJobEvent> streams
  -> singleton EtlExecutionProjectionStore
  -> Blazor Interactive Server circuit
  -> vis-timeline
```

The hosted service installs all queue subscriptions before it submits any
work. One singleton `EtlQueueObserver` copies immutable metadata from each
`IJobEvent` into a thread-safe, application-wide projection. Every Blazor
circuit reads immutable snapshots from that projection and marshals change
notifications through its own component synchronization context.

The page does not construct queues, payloads, handlers, retry policies, or job
graphs. It does not start or cancel the demonstration.

## Demonstration workload

After the web host starts, the backend waits approximately one second and
submits six instances of the same predefined ETL workflow:

- two roots to `ParallelQ`, submitted together so independent executions can
  overlap;
- two roots to `FifoQ`, submitted together to show strict ordered execution;
- two roots to the real `CacheQ`.

Each root contains the existing `Ingest measurements`, `Transform
measurements`, and `Load measurement summary` payload jobs. The handlers keep
their real sample behavior and include short deterministic asynchronous delays
so execution order is visible.

`CacheQ` uses an in-memory `IMemCache`. It dehydrates reconstructable payload
jobs, hydrates them through the restricted `EtlPayloadTypeResolver`, resolves
the three registered payload handlers by their stable keys, and dispatches the
hydrated graph through its dedicated inner queue. Timeline entries for the
CacheQ group come from that real queue's lifecycle events.

The cache and dashboard projection are process-local memory only. They do not
provide cross-process durability. A browser refresh or a second circuit sees
the current snapshot while the application process remains alive.

The sample also uses ASP.NET Core's ephemeral data-protection provider so it
can run without a writable user-profile key store. Protected Blazor and
antiforgery tokens become invalid when the process restarts; this setting is
appropriate for the local demonstration, not as an authentication or
persistent-cookie production template.

## Dashboard

The dashboard shows:

- exactly one group for each of `ParallelQ`, `FifoQ`, and `CacheQ`;
- compact total, running, completed, and failed queue summaries;
- queued, running, and terminal job ranges as real lifecycle events arrive;
- selected-job identifiers, timing, retry count, and safe error summary;
- a manual **Fit timeline** control that changes only the visualization.

TplQueue observer delivery is asynchronous and best-effort, so the projection
is idempotent and prevents older events from regressing terminal state.

No RabbitMQ, MassTransit, external broker, custom SignalR hub, database,
authentication layer, or JavaScript build pipeline is used. UI refreshes use
the existing Blazor Interactive Server circuit.

## vis-timeline

The standalone UMD build of `vis-timeline` **8.5.2** is vendored under:

```text
wwwroot/lib/vis-timeline/8.5.2/
```

The upstream dual-license notice and complete MIT and Apache-2.0 texts are
stored in that same directory. The application does not depend on a CDN at
runtime.

## Run

From this directory:

```powershell
dotnet restore
dotnet build
dotnet run --project .\TplQueue.Sample.BlazorSignalR.csproj
```

Open the URL printed by ASP.NET Core. The dashboard is available at `/`.

During the current package-version advancement workflow, this sample uses
source project references to the sibling Adapter DI and Core repositories.
