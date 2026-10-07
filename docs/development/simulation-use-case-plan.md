# Simulation use-case implementation plan

Created: 2026-09-22. Status: P00-P03 and UC01 complete; UC02-UC25 pending.

## Purpose and ownership

Turn the finite ETL demonstration into a configurable, timer-driven simulation that
exercises real TplQueue execution and the Blazor job monitor. Implement and validate
one bounded use case at a time, with a reviewable commit and a recorded checkpoint
before the next iteration.

The implementation belongs in `samples/TplQueue.Sample.Simulation`, renamed from
`TplQueue.Sample.Etl` in P01. Add contracts to `samples/TplQueue.Sample.Etl.Contracts`
when required. Renaming the contracts project has not been decided.

Blazor owns host composition, observer projection and presentation. Domain owns
graph construction, payloads, handlers and queue/cache wrappers. Simulation owns
measurements, workflow scheduling, admission and runtime access through Contracts.
Browser connections must not create or restart workloads.

This plan and its linked task files track proposed work and acceptance evidence. The maintained
[Blazor architecture guide](../architecture/blazor-consumer-sample.md) remains the
source for current architecture, contract ownership and rendering rules. Update that
guide when implemented behavior changes; do not treat this plan as a second current
architecture specification. Public product documentation and publishing boundaries
remain unchanged.

## Current package migration (2026-10-06)

The current implementation consumes TplQueue `0.2.0-preview.2` packages and keeps
Domain inside Usage. ETL and SingleJob run continuously with one timer per workflow;
the host registers both and uses fixed timing. Finite settings and profile selectors
have been removed. P00-P03/UC01 records retain their historical completion evidence;
they are not launch instructions or fresh acceptance evidence for this migration.
The broader UC23/UC25 lifecycle and retention work remains pending.

## Observed baseline (historical, before P02)

- The host attaches observers, waits one second, and submits two three-job roots
  to each of FIFO, Parallel and Cache: six roots and eighteen jobs, once at startup.
- The ETL graph is Ingest -> Transform -> Load. Load is the final root; "root"
  does not mean the first execution step. Current handler delays are 500, 700 and
  400 milliseconds respectively.
- `IEtlWorkflow` exposes enqueue and root cancellation. Each current graph is
  submitted to one selected queue; the sample has no cross-queue scenario factory.
- The inspected runtime has a dependency traversal depth guard of 512. This is
  not a total-jobs-per-root limit and is not a recommended demonstration size.
  Recheck the active runtime version when implementing boundary tests.
- Cross-queue ownership follows first enqueue of a shared job instance. Later
  queues may reference that instance without executing it again. Queue ownership
  is observed runtime state, not a scenario-assigned `CrossQueueId` value.
- The projection currently establishes root membership on `RootSuccessed` and
  exposes one nullable root ID per job. Running, failed and cancelled graphs, and
  dependencies shared by several roots, need explicit acceptance coverage.
- Projection jobs and event fingerprints accumulate without eviction. Continuous
  operation therefore requires retention and admission bounds.
- CacheQ uses process-local memory; it is not a durable restart/recovery store.
- Dashboard snapshots represent accumulated state. Coalescing means a fast job
  need not visibly display every intermediate event.

Relevant maintained sources:

- [Hosted workload](../../samples/TplQueue.Sample.BlazorSignalR/Application/SampleEtlDemoHostedService.cs)
- [ETL workflow](../../samples/TplQueue.Sample.Simulation/Workflows/Etl/EtlSimulationWorkflow.cs)
- [Workflow contract](../../samples/TplQueue.Sample.Etl.Contracts/ISimulationWorkflow.cs)
- [Queue runtime](../../samples/TplQueue.Sample.Simulation/Execution/EtlQueueRuntime.cs)
- [Projection](../../samples/TplQueue.Sample.BlazorSignalR/Presentation/Etl/EtlExecutionProjectionStore.cs)
- [Monitor behavior and bounds](../../tools/TplQueue.JobMonitor/README.md)

## Decisions to settle before dependent implementation

The normal graph limits below were confirmed by the human during P00 on 2026-09-23.
They are scenario limits, not measured performance limits. Other proposals remain
deferred until their dependent task; do not treat them as accepted defaults.

