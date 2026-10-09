# AGENTS.md

## Repository context

You are working in the `TplQueue.Usage` git repository, part of the overall `fmacias` TplQueue workspace.

Related repositories include:

- `TplQueue.Usage`
- `TplQueue.Adapter`
- `TplQueue.Abstractions`
- `TplQueue.Core`

Treat every repository as an independent git boundary even when samples, packages, contracts, documentation, or validation flows depend on another repository.

When this repository is checked out inside the overall workspace and an applicable parent `AGENTS.md` exists, apply those common instructions first.

This file adds the repository-specific instructions for `TplQueue.Usage`.

`TplQueue.Usage` is the public package-consumption, sample, integration, and validation surface of the TplQueue ecosystem.

It contains:

- runnable consumer samples
- package-based integration validation
- sample and integration architecture
- reusable monitoring tooling
- public-safe repository documentation under `docs/`

It does not own private `TplQueue.Core` implementation documentation.

---

## Instruction and source-of-truth hierarchy

Apply instructions and evidence in this order:

1. explicit human task
2. applicable parent workspace `AGENTS.md`
3. this repository root `AGENTS.md`
4. nearest more-specific `AGENTS.md`
5. maintained architecture documentation
6. current source code
7. current tests and validation harnesses
8. current project/build configuration
9. installed Agent Skills as workflow guidance

Skills complement repository-local instructions.

They do not replace the repository source of truth.

If a Skill describes an older baseline than the current repository, follow the current repository instructions, architecture documentation, source, tests, and configuration.

Do not silently reconcile contradictions between repositories.

---

## Agent Skills

### Generic engineering Skills

When available, use the `engineering` plugin for generic workflows such as:

- `review-csharp`
- `review-concurrency`
- `review-public-api`
- `architecture-review`
- `refactor-code`
- `implement-feature`
- `testing-dotnet`
- `validate-dotnet-change`
- `align-cross-repository-documentation`
- `document-csharp-api`
- `summarize-staged-commit`

Do not duplicate those generic workflows in this file.

### TplQueue.Usage Skills

When available, use the `tplqueue-usage` plugin for Usage-specific workflows:

- `review-usage-sample-architecture`
- `validate-usage-package-consumption`
- `modify-usage-simulation-runtime`
- `modify-blazor-monitor-projection`
- `modify-job-monitor-visualization`
- `validate-job-monitor-interaction`

Additional Usage-specific Skills should only be introduced when a distinct, repeated and independently triggerable workflow is not adequately covered by these Skills or by the `engineering` plugin.

Do not create one Skill per sample, project, folder, or implementation type automatically.

---

## Repository role

Treat this repository as a public-facing consumer repository.

Prefer public package-consumption behavior over assumptions about private source implementations.

Keep runnable examples and validation flows aligned with:

- the published/public package contracts
- current public package versions
- public TplQueue documentation
- current sample architecture

Do not expose or recreate private Core implementation documentation here.

Do not make Usage samples depend on undocumented private Core behavior.

---

## Cross-repository boundaries

### TplQueue.Adapter

`TplQueue.Adapter` provides public facade and integration packages consumed by Usage.

It also owns the current public TplQueue documentation trees.

When Adapter APIs or package behavior change, identify affected Usage samples and validation flows explicitly.

Use the `tplqueue-adapter` plugin for Adapter-specific work when available.

### TplQueue.Abstractions

`TplQueue.Abstractions` owns shared public contracts.

When a contract changes, identify affected Usage compilation, mapping, monitoring, simulation, and sample behavior.

Use the `tplqueue-abstractions` plugin for Abstractions-specific work when available.

### TplQueue.Core

Core owns runtime implementation behavior.

Do not expose private Core implementation knowledge through Usage samples or documentation.

If a Usage issue appears to originate in Core, identify the dependency and respect the separate repository boundary.

### Cross-repository changes

Do not modify another repository during an Usage-only task unless the explicit task authorizes cross-repository changes.

When documentation or contract alignment spans repositories, use `align-cross-repository-documentation`.

