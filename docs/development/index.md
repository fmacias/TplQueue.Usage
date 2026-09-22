# Development

This section groups local-development guidance for `TplQueue.Usage`.

## Local development

- [Local development](local-development.md)
- [Simulation use-case implementation plan and iteration checklist](simulation-use-case-plan.md)

## Typical workflow

```powershell
.\build.ps1
.\test.ps1
.\coverage.ps1 -EnforceBaseline
```

The current default package line is controlled through `TplQueuePackageVersion` in `Directory.Build.props`, and can be overridden at command time when you need to validate another package version.
