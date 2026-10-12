# P02 — Add finite timer-driven delivery and minimal contracts

**Status:** Completed historical milestone (2026-09-24).

This is historical context, not an active implementation specification. See the
[current use-case catalog](../simulation-use-case-plan.md).

## Retained outcome

Scheduling, admission and session coordination moved into Simulation. The host attached observers before starting delivery. Stop closed admission and awaited admitted submissions without draining or canceling accepted jobs; Completion described delivery, not execution.

## Historical context

The original form used independently configured finite scenarios per queue, two ticks, a one-second startup offset and three-second interval. That form and its settings were later removed. Its completed status does not require restoring it.

## Current relationship

`ScheduledWorkflow` now owns one `System.Threading.Timer` per workflow across all queues, with fixed continuous timing and per-queue admission. ETL and SingleJob run together. See [current delivery](../../architecture/blazor-consumer-sample.md#continuous-combined-demo); the old timer choice and finite settings are not active instructions.
