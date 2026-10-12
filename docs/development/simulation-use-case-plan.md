# Simulation use-case catalog

Extend the existing `TplQueue.Sample.BlazorSignalR` prototype with demonstrable
TplQueue capabilities. The current implementation and supported public APIs are
the baseline; the [architecture guide](../architecture/blazor-consumer-sample.md)
and [component diagram](../architecture/blazor-sample-components.puml) describe it.

## Current baseline

Domain constructs jobs/graphs, payloads and handlers. Simulation requests them
through Contracts and owns scheduling, admission, the session, queue runtime and
graph membership. Blazor composes these services and projects observer events into
the existing JobMonitor. Simulation has no Domain project reference.

`EtlSimulationWorkflow` and `SingleJobWorkflow` reuse `ScheduledWorkflow`, each
owning a `System.Threading.Timer`: one-second initial offset and three-second
interval, attempting one fresh root per queue per tick without sample active-root
caps. A full pair of ticks adds six roots and twelve unique jobs. Overlapping
submission callbacks and submission failures can reduce those counts.

The host starts every implemented workflow together, independently of browser connections. Stop arrivals
waits for admitted submissions, not job drain, and does not cancel accepted work.
Completion means arrivals/submissions have stopped. Start is single-use.
Continuous delivery exists now; retained history and business data are unbounded.
Finite profiles and configurable timing from earlier milestones remain unavailable.
UC03 always runs alongside ETL and SingleJob with continuous fresh FIFO roots at
100 ms intervals. Every workflow runs until Stop or host cancellation, with no
lifetime root limit. Queued jobs remain observable while execution capacity is busy.

## Extension boundaries

Add focused Domain graph factories/handlers and Simulation workflows or scenarios,
using small additions to shared Contracts when needed. Reuse current scheduling,
queue runtime, graph registration, observer projection and Blazor presentation.
Extend `ScheduledWorkflow` only for a selected scenario's concrete needs. It supports
selected queues and an arrival interval, with
one root per submission; defaults retain continuous delivery across all three queues.
Do not build a generic workflow language or duplicate timer infrastructure.
Register every added workflow unconditionally alongside existing workflows in the
host. Do not add configuration flags, profiles or mutually exclusive selection.
Create fresh jobs inside Submit for every arrival. Do not replay stored jobs or
cap sample submissions based on active-root counts; queue capacity governs execution.

Register composed membership before enqueue. Root IDs identify current one-root
runs; shared jobs retain every root membership and one global job identity.
Membership is not queue acceptance, execution ownership, status or a channel.
Use observed dependency edges and channel-bearing Started timestamps. Preserve
initial-snapshot plus observer-driven updates and per-view state without polling.

