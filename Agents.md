# TplQueue.Usage agent instructions

## Responsibility and scope

This repository owns public package-consumption samples, integration coverage,
sample-specific documentation and the reusable JobMonitor tool. Apply applicable
parent workspace instructions and the explicit human task.

For the Blazor use-case work, implement only inside `TplQueue.Usage`. Do not modify
`TplQueue.Abstractions`, `TplQueue.Adapter` or `TplQueue.Core`. If a scenario needs
a product enhancement, record the proposal, motivation, affected public API or
behavior, and blocked use-case ID in the [README Roadmap](README.md#roadmap).
Continue through supported APIs where possible; do not disguise changed product
semantics as a sample workaround.

Current source, project references and composition are the authoritative baseline,
subject to supported public API contracts. Derive maintained documentation from
them. Historical milestones do not override current behavior. Surface unresolved
contradictions rather than silently reconciling repositories.

## Component ownership

| Component | Owns |
| --- | --- |
| `samples/TplQueue.Sample.BlazorSignalR` | Host, DI composition, observer projection, presentation DTOs and Blazor frontend |
| `samples/TplQueue.Sample.Domain` | Job/graph factories, payloads, handlers, business data and queue/cache wrappers |
| `samples/TplQueue.Sample.Etl.Contracts` | Shared sample interfaces and DTOs, including non-ETL simulation contracts |
| `samples/TplQueue.Sample.Simulation` | Workflows, scheduling, admission, session lifecycle, runtime access and graph membership; references Contracts, not Domain |
| `tools/TplQueue.JobMonitor` | Maintained JavaScript Web Component and bridge assets; generated sample copies are not source |

Preserve runtime queue identity between submission, observers and the queue catalog.
Reuse `ScheduledWorkflow` and its `System.Threading.Timer` ownership. The host owns
workloads; browsers observe them. Preserve observer-driven snapshots, actual
execution-channel metadata and the existing JobMonitor integration. Read the
architecture guide for detailed lifecycle, graph and presentation semantics.

Register every added simulation workflow unconditionally in the Blazor host,
alongside all existing workflows. Do not introduce configuration flags, profiles
or mutually exclusive workflow selection. Preserving existing workflows means
keeping their behavior and schedules while including each new workflow.
Every workflow continuously creates fresh jobs through its factory in Submit on
each scheduled arrival until StopAsync or host cancellation. Do not cache jobs
for replay, stop after a lifetime root count, or suppress submissions because
earlier roots are queued/running. Execution capacity belongs to the product queue;
waiting jobs must be enqueued so their real observations reach the monitor.
Keep the existing guard against overlapping submission callbacks. Delivery history
stores root IDs only. Stop/Completion concern arrivals, not execution/observer drain.

## Documentation map

- [README](README.md): repository/sample overview and product-dependency Roadmap.
- [Architecture](docs/architecture/blazor-consumer-sample.md): maintained sample behavior and boundaries.
- [Component diagram](docs/architecture/blazor-sample-components.puml): source/package dependencies and integration.
- [Development index](docs/development/index.md): entry point for sample extensions.
- [Use-case catalog](docs/development/simulation-use-case-plan.md): current status and dependencies.
- [Use-case specifications](docs/development/simulation-tasks/): `ucXX-*.md` describes purpose, implementation and observable behavior; `p00`–`p03` are completed history.
- [Documentation index](docs/index.md): usage, architecture and operations navigation.

Installed `TplQueue.AgentMarketplace` skills provide reusable engineering
procedures. Use relevant `engineering` and `tplqueue-usage` guidance when available;
do not duplicate those procedures in repository instructions or use-case files.
The human developer handles builds, test execution, packaging, staged reviews,
commits and workflow administration for these use cases unless explicitly delegated.

## Package and publication boundaries

Standalone consumers use public product packages, with their version controlled
by `TplQueuePackageVersion` in [Directory.Build.props](Directory.Build.props).
Workspace source-reference switching is a separate maintainer mode.

Usage documentation remains public-safe and sample-focused.
`TplQueue.Adapter/docs/<lang>/` remains the public product-documentation source
synchronized by `fmacias.github.io`; private Core documentation stays in Core.
This sample documentation does not redefine that publishing boundary.