| Decision                   | Proposal                                                                                            | Status                                   |
| -------------------------- | --------------------------------------------------------------------------------------------------- | ---------------------------------------- |
| Default graph size         | 15 unique jobs including the root                                                                   | Accepted by human, 2026-09-23 (P00)      |
| Normal maximum             | 50 unique jobs per root                                                                             | Accepted by human, 2026-09-23 (P00)      |
| Normal graph depth         | At most 8 levels on the longest dependency path, counting the root as one level                     | Accepted by human, 2026-09-23 (P00)      |
| Readable branching         | Usually 2-4 branches                                                                                | Deferred to UC06; proposal only          |
| Tighter alternative        | Default 10 jobs, maximum 30                                                                         | Not selected; 15/50 accepted             |
| Stress profile             | 100-500 jobs, explicitly enabled and bounded                                                        | Deferred to UC20; proposal only          |
| Standard delivery          | One root every 3 seconds                                                                            | Implemented in P02; see execution record |
| Independent arrivals       | Queue/scenario intervals of 2, 3 and 5 seconds                                                      | Deferred to UC09; preset to validate     |
| Burst delivery             | Five small roots every 10 seconds                                                                   | Deferred to UC19; preset to validate     |
| Shared-root representation | One job ID, all dependency edges and rootJobIds; root ID identifies the current one-root run     | Implemented in P03                      |
| Continuous retention       | Bound completed history and event fingerprints while retaining active graphs and their dependencies | Resolve before UC25                      |
| Project rename             | Implementation becomes `TplQueue.Sample.Simulation`; contracts name remains unchanged until decided | Complete, 2026-09-24 (P01)               |