All implementation stays in Usage. If supported public APIs cannot express a
scenario, record the enhancement, motivation, affected API/behavior and blocked
UC ID under the [README Roadmap](../../README.md#roadmap). Continue with supported
variants where possible. Do not change product repositories or hide semantic
changes in sample workarounds.

## Graph sizing

P00 accepted a target of 15 unique jobs for future general graph examples, a normal
maximum of 50 unique jobs per root, and a maximum depth of eight levels including
the root. These are scenario design limits, not implemented runtime defaults or
product performance guarantees. Small focused cases keep their explicit counts,
including the existing one-job and three-job graphs.

Count unique reachable jobs once per root and shared jobs once globally. For
multi-root cases describe both counts. Graph size/depth, arrival rate, active-root
limits and retention size are separate dimensions. UC20 owns any explicit stress
size; the earlier 100–500-job suggestion is not an enabled profile.

## Use-case catalog

Status describes source implementation, not newly executed runtime acceptance.
Implemented means the described scenario exists; Partially implemented identifies
both existing behavior and remaining scenario work. Pending means a dedicated
scenario is absent even when reusable infrastructure exists. Blocked is reserved
for an established dependency limitation recorded in the README.

| Use case | Status | Existing behavior / remaining intent |
| --- | --- | --- |
| [UC01 — Single job on each queue](simulation-tasks/uc01-single-job-on-each-queue.md) | Implemented | Independent ingest roots on all queues. |
| [UC02 — Sequential ETL chain](simulation-tasks/uc02-sequential-etl-chain.md) | Implemented | Named ETL workflow and three-job dependency chain. |
| [UC03 — Multiple FIFO roots](simulation-tasks/uc03-multiple-fifo-roots.md) | Implemented; validation pending | Continuous fresh FIFO roots alongside ETL/SingleJob; observable backlog drains after Stop. |
| [UC04 — Multiple parallel roots](simulation-tasks/uc04-multiple-parallel-roots.md) | Pending | Add deliberate parallel saturation and channel reuse. |
| [UC05 — Cache-backed ETL](simulation-tasks/uc05-cache-backed-etl.md) | Partially implemented | Cache ETL exists; acknowledgment and outcome variants remain. |
| [UC06 — Branching and joining](simulation-tasks/uc06-branching-and-joining.md) | Pending | Add branches converging on a root. |
| [UC07 — Diamond graph](simulation-tasks/uc07-diamond-graph.md) | Pending | Add one shared prerequisite inside a diamond. |
| [UC08 — Uneven branches](simulation-tasks/uc08-uneven-branches.md) | Pending | Add unequal branch durations. |
| [UC09 — Independent workloads across queues](simulation-tasks/uc09-independent-workloads-across-queues.md) | Partially implemented | Combined workflows exist; independent queue schedules remain. |
| [UC10 — Two-queue shared dependency](simulation-tasks/uc10-two-queue-shared-dependency.md) | Pending | Add a supported shared graph across two queues. |
| [UC11 — Three-queue dependency graph](simulation-tasks/uc11-three-queue-dependency-graph.md) | Pending | Extend supported sharing through all three queues. |
| [UC12 — One prerequisite shared by several roots](simulation-tasks/uc12-one-prerequisite-shared-by-several-roots.md) | Pending | Add several roots consuming one prerequisite. |
| [UC13 — Cross-queue ownership variants](simulation-tasks/uc13-cross-queue-ownership-variants.md) | Pending | Vary ordered/concurrent ownership claims. |
| [UC14 — Cross-queue bottleneck](simulation-tasks/uc14-cross-queue-bottleneck.md) | Pending | Add a controlled cross-queue bottleneck. |
| [UC15 — Transient failure followed by success](simulation-tasks/uc15-transient-failure-followed-by-success.md) | Pending | Add deterministic transient failure and recovery. |
| [UC16 — Retry exhaustion and dependency failure](simulation-tasks/uc16-retry-exhaustion-and-dependency-failure.md) | Pending | Add exhausted retry and dependency failure scenarios. |
| [UC17 — Cancellation at different phases](simulation-tasks/uc17-cancellation-at-different-phases.md) | Pending | Expose sample cancellation control and phase-specific scenarios. |
| [UC18 — Shared-dependency failure or cancellation](simulation-tasks/uc18-shared-dependency-failure-or-cancellation.md) | Pending | Describe and demonstrate shared-work failure/cancellation. |
| [UC19 — Burst, overload and recovery](simulation-tasks/uc19-burst-overload-and-recovery.md) | Pending | Add bounded burst and overload recovery. |
| [UC20 — Large shallow and deep narrow graphs](simulation-tasks/uc20-large-shallow-and-deep-narrow-graphs.md) | Pending | Add separate width/depth scenarios. |
| [UC21 — Very short jobs and clustered starts](simulation-tasks/uc21-very-short-jobs-and-clustered-starts.md) | Pending | Add a dedicated short-job workload; grouping already exists. |
| [UC22 — Long execution and idle periods](simulation-tasks/uc22-long-execution-and-idle-periods.md) | Pending | Add long-work/arrival-gap scenarios; idle rendering exists. |
| [UC23 — Simulation lifecycle](simulation-tasks/uc23-simulation-lifecycle.md) | Partially implemented | Continuous start/stop/shutdown exist; pause/resume and explicit drain remain. |
| [UC24 — Browser lifecycle during execution](simulation-tasks/uc24-browser-lifecycle-during-execution.md) | Implemented | Host/view lifetime separation and event-driven reconnect paths exist. |
| [UC25 — Bounded continuous operation](simulation-tasks/uc25-bounded-continuous-operation.md) | Partially implemented | Continuous delivery exists without sample caps; completed-history retention remains. |

Dependencies in each specification define useful implementation order, not a
mandatory numeric sequence. UC06 and UC15 can build on the existing foundation;
UC03/UC04 extend UC02. Shared/cross-queue cases build on UC07/UC10. Cancellation
variants depend on their waiting scenarios; retention must account for shared
roots and pressure. UC24's current browser behavior does not depend on future
schedule or lifecycle extensions.

## Completed milestones

These remain completed history, separate from active use-case specifications:

- [P00 — Decisions and baseline](simulation-tasks/p00-record-decisions-and-establish-baseline.md): accepted graph sizing.
- [P01 — Simulation module rename](simulation-tasks/p01-isolate-the-module-rename.md): implementation renamed; Contracts name retained.
- [P02 — Original finite delivery](simulation-tasks/p02-add-finite-timer-driven-delivery-and-minimal-contracts.md): established scheduling/admission ownership, later superseded by continuous workflows.
- [P03 — Graph identity](simulation-tasks/p03-establish-graph-identity-for-all-outcomes.md): composition membership before enqueue and all root IDs.
- UC01 remains implemented; its [historical milestone summary](simulation-continuous-demo.md#uc01-completed-milestone) records the earlier finite form.

The [historical continuous-demo record](simulation-continuous-demo.md) explains the
subsequent transition. Old operational records do not prescribe current runtime
behavior or authorize product changes.

## Open design decisions

- UC09/UC19: minimal scheduling inputs and multi-root admission accounting.
- UC10–UC14/UC18: supported root binding, ownership and Cache sharing at public API boundaries.
- UC23: explicit pause/resume and drain contracts without changing the existing Stop/Completion meaning.
- UC25: completed-history count/horizon, shared-node retention and bounded late-event policy.
- Contracts naming: `TplQueue.Sample.Contracts` is a proposed future name; see
  [shared contract responsibility](../architecture/blazor-consumer-sample.md#shared-contract-responsibility).
