# UC17 - Cancellation at different phases

Status: pending.

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

Queued/executing variants can start first. Dependency-waiting and retry-waiting variants depend on [UC14](uc14-cross-queue-bottleneck.md) and [UC15](uc15-transient-failure-followed-by-success.md) respectively.

Start with the decisions and baseline from [P00](p00-record-decisions-and-establish-baseline.md).
Use [P02](p02-add-finite-timer-driven-delivery-and-minimal-contracts.md) for recurring delivery and [P03](p03-establish-graph-identity-for-all-outcomes.md) before claiming
complete graph identification. P01 may be deferred.

## Scope and acceptance

- [ ] Implement and validate. Cancel queued, executing, dependency-waiting and
  retry-waiting runs; the latter variants depend on UC14/UC15.
- Trigger cancellation from observed milestones or controlled handler gates, rather
  than guessed sleep durations. Verify terminal behavior, resource cleanup and
  isolation of unrelated runs. Include repeated/unknown-root cancellation.

## Suggested commit

`feat(simulation): cover cancellation across execution phases`

## Working rules and validation

Read the [main plan](../simulation-use-case-plan.md) before starting. Its
[common implementation rules](../simulation-use-case-plan.md#common-implementation-rules),
[iteration workflow](../simulation-use-case-plan.md#iteration-workflow-and-definition-of-done),
[validation entry points](../simulation-use-case-plan.md#validation-entry-points) and
[additional automated coverage](../simulation-use-case-plan.md#additional-automated-coverage)
apply to this task. Resolve only decisions needed by this task and include the
relevant tests, documentation and acceptance evidence in the same iteration.

## Execution record

Pending. When work starts, copy the main plan's
[iteration record template](../simulation-use-case-plan.md#iteration-record) here.
Record exact checks, outcomes, skipped checks and blockers. Update the task status
and the main checklist together; mark complete only after acceptance passes.
