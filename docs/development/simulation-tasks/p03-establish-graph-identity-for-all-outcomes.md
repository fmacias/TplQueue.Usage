# P03 - Establish graph identity for all outcomes

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

[P00](p00-record-decisions-and-establish-baseline.md). Coordinate with [P02](p02-add-finite-timer-driven-delivery-and-minimal-contracts.md) when run identity needs a minimal contract.

## Scope

Determine the smallest
  reliable path for run/root identity before terminal success, preserving real
  event-owned lifecycle facts. Test running, failed and cancelled graph selection.
  Resolve shared membership before UC12 without inventing execution channels.
  Suggested commit: `fix(monitor): preserve simulation graph identity across outcomes`.

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

- Item: P03 - Establish graph identity for all outcomes.
- Status: complete, 2026-09-24.
- Baseline: Usage `1b07116` (P02), WorkspaceTplQueue `2df5774` (P01), Core
  `5440d20`, Adapter `74ca4b9`, Abstractions `2c4e4f0`. Read P02's execution
  record and P01's validation limitations. Source validation includes the
  pre-existing staged workspace reference switch and Core channel changes.
- Decisions: root ID identifies the current one-root simulation run. Capture
  composition membership before enqueue; retain all root IDs for shared jobs,
  one global job identity and all observed dependency edges. The compatible
  singular root is null for shared non-roots, self for roots, otherwise the
  sole known root. Selection highlights the connected dependency component.
  FIFO ordering edges do not change composed run memberships.
- Settings/counts: the default delivery stays six three-job roots/eighteen jobs,
  two ticks per queue, one root per tick, one-second offset and three-second
  interval. No use case, new delivery profile or graph-size limit implemented.
  New controlled integration fixtures use one two-job graph per queue/outcome.
  The shared selection fixture has two roots and one prerequisite (three unique
  connected jobs), plus one unrelated job to verify focus isolation.
- Changed behavior/files: additive `ISimulationGraphCatalog` in unchanged
  Etl.Contracts; internal ID-only catalog and pre-enqueue registration in
  Simulation; projection snapshot join, additive `rootJobIds` DTO/model/selection
  field; focused tests and browser assertions; maintained documentation.
  Event-owned queue, lifecycle, timestamps, dependencies and channel facts remain
  authoritative. Registered membership creates no synthetic job observations.
- Failing evidence: the new registration test failed before implementation
  (`p03-red.trx`). Both new Node membership tests failed because normalization
  discarded root lists; the existing 24 tests passed. Catalog/projection tests
  were written before their implementation. No tests removed.
- Compatibility: sample module/contracts and product libraries remain
  netstandard2.0/C# 9; host/tests net8.0/C# 12. No product API, package version,
  renderer, scheduling, handler, cancellation or publishing change.
- Commit subject: `fix(monitor): preserve simulation graph identity across outcomes`.
- Commit hash: record in the next checkpoint; Git history and this thread's final
  handoff identify it. A commit cannot contain its own final hash.
- Next eligible item: UC01; not started here.

### Validation evidence

Windows PowerShell 5.1, .NET SDK 10.0.401, Debug. Logs/TRX/headless-browser
artifacts live under workspace `out/simulation-p03/` and are not committed.
The test project below is
`test/integration/Fmacias.TplQueue.Usage.Integration.Test/Fmacias.TplQueue.Usage.Integration.Test.csproj`.
Source flags are
`-m:1 -nr:false -p:SolutionFileName=WorkspaceTplQueue.sln -p:SolutionDir=C:/Users/Fernando/source/fmacias/WorkspaceTplQueue/ -p:SkipPackLocal=true`.

| Working directory | Command/check | Result |
| --- | --- | --- |
| Workspace root | `dotnet test TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-flags> --filter FullyQualifiedName~WorkflowRegistration_IncludesGraphMembershipBeforeAnyOutcome --logger 'trx;LogFileName=p03-red.trx' --results-directory out/simulation-p03` | Expected assertion failure before implementation. |
| Workspace root | `dotnet build TplQueue.Usage/<test-project> --configuration Debug --no-restore <source-flags>` | Pass; zero errors, seven existing test warnings. Repeated after adding real-queue integration fixtures. |
| Workspace root | `dotnet test TplQueue.Usage/<test-project> --no-build --no-restore --configuration Debug --filter 'FullyQualifiedName~SimulationGraphIntegrationTests\|FullyQualifiedName~EtlExecutionProjectionStoreTests\|FullyQualifiedName~SimulationGraphCatalogTests\|FullyQualifiedName~SimulationRegistrationTests\|FullyQualifiedName~EtlWorkflowSampleTests\|FullyQualifiedName~EtlQueueRuntimeCancellationCleanupTests' --logger 'trx;LogFileName=p03-focused.trx' --results-directory out/simulation-p03` | 58 pass. Use literal OR separators without backslashes in PowerShell. |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug -RunTests` | Pass outside sandbox; build zero warnings/errors; 670 tests pass: Core 328, Usage 157, facade 64, RetryPolicies 57, Cache.Abstract 38, DI 17, Observers 6, MemCache 3. |
| tools/TplQueue.JobMonitor | `node scripts/check.mjs`; `node --test tests/layout.test.js` | 15 modules checked; 26 model/layout tests pass. Initial sandbox Node worker launch failed with EPERM; unrestricted runs produced the expected red results and then green results. |
| Workspace root | `powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-p03/browser-live.ps1` | Fresh Development host, real Blazor circuit: 5 pass, 0 fail. Browser launched 0.665 seconds after host creation; selection carries the real root list. |
| Workspace root | `powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-p03/browser-standalone.ps1` | Standalone headless Edge: 25 pass, 0 fail, including running/failed/cancelled shared graph selection, full membership callbacks and unrelated-job dimming. |
| Usage | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` | Existing standalone SignalR/Core CS0246 for missing package `IJobExecutionEvent`, as in P01/P02. |
| Usage | `dotnet build samples/TplQueue.Sample.BlazorSignalR/TplQueue.Sample.BlazorSignalR.csproj --configuration Debug --no-restore <source-flags>` | Post-package restore assets still reference the older package; CS0246 repeats. This no-restore command cannot switch those restore assets back. |

