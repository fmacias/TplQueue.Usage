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
The Simulation module owns finite ETL delivery: two timer ticks per queue,
one root per tick, a one-second startup offset and a three-second interval.
The host attaches observers before starting the simulation; browser connections
do not start or restart it. See [delivery settings and lifecycle](docs/architecture/blazor-consumer-sample.md#finite-scenario-delivery).
An opt-in `single-job` profile submits one independent ingest root per queue per
tick, six one-job roots in total. See the [UC01 profile and launch command](samples/TplQueue.Sample.BlazorSignalR/README.md#single-job-profile-uc01).
Graph membership is captured before enqueue, so running, failed and cancelled jobs
retain their root identity. Shared jobs keep all root memberships and one job ID;
see [graph identity](docs/architecture/blazor-consumer-sample.md#simulation-graph-identity).
Assigned positions use backend Started timestamps. The collapsible Unassigned strip retains recorded enqueue markers after assignment, with directed connectors to Started positions when both are visible. Both markers select the same job.
After its initial snapshot, the Blazor monitor refreshes data when new observer events change the projection. Live time advances on snapshot arrival; the idle view has no periodic refresh. User interactions and resizing still redraw locally.

## Public package consumption

The published preview line is consumable directly from `nuget.org`. Public documentation and sample guidance should assume the normal package-install path first, for example:

```powershell
dotnet add package Fmacias.TplQueue --version 0.1.0-preview.1
dotnet add package Fmacias.TplQueue.Core --version 0.1.0-preview.1
```

The sibling `..\TplQueue.NugetLocal` feed is only a maintainer and local-preview convenience for workspace development.

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

The default local preview line is controlled by `TplQueuePackageVersion` in [Directory.Build.props](Directory.Build.props). Override it at command time when needed:

```powershell
.\build.ps1 -TplQueuePackageVersion <version>
.\test.ps1 -TplQueuePackageVersion <version>
```

## Public boundary

`TplQueue.Usage` is intentionally public-facing.

- the projects consume published packages instead of private `TplQueue.Core` source projects
- temporary exception: while `TPLQ-V1-015A` advances the package version, the
  `TplQueue.Sample.BlazorSignalR` host and its `TplQueue.Sample.Simulation` module use source project
  references to the sibling Adapter and Core repositories; this is a deliberate
  upgrade workflow, not the published consumer model
- when loaded through `WorkspaceTplQueue.sln`, workspace targets also switch the
  loaded Usage integration and console validation projects to sibling source
  references; other standalone Usage projects remain package-based
- the public consumption path is `nuget.org`; `..\TplQueue.NugetLocal` is only for local preview and maintainer workflows
- restricted source access remains outside this repository and is documented in [docs/architecture/source-access-boundary.md](docs/architecture/source-access-boundary.md)

## License

`TplQueue.Usage` is distributed under the MIT license.
