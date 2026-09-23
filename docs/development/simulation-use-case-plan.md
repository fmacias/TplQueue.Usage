# Simulation use-case implementation plan

Created: 2026-09-22. Status: planning only; no implementation completed by this document.

## Purpose and ownership

Turn the finite ETL demonstration into a configurable, timer-driven simulation that
exercises real TplQueue execution and the Blazor job monitor. Implement and validate
one bounded use case at a time, with a reviewable commit and a recorded checkpoint
before the next iteration.

The implementation belongs in `samples/TplQueue.Sample.Etl`, intended to become
`TplQueue.Sample.Simulation`. Add contracts to `samples/TplQueue.Sample.Etl.Contracts`
when required. Renaming the implementation is a future step, not part of this
documentation change. Renaming the contracts project has not been decided.

Blazor owns host composition, observer projection and presentation. The simulation
owns scenarios, graph construction, handlers, delivery scheduling and run control.
Browser connections must not create or restart workloads.

This document tracks proposed work and its acceptance evidence. The maintained
[Blazor architecture guide](../architecture/blazor-consumer-sample.md) remains the
source for current architecture, contract ownership and rendering rules. Update that
guide when implemented behavior changes; do not treat this plan as a second current
architecture specification. Public product documentation and publishing boundaries
remain unchanged.

## Observed baseline

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
- [ETL workflow](../../samples/TplQueue.Sample.Etl/EtlWorkflow.cs)
- [Workflow contract](../../samples/TplQueue.Sample.Etl.Contracts/IEtlWorkflow.cs)
- [Queue runtime](../../samples/TplQueue.Sample.Etl/Runtime/EtlQueueRuntime.cs)
- [Projection](../../samples/TplQueue.Sample.BlazorSignalR/Presentation/Etl/EtlExecutionProjectionStore.cs)
- [Monitor behavior and bounds](../../tools/TplQueue.JobMonitor/README.md)

## Decisions to settle before dependent implementation

The normal graph limits below were confirmed by the human during P00 on 2026-09-23.
They are scenario limits, not measured performance limits. Other proposals remain
deferred until their dependent task; do not treat them as accepted defaults.

| Decision | Proposal | Status |
| --- | --- | --- |
| Default graph size | 15 unique jobs including the root | Accepted by human, 2026-09-23 (P00) |
| Normal maximum | 50 unique jobs per root | Accepted by human, 2026-09-23 (P00) |
| Normal graph depth | At most 8 levels on the longest dependency path, counting the root as one level | Accepted by human, 2026-09-23 (P00) |
| Readable branching | Usually 2-4 branches | Deferred to UC06; proposal only |
| Tighter alternative | Default 10 jobs, maximum 30 | Not selected; 15/50 accepted |
| Stress profile | 100-500 jobs, explicitly enabled and bounded | Deferred to UC20; proposal only |
| Standard delivery | One root every 3 seconds | Deferred to P02/UC02; preset to validate |
| Independent arrivals | Queue/scenario intervals of 2, 3 and 5 seconds | Deferred to UC09; preset to validate |
| Burst delivery | Five small roots every 10 seconds | Deferred to UC19; preset to validate |
| Shared-root representation | Preserve one job identity and all dependency edges; decide how run/root memberships are exposed | Resolve before UC12 |
| Continuous retention | Bound completed history and event fingerprints while retaining active graphs and their dependencies | Resolve before UC25 |
| Project rename | Implementation becomes `TplQueue.Sample.Simulation`; contracts name remains unchanged until decided | Future preparation step |