Node executable used:
`C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Microsoft/VisualStudio/NodeJs/node.exe`.
Both browser scripts start/check the loopback demo barrier at port 4178 and use
headless Microsoft Edge with `--disable-gpu --no-first-run --user-data-dir=<unique
workspace out profile> --dump-dom`. Standalone URL:
`http://127.0.0.1:4178/tests/browser.html?automation=1`; Blazor URL:
`http://127.0.0.1:5184/job-monitor/tests/blazor.html?automation=1`.
The host launches from its sample directory with Development environment and
`dotnet bin/Debug/net8.0/TplQueue.Sample.BlazorSignalR.dll --urls http://127.0.0.1:5184`.
Scripts clean up the hosts they start. HTTP checks are included in the source
suite; interactive acceptance comes from the browser checks above.

Tests cover detached/immutable memberships, duplicate and concurrent registrations,
cycle-safe catalog traversal, missing membership, malformed browser lists,
legacy singular fallback, late/duplicate lifecycle behavior, shared jobs across
queue metadata, pre-success identity and no fabricated lifecycle/channel data.
Nine gated real-queue cases exercise running followed by success, failure or
cancellation on FIFO, Parallel and Cache (including real hydration). The gates
avoid sleep-based outcome assertions. Submission failure retains composition
identity without retaining cancellation state. Existing tests remain valid.

Packaging is not applicable: Usage has no pack-local script and no product
package output changed. Standalone `test.ps1` and `coverage.ps1 -EnforceBaseline`
were not rerun after their build prerequisite failed; no package/coverage success
is claimed. The passing source suite does not establish standalone consumption.

### Staged review and preservation

Configuration checked before review. Review covers catalog locking and ID-only
retention, registration before observer publication, finite retention scope,
cache identity, legacy fallback, detached membership lists, actual dependency
traversal and unchanged channel/lifecycle handling. No critical, design or
maintainability blockers found. Test gaps are limited to later scenario/retention
tasks; no broad refactor or optional feature added.

Only Usage receives task changes. The main plan is staged from a HEAD-derived
blob containing only P03 semantic updates; the original decision-table formatting
remains unstaged. Saved pre-task staged/unstaged diffs are used to verify sibling
repositories remain unchanged. Public documentation ownership and the existing
standalone SignalR source-reference contradiction remain unchanged.

Final local check:
`powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-p03/validate-docs.ps1`
passes: 94 local documentation links/anchors resolve; all four sibling repositories'
original staged/unstaged diffs match; Usage retains exactly the original table
spacing and separator formatting outside the staged semantic update.
`git diff --cached --check` passes. The staged implementation and test diffs were
reviewed after configuration verification; no remaining review findings.

### Remaining limitations

The catalog is finite, process-local composition metadata. It retains membership
after terminal events and enqueue exceptions; that does not establish acceptance
or durability. A later observed event triggers the normal snapshot refresh;
registration adds no polling or synthetic lifecycle notification. Missing job
observations leave dependency endpoints unresolved. Legacy producers without the
catalog still discover roots only through the prior success fallback. Continuous
retention and future multi-root scenario grouping remain deferred.

After the package check changed ignored restore assets, an optional
`WorkspaceTplQueue/build.ps1 -Configuration Debug` restore/rebuild was attempted
twice. Automatic approval review disconnected before completing either review;
neither command executed, and this was not a safety rejection. The next source
launch should first run that workspace build with restore. The earlier full
source suite and both browser acceptances passed against the implemented code.

### Handoff

Next eligible task:
`C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\uc01-single-job-on-each-queue.md`.

```text
Execute only the task in:
C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\uc01-single-job-on-each-queue.md

Follow its completion and commit instructions. Preserve unrelated staged and
unstaged changes. Read P03's execution record and P01/P02's validation limitations.
Keep the Simulation implementation and unchanged Etl.Contracts project.
Restore/build through WorkspaceTplQueue before source validation; the last
standalone package check changed ignored restore assets.
Stop after providing the handoff for the next thread. Do not push commits.
```