---

## Public documentation boundary

The current public-documentation ownership model is:

- `TplQueue.Adapter/docs/<lang>/` is the public integrated TplQueue documentation source of truth.
- `TplQueue.Core/docs/<lang>/` is private Core documentation.
- `fmacias.github.io` publishes public TplQueue documentation from Adapter's public documentation trees.
- `TplQueue.Usage` contains sample-specific and public-safe integration documentation.

Usage documentation should link to authoritative public product documentation rather than duplicate it unnecessarily.

Do not recreate deprecated or competing public documentation trees inside Usage.

Do not publish private workspace or Core documentation from Usage.

When changing this boundary, update affected repository instructions and documentation coherently.

---

## Package-consumption model

Usage is intended to validate TplQueue as a real consumer.

Standalone validation and workspace development are different modes.

### Standalone package consumption

Standalone Usage projects should consume the intended TplQueue packages.

Resolve the current package line from the shared `TplQueuePackageVersion` property in `Directory.Build.props`.

Do not hard-code the package version independently across sample projects.

The build, test, and coverage workflows must use a consistent package line.

Successful source/project-reference execution does not prove that package consumption works.

When validating published/local package behavior, verify that the consumer actually resolves the intended package artifacts.

### Workspace project-reference mode

Workspace project-reference switching is an intentional development mode.

Do not confuse it with standalone package-consumer validation.

Do not modify dependencies merely to make a documentation-only change.

---

## Repository validation entry points

The repository-supported root validation commands currently include:

```powershell
.\build.ps1
.\test.ps1
.\coverage.ps1
```

Inspect the scripts for their current parameters and exact behavior before using specialized options.

Use `validate-dotnet-change` for the generic validation workflow.

Use `validate-usage-package-consumption` when the important question is whether the Usage consumer actually resolves and validates the intended TplQueue packages.

If a validation step cannot run, report that clearly.

---

## Sample architecture

The maintained sample architecture is documented in:

`docs/architecture/blazor-consumer-sample.md`

Read that document before modifying:

- the Blazor sample host
- simulation integration
- queue runtime composition
- monitoring projection
- JobMonitor integration
- graph identity behavior

The architecture guide is the maintained detailed source for these relationships.

This `AGENTS.md` records the principal invariants and routing rules but should not become a duplicate architecture specification.

---

## Module ownership

Preserve the current module responsibility model unless the explicit task deliberately changes it.

### Domain

`samples/TplQueue.Sample.Domain` owns domain-oriented sample implementation such as:

- jobs
- payloads
- handlers
- queue/cache wrappers
- business data

### Contracts

Public sample interfaces remain in the ETL Contracts module.

Keep contracts independent from unnecessary Domain implementation details.

### Simulation

Simulation references Contracts rather than Domain.

Simulation owns concerns such as:

- measurements
- workflows
- submissions
- graph tracking
- shared simulation runtime

### Blazor host

The Blazor host owns composition of the sample modules and observer-driven presentation integration.

Do not move responsibilities between Domain, Contracts, Simulation, and host merely for convenience.

Use `review-usage-sample-architecture` when changing these boundaries.

---

## Runtime and queue ownership

Domain queue wrappers are transient.

The singleton `IEtlQueueRuntime` captures the queue instances used for actual submission.

Observers and dashboard/catalog services must observe those same runtime-owned queue instances.

Do not resolve new queue wrappers solely for monitoring.

Preserve queue identity between:

- submission
- observer attachment
- monitoring
- dashboard representation

Review service-lifetime changes for ownership and disposal consequences.

---

## Simulation workflow lifecycle

The current ETL and SingleJob workflows implement `ISimulationWorkflow` through the scheduled workflow infrastructure.

Each workflow owns its scheduling timer.

The current timing baseline is:

- one-second initial offset
- three-second interval

These values are current repository behavior, not immutable product guarantees.

If deliberately changed, update source, tests, architecture documentation, and this file if the baseline is no longer accurate.