See the [P00 execution record](simulation-tasks/p00-record-decisions-and-establish-baseline.md#execution-record)
for reference modes, retained tests, exact validation results and baseline limitations.
Shared-root representation, continuous retention and the contracts-project name
remain unresolved at their existing checkpoints. Delivery lifecycle and admission
semantics remain proposals for P02; P00 does not approve them or change runtime defaults.

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

For each preparation step or use case:

1. Read applicable root instructions and relevant surface documentation. Inspect
   repository status, active project references, configuration and existing tests.
   Record dependencies and any contradiction before expanding scope. The Usage
   source-reference preview exception remains intentional.
2. Select one item below. Specify its observable outcome, settings, graph shape,
   expected root/job counts and failure/cancellation semantics. Resolve only the
   pending decisions that block that item.
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
   record below. Stage only the intended files, then inspect `git diff --cached`
   and perform the staged review required by repository instructions. Verify
   compatibility configuration first. Fix findings and repeat affected checks.
7. Commit the validated use case as a bounded change with a human-readable subject.
   Include tests and relevant documentation. Do not include unrelated changes.
   If dependent repository changes become necessary, first apply the scope and
   cross-repository documentation rules; do not silently expand the iteration.
8. Record the resulting commit hash in the next checkpoint/documentation update
   (a commit cannot contain its own final hash). Mark the item complete only when
   its acceptance checks pass; keep partial or blocked items explicitly pending.

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

For the current coordinated source layout, the documented workspace entry point
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

## Preparation checkpoints

- [x] **[P00 - Record decisions and establish baseline](simulation-tasks/p00-record-decisions-and-establish-baseline.md).** Confirm normal graph limits,
  run current relevant checks, record reference mode and existing failures. Identify
  the integration tests to retain. Suggested commit, if decisions change this file:
  `docs(simulation): agree initial scenario limits and validation baseline`.
- [ ] **P01 - Isolate the module rename.** When implementing the intended rename,
  update project/namespace references, solutions, scripts, test links and affected
  documentation together. Preserve behavior and verify old references deliberately
  retained for history. Inventory workspace consumers before touching them; apply
  their instructions if they are affected. Do not infer a contracts-project rename.
  Suggested commit: `refactor(samples): rename ETL implementation to simulation`.
- [ ] **P02 - Add finite timer-driven delivery and minimal contracts.** Move scenario
  orchestration into the module, retain observer attachment before first submission,
  and keep the host as lifecycle adapter. Verify finite tick counts, validation,
  overlap protection, exception observation and no accepted submissions after stop.
  Suggested commit: `feat(simulation): add bounded timer-driven scenario delivery`.
- [ ] **P03 - Establish graph identity for all outcomes.** Determine the smallest
  reliable path for run/root identity before terminal success, preserving real
  event-owned lifecycle facts. Test running, failed and cancelled graph selection.
  Resolve shared membership before UC12 without inventing execution channels.
  Suggested commit: `fix(monitor): preserve simulation graph identity across outcomes`.

P00 precedes size-dependent defaults. P01 is an isolated future rename checkpoint;
scenario development can still use the current project name if it is deferred.
P02 enables recurring finite scenarios. P03 gates claims about complete graph
identification, but does not require implementing every presentation feature upfront.

## Use-case checklist and acceptance

Each item follows the iteration workflow above. Suggested commit subjects identify
scope; rewrite them to match the actual completed change.

### UC01 - Single job on each queue

- [ ] Implement and validate. Submit one independent root separately to FIFO,
  Parallel and Cache. Verify one execution per job and the expected terminal outcome.
- Check enqueue history in Unassigned, actual channel/start placement, and one job
  count despite two position markers. Inspect channel reuse after completion.
- Commit: `feat(simulation): add single-job queue scenarios`.

### UC02 - Sequential ETL chain

- [ ] Implement and validate. Preserve the three-step ETL example as a named scenario
  and deliver a finite number of fresh roots at a configured interval.
- Verify prerequisite completion before downstream handler execution, correct
  business output, three unique jobs per root and actual dependency edges.
- Commit: `feat(simulation): schedule the sequential ETL scenario`.

### UC03 - Multiple FIFO roots

- [ ] Implement and validate after UC02. Submit a controlled sequence faster than
  completion, using known submission order and readable job durations.
- Verify FIFO behavior against the queue contract, serialized handler execution,
  channel zero, backlog visibility and eventual drain. Do not infer ordering of
  asynchronous observer callbacks from scheduling order.
- Commit: `feat(simulation): demonstrate FIFO backlog and ordering`.

### UC04 - Multiple parallel roots

- [ ] Implement and validate after UC02. Submit enough independent roots to exercise
  configured queue capacity and queue additional work.
- Verify concurrent execution, channels within capacity, no conflicting ownership
  of a channel, reuse after completion and separation of unrelated roots.
- Commit: `feat(simulation): demonstrate parallel capacity and channel reuse`.

### UC05 - Cache-backed ETL

- [ ] Implement and validate after UC02. Exercise real payload serialization,
  resolution and handlers through CacheQ, retaining readable ETL output.
- Verify successful execution and cache acknowledgment separately; a root wait does
  not prove observer delivery or acknowledgment finished. Add failure/cancellation
  variants alongside UC16/UC17. Do not claim persistence across process restart.
- Commit: `feat(simulation): cover cache-backed ETL execution`.

### UC06 - Branching and joining

- [ ] Implement and validate. Build 2-4 independent prerequisite branches converging
  on a final root; initially keep the graph within 5-10 unique jobs.
- Verify every required branch completes before the join handler executes, distinct
  dependency edges and root highlighting. Separate graph topology from actual
  parallelism, which remains governed by the selected queue.
- Commit: `feat(simulation): add branching and joining graphs`.

### UC07 - Diamond graph

- [ ] Implement and validate after UC06. Share one prerequisite between two branches
  and join those branches at the root.
- Verify the shared job executes once, has one identity and renders all dependency
  edges. Count unique nodes rather than traversed references.
- Commit: `feat(simulation): add a shared-dependency diamond graph`.

### UC08 - Uneven branches

- [ ] Implement and validate after UC06. Give one branch a longer controlled duration.
- Verify finished branches stay finished, the join waits for the slow prerequisite,
  and handler start order is correct. Channel occupancy may include dependency
  waiting; do not equate a channel-bearing Started event with handler CPU activity.
- Commit: `feat(simulation): demonstrate slow-branch joins`.

### UC09 - Independent workloads across queues

- [ ] Implement and validate after UC01-UC05. Enable finite scenario schedules with
  different intervals and initial offsets, initially 2/3/5 seconds.
- Verify independent delivery counts, unique run identities, correctly separated
  root graphs, and no new workloads when another browser connects.
- Commit: `feat(simulation): schedule independent queue workloads`.

### UC10 - Two-queue shared dependency

- [ ] Implement and validate after UC07. Compose graphs sharing a job instance and
  submit in a controlled order so its actual execution owner is known.
- Verify execution once on the owning queue, waiting on the other queue, no duplicate
  enqueue event from a rejected ownership claim, and a real cross-queue edge.
  Recheck construction/root-binding constraints before selecting the exact topology.
- Commit: `feat(simulation): add two-queue dependency scenarios`.

### UC11 - Three-queue dependency graph

- [ ] Implement and validate after UC10 and UC05. Extend the graph across FIFO,
  Parallel and Cache using supported shared-dependency composition.
- Verify queue ownership and prerequisite order at each boundary, real channels,
  distinct roots where required, and CacheQ hydration preserves supported identities.
  Record any unsupported cache-sharing composition as a dependency limitation.
- Commit: `feat(simulation): cover dependencies across three queues`.

### UC12 - One prerequisite shared by several roots

- [ ] Implement and validate after UC10 and the P03 membership decision. Give several
  roots the same prerequisite instance and distinct run/root membership information.
- Verify a single execution/node, all consumer edges and stable selection before
  and after completion. Completion order must not silently overwrite membership.
- Commit: `feat(simulation): cover dependencies shared by multiple roots`.

### UC13 - Cross-queue ownership variants

- [ ] Implement and validate after UC10. Reverse enqueue order, then add a bounded
  concurrent-submission variant.
- Verify the controlled-order winner and, for the race, exactly one actual owner
  without assuming which queue wins. Render the observed owner and execute once.
- Commit: `feat(simulation): exercise cross-queue ownership ordering`.

### UC14 - Cross-queue bottleneck

- [ ] Implement and validate after UC10. Delay an externally owned prerequisite
  while several consumers wait under bounded queue capacities.
- Verify consumers do not bypass the prerequisite, unrelated work follows runtime
  capacity rules, and all consumers progress once the bottleneck clears. Use a
  bounded timeout to detect stalls; do not build accidental cyclic waits.
- Commit: `feat(simulation): demonstrate cross-queue bottlenecks`.

### UC15 - Transient failure followed by success

- [ ] Implement and validate. Fail selected jobs for a configured number of attempts,
  then succeed under the existing retry abstraction.
- Verify attempt counts, configured delay policy and eventual completion on the
  same job identity. Verify no duplicate downstream execution or business output.
  Inspect retry metadata without requiring every brief transition to be painted.
- Commit: `feat(simulation): add deterministic retry recovery scenarios`.

### UC16 - Retry exhaustion and dependency failure

- [ ] Implement and validate after UC15. Fail a leaf, an intermediate job and the
  root in separate variants; include CacheQ where supported.
- Verify policy exhaustion, actual terminal outcomes and that dependent handlers
  do not run incorrectly. Determine root/branch propagation from runtime contracts
  and tests, then document it. Render failed graphs with identifiable roots.
- Commit: `feat(simulation): cover exhausted retries and dependency failures`.

### UC17 - Cancellation at different phases

- [ ] Implement and validate. Cancel queued, executing, dependency-waiting and
  retry-waiting runs; the latter variants depend on UC14/UC15.
- Trigger cancellation from observed milestones or controlled handler gates, rather
  than guessed sleep durations. Verify terminal behavior, resource cleanup and
  isolation of unrelated runs. Include repeated/unknown-root cancellation.
- Commit: `feat(simulation): cover cancellation across execution phases`.

### UC18 - Shared-dependency failure or cancellation

- [ ] Implement and validate after UC12, UC16 and UC17. Fail/cancel the shared
  prerequisite and separately cancel one consuming root.
- Establish supported cancellation ownership first: cancelling one consumer must
  not be assumed to cancel shared work. Verify each consumer's actual outcome,
  unrelated-root isolation, single job identity and absence of hangs.
- Commit: `feat(simulation): cover shared-dependency failure and cancellation`.

### UC19 - Burst, overload and recovery

- [ ] Implement and validate after UC03/UC04 and P02 bounds. Submit finite bursts,
  then finite arrivals faster than service, and finally stop arrivals to drain.
- Verify admission bounds, skipped-tick counts, visible backlog growth and recovery.
  Distinguish skipped submissions from accepted jobs. Do not enable unbounded load.
- Commit: `feat(simulation): add bounded burst and overload scenarios`.

### UC20 - Large shallow and deep narrow graphs

- [ ] Implement and validate after graph-size agreement, UC06/UC07 and P03. Build
  each shape separately, including the normal cap and an opt-in stress profile.
- Verify unique-node counts, depth validation and useful validation errors. Check
  graph focus, search and grouped markers without fabricating edges or timestamps.
  Record browser/environment, snapshot size and update timings; report measured
  limits rather than claiming the stress profile is universally responsive.
- Commit: `feat(simulation): add bounded graph-size scenarios`.

### UC21 - Very short jobs and clustered starts

- [ ] Implement and validate after UC04. Deliver a bounded set of very short jobs.
- Verify grouping, count accuracy, individual selection, zoom and retained enqueue
  history. Use deterministic monitor fixtures for exact timestamp collisions;
  real timers do not guarantee same-millisecond timestamps. Never shift timestamps
  simply to separate markers.
- Commit: `feat(simulation): exercise dense job timing and inspection`.

### UC22 - Long execution and idle periods

- [ ] Implement and validate. Alternate finite long-running work and arrival gaps.
- Verify the monitor refreshes on accepted events, stays still without changes,
  and resumes on new events. Check Pause/history reference stability during incoming
  updates and Follow live behavior without introducing periodic redraws.
- Commit: `feat(simulation): demonstrate long-running and idle intervals`.

### UC23 - Simulation lifecycle

- [ ] Implement and validate after P02 and UC17. Exercise start, pause-arrivals,
  resume, finite completion, drain, cancellation and host shutdown.
- Verify idempotent lifecycle operations, documented restart semantics, no duplicate
  schedules, no post-stop accepted submissions, observed callback failures and
  disposed timers/subscriptions/cancellation resources. Keep view pause separate.
- Commit: `feat(simulation): complete lifecycle and shutdown scenarios`.

### UC24 - Browser lifecycle during execution

- [ ] Implement and validate after UC09/UC23. Connect late, reconnect, navigate
  away/back and use multiple viewers during the same backend run.
- Verify shared backend execution with independent selection/viewports, initial
  snapshot plus event-driven updates, no extra submissions and safe subscription/
  interop disposal. Run the interactive Blazor harness; HTTP checks are insufficient.
- Commit: `test(simulation): cover browser lifecycle during active workloads`.

### UC25 - Bounded continuous operation

- [ ] Implement and validate after UC12, UC19 and UC23 plus the retention decision.
  Bound active runs, completed history, event fingerprints and sample business data.
- Retain active graphs and shared dependencies while any active consumer needs them.
  Evict completed graph history coherently, handle selection of retired jobs, and
  prevent late/duplicate events from recreating unbounded expired history. Define
  the bounded late-event policy explicitly.
- Run beyond several retention cycles and verify retained counts and memory trends
  stabilize, active graphs remain intact and shutdown cleans up. Record duration,
  throughput and environment. Only then enable optional continuous operation.
- Commit: `feat(simulation): bound continuous workload history and retention`.

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

Copy this template for every implemented item. Keep the checklist and records in
this document so the next session can resume without reconstructing the conversation.

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
