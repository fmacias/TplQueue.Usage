# Local development

`TplQueue.Usage` is designed to validate the current preview package line from the local workspace.

## Package source

The repository uses [NuGet.config](../../NuGet.config) with two sources:

- `..\TplQueue.NugetLocal`
- `nuget.org`

During current workspace development, the local feed is expected to provide the active preview packages first.

## Current default version

The default package line is defined in [Directory.Build.props](../../Directory.Build.props):

```xml
<TplQueuePackageVersion>0.1.0-preview.1</TplQueuePackageVersion>
```

When the preview line changes later, update that property in this repository so local builds and tests follow the intended package version by default.

## Refreshing local preview packages

1. Pack the product repositories into `..\TplQueue.NugetLocal`.
2. Run `.\build.ps1`.
3. Run `.\test.ps1` to execute the package-based integration suite adapted from `TplQueue.Core`.

The current integration suite also builds and launches the `QueueObserverConsole` sample in its documented `wait` and `cancel` modes, and it launches the `QueueObserverSignalRDashboard` sample through its HTTP surface. Both samples remain part of the public verification surface instead of drifting away from the documented behavior.

Typical pack entry points in the workspace:

- `WorkspaceTplQueue\pack.ps1`
- `TplQueue.Abstractions\pack-local.ps1`
- `TplQueue.Adapter\pack-local.ps1`
- `TplQueue.Core\pack-local.ps1`

## Testing another version without editing the repo

You can override the consumed package line at command time:

```powershell
.\build.ps1 -TplQueuePackageVersion <version>
.\test.ps1 -TplQueuePackageVersion <version>
```

That keeps the committed default stable while letting you validate a different package set.
