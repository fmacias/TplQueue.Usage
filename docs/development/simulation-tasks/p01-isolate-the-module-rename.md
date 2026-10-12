# P01 — Isolate the module rename

**Status:** Completed historical milestone (2026-09-24).

This is historical context, not an active implementation specification. See the
[current use-case catalog](../simulation-use-case-plan.md).

## Retained outcome

The implementation project, assembly and namespaces were renamed from `TplQueue.Sample.Etl` to `TplQueue.Sample.Simulation`. The contracts project retained its name. This was a completed structural milestone, not a request to rename the contracts now.

## Historical context

At that point the implementation module also contained ETL responsibilities that now belong to the repository-local Domain project. Old assembly-qualified payload names and workspace references describe that earlier layout.

## Current relationship

Domain now owns factories, payloads, handlers, stores and wrappers; Simulation depends on Contracts. The [component diagram](../../architecture/blazor-sample-components.puml) describes current references. `TplQueue.Sample.Contracts` is only a future naming proposal.
