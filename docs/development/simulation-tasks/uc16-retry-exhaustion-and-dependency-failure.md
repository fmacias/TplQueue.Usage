# UC16 - Retry exhaustion and dependency failure

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

[UC15](uc15-transient-failure-followed-by-success.md); use the CacheQ path from [UC05](uc05-cache-backed-etl.md) for supported cache variants.

Start with the decisions and baseline from [P00](p00-record-decisions-and-establish-baseline.md).
Use [P02](p02-add-finite-timer-driven-delivery-and-minimal-contracts.md) for recurring delivery and [P03](p03-establish-graph-identity-for-all-outcomes.md) before claiming
complete graph identification. P01 may be deferred.

## Scope and acceptance

- [ ] Implement and validate after UC15. Fail a leaf, an intermediate job and the
  root in separate variants; include CacheQ where supported.
- Verify policy exhaustion, actual terminal outcomes and that dependent handlers
  do not run incorrectly. Determine root/branch propagation from runtime contracts
  and tests, then document it. Render failed graphs with identifiable roots.

## Suggested commit

`feat(simulation): cover exhausted retries and dependency failures`

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
