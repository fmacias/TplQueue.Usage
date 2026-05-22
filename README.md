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

## Runnable samples

- [PackageConsumptionSmokeConsole](samples/PackageConsumptionSmokeConsole/README.md)
- [QueueObserverConsole](samples/QueueObserverConsole/README.md)
- [QueueObserverSignalRDashboard](samples/QueueObserverSignalRDashboard/README.md)

These samples are the canonical runnable examples cited by the product-repository docs.

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
- the public consumption path is `nuget.org`; `..\TplQueue.NugetLocal` is only for local preview and maintainer workflows
- restricted source access remains outside this repository and is documented in [docs/architecture/source-access-boundary.md](docs/architecture/source-access-boundary.md)

## License

`TplQueue.Usage` is distributed under the MIT license.