Current lifecycle ownership is:

- `StopAsync` owns timer cleanup
- `ISimulationWorkflow` is not `IDisposable`
- `ISimulationService` is not `IDisposable`
- the hosted service awaits `StopAsync`
- runtime disposal owns queues and subscriptions

Do not duplicate timer, queue, subscription, or disposal ownership across layers.

Use `modify-usage-simulation-runtime` when changing this behavior.

---

## Host and browser ownership

The host owns the workload.

The workload starts independently of browser connections.

Browser connections must not:

- create the workload
- restart the workload
- multiply the workload
- become owners of queue lifetime

Stopping arrivals means stopping future submissions.

It does not cancel already accepted jobs unless the explicit task deliberately changes that contract.

Do not couple simulation lifetime to Blazor circuit/browser lifetime.

---

## Graph identity and membership

Capture simulation graph membership before enqueue through `ISimulationGraphCatalog`.

Preserve all `rootJobIds` for jobs shared by multiple roots.

A root ID identifies a one-root run.

Graph membership is composition metadata.

Do not treat membership as evidence of:

- queue acceptance
- queue ownership
- lifecycle state
- execution channel
- execution success

Keep real dependency relationships distinct from graph membership.

This includes FIFO ordering/dependency edges where represented by the current architecture.

Use `modify-usage-simulation-runtime` and `modify-blazor-monitor-projection` when these boundaries are affected.

---

## Blazor monitoring and projection

The current monitoring sample uses .NET 8 Interactive Server.

It observes workloads owned by the host.

Keep runtime behavior in C#.

Preserve:

- immutable snapshots
- Observer isolation
- `InvokeAsync` dispatch into the Blazor context
- coalesced updates
- asynchronous subscription disposal
- asynchronous JS interop disposal

Send one initial snapshot.

After that, refresh data from accepted Observer events.

Do not add polling or periodic backend-driven live redraws as a replacement for the current event-driven snapshot model.

Advance live time when new snapshots arrive.

Preserve paused/history reference behavior.

A parent Razor rerender using the same snapshot must not resend the snapshot through JavaScript interop merely because the parent rerendered.

User interaction and resize may redraw locally.

Use `modify-blazor-monitor-projection` when changing this boundary.

---

## Execution-channel observation

`IJobExecutionEvent` is optional additive Observer metadata.

Do not break existing `IJobEvent` implementations.

Do not fabricate execution channels for events that do not provide them.

A null execution channel remains unassigned.

Execution-channel metadata is an observed runtime fact.

Do not derive channel placement from:

- root identity
- graph membership
- arbitrary frontend allocation

Do not reinterpret an execution channel as a thread ID.

---

## JobMonitor frontend

The current frontend is:

`tools/TplQueue.JobMonitor`

It is a reusable JavaScript Web Component connected through a small Razor/JavaScript snapshot bridge.

ScatterChart and vis-timeline are retired from the active frontend.

Legacy support code or tests do not make them part of the running architecture.

Use `modify-job-monitor-visualization` for changes to the visualization model.

---

## JobMonitor time and channel model

The current viewer uses:

- vertical time
- backend-provided logical execution channels
- explicit Unassigned placement for null channels

Root identity does not determine channel placement.

Preserve exact event timestamps.

Do not move timestamps to avoid visual collisions.

Use grouping/count markers and interval inspection where required by the current UI model.

Time zoom must not widen execution channels.

Preserve:

- the fixed left UTC ruler
- keyboard accessibility for grouped jobs

---

## Enqueue and execution positions

Assigned execution positions use the backend channel-bearing `Started` timestamp.

Preserve observed enqueue time separately in the typed `enqueuedAt` field and metadata.

When both positions are visible:

- retain the enqueue marker in the Unassigned strip
- draw the directed enqueue-to-start connector

These represent two observed positions of one job.

They do not represent:

- two jobs
- a dependency edge

If a job lacks the required execution-channel/Started pair, keep it in Unassigned.

Do not substitute Running or terminal timestamps for channel acquisition.

