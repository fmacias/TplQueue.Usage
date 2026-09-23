# P01 - Isolate the module rename

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

[P00](p00-record-decisions-and-establish-baseline.md). The rename may be deferred; scenario work can use the current module name.

## Scope

When implementing the intended rename,
  update project/namespace references, solutions, scripts, test links and affected
  documentation together. Preserve behavior and verify old references deliberately
  retained for history. Inventory workspace consumers before touching them; apply
  their instructions if they are affected. Do not infer a contracts-project rename.
  Suggested commit: `refactor(samples): rename ETL implementation to simulation`.

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

- Item: P01 - Isolate the module rename.
- Status: complete, 2026-09-24.
- Baseline commits: Usage `44e1d18` and WorkspaceTplQueue
  `ab8e0a4a280c920c3fb7f618023c7b2d17780b1c`. P00's decision/baseline commit
  is `6a0744e`. Validation includes the pre-existing staged workspace reference
  switch and Core execution-channel changes recorded by P00.
- Decisions confirmed: rename only the implementation project, assembly and
  namespaces to `TplQueue.Sample.Simulation`. Keep
  `TplQueue.Sample.Etl.Contracts`, `IEtlWorkflow`, ETL class/method names and all
  existing behavior. No timer, scenario or graph-limit implementation in P01.
- Scenario settings and expected counts: unchanged startup workload, two
  three-job roots on each of FIFO, Parallel and Cache; six roots, eighteen
  unique jobs, Ingest -> Transform -> Load, delays 500/700/400 ms.
- Changed behavior and files: module directory/project/namespaces, host and test
  references/imports, both solutions, Usage build/test scripts and current
  documentation. Assembly-qualified payload names now use Simulation; the
  process-local CacheQ has no persisted-data migration requirement. Consumers
  of implementation namespaces need updated references and a rebuild.
- Failing-test evidence before implementation: adapted the existing cache
  round-trip test to cover full and assembly-qualified payload names and verify
  the implementation/contract boundary. Both cases failed on the old assembly
  and payload namespace, then passed in the full source suite. No test removed.
- Build / unit / pack / integration commands and results: below.
- Browser checks: existing HTTP host tests passed, including source/output
  launches. Interactive browser checks were not run: no UI or lifecycle behavior
  changes. HTTP checks are not interactive coverage.
- Staged review: compatibility checked first; module/contracts and product
  libraries remain netstandard2.0/C# 9, Usage hosts/tests net8.0/C# 12.
  Rename-only implementation diff preserves handlers, queue/cancellation logic,
  DI registrations and serialization allowlist logic. Tests cover renamed payload
  hydration and unchanged contract assembly. No critical/design/maintainability
  findings; package and browser limitations remain explicit below.
- Commit subjects: `refactor(samples): rename ETL implementation to simulation`
  (Usage), `build(samples): align workspace with simulation module rename`
  (WorkspaceTplQueue).
- Commit hashes: record in the next checkpoint update; Git history and this
  thread's final handoff identify the commits until then.
- Next eligible item: P02; not started here.

### Consumer inventory and preservation

Workspace-wide search covered maintained and hidden text excluding Git internals,
build outputs, test results and local validation logs. Active implementation
consumers are the Usage Blazor host, Usage integration tests, Usage build/test
scripts and the Usage/WorkspaceTplQueue solutions. Test-linked host sources use
the unchanged contracts; their paths remain valid. No other product repository
references the implementation name. Root instructions/READMEs were read for both
affected repositories and their build/publication boundaries remain unchanged.

The workspace module entry was part of pre-existing staged solution additions,
absent from HEAD. Only that module entry and its twelve existing build mappings
are incorporated into the task commit. Other staged solution entries, reference
switching and documentation remain staged. Workspace README/instructions/local
pack guide receive only the rename boundary note; the previously staged general
instructions remain separate. Usage's unrelated decision-table formatting stays
unstaged. Core's staged work is untouched.

Old implementation references deliberately retained:

