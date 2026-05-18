# TplQueue.Usage

`TplQueue.Usage` is the public package-consumption repository for the TplQueue preview line.

It exists to show how consumers use the published binaries without requiring access to the restricted `TplQueue.Core` source repository. The repository hosts usage documentation, package-based integration tests adapted from `TplQueue.Core`, and small runnable samples that act as the facility-style validation surface.

## Repository purpose

- provide public-safe usage documentation
- validate the current preview line through packages, not private project references
- demonstrate queue, job, and observer flows from a consumer point of view
- keep the boundary between public binary use and restricted source access explicit

## Repository layout

- [docs/overview.md](docs/overview.md)
- [docs/source-access-boundary.md](docs/source-access-boundary.md)
- [docs/local-development.md](docs/local-development.md)
- [samples/QueueObserverConsole](samples/QueueObserverConsole/README.md)
- [samples/QueueObserverSignalRDashboard](samples/QueueObserverSignalRDashboard/README.md)
- [test/integration/Fmacias.TplQueue.Usage.Integration.Test](test/integration/Fmacias.TplQueue.Usage.Integration.Test)
- [consumers/README.md](consumers/README.md)

## Package-consumption model

This repository consumes `Fmacias.TplQueue` and `Fmacias.TplQueue.Core` packages instead of referencing private `TplQueue.Core` source projects.

The default local preview line is controlled by `TplQueuePackageVersion` in [Directory.Build.props](Directory.Build.props). The current local baseline is `0.1.0-preview.1`.

To validate a different package line without editing the repo, override the property at build or test time:

```powershell
.\build.ps1 -TplQueuePackageVersion 0.1.0-preview.2
.\test.ps1 -TplQueuePackageVersion 0.1.0-preview.2
```

## Local development

Local development expects the TplQueue product packages to exist in the sibling feed `..\TplQueue.NugetLocal`, with `nuget.org` retained as the secondary source. Before running this repository locally:

1. pack the current preview line from `WorkspaceTplQueue\pack.ps1` or the product-repository `pack-local.ps1` scripts
2. restore and build `TplQueue.Usage`
3. run the package-consumption tests

Commands:

```powershell
.\build.ps1
.\test.ps1
.\coverage.ps1 -EnforceBaseline
```

More detail is in [docs/local-development.md](docs/local-development.md).

## Public boundary

`TplQueue.Usage` is intentionally public-facing.

- the sample and test projects exercise the binary packages
- the repository does not require private `TplQueue.Core` project references
- restricted source access stays outside this repository and is documented in [docs/source-access-boundary.md](docs/source-access-boundary.md)

## Current validation surface

- adapted integration tests moved from `TplQueue.Core`
- queue creation through the adapter `API` facade
- rooted job-graph execution through public packages
- a runnable console sample with documented `wait` and `cancel` modes that acts as the facility-style consumer example surface
- a runnable SignalR dashboard sample that registers two long-lived queues through `Fmacias.TplQueue.Microsoft.DependencyInjection`, including a payload queue that projects detached JSON snapshots for `IDataJob` events

## Build and test workflow

This repository currently uses local PowerShell workflows:

- [build.ps1](build.ps1) restores and builds the sample and test projects
- [test.ps1](test.ps1) restores and runs the package-consumption test projects
- [coverage.ps1](coverage.ps1) collects deterministic coverage for the package-consumption harness and can enforce the accepted baseline

Those workflows are designed to work against the local preview feed during development and against published packages later. Coverage artifacts are written under `artifacts/coverage/`, including `artifacts/coverage/html/index.html` when the standard `ReportGenerator` tool is available, while the release-facing baseline record is maintained in `..\WorkspaceTplQueue\docs\test-coverage.md`.