Search must reveal the selected job's relevant strip, including retained enqueue history.

---

## Frontend model separation

Keep the following concerns outside the mechanical SVG renderer:

- model validation
- graph traversal
- layout
- time control
- theme tokens

The renderer should render an interpreted model rather than own runtime/domain semantics.

Keep graph membership and real dependency relationships separate.

Handwritten DTOs and mappers require semantic review.

Do not treat them as automatically correct mechanical field copies.

If code generation is introduced, require:

- authoritative source inputs
- pinned tooling
- repeatable generation
- repeatable validation

Keep generated transport contracts separate from handwritten frontend models, mappings, use cases, ports, and validation unless an explicit architecture decision says otherwise.

---

## Generated frontend assets

MSBuild synchronizes shared JobMonitor assets from the tool project into an ignored sample `wwwroot` location before static asset discovery.

Do not edit generated/synchronized copies as source.

Modify the maintained assets under the JobMonitor tool and allow the repository synchronization mechanism to produce the sample copies.

---

## Sample-profile boundaries

The future TypeScript/OpenAPI architecture is a separate future sample profile.

It is not a mandatory intermediary for the existing Blazor sample.

Do not introduce the future TypeScript architecture into the Blazor profile unless the requested feature requires that architectural change.

Use one chosen presentation framework per example.

`QueueObserverSignalRDashboard` is a distinct existing transport sample.

Do not attribute its:

- custom hub
- endpoints
- transport responsibilities

to the Blazor JobMonitor/timeline sample.

---

## Interactive validation

Normal .NET validation does not replace interactive frontend validation when browser behavior changes.

The repository contains complementary validation surfaces, including:

- NUnit/integration tests
- JobMonitor Node tests
- standalone browser harnesses
- Debug-only Blazor circuit/browser harnesses
- HTTP smoke or prerender checks

HTTP/prerender success is not evidence that interactive browser behavior works.

Use `validate-job-monitor-interaction` when changes affect:

- JavaScript interop
- browser interaction
- selection
- search
- zoom
- history
- reconnect behavior
- disposal
- layout
- frontend state
- Blazor circuit lifecycle

Validate only the affected interactive scope.

Do not edit generated frontend assets to make an interactive test pass.

---

## Tests

Preserve existing tests unless they are objectively obsolete under an intentionally changed contract.

Use the generic `testing-dotnet` Skill for normal .NET test methodology.

Use repository-specific tests and harnesses for sample, projection, and browser behavior.

Keep integration tests readable and representative of actual composition behavior.

Do not treat a static HTTP check as a substitute for an interactive test.

---

## Documentation

Usage documentation must remain public-safe and sample-focused.

Do not duplicate private Core documentation.

Do not create a second competing public TplQueue product-documentation tree.

When a Usage document needs product-level explanation, link to the authoritative Adapter public documentation when appropriate.

For cross-repository documentation work:

- inspect root instructions and README files of directly affected repositories;
- determine the documented source of truth;
- do not silently normalize contradictions;
- use `align-cross-repository-documentation`.

For documentation-only changes:

- verify links
- verify source claims
- inspect the diff
- do not claim runtime validation if runtime tests were not executed

---

## Change-scope guidance

Prefer the smallest coherent change that satisfies the requested behavior.

A normal Usage change should remain within:

- the owning sample/tool/module
- directly affected tests
- directly affected Usage documentation

Expand scope only when the actual contract or architecture requires it.

If Adapter, Abstractions, or Core must change:

- identify why
- identify the affected contract or behavior
- respect that repository's independent git boundary
- use its repository instructions and plugin Skills

Do not silently expand an Usage-only task into another repository.

---

## Validation reporting

For completed changes, report the validation actually performed.

Distinguish:

- passed
- failed
- skipped
- blocked
- not applicable

Do not report unexecuted checks as successful.

For package-consumption changes, state whether the consumer actually resolved the intended package configuration.

For frontend changes, state whether interactive validation was performed or only static/HTTP validation.