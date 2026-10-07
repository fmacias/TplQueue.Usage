# TplQueue.Usage

`TplQueue.Usage` is the public package-consumption repository for the TplQueue preview line.

It exists to show how consumers use the published binaries without requiring access to the restricted `TplQueue.Core` source repository. The repository hosts usage documentation, package-based integration tests adapted from `TplQueue.Core`, and small runnable samples that act as the facility-style validation surface.

## Repository purpose

- provide public-safe usage documentation
- validate the current preview line through packages, not private project references
- demonstrate queue, job, and observer flows from a consumer point of view
- keep the boundary between public binary use and restricted source access explicit

## Repository layout

- [docs/usage/overview.md](usage/overview.md)
- [docs/architecture/source-access-boundary.md](architecture/source-access-boundary.md)
- [docs/development/local-development.md](development/local-development.md)
- [samples/PackageConsumptionSmokeConsole](../samples/PackageConsumptionSmokeConsole/README.md)
- [samples/QueueObserverConsole](../samples/QueueObserverConsole/README.md)
- [samples/QueueObserverSignalRDashboard](../samples/QueueObserverSignalRDashboard/README.md)
- [test/integration/Fmacias.TplQueue.Usage.Integration.Test](../test/integration/Fmacias.TplQueue.Usage.Integration.Test)
- [consumers/README.md](../consumers/README.md)

## Package-consumption model

This repository consumes `Fmacias.TplQueue` and `Fmacias.TplQueue.Core` packages instead of referencing private `TplQueue.Core` source projects.

Sample and integration projects use the shared TplQueuePackageVersion property,
defaulting to `0.2.0-preview.2`. The build, test and coverage scripts accept that
parameter to select another version. See [local development](development/local-development.md).

## Local development

Local development expects the TplQueue product packages to exist in the sibling feed `..\TplQueue.NugetLocal`, with nuget.org also enabled. The local path is relative to this repository's NuGet.config. Before running this repository locally:

1. pack the current preview line from `WorkspaceTplQueue\pack.ps1` or the product-repository `pack-local.ps1` scripts
2. restore and build `TplQueue.Usage`
3. run the package-consumption tests

Commands:

```powershell
.\build.ps1
.\test.ps1
.\coverage.ps1 -EnforceBaseline
```

More detail is in [docs/development/local-development.md](development/local-development.md).

## Public boundary

`TplQueue.Usage` is intentionally public-facing.

- the sample and test projects exercise the binary packages
- the repository does not require private `TplQueue.Core` project references
- restricted source access stays outside this repository and is documented in [docs/architecture/source-access-boundary.md](architecture/source-access-boundary.md)

## License

`TplQueue.Usage` is distributed under the MIT license.

That repository license covers the consumer documentation, samples, and package-consumption test harness published here. It does not change the separate package-license terms of `TplQueue.Core`, which remain governed by the Core repository `LICENSE.txt` and the corresponding NuGet package metadata.

## Current validation surface

- the [Blazor job monitor](../samples/TplQueue.Sample.BlazorSignalR/README.md), with repository-local Domain/Contracts/Simulation modules and two continuous workflows

- adapted integration tests moved from `TplQueue.Core`
- queue creation through the adapter `API` facade
- rooted job-graph execution through public packages
- a small release-smoke console sample with one simple mode per package-consumption checklist scenario
- a runnable console sample with documented `wait` and `cancel` modes that acts as the facility-style consumer example surface
- a runnable SignalR dashboard sample that registers two long-lived queues through `Fmacias.TplQueue.Microsoft.DependencyInjection`, including a payload queue that projects detached JSON snapshots for `IDataJob` events

## Build and test workflow

This repository currently uses local PowerShell workflows:

- [build.ps1](../build.ps1) restores and builds the sample and test projects
- [test.ps1](../test.ps1) restores and runs the package-consumption test projects
- [coverage.ps1](../coverage.ps1) collects deterministic coverage for the package-consumption harness and can enforce the accepted baseline

Those workflows are designed to work against the local preview feed during development and against published packages later. Coverage artifacts are written under `artifacts/coverage/`, including `artifacts/coverage/html/index.html` when the standard `ReportGenerator` tool is available, while the release-facing baseline record is maintained in `..\WorkspaceTplQueue\docs\test-coverage.md`.
