# QueueObserverSignalRDashboard

`QueueObserverSignalRDashboard` is the browser-oriented consumer sample for the SignalR observer scenario discussed in `TPLQ-V1-008`.

It demonstrates:

- one ASP.NET Core host that consumes the public `TplQueue` packages
- one `Fmacias.TplQueue.Microsoft.DependencyInjection` registration path
- one regular `appsettings.json` section that defines retry policies and dispatcher options
- two singleton `IParallelQ` instances created once for the host lifetime and injected into the service layer
- one SignalR hub that pushes simple DTOs to the browser
- one observer-fed transport layer that projects `IJobEvent` into browser-friendly DTOs
- one metadata queue with a rooted job graph plus one standalone helper task in the same dispatcher
- one payload queue with `IDataJob` / `IDataJobRoot` workflow steps for `Extract -> Transform -> Load`
- deterministic `wait` and `cancel` runs started from the UI or from HTTP

## What the browser receives

The browser does not receive raw `IJobEvent` objects or live payload references.

It receives simple DTO objects projected by the sample:

- `QueueRunDto`
- `QueueEventDto`

This keeps the sample aligned with the current payload-ownership model:

- queue event publications are metadata-first
- browser transport is an explicit projection concern
- live payload objects are not forwarded implicitly through observer events
- the payload queue uses an explicit consumer-side projection that captures detached JSON snapshots from live `IDataJobNode` instances on terminal events

## Queue composition

The sample binds the `TplQueue` section from `appsettings.json`, converts those descriptors into `RetryPolicyOptions` and `QOptions`, and registers them through `AddTplQueue(...)`.

After registration, the host creates one named queue for metadata runs and one named queue for payload runs. `DashboardRunCoordinator` receives those queues, `IJobFactory`, `IDataJobFactory`-backed workflow helpers, and the retry-policy infrastructure by dependency injection instead of constructing queues for every run.

## Run the sample

```powershell
dotnet run --project samples/QueueObserverSignalRDashboard/QueueObserverSignalRDashboard.csproj
```

Then open the printed local URL in a browser and start either:

- `Metadata wait run`
- `Metadata cancel run`
- `Payload wait run`
- `Payload cancel run`

The page renders the queued DTO stream as the observer receives events. Clicking a payload event row opens the detached serialized payload snapshot in the side inspector.

## HTTP endpoints

The sample also exposes a tiny HTTP surface for diagnostics and automated verification:

- `POST /api/sample/runs/wait`
- `POST /api/sample/runs/cancel`
- `POST /api/sample/payload-runs/wait`
- `POST /api/sample/payload-runs/cancel`
- `GET /api/sample/runs`
- `GET /api/sample/runs/{runId}`
- `GET /api/sample/runs/{runId}/events`
- `POST /hubs/queue-events/negotiate?negotiateVersion=1`

## Notes

- The browser page loads the SignalR JavaScript client from a CDN. The sample host still runs locally, but the page needs internet access to fetch that script.
- The integration tests validate the HTTP surface and hub availability without depending on a real browser.
