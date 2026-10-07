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
- [Simulation implementation plan and individual tasks](docs/development/simulation-use-case-plan.md#task-checklist)

These samples are the canonical runnable examples cited by the product-repository docs.

The Blazor sample uses the reusable `job-queue-timeline` Web Component with real runtime execution channels, vertical time, and a full-area viewer without a permanent details panel. See the maintained architecture guide for contracts and validation.
The component's five-second overview uses exact square position markers, a left UTC ruler and compact channels. Dense jobs use count markers with temporary closer inspection in the same viewer.
The Simulation module runs ETL and independent single-job scenarios together by
default, starting after one second and repeating every three seconds. **Stop
arrivals** closes admission across all browser tabs while accepted jobs finish.
The host starts delivery; opening a browser never starts or restarts it.
ETL and SingleJob share `ISimulationWorkflow`, with a `System.Threading.Timer`
owned by each workflow instance. Simulation folders separate workflows,
measurements, session lifecycle, execution and graph tracking. Domain owns job
factories, payloads, handlers and queue/cache wrappers under `samples/TplQueue.Sample.Domain`.
See [workflow structure](docs/architecture/blazor-consumer-sample.md#workflow-structure).
The host registers both workflows directly; timing is fixed and finite host profiles
are no longer available.
See [continuous delivery and controls](docs/architecture/blazor-consumer-sample.md#continuous-combined-demo)
and the [sample launch instructions](samples/TplQueue.Sample.BlazorSignalR/README.md).
Graph membership is captured before enqueue, so running, failed and cancelled jobs
retain their root identity. Shared jobs keep all root memberships and one job ID;
see [graph identity](docs/architecture/blazor-consumer-sample.md#simulation-graph-identity).
Assigned positions use backend Started timestamps. The collapsible Unassigned strip retains recorded enqueue markers after assignment, with directed connectors to Started positions when both are visible. Both markers select the same job.
After its initial snapshot, the Blazor monitor refreshes data when new observer events change the projection. Live time advances on snapshot arrival; the idle view has no periodic refresh. User interactions and resizing still redraw locally.

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
