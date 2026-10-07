# Development

This section groups local-development guidance for `TplQueue.Usage`.

## Local development

- [Local development](local-development.md)
- [Simulation implementation plan and individual task checklist](simulation-use-case-plan.md#task-checklist)

## Typical workflow

```powershell
.\build.ps1
.\test.ps1
.\coverage.ps1 -EnforceBaseline
```

The shared TplQueuePackageVersion defaults to `0.2.0-preview.2`. See
[local development](local-development.md) for feed setup, repeated package rebuilds
and command-line version overrides.