- `CurrentIssue.md`: historical original ETL implementation record, now explicitly
  linked to the maintained architecture guide.
- `docs/architecture/blazor-signalr-current-state.md`: already labeled historical
  ScatterChart snapshot.
- Main plan/current architecture/P01: explanatory old-to-new naming history.
- Workspace-root `BlazorSampleApplication.md` and `.obsidian/workspace.json`:
  personal historical sketch/editor state, not maintained build consumers.
- `TplQueue.Sample.Etl.Contracts` references: current contracts, not stale names.

P00's contradiction about standalone package-only wording versus the SignalR
sample's source references is retained, not normalized. Historical renderer and
test-count claims are likewise not rewritten as current behavior. Product docs
still publish from Adapter language trees; no site sync or package IDs change.

### Validation evidence

Windows PowerShell 5.1, .NET SDK 10.0.401, Debug configuration. Local logs and the
red-test TRX are under workspace `out/simulation-p01/` (not committed).

| Working directory | Command | Result |
| --- | --- | --- |
| Workspace root | `dotnet test TplQueue.Usage/test/integration/Fmacias.TplQueue.Usage.Integration.Test/Fmacias.TplQueue.Usage.Integration.Test.csproj --configuration Debug --no-restore -m:1 -nr:false -p:SolutionFileName=WorkspaceTplQueue.sln -p:SolutionDir=C:/Users/Fernando/source/fmacias/WorkspaceTplQueue/ -p:SkipPackLocal=true --filter FullyQualifiedName~CachePayloadGraph_RoundTripsThroughRegisteredResolverAndSerializer --logger 'trx;LogFileName=p01-red.trx' --results-directory out/simulation-p01` | Before rename: exit 1, both cases fail on old assembly/namespace as expected. Initial sandbox attempt without `-m:1 -nr:false` stalled without output; interrupted and retried outside the sandbox. |
| WorkspaceTplQueue | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug -RunTests` | Exit 0, build succeeds with 7 existing warnings and 0 errors. All 629 tests pass: Core 328, Usage 116, facade 64, RetryPolicies 57, Cache.Abstract 38, DI 17, Observers 6, MemCache 3. Source mode, not package proof. |
| Usage | `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Configuration Debug` | Exit 1 at the existing SignalR/Core CS0246 for `IJobExecutionEvent`. Contracts, renamed Simulation, smoke console and observer console build first. Confirms updated script path; the same P00 package-reference mismatch remains. |

The workspace script builds first and then runs unit/integration assemblies
together with `dotnet test --no-build`. No product package outputs changed;
packaging is not applicable and Usage has no pack-local script. Standalone
`test.ps1` and `coverage.ps1 -EnforceBaseline` were not rerun because their
standalone build prerequisite still fails; no package coverage success claimed.
Node/browser checks were not applicable to this rename. No dependency baseline
fixes or product package rebuilds were included.

`powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-p01/validate-task.ps1`
from the workspace root passed: all 12 renamed files match their originals except
the implementation name; 78 local documentation links/anchors and 47 project,
test-link and solution paths resolve. Both isolated staged whitespace checks pass;
the three original normal indexes were unchanged before committing. Reference
audit and isolated task diffs were reviewed. Contracts files are unchanged.
Remaining limitation: the passing full source suite
uses the existing staged sibling changes, so the task commits alone do not capture
the complete source-validation baseline.

### Handoff

Next eligible task:
`C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\p02-add-finite-timer-driven-delivery-and-minimal-contracts.md`.

```text
Execute only the task in:
C:\Users\Fernando\source\fmacias\TplQueue.Usage\docs\development\simulation-tasks\p02-add-finite-timer-driven-delivery-and-minimal-contracts.md

Follow its completion and commit instructions. Preserve unrelated staged and
unstaged changes. Use the renamed TplQueue.Sample.Simulation implementation and
unchanged TplQueue.Sample.Etl.Contracts. Read P01's validation limitations.
Stop after providing the handoff for the next thread. Do not push commits.
```
