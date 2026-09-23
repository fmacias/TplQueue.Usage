# P02 - Add finite timer-driven delivery and minimal contracts

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

Pending. When work starts, copy the main plan's
[iteration record template](../simulation-use-case-plan.md#iteration-record) here.
Record exact checks, outcomes, skipped checks and blockers. Update the task status
and the main checklist together; mark complete only after acceptance passes.
