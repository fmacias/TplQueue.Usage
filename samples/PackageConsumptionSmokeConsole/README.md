# PackageConsumptionSmokeConsole

`PackageConsumptionSmokeConsole` is the simplest human-readable consumer project used for the release smoke checklist.

It stays intentionally small:

- one console application
- package references only
- one execution mode per checklist scenario
- direct pass/fail output for manual runs

The more detailed behavioral validation remains in the `TplQueue.Usage` integration tests.

## Purpose

This sample exists to cover the `Package-Consumption smoke-testable consumer applications` section of the release checklist with a clean package-based consumer outside the product repositories.

It validates:

- minimal `IJob` / `IJobRoot` composition
- parallel queue behavior with closure-based work
- FIFO queue behavior with closure-based work
- retry-policy selection for a rooted job
- observer event consumption
- payload/cache hydration with `IDataJobRoot`

## Run

Run all smoke modes:

```powershell
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- all
```

Run one mode only:

```powershell
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- job-root
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- parallel-closures
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- fifo-closures
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- retry
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- observer
dotnet run --project .\samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj -- payload-cache
```

If no argument is provided, the sample defaults to `all`.

## Feed and restore model

This project uses package references only.

It is restored through the `TplQueue.Usage/NuGet.config` file, which points to:

- the sibling local feed `..\TplQueue.NugetLocal`
- `nuget.org` as the secondary source

That keeps the smoke surface representative of real package consumption instead of private project-reference composition.
