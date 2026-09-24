# P02 - Add finite timer-driven delivery and minimal contracts

Status: complete.

[Back to the task checklist](../simulation-use-case-plan.md#task-checklist)

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

[P00](p00-record-decisions-and-establish-baseline.md). Use the current module name if [P01](p01-isolate-the-module-rename.md) is deferred.

## Scope

Move scenario
  orchestration into the module, retain observer attachment before first submission,
  and keep the host as lifecycle adapter. Verify finite tick counts, validation,
  overlap protection, exception observation and no accepted submissions after stop.
  Suggested commit: `feat(simulation): add bounded timer-driven scenario delivery`.

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

- Item: P02 - Add finite timer-driven delivery and minimal contracts.
- Status: complete, 2026-09-24.
- Baseline: Usage `2ff8b2f` and WorkspaceTplQueue `2df5774` (P01 commits).
  Read P01's execution record. Source validation still includes the pre-existing
  staged workspace reference switch and Core execution-channel work.
- Decisions selected for this iteration: one independently scheduled finite
  scenario per queue; one root per tick, two ticks, one-second startup offset,
  three-second interval, maximum two active roots per scenario. Repetitions count
  skipped/failed ticks. Busy ticks and whole batches that exceed capacity are
  skipped without catch-up. This implements the proposed standard delivery preset.
- Graph and expected counts: unchanged Ingest -> Transform -> Load,
  500/700/400 ms handlers, two roots on each of FIFO/Parallel/Cache, six roots and
  eighteen unique jobs. The 15/50-job graph defaults remain for later tasks.
- Lifecycle: single-use Start; Stop closes admission and waits for already-admitted
  submissions. It does not drain/cancel graphs. The host shutdown token continues
  to cancel jobs. Completion means arrivals/submissions finished, not job execution
  or observer/cache completion. Restart, drain controls and continuous mode remain
  deferred. Late callbacks cannot submit after StopAsync completes or disposal.
- Changed behavior/files: collection/scenario services moved from the host to
  Simulation, preserving their existing tests; internal timer adapter and finite
  delivery worker; singleton simulation service and validated immutable settings;
  detached counters/error message/accepted root IDs in the unchanged contracts
  project. Runtime active-root registration supplies conservative admission state.
  Host reduced to observer/lifecycle adapter. Current architecture, README and
  agent guidance updated; browser harness channel assertion synchronized with
  completed execution and made aware of both markers for one selected job.
  No projection, renderer, graph or package changes.
- Failing-test evidence: `WorkflowRegistration_IncludesModuleOwnedSimulationLifecycle`
  failed against the original module because the lifecycle registration was absent.
  Scheduler tests were written before its implementation. No existing tests removed.
- Staged review: configuration checked first; module/contracts and product
  libraries remain netstandard2.0/C# 9; host/tests remain net8.0/C# 12. Reviewed
  lifecycle locks, tracked worker tasks, exception observation, finite bounds,
  conservative terminal cleanup, observer ordering, detached snapshots and DI
  disposal order. No critical, design or maintainability blockers found. Optional
  future lifecycle/graph identity work remains at its existing checkpoints.
- Commit subject: `feat(simulation): add bounded timer-driven scenario delivery`.
- Commit hash: record in the next checkpoint; Git history and this thread's
  handoff identify the completed commit.
- Next eligible item: P03; not started here.

### Validation evidence

Windows PowerShell 5.1, .NET SDK 10.0.401, Debug. Local logs/TRX/browser artifacts
are under workspace `out/simulation-p02/`, not committed.

Source-mode flags used below:
`-m:1 -nr:false -p:SolutionFileName=WorkspaceTplQueue.sln -p:SolutionDir=C:/Users/Fernando/source/fmacias/WorkspaceTplQueue/ -p:SkipPackLocal=true`.
The test project is
`test/integration/Fmacias.TplQueue.Usage.Integration.Test/Fmacias.TplQueue.Usage.Integration.Test.csproj`.

| Working directory | Command/check | Result |
| --- | --- | --- |
| Workspace root | `dotnet test TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-mode-flags> --filter FullyQualifiedName~SimulationRegistrationTests --logger 'trx;LogFileName=p02-red.trx' --results-directory out/simulation-p02` | Exit 1: one expected assertion failure before implementation. |
| Workspace root | `dotnet build TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-mode-flags>` | Exit 0: affected projects build; seven existing test warnings, zero errors. |
| Usage | `dotnet test <test-project> --no-build --no-restore --configuration Debug --filter 'FullyQualifiedName~FiniteScenarioDeliveryTests\|FullyQualifiedName~SimulationRegistrationTests\|FullyQualifiedName~SimulationHostedServiceTests\|FullyQualifiedName~LegacyMeasurementScenarioTests' --logger 'trx;LogFileName=p02-focused.trx' --results-directory ../out/simulation-p02` | Exit 0: 27 focused tests pass. The filter's vertical bars are literal OR separators, without backslashes when invoking PowerShell. |
| Usage | `dotnet test <test-project> --no-build --no-restore --configuration Debug --logger 'trx;LogFileName=p02-integration.trx' --results-directory ../out/simulation-p02` | Sandboxed run: 139 pass, separate SignalR cancellation test times out. A sandboxed focused retry also fails. The same focused test outside the sandbox passes in one second (`p02-cancellation-unrestricted.trx`). |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug -RunTests` | Outside sandbox: exit 0; build zero warnings/errors; all 653 tests pass (Core 328, Usage 140, facade 64, RetryPolicies 57, Cache.Abstract 38, DI 17, Observers 6, MemCache 3). |
| Usage | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` | Sandbox restore stops early; retry outside sandbox builds contracts/module/consoles, then reproduces P01's CS0246 `IJobExecutionEvent` package mismatch in SignalR/Core. |
| Usage | `dotnet build samples/TplQueue.Sample.BlazorSignalR/TplQueue.Sample.BlazorSignalR.csproj --configuration Debug --no-restore <source-mode-flags>` | Exit 0, zero warnings/errors; restores the source-mode host output after package validation. An earlier attempt encountered validation-host DLL locks; those hosts were stopped before the successful retry. |

Tests cover finite/exact batch counts, unique root IDs, offset/interval forwarding,
invalid settings and duplicate IDs, busy/admission skips, terminal capacity release,
submission failure recovery, cancellation, stop racing an in-flight batch, late
callbacks/disposal and invalid lifecycle transitions. Manual ticks and synchronization
gates cover decisions; a real timer covers delivery. Real FIFO/Parallel/Cache
composition verifies six terminal roots, eighteen jobs and runtime admission cleanup,
including cache rehydration. The host test verifies all observers are attached
before Start and remain attached through Stop. Existing HTTP tests pass in source
and output launch modes.

Browser acceptance used the existing Debug harness and headless Microsoft Edge.
The demo barrier server was launched with
`& 'C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Microsoft/VisualStudio/NodeJs/node.exe' demo/server.mjs`
from `tools/TplQueue.JobMonitor`. The Development host was launched with
`$env:ASPNETCORE_ENVIRONMENT = 'Development'; dotnet run --no-build --no-launch-profile --project TplQueue.Sample.BlazorSignalR.csproj --urls http://127.0.0.1:5183`
from the sample directory. Edge used `--headless --disable-gpu --no-first-run
--user-data-dir=<workspace>/out/simulation-p02/edge-development --dump-dom
http://127.0.0.1:5183/job-monitor/tests/blazor.html?automation=1`.
Result: **5 passed, 0 failed**: real circuit snapshots, channels/selection,
idle behavior, navigation/disposal/reconnect and retained enqueue relations.
The initial Production source launch passed four checks but failed the error-banner
style check; the Development launch loads the scoped CSS and passes all five.
An additional fresh-host attempt encountered a redirected-output handle race and
then a missing generated Debug harness (404); these were validation setup failures.
The fresh-start check then exposed the old assertion's assumptions about immediate
channel readiness and the first selected marker. It now waits for completed queue
counts and checks the selected runtime marker alongside retained enqueue history.
`node --check tools/TplQueue.JobMonitor/tests/blazor.js` passes (using the Node
executable above). After the host asset-sync build, the final
`powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-p02/browser-live.ps1`
run from the workspace root passes **5/5**, with the browser launched 0.754 seconds
after a fresh host process. That local script starts/checks the required barrier
server, uses port 5184 and a fresh Edge profile, captures the result, and cleans up
its host. Live arrivals, eventual completion and navigation retaining eighteen jobs
were exercised; no renderer changes were necessary.

Packaging is not applicable: no product package output changed and Usage has no
pack-local script. Standalone `test.ps1` and `coverage.ps1 -EnforceBaseline` were
not rerun after their build prerequisite failed; no package/coverage success is
claimed. Node model/layout and standalone browser checks were not applicable:
the renderer and production component assets are unchanged. Full workspace source success
does not establish standalone package consumption.

### Preservation and limitations

Only Usage receives task changes. Core and WorkspaceTplQueue staged diffs match
their saved pre-task hashes. Usage's existing decision-table formatting stays
unstaged: the task stages a plan blob derived from HEAD with only P02 semantic
updates. Public documentation ownership and the known standalone SignalR source
reference contradiction from P00/P01 remain unchanged.
Final checks: all 70 local documentation links/anchors resolve, the staged
whitespace check passes, and the only unstaged Usage diff is the original table
formatting. The staged review includes the browser harness readiness correction.

Observer terminal delivery is asynchronous/best-effort; a missing terminal event
can retain an admission slot and cause bounded skipped ticks. It cannot admit
extra roots. Cache remains process-local, not restart-durable. Delivery snapshots
do not fix monitor graph membership before success; P03 owns that work. No P03
or use-case implementation, product dependency fix, push or packaging is included.

### Handoff

Next eligible task:
`C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\p03-establish-graph-identity-for-all-outcomes.md`.

```text
Execute only the task in:
C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\p03-establish-graph-identity-for-all-outcomes.md

Follow its completion and commit instructions. Preserve unrelated staged and
unstaged changes. Read P02's execution record and P01's validation limitations.
Keep the renamed Simulation module and unchanged Etl.Contracts project.
Stop after providing the handoff for the next thread. Do not push commits.
```
