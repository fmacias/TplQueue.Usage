# Usage

This section groups the consumer-facing usage material for `TplQueue.Core`.

## Getting started

Use the step-by-step usage guide here:

- [Getting started](getting-started.md)

That guide covers:

- creating `ICoreApi`
- composing `IJob` and `IJobRoot`
- using `IDataJob` and `IDataJobRoot`
- enqueueing work on `IParallelQ` and `IFifoQ`
- observing queue events
- using retry-policy hooks
- understanding where adapter-side cache and serializer helpers fit

## Public package-consumption examples

For full runnable package-based examples outside the private source layout, use [TplQueue.Usage](https://github.com/fmacias/TplQueue.Usage).

The smallest rooted-graph composition used by the public samples is:

```csharp
extract.Then(transform).Then(load);
queue.Enqueue(load, workflowCancellation.Token);
await queue.Wait().ConfigureAwait(false);
```

Canonical runnable examples:

- [PackageConsumptionSmokeConsole](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/PackageConsumptionSmokeConsole)
  Key focus: minimal `IJob` / `IJobRoot` composition and queue semantics.
- [QueueObserverConsole](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/QueueObserverConsole)
  Key focus: `Extract -> Transform -> Load`, observers, and graceful queue finalization.
- [QueueObserverSignalRDashboard](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/QueueObserverSignalRDashboard)
  Key focus: long-lived metadata and payload queues plus explicit browser DTO projection.
