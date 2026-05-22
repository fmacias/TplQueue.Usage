# Usage

This section groups the consumer-facing usage material for `TplQueue.Usage`.

## Overview

Start with the repository overview:

- [Overview](overview.md)

## Canonical runnable examples

These runnable samples are the canonical examples for the rest of the TplQueue documentation set:

- [PackageConsumptionSmokeConsole](../../samples/PackageConsumptionSmokeConsole/README.md)
  Key focus: the minimal package-consumption surface for `IJob`, `IJobRoot`, `IParallelQ`, `IFifoQ`, observers, retry policy selection, and payload-cache hydration.
- [QueueObserverConsole](../../samples/QueueObserverConsole/README.md)
  Key focus: one queue created through the adapter facade, one `Extract -> Transform -> Load` graph, one standalone helper task in the same dispatcher, and logging/profiling observers.
- [QueueObserverSignalRDashboard](../../samples/QueueObserverSignalRDashboard/README.md)
  Key focus: `AddTplQueue(...)`, configuration-driven queue registration, two long-lived injected queues, and observer-fed DTO projection to the browser.

## Consumer notes

Representative consumer notes are grouped in:

- [Consumers README](../../consumers/README.md)

The package-based verification surface remains under `test/integration/`.