See the [P00 execution record](simulation-tasks/p00-record-decisions-and-establish-baseline.md#execution-record)
for reference modes, retained tests, exact validation results and baseline limitations.
Continuous retention and the contracts-project name
remain unresolved at their existing checkpoints. P02 implements finite delivery and
admission semantics, recorded in its [execution record](simulation-tasks/p02-add-finite-timer-driven-delivery-and-minimal-contracts.md#execution-record).
The observed baseline below the ownership section describes the pre-P02 workload.
P03 adds composition membership before enqueue, retaining all shared root IDs across outcomes; see its [execution record](simulation-tasks/p03-establish-graph-identity-for-all-outcomes.md#execution-record).
See the [maintained delivery contract](../architecture/blazor-consumer-sample.md#finite-scenario-delivery) for current behavior.

Suggested profiles are 3-15 jobs for normal explanation, 16-30 for complex
demonstrations, 31-50 for large interactive examples, and 100-500 for stress tests.
Count each unique reachable job once per root, including that root. Count a shared
job only once globally for simulation load. For scenarios involving several roots,
report both root count and globally unique job count.

Graph size, graph depth, arrival rate, active-run count and retained-history size
are separate limits. A shallow 500-job graph says little about a 500-level chain.

## Common implementation rules

1. Use `System.Timers.Timer` for scenario delivery, normally one timer per
   independently scheduled scenario. A timer submits work; dependencies control
   downstream execution. Do not create a timer for every job.
2. Configure interval, startup offset, roots per tick, finite repetitions and
   maximum active runs. Add graph size, handler duration and deterministic failure
   settings only as required by implemented scenarios.
3. Keep the elapsed callback short and serialize submissions for each scenario.
   Define missed-tick behavior explicitly: proposed default is skip and count a
   tick while busy or at the admission bound, without an unbounded catch-up backlog.
4. Guard stop/dispose with lifecycle state. Elapsed callbacks can overlap and can
   arrive after Stop or Dispose. Track any asynchronous work and observe failures;
   avoid untracked async event-handler work. See
   [Microsoft Timer.Elapsed documentation](https://learn.microsoft.com/en-us/dotnet/api/system.timers.timer.elapsed?view=net-10.0).
5. Distinguish pause-arrivals, drain, and cancellation. Proposed semantics:
   pause stops new submissions while active jobs continue; drain stops arrivals
   and waits for active runs; cancel requests cancellation of owned active runs.
   Dashboard Pause freezes viewing time and is a separate operation.
6. Use predictable scenario names, run IDs and job names. Use seeded variation
   when useful, but unique runtime job IDs for each new execution. Retries retain
   the identity of the job being retried. Repeatable input does not imply identical
   concurrent execution order or exact timestamps.
7. Keep the existing event-driven refresh path: observer -> projection -> detached
   snapshot -> Blazor dispatch -> monitor. Do not introduce UI polling or redraw
   timers. Use actual queue-local channels and channel-bearing Started timestamps.
8. Add small contracts when needed: simulation lifecycle service, scenario ID and
   settings, and run identity/status including root IDs are candidates. Keep timer
   types and implementation details internal. Do not prebuild a general graph DSL,
   add a custom SignalR hub, or move live runtime objects into presentation DTOs.
9. Preserve .NET Standard 2.0 compatibility for the sample module/contracts and
   current project conventions. Use NUnit, Moq when useful, and Arrange/Act/Assert.
   Write relevant failing tests before implementation, including integration tests
   where real queue composition is necessary. Preserve existing valid tests.
10. Implement basic scenarios as finite runs first. Do not enable endless default
    arrivals until UC25 retention and lifecycle acceptance are complete.

## Iteration workflow and definition of done

### One task per thread

Open a new thread with one task file. That file is the entry point: read its
dependencies, this plan's shared rules and the applicable repository instructions.
Use the recorded decisions and execution evidence on disk; do not depend on the
previous thread's conversation. Implement only the selected task in that thread.

Complete its validation, make the local commits, mark its status complete and check
its entry in this plan. Commit those completion records as part of the task. Then
stop: the next eligible task starts in a new thread opened by the human. Selecting
the next task for the handoff does not authorize starting it in the current thread.

Use this prompt in each new thread, replacing the path with the selected task:

```text
Execute only the task in <task-file-path>.
Read its linked plan and applicable repository instructions. Check dependencies,
implement the task, run the required validation and perform the staged review.
Create the local commit(s), including the task's execution record and its completed
checkbox in the main plan once acceptance passes. Preserve unrelated changes.
If blocked, record the blocker and leave the task incomplete.
Finish with the commit hashes, validation results and the next eligible task's
file path and prompt for a new thread. Do not start the next task or push commits.
```

If dependencies or acceptance checks block completion, keep the main checkbox
unchecked and record the task as blocked or in progress with the exact remaining
work. A continuation thread resumes that same task before moving to dependent work.

For the selected preparation task or use case:

1. Read applicable root instructions and relevant surface documentation. Inspect
   repository status, active project references, configuration and existing tests.
   Record dependencies and any contradiction before expanding scope. The Usage
   source-reference preview exception remains intentional.
2. Select one task from the checklist below. Specify its observable outcome,
   settings, graph shape, expected root/job counts and failure/cancellation
   semantics. Resolve only the pending decisions that block that item.
3. Add or adapt focused tests and demonstrate the failing condition. Prefer
   controllable synchronization over sleeps for concurrency assertions. Test
   scenario decisions independently of real timers, then include bounded real-timer
   integration coverage for the delivery/lifecycle boundary.
4. Implement the smallest coherent change in the simulation/contracts. Change
   host projection or the reusable monitor only when the use case requires it.
   Keep generated `wwwroot/job-monitor` copies untouched. Add precise English XML
   documentation for new or substantially changed C# APIs.
5. Validate in order: build affected projects, unit tests, relevant local packaging,
   then integration tests that depend on those outputs. Run browser acceptance for
   rendering/lifecycle changes. Record exact commands, results and skipped checks.
6. Update current-behavior documentation when behavior changes and add the iteration
   record in that task file. After acceptance passes, set its status to complete
   and check its entry in this plan. Stage these records with the intended task
   changes, then inspect
   `git diff --cached` and perform the staged review required by repository
   instructions. Verify compatibility configuration first. Fix findings and
   repeat affected checks.
7. Create the task's local commit(s) with human-readable subjects. Include tests,
   relevant documentation, the task record and the main-plan completion checkbox.
   Verify the commits succeeded and intended changes are committed before reporting
   completion. Do not include unrelated changes or push commits.
   If dependent repository changes become necessary, first apply the scope and
   cross-repository documentation rules; do not silently expand the iteration.
8. End the thread with the completed task ID, commit hashes, validation results,
   limitations and the next eligible task's path and ready-to-paste prompt. Do not
   begin that task. Record final commit hashes in the next checkpoint/documentation
   update (a commit cannot contain its own final hash); Git history and the final
   handoff identify the commits until then. Keep partial or blocked items unchecked.

A saved plan, passing build, or HTTP prerender check alone does not complete a
use case. Do not claim checks that were not run. A blocking dependency is recorded
and reported rather than hidden behind a completion checkbox.

### Validation entry points

Follow [local development](local-development.md) and the architecture guide for
the active reference mode. Commands below use their stated working directory;
recheck paths after a rename.

From `TplQueue.Usage`, the repository commands are `./build.ps1 -Configuration Debug`,
`./test.ps1 -Configuration Debug`, and `./coverage.ps1 -EnforceBaseline` as applicable.
The test script also builds samples; record that when describing execution order.

For optional maintainer source validation, the documented workspace entry point
from `WorkspaceTplQueue` is `./build.ps1 -Configuration Debug -RunTests`. Inspect
its current options to split build/unit/package/integration phases when needed.
Packing uses the maintained workspace `pack.ps1` or applicable product
`pack-local.ps1` scripts into `TplQueue.NugetLocal`, never `_local-packages`.
Usage has no own `pack-local.ps1`. Mark packaging not applicable when no packaged
output changes; do not rebuild product packages without a reason.

Source-reference success is not package-consumption success. Validate packaged
outputs through a consumer whose active references actually resolve to packages.

For changed monitor behavior, from `tools/TplQueue.JobMonitor`, run
`node scripts/check.mjs` and `node --test tests/layout.test.js`. Use the standalone
`/tests/browser.html` harness and the Debug Blazor `/job-monitor/tests/blazor.html`
harness for the relevant interactive checks. Test live execution, selection, search,
zoom, history, reconnect and disposal only to the extent affected by the iteration.

## Task checklist

The plan is split into 29 task files: four preparation tasks and 25 use cases.
Start with [P00 - Record decisions and establish baseline](simulation-tasks/p00-record-decisions-and-establish-baseline.md).
Each file contains its scope, dependencies, acceptance requirements, suggested
commit and space for execution evidence. The shared rules and decision table stay
in this plan; task files own the detailed acceptance and execution records.

Work on one task per thread. The IDs preserve the original plan order; use each
task's dependencies to choose the next eligible item. For tasks with several
variants, validate one bounded variant at a time and keep the task pending until
all its acceptance requirements pass. Suggested commit subjects identify scope;
rewrite them to match the actual completed change.

### Preparation checkpoints

- [x] [P00 - Record decisions and establish baseline](simulation-tasks/p00-record-decisions-and-establish-baseline.md)
- [x] [P01 - Isolate the module rename](simulation-tasks/p01-isolate-the-module-rename.md)
- [x] [P02 - Add finite timer-driven delivery and minimal contracts](simulation-tasks/p02-add-finite-timer-driven-delivery-and-minimal-contracts.md)
- [x] [P03 - Establish graph identity for all outcomes](simulation-tasks/p03-establish-graph-identity-for-all-outcomes.md)

P00 precedes size-dependent defaults. P01 completed the isolated implementation
rename to `TplQueue.Sample.Simulation`; the contracts name remains unchanged.
P02 enables recurring finite scenarios. P03 gates claims about complete graph
identification, but does not require implementing every presentation feature upfront.

### Use-case checklist and acceptance

- [x] [UC01 - Single job on each queue](simulation-tasks/uc01-single-job-on-each-queue.md)
- [ ] [UC02 - Sequential ETL chain](simulation-tasks/uc02-sequential-etl-chain.md)
- [ ] [UC03 - Multiple FIFO roots](simulation-tasks/uc03-multiple-fifo-roots.md)
- [ ] [UC04 - Multiple parallel roots](simulation-tasks/uc04-multiple-parallel-roots.md)
- [ ] [UC05 - Cache-backed ETL](simulation-tasks/uc05-cache-backed-etl.md)
- [ ] [UC06 - Branching and joining](simulation-tasks/uc06-branching-and-joining.md)
- [ ] [UC07 - Diamond graph](simulation-tasks/uc07-diamond-graph.md)
- [ ] [UC08 - Uneven branches](simulation-tasks/uc08-uneven-branches.md)
- [ ] [UC09 - Independent workloads across queues](simulation-tasks/uc09-independent-workloads-across-queues.md)
- [ ] [UC10 - Two-queue shared dependency](simulation-tasks/uc10-two-queue-shared-dependency.md)
- [ ] [UC11 - Three-queue dependency graph](simulation-tasks/uc11-three-queue-dependency-graph.md)
- [ ] [UC12 - One prerequisite shared by several roots](simulation-tasks/uc12-one-prerequisite-shared-by-several-roots.md)
- [ ] [UC13 - Cross-queue ownership variants](simulation-tasks/uc13-cross-queue-ownership-variants.md)
- [ ] [UC14 - Cross-queue bottleneck](simulation-tasks/uc14-cross-queue-bottleneck.md)
- [ ] [UC15 - Transient failure followed by success](simulation-tasks/uc15-transient-failure-followed-by-success.md)
- [ ] [UC16 - Retry exhaustion and dependency failure](simulation-tasks/uc16-retry-exhaustion-and-dependency-failure.md)
- [ ] [UC17 - Cancellation at different phases](simulation-tasks/uc17-cancellation-at-different-phases.md)
- [ ] [UC18 - Shared-dependency failure or cancellation](simulation-tasks/uc18-shared-dependency-failure-or-cancellation.md)
- [ ] [UC19 - Burst, overload and recovery](simulation-tasks/uc19-burst-overload-and-recovery.md)
- [ ] [UC20 - Large shallow and deep narrow graphs](simulation-tasks/uc20-large-shallow-and-deep-narrow-graphs.md)
- [ ] [UC21 - Very short jobs and clustered starts](simulation-tasks/uc21-very-short-jobs-and-clustered-starts.md)
- [ ] [UC22 - Long execution and idle periods](simulation-tasks/uc22-long-execution-and-idle-periods.md)
- [ ] [UC23 - Simulation lifecycle](simulation-tasks/uc23-simulation-lifecycle.md)
- [ ] [UC24 - Browser lifecycle during execution](simulation-tasks/uc24-browser-lifecycle-during-execution.md)
- [ ] [UC25 - Bounded continuous operation](simulation-tasks/uc25-bounded-continuous-operation.md)

## Additional automated coverage

Add these cases with the iteration that introduces the relevant boundary, rather
than making misleading runtime demonstrations or expanding unrelated tests:

- Invalid intervals, graph sizes/depths, queue selections, durations, retry settings
  and active-run limits; null/empty inputs and invalid lifecycle transitions.
- Invalid/cyclic graph rejection through the existing graph API, within bounded
  tests. Do not run intentionally invalid graphs in the normal demonstration loop.
- Duplicate and delayed observer events, legacy events without channels, late
  Started enrichment and out-of-range channel rejection. Preserve detached snapshots.
- Submission/handler/observer failures, cancellation races and timer callbacks that
  arrive during stop/dispose. Ensure errors are recorded and tests terminate.
- Cache serialization/type-resolution errors and failed/cancelled acknowledgment
  behavior, using the actual supported cache policy.

## Iteration record

Copy this template into the selected task file when work starts. Keep the summary
checklist in this plan and detailed records in the task files so the next session
can resume without reconstructing the conversation. Record shared decisions in
this plan's decision table and link to the task evidence.

```text
Item: Pxx / UCxx - title
Status: pending | in progress | blocked | complete
Date:
Baseline commit and reference mode:
Decisions confirmed:
Scenario settings and expected counts:
Changed behavior and files:
Failing-test evidence before implementation:
Build / unit / pack / integration commands and results:
Browser checks and observed outcomes:
Staged review findings and resolution:
Skipped checks and reasons:
Remaining limitations or dependency blockers:
Commit subject:
Commit hash: record in the next checkpoint update
Next eligible item:
Next task file and new-thread prompt:
```

### Initial checkpoint - 2026-09-22

- Status: analysis stored; all preparation and use-case checkboxes remain pending.
- No sample code, contracts, project names or runtime behavior changed in this step.
- Graph limits remain proposals pending agreement.
- Documentation validation passed: 24 local links across the plan and its two
  navigation files; all 25 use-case sections present; `git diff --check` and a
  separate whitespace check for the new plan passed. Navigation changes were
  inspected. Runtime tests were not run for this documentation-only checkpoint.
- Next eligible work: P00 decisions/baseline, followed by the agreed preparation
  checkpoint and one finite scenario. This plan does not mark future work complete.

### Task split checkpoint - 2026-09-23

- Split P00-P03 and UC01-UC25 into 29 linked task files, preserving their scope,
  acceptance requirements and suggested commits.
- P00 remains the first task. All implementation tasks and proposed decisions
  remain pending; splitting the plan does not establish or validate the baseline.
- Shared rules, the decision table and the summary checklist remain in this plan.
  Detailed acceptance and future execution evidence belong in the task files.
- Documentation validation passed: all 29 original task scopes, acceptance
  requirements and commit subjects are preserved; 381 local links and their
  anchors resolve. Whitespace checks covered all 32 affected documentation files,
  and `git diff --check` passed. Runtime tests were not run for this documentation
  split; baseline execution remains part of P00.
