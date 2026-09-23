# UC18 - Shared-dependency failure or cancellation

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

[UC12](uc12-one-prerequisite-shared-by-several-roots.md), [UC16](uc16-retry-exhaustion-and-dependency-failure.md) and [UC17](uc17-cancellation-at-different-phases.md).

Start with the decisions and baseline from [P00](p00-record-decisions-and-establish-baseline.md).
Use [P02](p02-add-finite-timer-driven-delivery-and-minimal-contracts.md) for recurring delivery and [P03](p03-establish-graph-identity-for-all-outcomes.md) before claiming
complete graph identification. P01 may be deferred.

## Scope and acceptance

- [ ] Implement and validate after UC12, UC16 and UC17. Fail/cancel the shared
  prerequisite and separately cancel one consuming root.
- Establish supported cancellation ownership first: cancelling one consumer must
  not be assumed to cancel shared work. Verify each consumer's actual outcome,
  unrelated-root isolation, single job identity and absence of hangs.

## Suggested commit

`feat(simulation): cover shared-dependency failure and cancellation`

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
