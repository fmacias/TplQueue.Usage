# Development

This section covers local source-build concerns for `TplQueue.Core`.

## Language-version policy

The shipped `netstandard2.0` product line is pinned to `LangVersion=9.0`.

That is a source-build policy for the repository, not a runtime requirement for consumers of the compiled package.

## Local validation

Build and test from repository root:

```powershell
dotnet restore .\core.sln --configfile .\NuGet.config
dotnet build .\core.sln
dotnet test .\core.sln
```

Run deterministic coverage validation:

```powershell
.\coverage.ps1
.\coverage.ps1 -EnforceBaseline
```

Coverage artifacts are written under `artifacts/coverage/`.

## Local packaging

Build the local preview package with:

```powershell
.\pack-local.ps1
```

That writes the package into `..\TplQueue.NugetLocal`.
