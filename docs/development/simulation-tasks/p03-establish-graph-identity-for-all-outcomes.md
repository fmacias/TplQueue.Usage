# P03 — Establish graph identity for all outcomes

**Status:** Completed historical milestone (2026-09-24).

This is historical context, not an active implementation specification. See the
[current use-case catalog](../simulation-use-case-plan.md).

## Retained outcome

`ISimulationGraphCatalog` captured composition membership before enqueue and retained every root ID for shared jobs. Projection/monitor contracts gained `rootJobIds`, allowing root identity before success and through failure/cancellation. The root ID identifies a one-root run; no extra run GUID is needed.

## Historical context

FIFO ordering edges did not merge composed memberships. The compatible singular root ID remained self for a root, the sole root for an unshared job and null for a shared non-root. Shared-selection fixtures demonstrated presentation behavior, not every public cross-queue graph composition.

## Current relationship

These identity rules remain in the [architecture guide](../../architecture/blazor-consumer-sample.md#simulation-graph-identity). The catalog retains membership for the process session; bounded retention is UC25. Membership alone is never acceptance, execution ownership or a lifecycle observation.
