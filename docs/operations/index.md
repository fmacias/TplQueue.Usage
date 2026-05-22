# Operations

This section groups repository operations for `TplQueue.Usage`.

## Build and test scripts

- [build.ps1](../../build.ps1)
- [test.ps1](../../test.ps1)
- [coverage.ps1](../../coverage.ps1)

## Local feed and release-smoke role

`TplQueue.Usage` validates the current preview line from packages. During local workspace development it normally restores from:

- `..\TplQueue.NugetLocal`
- `nuget.org`

The repository also owns the simple release-smoke consumer application:

- [PackageConsumptionSmokeConsole](../../samples/PackageConsumptionSmokeConsole/README.md)

Coverage artifacts are written under `artifacts/coverage/`, including `artifacts/coverage/html/index.html` when the standard `ReportGenerator` tool is available.
