# UC01 - Single job on each queue

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

No additional use-case prerequisite.

Start with the decisions and baseline from [P00](p00-record-decisions-and-establish-baseline.md).
Use [P02](p02-add-finite-timer-driven-delivery-and-minimal-contracts.md) for recurring delivery and [P03](p03-establish-graph-identity-for-all-outcomes.md) before claiming
complete graph identification. P01 may be deferred.

## Scope and acceptance

- [x] Implement and validate. Submit one independent root separately to FIFO,
  Parallel and Cache. Verify one execution per job and the expected terminal outcome.
- Check enqueue history in Unassigned, actual channel/start placement, and one job
  count despite two position markers. Inspect channel reuse after completion.

## Suggested commit

`feat(simulation): add single-job queue scenarios`

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

- Item: UC01 - Single job on each queue; implementation validated 2026-09-25,
  final browser verification and commit checkpoint 2026-09-28.
- Baseline: Usage `8a8c032` (P03), WorkspaceTplQueue `2df5774`, Core `5440d20`,
  Adapter `74ca4b9`, Abstractions `2c4e4f0`. Read P03's execution record and
  P01/P02's validation limitations. Source validation includes the pre-existing
  staged workspace reference switch and Core channel changes.
- Decisions/settings: opt-in `single-job` host profile, preserving default ETL.
  One independent ingest root per tick on each of FIFO, Parallel and Cache;
  one-second startup offset, three-second interval, two ticks, maximum one
  active root per scenario. Six roots and six unique jobs in the preset.
  Each root has a fresh job/operation ID and no composed dependencies.
  Existing ingest handler, deterministic measurements and 500 ms delay; expected
  outcome is success. Existing cancellation, admission and finite stop rules apply.
- Changed behavior/files: additive scenario-kind contract and constructor overload
  in the unchanged Etl.Contracts project; module-owned SingleJobScenario and preset;
  finite service dispatch by kind; configuration-only host profile selection;
  focused NUnit fixtures, parameterized Debug browser harness, maintained docs.
  No new payload type, handler registration policy, renderer or scheduler change.
- Failing-test evidence: `WorkflowRegistration_IncludesSingleJobScenario` failed
  before implementation (`uc01-red.trx`). Build then unit/integration checks passed
  after implementation and fixture corrections. No existing tests removed.
- Counts/reuse: each real-queue case first verifies one observed root/job with no
  prerequisites, then submits two more separate roots after terminal observation
  and queue drain. FIFO uses channel 0; with Parallel/Cache capacity two, the third
  root reuses the first channel. A counting decorator calls the real ingest handler
  and proves exactly one invocation per job, including actual cache hydration.
  Tests verify normalized business data, terminal success, unique IDs, root lists,
  null-channel enqueue events and exact event-owned enqueue/Started timestamps.
- Compatibility: Simulation/contracts and product libraries retain netstandard2.0
  and C# 9; host/tests retain net8.0 and C# 12. The original settings constructor
  selects ETL. No package dependencies or public product APIs changed.
- Commit subject: `feat(simulation): add single-job queue scenarios`.
- Commit hash: record in the next checkpoint; Git history and the thread handoff
  identify this commit until then. A commit cannot contain its own final hash.
- Next eligible task: UC02; not started here.

### Validation evidence

Windows PowerShell 5.1, .NET SDK 10.0.401, Debug. Supplemental logs, TRX and browser
artifacts are under workspace `out/simulation-uc01/` (not committed).
The test project is
`test/integration/Fmacias.TplQueue.Usage.Integration.Test/Fmacias.TplQueue.Usage.Integration.Test.csproj`.
Source flags are
`-m:1 -nr:false -p:SolutionFileName=WorkspaceTplQueue.sln -p:SolutionDir=C:/Users/Fernando/source/fmacias/WorkspaceTplQueue/ -p:SkipPackLocal=true`.

