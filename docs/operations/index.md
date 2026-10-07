# Operations

This section groups repository operations for `TplQueue.Usage`.

## Build and test scripts

- [build.ps1](../../build.ps1)
- [test.ps1](../../test.ps1)
- [coverage.ps1](../../coverage.ps1)

## Local feed and release-smoke role

`TplQueue.Usage` defaults to `0.2.0-preview.2` through the shared TplQueuePackageVersion property.
Restore uses the sources configured in NuGet.config:

- LocalPackages: `..\TplQueue.NugetLocal`, relative to the repository's config file
- `nuget.org`

The repository also owns the simple release-smoke consumer application:

- [PackageConsumptionSmokeConsole](../../samples/PackageConsumptionSmokeConsole/README.md)

Coverage artifacts are written under `artifacts/coverage/`, including `artifacts/coverage/html/index.html` when the standard `ReportGenerator` tool is available.

See [local development](../development/local-development.md) for portable feed setup
and repeated local rebuilds. Usage does not publish product packages or supply the
public site tree; site synchronization remains owned by Adapter documentation.
