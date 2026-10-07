# Local development

Run the standalone TplQueue.Usage scripts to validate product packages. Loading
projects through WorkspaceTplQueue can switch some references to sibling source,
which is a separate maintainer workflow.

## Package source and version

Sample and integration projects use `TplQueuePackageVersion`, defined in
[Directory.Build.props](../../Directory.Build.props), for every TplQueue dependency.
The default is `0.2.0-preview.2`. Change that property to update the default across
the repository, or pass an available version to the scripts:

```powershell
.\build.ps1 -TplQueuePackageVersion <version>
.\test.ps1 -TplQueuePackageVersion <version>
.\coverage.ps1 -TplQueuePackageVersion <version> -EnforceBaseline
```

[NuGet.config](../../NuGet.config) enables nuget.org and LocalPackages. The local
path is `..\TplQueue.NugetLocal`, relative to the repository's config file. Source listing
order does not guarantee that a particular feed supplies a package. A source push
does not publish a NuGet package: the selected version must exist in an enabled feed.

## Refreshing local preview packages

1. Rebuild the changed product packages and their affected dependents into
   `..\TplQueue.NugetLocal`, using their `pack-local.ps1` scripts. Local builds are
   unsigned; official signing belongs to the product release workflow.
2. When reusing a package version, ensure the old TplQueue cache entries are removed
   before restoring consumers. The current product pack-local scripts clear these
   entries before packing; packages restored by later packing steps may be cached again.
3. Restore and rebuild Usage, then run tests against the resulting binaries.

```powershell
.\build.ps1 -Configuration Debug
.\test.ps1 -Configuration Debug
.\coverage.ps1 -Configuration Debug -EnforceBaseline
dotnet run --no-build --project .\samples\PackageConsumptionSmokeConsole -- all
```

Stop running sample hosts before replacing the binaries they use. A rebuild does
not reload an already running Blazor server; restart it after the package refresh.

## Blazor sample

Domain, Contracts and Simulation now live under `samples/` in this repository.
The host references Core and DI packages; Domain references MemCache, Contracts
references Abstractions, and Simulation references Contracts. No sibling product
source checkout is needed for standalone package consumption.

```powershell
dotnet run --no-build --project .\samples\TplQueue.Sample.BlazorSignalR
```

The host starts both continuous workflows independently of browsers. Stop arrivals
closes admission; accepted jobs can finish. Timing is fixed at one second initially
and three seconds between ticks. See the
[architecture guide](../architecture/blazor-consumer-sample.md) and
[sample README](../../samples/TplQueue.Sample.BlazorSignalR/README.md).

The integration suite exercises console and HTTP samples as well as Domain,
workflow lifecycle, queue projection and graph identity. Browser circuit acceptance
is separate: HTTP success alone does not establish interactive behavior.
