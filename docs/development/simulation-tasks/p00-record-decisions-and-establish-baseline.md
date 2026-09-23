# P00 - Record decisions and establish baseline

Status: complete.

[Back to the preparation checklist](../simulation-use-case-plan.md#preparation-checkpoints)

## Thread scope and completion

Execute only this task in this thread. Follow the main plan's
[one-task-per-thread workflow](../simulation-use-case-plan.md#one-task-per-thread).
After acceptance passes, update this task's status and execution record, check its
entry in the main plan, and include those updates in the local task commit(s).
Verify the commits succeeded before reporting completion. If blocked, record the
blocker and leave the main checkbox unchecked. End with commit hashes, validation
results and the next eligible task's file path and prompt for a new thread.
Stop there; do not start another task or push commits.

## Dependencies

None. This is the first task.

## Scope

Confirm normal graph limits,
  run current relevant checks, record reference mode and existing failures. Identify
  the integration tests to retain. Suggested commit, if decisions change this file:
  `docs(simulation): agree initial scenario limits and validation baseline`.

## Steps and acceptance

- [x] Inspect repository status, relevant configuration and active references;
  record the baseline commit and whether checks use sibling source or packages.
- [x] Confirm the normal default job count, maximum unique jobs per root and graph
  depth with the human. Record accepted values in the main plan's decision table;
  leave unconfirmed proposals pending. Record any deferred decisions explicitly.
- [x] Inventory the existing integration tests relevant to ETL composition, queue
  execution, observers/projection and host lifecycle. Record the test paths or
  names to retain during subsequent tasks; preserve existing valid coverage.
- [x] Run current relevant checks through the documented validation entry points.
  Record exact commands, environment/reference mode, results and existing failures.
  Distinguish source validation from package-consumption validation; record checks
  not run and the reasons. Packaging is not applicable if no packaged output changes.
- [x] Save the baseline evidence in this task's execution record and identify the
  next eligible task. Record unresolved failures as limitations or blockers; do not
  describe a failing baseline as passing validation.

This task records decisions and existing behavior. Renaming projects, implementing
scheduling and fixing unrelated baseline failures belong to separate work.

## Working rules and validation

Read the [main plan](../simulation-use-case-plan.md) before starting. Its
[common implementation rules](../simulation-use-case-plan.md#common-implementation-rules),
[iteration workflow](../simulation-use-case-plan.md#iteration-workflow-and-definition-of-done),
[validation entry points](../simulation-use-case-plan.md#validation-entry-points) and
[additional automated coverage](../simulation-use-case-plan.md#additional-automated-coverage)
apply to this task. Resolve only decisions needed by this task and include the
relevant tests, documentation and acceptance evidence in the same iteration.

## Execution record

### Checkpoint

- Item: P00 - Record decisions and establish baseline.
- Status: complete. Acceptance is recording agreed decisions and an honest baseline;
  the standalone build/test baseline is failing, as detailed below.
- Date: 2026-09-23.
- Baseline commit: Usage `f9b8fa3ed4ae5bd3c534e1d6540743bf8516e892`.
- Decisions confirmed: the human accepted **15 / 50 / 8** in this thread: default
  15 unique jobs including the root, normal maximum 50 unique jobs per root, and
  maximum 8 levels along the longest dependency path, counting the root as one level.
  The main decision table records acceptance and explicitly defers other proposals.
- Scenario settings and expected counts: existing startup behavior remains two
  three-job roots on each of FIFO, Parallel and Cache, once after a one-second
  delay: six roots and eighteen globally unique jobs. Ingest -> Transform -> Load;
  Load is the root. Handler delays remain 500/700/400 ms. The accepted limits are
  future scenario settings, not implemented defaults or measured performance limits.
- Changed behavior and files: documentation only, this record and the main plan's
  decisions/completion entry and task workflow needed to link this record.
- Failing-test evidence before implementation: no implementation or new test is
  required by P00. Existing build failures and passing checks are recorded below.
- Build / unit / pack / integration commands and results: see validation evidence.
- Browser checks and observed outcomes: HTTP host tests passed within the source
  suite; interactive browser behavior was not exercised.
- Staged review findings and resolution: compatibility configuration checked first;
  task changes reviewed with `git diff --cached` in an isolated task index. No code,
  dependency, public API or architecture changes. Link and whitespace validation
  covers both the working documents and the isolated commit. The pre-existing task
  split remains staged except the P00 file and shared workflow needed by this task.
  No critical, design or maintainability findings in the task diff; test gaps and
  baseline limitations are explicitly retained below. No broader fixes were made.
- Skipped checks and reasons: see validation evidence.
- Remaining limitations or dependency blockers: standalone build/test and coverage
  remain limited by preview reference alignment; this does not block completion of
  this recording task. No unresolved human decision blocks P00 or the P01 rename.
- Commit subject: `docs(simulation): agree initial scenario limits and validation baseline`.
- Commit hash: record in the next checkpoint update; Git history and the thread
  handoff identify this commit until then.
- Next eligible item: P01 - Isolate the module rename; not started in this thread.
- Next task file and new-thread prompt: see handoff below.

### Environment, status and reference mode

Windows `10.0.26200`, `win-x64`; Windows PowerShell 5.1; .NET SDK `10.0.401`,
MSBuild `18.9.11`; .NET/ASP.NET Core runtime `8.0.31` is installed for net8.0
tests and hosts. No `global.json` selects a different SDK. Debug configuration
was used throughout. No package version override or dependency edits were made.

Usage initially had 32 staged documentation files (the task split, main plan,
README and development index), with no unstaged changes. Preserve the remaining
split/navigation changes for their own commit; do not include the other tasks in
the P00 commit. Source validation also includes pre-existing sibling work, so the
Usage HEAD alone is not a reproducible description of the source baseline:

| Repository | HEAD at baseline | Existing changes used by the checks |
| --- | --- | --- |
| Abstractions | `2c4e4f07826d9f93cafd12188ab9cd2050891eeb` | Clean |
| Adapter | `74ca4b9d7db0ffe3aa6b72d7dbc60152c58bc733` | Clean |
| Core | `5440d201110e93b6b91f73f3d3f5a402aa69d30a` | Six staged files covering execution-channel events/runtime/tests and root documentation |
| WorkspaceTplQueue | `ab8e0a4a280c920c3fb7f618023c7b2d17780b1c` | Staged solution/reference-switch and documentation changes |

Configuration inspection included root props/targets, Abstractions' nearer
`src/Directory.Build.props`, project files, NuGet configuration and evaluated
MSBuild properties. Abstractions, Adapter and Core production projects target
`netstandard2.0` with C# 9; ETL and its contracts also target `netstandard2.0`
with C# 9. Usage hosts and tests target `net8.0` with C# 12. No incompatible
target/language configuration was found.

- Standalone Usage is **mixed**: ETL references sibling Abstractions and MemCache
  source, Blazor references sibling DI/Core source, and the integration project
  declares TplQueue packages plus ETL and host build dependencies. Transitive
  product dependencies still resolve packages without the workspace switch.
- `QueueObserverSignalRDashboard` also directly references sibling DI/Core source.
  This is an existing mismatch with README/local-development wording that describes
  other standalone consumers as package-based and describes that dashboard as
  package validation. P00 records the contradiction; it does not normalize it or
  expand the documented exception across repositories.
- `WorkspaceTplQueue.sln` intentionally enables `UseProjectReferences=true` and
  replaces selected package references with sibling source. The restored Usage
  integration assets identify all ten TplQueue libraries as `project` in this mode.
  The passing workspace suite is source validation, not package-consumption proof.
- `PackageConsumptionSmokeConsole` evaluates with no project references. Its
  restored assets identify all nine TplQueue libraries as `package`, version
  `0.1.0-preview.1`, using the existing global package cache. Configured feeds are
  `nuget.org` and `../TplQueue.NugetLocal`; this run does not establish a fresh
  download from nuget.org or validate newly packed artifacts.

### Existing coverage to retain

Paths below are relative to
`test/integration/Fmacias.TplQueue.Usage.Integration.Test/`. Retain the existing
valid suite; these are the principal fixtures for subsequent simulation work.

| Area | Test paths | Coverage to preserve |
| --- | --- | --- |
| ETL composition | `Samples/EtlWorkflowSampleTests.cs` | Fixed payload graph, selected queue, null/empty measurements, idempotent disposal and enqueue rejection after disposal |
| ETL cancellation/cache | `Samples/EtlQueueRuntimeCancellationCleanupTests.cs` | Terminal-event cancellation cleanup, enqueue failure cleanup, generic Cache enqueue, resolver/serializer graph round-trip |
| Scenario boundary | `Samples/LegacyMeasurementScenarioTests.cs` | Deterministic input, delegation to shared workflow, cancellation before submission and missing dependencies |
| Queue execution | `Queues/StrictFifoTaskQueueTest.cs`, `Queues/ParallelTaskQueueTest.cs`, `Queues/CacheableQTest.cs` | FIFO/parallel execution, cache enqueue/start, cancellation before and during execution |
| Graph execution | `Runners/JobIntegrationTests.cs`, `Runners/TaskRunnerRootIntegrationTests.cs`, `Runners/PayloadTaskRunnerIntegrationTests.cs`, `Runners/PayloadTaskRunnerRootIntegrationTests.cs`, `Factories/` | Dependency order, root/job/payload construction, cancellation and continuation of following roots |
| Cache/payload composition | `Cache/CacheHydrationSerializerIntegrationTests.cs`, `SystemTextJsonSerializerIntegrationTests.cs`, `PayloadHandlerRegistrationIntegrationTests.cs`, `PayloadHandlerGroupingIntegrationTests.cs` | Hydration, serialization, handler resolution/registration and grouped handlers |
| Observer/projection | `Samples/EtlExecutionProjectionStoreTests.cs` | Immutable snapshots, concurrent streams, deduplication, late events, terminal-success graph membership, subscriber isolation, channel validation, Started/enqueue placement, legacy channels and unsubscribe behavior |
| Preserved historical assertions | `Legacy/ScatterTimeline.cs` and `ScatterTimeline_*` tests in the projection fixture | Existing graph/mapper tests; helper remains test-only, not a runtime renderer |
| Public transport | `Consumers/JobEventTransport/JobEventTransportProjectionIntegrationTests.cs` | Metadata-only event projection, terminal payload snapshots and detached transport values |
| Console lifecycle | `Samples/QueueObserverConsoleSampleTests.cs` | `Wait_Mode_CompletesPipeline_AndWritesEntityLogs`, `Cancel_Mode_CancelsExtract_AndStillFinalizesStandaloneTask` |
| Separate SignalR sample | `Samples/QueueObserverSignalRDashboardSampleTests.cs` | Wait/cancel HTTP flows, detached JSON snapshots and sequential reuse of an injected queue |
| Passive Blazor host | `Samples/TplQueueSampleBlazorSignalRSampleTests.cs` | `PassiveTimelineDashboard_IsTheOnlyApplicationSurface(false/true)`: launch from output/source, workload completion on all queues, assets and absence of extra application endpoints |

The source workspace run also retains the existing Core execution-channel tests.
The host tests start and dispose real processes; HTTP completion/prerender checks
do not prove circuit lifecycle or timer shutdown races. Timer delivery/admission,
explicit lifecycle transitions, pre-success/shared-root membership and bounded
retention remain coverage work for their named future tasks. No tests were deleted,
renamed or added during P00.

### Validation evidence

Commands below were run in the stated directory. Script invocations used
`powershell -NoProfile -ExecutionPolicy Bypass -File <script>`; output was captured
under the workspace's `out/simulation-p00/`. This record contains the durable
results; those local logs are supplemental and are not committed.

| Working directory | Exact validation command | Result |
| --- | --- | --- |
| Usage | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` (sandboxed attempt) | Exit 1 during restore after the contracts build; NU1900 reported inaccessible NuGet vulnerability data. Repeated with approved network access, below. |
| Usage | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` (approved network retry) | Exit 1. Contracts, ETL, package smoke and observer console built; SignalR dashboard dependency compilation failed with CS0246 for `IJobExecutionEvent`. Core's standalone package reference resolves the older preview Abstractions contract. Blazor and integration builds were not reached. A cached NU1900 warning remained. |
| Usage | `powershell -NoProfile -ExecutionPolicy Bypass -File ./test.ps1 -Configuration Debug` (approved network access) | Exit 1 with the same CS0246 during sample builds. The script builds samples before testing; no NUnit tests ran through this entry point. |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug -RunTests` (approved network access) | Exit 0. Build passed with 7 existing warnings, 0 errors; all 628 tests passed, 0 failed/skipped. Source mode; no `-PackOnBuild`, so the script sets `SkipPackLocal=true`. |
| Usage | `dotnet run --no-build --no-restore --configuration Debug --project ./samples/PackageConsumptionSmokeConsole/PackageConsumptionSmokeConsole.csproj -- all` | Exit 0; all six package-only modes passed: job-root, parallel-closures, fifo-closures, retry, observer, payload-cache. Uses the Debug output built successfully by the standalone script. |

The workspace script builds first, then runs the loaded test projects together
with `dotnet test ... --no-build`; it does not split unit and integration phases.
Results by assembly: Core 328; Usage integration 115; Adapter facade 64;
RetryPolicies 57; Cache.Abstract 38; DI 17; Observers 6; MemCache 3. The seven build
warnings are five CS8625 nullable test arguments, CS0649 in `ApiTests`, and CS0414
in `CacheFactoryTests`. Error-path console messages appeared in passing tests;
they were not test failures.

Additional inspection: `dotnet --info`; `git status --short` and `git rev-parse HEAD`
for Usage and the four siblings above; `dotnet msbuild <project> -getProperty:TargetFramework,LangVersion,TplQueuePackageVersion`
for Abstractions, facade, Core, ETL, contracts and Usage tests; smoke evaluation
also used `-getItem:ProjectReference,PackageReference`. Restored `project.assets.json`
files were inspected to distinguish source libraries from package libraries.

Skipped checks and reasons:

- `./coverage.ps1 -EnforceBaseline`: not run; the standalone sample-build prerequisite
  already fails. The accepted package-consumption threshold is 93.5%; no coverage
  percentage or gate success is claimed. Running against the workspace-built
  assembly with `-NoBuild` would measure source mode under a package label.
- Packaging: not applicable; no packaged output changed. Usage has no own
  `pack-local.ps1`; no product package was rebuilt or feed/cache cleared.
- Independent unit command: not needed; loaded unit projects ran in the documented
  workspace command. Other product suites outside that solution were not expanded.
- Monitor Node checks (`node scripts/check.mjs`, `node --test tests/layout.test.js`)
  and interactive harnesses: not run because P00 changes no rendering or lifecycle
  behavior. `node --version` also found no Node executable on this shell's PATH.
  Browser acceptance remains unverified, separate from the passing HTTP host tests.

Documentation validation: the local helper command
`powershell -NoProfile -ExecutionPolicy Bypass -File ../out/simulation-p00/validate-docs.ps1`
passed 46 working-document links/anchors, 17 isolated-commit links/anchors and
21 inventoried test paths;
`git diff --check` and isolated `git diff --cached --check` passed. The P00 checkbox
and accepted decisions are included in the task commit. The remaining task-split
files stay staged; the commit links only to documentation present in its own tree.

### Handoff

Next eligible task file:
`C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\p01-isolate-the-module-rename.md`.
That file is part of the preserved staged task split. Keep those pre-existing
changes intact and inspect repository status in the next thread. Reuse the source
validation entry point; do not interpret the standalone compile failure as fixed.

```text
Execute only the task in:
C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\p01-isolate-the-module-rename.md

Read its linked plan and applicable repository instructions. Check dependencies,
implement the task, run the required validation and perform the staged review.
Create the local commit(s), including the task's execution record and its completed
checkbox in the main plan once acceptance passes. Preserve unrelated changes.
If blocked, record the blocker and leave the task incomplete.
Finish with the commit hashes, validation results and the next eligible task's
file path and prompt for a new thread. Do not start the next task or push commits.
```
