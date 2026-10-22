# TplQueue.Usage

`TplQueue.Usage` is the public package-consumption repository for the TplQueue preview line.

It is the canonical consumer-facing sample and verification surface for the published packages. The repository keeps runnable examples, package-based integration tests, and public-safe documentation outside the restricted `TplQueue.Core` source boundary.

## What this repository owns

- public package-consumption documentation
- runnable consumer samples
- package-based integration tests adapted from the private source layout
- public observer, DTO transport, and payload-projection examples

## Documentation map

Repository-level documentation now lives under [docs/](docs/index.md):

- [Usage](docs/usage/index.md)
- [Architecture](docs/architecture/index.md)
- [Development](docs/development/index.md)
- [Operations](docs/operations/index.md)
- [Full reference](docs/reference.md)

Private `TplQueue.Core` documentation now lives in the private `TplQueue.Core` repository and is no longer maintained in this public repository.

This repository focuses on runnable package-consumption samples, public integration coverage, and validation flows for the published packages.

`fmacias.github.io` syncs the public TplQueue documentation from `TplQueue.Adapter/docs/<lang>/`.

## Runnable samples

- [PackageConsumptionSmokeConsole](samples/PackageConsumptionSmokeConsole/README.md)
- [QueueObserverConsole](samples/QueueObserverConsole/README.md)
- [QueueObserverSignalRDashboard](samples/QueueObserverSignalRDashboard/README.md)
- [TplQueue Blazor observable ETL sample](samples/TplQueue.Sample.BlazorSignalR/README.md)
- [Blazor job monitor architecture and execution channels](docs/architecture/blazor-consumer-sample.md)
- [Simulation use-case catalog and specifications](docs/development/simulation-use-case-plan.md#use-case-catalog)

These samples are the canonical runnable examples cited by the product-repository docs.

### BlazorSignalR sample

The [component diagram](docs/architecture/blazor-sample-components.puml) shows the
four sample projects, direct package references and runtime integration.
The [development index](docs/development/index.md) leads to implementation-oriented
use-case specifications derived from the current source.

The Blazor sample uses the reusable `job-queue-timeline` Web Component with real runtime execution channels, vertical time, and a full-area viewer without a permanent details panel. See the maintained architecture guide for contracts and validation.
The component's five-second overview uses exact square position markers, a left UTC ruler and compact channels. Dense jobs use count squares, white until every member completes and then green. Clicking a square or zooming expands time enough to separate distinct timestamps, with vertical scrolling for large groups and a docked inspector for individual selection. The view holds before a visible group would scroll away; incoming snapshots still update its state. Follow live explicitly catches up again. Inspection holds a captured snapshot until Refresh or Follow live.
The Simulation module runs ETL, independent single-job and UC03 FIFO scenarios
together. ETL and SingleJob start after one second and repeat every three seconds. **Stop
arrivals** closes admission across all browser tabs while accepted jobs finish.
The host starts delivery; opening a browser never starts or restarts it.
All three workflows share `ISimulationWorkflow`, with a `System.Threading.Timer`
owned by each workflow instance. Simulation folders separate workflows,
measurements, session lifecycle, execution and graph tracking. Domain owns job
factories, payloads, handlers and queue/cache wrappers under `samples/TplQueue.Sample.Domain`.
See [workflow structure](docs/architecture/blazor-consumer-sample.md#workflow-structure).
The host always registers every implemented workflow together, without a selector.
UC03 continuously creates a fresh independent Domain ingest root for each of FIFO,
ParallelQ and CacheQ through `IEtlQueueRuntime.EnqueueFifo`,
starting after one second at 100 ms intervals. Each queue receives its own job ID.
The existing 500 ms handler makes
waiting work visible. All workflows enqueue fresh jobs until Stop arrivals or host
cancellation, without sample active-root caps or lifetime root limits. FIFO retains
serial execution; queued jobs remain observable while waiting. The backlog grows
during delivery and drains after Stop arrivals while the host remains running.
The former general-purpose finite host profiles remain unavailable.
See [continuous delivery and controls](docs/architecture/blazor-consumer-sample.md#continuous-combined-demo)
and the [sample launch instructions](samples/TplQueue.Sample.BlazorSignalR/README.md).
Graph membership is captured before enqueue, so running, failed and cancelled jobs
retain their root identity. Shared jobs keep all root memberships and one job ID;
see [graph identity](docs/architecture/blazor-consumer-sample.md#simulation-graph-identity).
Assigned positions use backend Started timestamps. The collapsible Unassigned strip retains recorded enqueue markers after assignment, with directed connectors to Started positions when both are visible. Both markers select the same job.
After its initial snapshot, the Blazor monitor refreshes data when new observer events change the projection. Live time advances on snapshot arrival; the idle view has no periodic refresh. User interactions and resizing still redraw locally.

The observer captures each callback as an immutable `JobEventSnapshot`. The host
retains these objects directly and sends a bounded observation window with queue
capacity and graph memberships. `src/event-stream.js` derives the renderer's current
positions; there is no `JobMonitorJob` or ETL DTO-to-DTO mapping chain.
`JobMonitor:MaximumEvents` defaults to 20,000. Retention evicts whole jobs in
first-observed order, including active or oversized jobs, and reports expired
observations in the dashboard. Equal-valued occurrences remain distinct.

`TplQueue.Sample.Etl.Contracts` supplies shared sample interfaces and DTOs beyond
ETL, including workflow/session, runtime and graph membership contracts.
`TplQueue.Sample.Contracts` is a proposed future name; no rename is applied or
required for these use cases. See [contract responsibility](docs/architecture/blazor-consumer-sample.md#shared-contract-responsibility).

#### Roadmap

Future sample implementation stays inside `TplQueue.Usage`. Do not modify
`TplQueue.Abstractions`, `TplQueue.Adapter` or `TplQueue.Core` for a use case.
If supported public APIs are insufficient, add a concrete entry here with the
proposed enhancement, motivation, owning product repository, affected API/behavior
and blocked UC ID. Continue supported variants where possible; do not substitute
an undocumented workaround that changes product semantics.

No product enhancement is confirmed as required by this source inspection. The
following questions are retained explicitly; they are not claims of current
package defects or blanket blockers for the use-case catalog.

| Question / potential enhancement if confirmed | Motivation and affected public boundary | Affected use cases / blocking status |
| --- | --- | --- |
| Clarify supported shared-root binding and cross-queue ownership; request additive support only if the intended topology is unavailable | Public job/graph factories, dependency composition and enqueue ownership must support a shared instance without cloning work or assigning ownership in the UI. Contract ownership is Abstractions/Adapter; runtime behavior is Core. | UC10–UC14, UC18; pending contract-dependent scenarios, no confirmed blocked UC. |
| Clarify CacheQ sharing across hydration and acknowledgment observation | Public `ICacheQ`, cache/serialization contracts and registered handlers must preserve the identities and completion boundary required by a scenario. Any missing supported capability would need an explicit product proposal. | UC05, UC11, UC16–UC18, UC23; no confirmed blocked UC. |
| Investigate the historical capacity-one Cache stall before claiming that variant | The old UC01 record observed a stall at capacity one and success at capacity two. Any confirmed defect would concern CacheQ/underlying dispatcher capacity progress in Core, not a sample-side semaphore workaround. Historical observations do not establish current package behavior. | UC01 capacity-one variant and UC05; current use cases are not marked Blocked on historical evidence. |

Usage-owned work remains in the specifications: varied schedules and burst
accounting (UC09/UC19), cancellation contract exposure (UC17), explicit lifecycle
extensions (UC23), and bounded projection/graph/business-data retention (UC25).
Continuous operation and bounded monitor observation retention are implemented.
Coordinated graph/business-data retention and byte limits on individual snapshots
remain future work.

## Public package consumption

Samples and integration tests use TplQueue `0.2.0-preview.2` by default. The
Blazor host uses the Core and DI packages, Domain uses MemCache, and Contracts uses
Abstractions. Simulation references Contracts; all sample source projects live here.

Restore uses [NuGet.config](NuGet.config), which enables nuget.org and a local
preview feed at `..\TplQueue.NugetLocal`, relative to this repository. Pushing source commits
does not itself publish packages. See [local development](docs/development/local-development.md)
for package availability and repeated local rebuilds.

## Quick operations

Build the sample and test surface:

```powershell
.\build.ps1
```

Run the package-consumption tests:

```powershell
.\test.ps1
```

Run the coverage gate:

```powershell
.\coverage.ps1
.\coverage.ps1 -EnforceBaseline
```

`TplQueuePackageVersion` in [Directory.Build.props](Directory.Build.props) controls
the shared package version. All TplQueue PackageReference entries use this property.
To validate another available package build without changing the default:

```powershell
.\build.ps1 -TplQueuePackageVersion <version>
.\test.ps1 -TplQueuePackageVersion <version>
```

## Public boundary

Domain, Simulation and Contracts are repository-local sample projects. A standalone
Usage build consumes product packages and does not require private Core source or
the temporary WorkspaceTplQueue Domain checkout used before this migration.

- `TplQueue.Adapter/docs/<lang>/` remains the public product-documentation and site-sync source.
- WorkspaceTplQueue can still switch selected loaded Usage projects to sibling source
  references for maintainer development; validate packages through the standalone Usage scripts.
- The local preview feed is a maintainer convenience. Published packages can be restored
  from nuget.org when the requested version is available there.
- Restricted source access remains outside this repository; see the
  [source-access boundary](docs/architecture/source-access-boundary.md).

## License

`TplQueue.Usage` is distributed under the MIT license.