| Working directory | Command/check | Result |
| --- | --- | --- |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` | Restored source assets before any source validation. Sandboxed build exited 1 with no compiler diagnostics; unrestricted retry passed with seven existing test warnings and no errors. |
| Workspace root | `dotnet test TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-flags> --filter FullyQualifiedName~WorkflowRegistration_IncludesSingleJobScenario --logger 'trx;LogFileName=uc01-red.trx' --results-directory out/simulation-uc01` | One expected assertion failure before implementation. |
| Workspace root | `dotnet build TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-flags>` | Affected build passes. Initial fixture referenced unavailable Moq; replaced with a small counting decorator, adding no dependency. |
| Workspace root | `dotnet test TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-flags> --filter 'FullyQualifiedName~SingleJobSimulationTests\|FullyQualifiedName~SimulationRegistrationTests\|FullyQualifiedName~FiniteScenarioDeliveryTests' --logger 'trx;LogFileName=uc01-focused.trx' --results-directory out/simulation-uc01` | 29 pass. Use literal OR separators without backslashes in PowerShell. Initial fixture double-registered the ingest handler; corrected to register the counting handler once. |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug -RunTests` | Unrestricted source build passes with zero warnings/errors; all 676 tests pass: Usage 163, Core 328, facade 64, RetryPolicies 57, Cache.Abstract 38, DI 17, Observers 6, MemCache 3. |
| Workspace root | `powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-uc01/browser-live.ps1` | Fresh Development host, real Blazor circuit: 6 passed, 0 failed. Browser launched 0.654 seconds after host creation. |
| Workspace root | `powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-uc01/browser-default.ps1` | Final run on 2026-09-28: 5 pass, 0 fail. Initial probe had 4 passes and one assertion failure on a valid assigned collision-group title; corrected the assertion to accept individual and grouped channel labels. Two earlier reruns could not execute because approval review disconnected. A later attempt encountered missing generated Debug assets; after the workspace rebuild below, all checks passed. |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` | Final restore/build on 2026-09-28 regenerated Debug harness assets; passes with seven existing test warnings and no errors (`final-restore-build.log`). |
| Workspace root | `dotnet build TplQueue.Usage/samples/TplQueue.Sample.BlazorSignalR/TplQueue.Sample.BlazorSignalR.csproj --configuration Debug --no-restore <source-flags>` | Final Debug asset-sync build passes after the test-only group-label correction. |
| Usage | `node --check tools/TplQueue.JobMonitor/tests/blazor.js` | Pass using the installed Visual Studio Node executable. |

The single-job host command (sample working directory) is
`dotnet bin/Debug/net8.0/TplQueue.Sample.BlazorSignalR.dll --urls http://127.0.0.1:5184 --Simulation:Profile=single-job --TplQueue:Queues:ParallelQ:MaxParallelism=2 --TplQueue:Queues:CacheQ:MaxParallelism=2`,
with `ASPNETCORE_ENVIRONMENT=Development`. The harness URL is
`http://127.0.0.1:5184/job-monitor/tests/blazor.html?automation=1&profile=single-job`.
Headless Edge uses `--disable-gpu --no-first-run --user-data-dir=<unique workspace
out profile> --dump-dom`; the loopback barrier is the existing Node demo server at
port 4178. Node executable:
`C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Microsoft/VisualStudio/NodeJs/node.exe`.
The local scripts clean up the hosts they start.

Browser assertions inspect all six roots: each retains an Unassigned enqueue
marker and an assigned Started marker, both selecting the same root; the count
remains six. Existing channel selection, idle snapshot behavior, navigation and
wrapper disposal/reconnect checks pass. These are interactive circuit results,
separate from the HTTP default-host checks in the source suite.
The final group-label assertion passes syntax checking and the final interactive
default-profile rerun. Earlier approval failures were disconnected review streams,
not determinations that running the local browser was unsafe; the verification gap
is resolved. The final default-profile browser launched 1.394 seconds after host
creation and passed all five checks.

### Staged review and preservation

Configuration was checked before staged review: target frameworks and language
versions remain compatible. Reviewed all staged implementation, contract, test
and documentation changes, including factory submission, cache hydration identity,
timer dispatch, DI ownership, cancellation and the passive host boundary.
No critical/design/maintainability blockers in UC01's validated configuration.
The capacity-one Cache finding remains explicit below. The default-browser
revalidation gap is resolved. Later scenario and retention work remains outside scope.

`powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-uc01/validate-task.ps1`
passes: 77 local documentation links/anchors resolve; all four siblings' staged
and unstaged diffs match their pre-task patches byte for byte. Usage's main plan
is staged from a HEAD-derived blob containing only its UC01 status and checkbox;
the original table spacing/separator formatting remains unstaged.
`git diff --cached --check` passes. No pre-existing tests or unrelated changes
are removed or included in the task commit.

### Limitations and scope

A diagnostic capacity-one Cache run stalled before its first job observation;
the real-queue fixture timed out after 15 seconds and the browser reported zero
Cache jobs while FIFO/Parallel completed. Capacity two passes. Static inspection
suggests the idle underlying dispatcher reserves its sole semaphore permit before
waiting for a queued job, while cache leasing requires a free permit. This is a
dependency investigation, not a proven fix, and no Core change is included.
UC01 validates the documented two-channel Cache configuration; it does not claim
capacity-one Cache support. Initial two-arrival assertions also assumed immediate
channel reuse; the corrected three-arrival test respects channel rotation.

Packaging is not applicable: no product package output changed and Usage has no
pack-local script. Standalone Usage `build.ps1`, `test.ps1` and
`coverage.ps1 -EnforceBaseline` were not rerun: P01-P03 document the unchanged
SignalR/Core CS0246 mismatch with the older package `IJobExecutionEvent` contract.
No package or coverage success is claimed. Restore assets remain in source mode.
Node model/layout and standalone browser suites were not rerun because production
monitor code is unchanged; the affected Debug circuit harness was validated.

Only Usage receives task changes. Public documentation ownership, publishing and
the recorded standalone-reference contradiction remain unchanged. The default ETL
scenario is preserved; UC02 and later use cases, broader queue fixes, continuous
retention and package upgrades are outside this task. The finite ingest data and
graph catalog remain process-local. Source suite success depends on the preserved
staged sibling baseline and does not establish standalone package consumption.

### Handoff

Next eligible task:
`C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\uc02-sequential-etl-chain.md`.

```text
Execute only the task in:
C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\uc02-sequential-etl-chain.md

Follow its completion and commit instructions. Preserve unrelated staged and
unstaged changes. Read UC01's execution record, P03's graph identity contract and
P01/P02's validation limitations. Restore/build through WorkspaceTplQueue before
source validation. Keep the default ETL and opt-in single-job profiles separate;
use the documented Cache capacity for acceptance and retain the capacity-one
limitation. Stop after providing the handoff for the next thread. Do not push commits.
```
