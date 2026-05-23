# Usage

This guide shows the current public `TplQueue.Core` API.

It uses the current runtime model:

- `ICoreApi`
- `IJob` and `IJobRoot`
- `IDataJob` and `IDataJobRoot`
- `IQ`, `IParallelQ`, `IFifoQ`, and `ICacheQ`
- `IObservable<IJobEvent>`

For broader behavior, queue internals, and architectural notes, see [Core reference](../reference.md) and [Architecture](../architecture/index.md). For named queue creation, logging observers, UI observers, cache modules, serialization, and DI helpers, see the [TplQueue.Adapter documentation](https://github.com/fmacias/TplQueue.Adapter/blob/main/docs/index.md).

For complete runnable package-based implementations of the patterns shown below, see:

- [PackageConsumptionSmokeConsole](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/PackageConsumptionSmokeConsole)
- [QueueObserverConsole](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/QueueObserverConsole)
- [QueueObserverSignalRDashboard](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/QueueObserverSignalRDashboard)

## 0. Prepare

Create the Core facade and the loggers used by queue factories.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;

ICoreApi core = CoreApi.Create();

ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.ClearProviders());
ILogger<IParallelQ> parallelLogger = loggerFactory.CreateLogger<IParallelQ>();
ILogger<IFifoQ> fifoLogger = loggerFactory.CreateLogger<IFifoQ>();
```

## 1. Create queues

Use `IQFactory` to create the runtime dispatcher that will execute your job graphs.

```csharp
IParallelQ parallelQ = core.QFactory.Parallel(
    Guid.NewGuid(),
    "parallel-main",
    maxParallelism: 4,
    logger: parallelLogger);

IFifoQ fifoQ = core.QFactory.Fifo(
    Guid.NewGuid(),
    "fifo-main",
    logger: fifoLogger);
```

- `IParallelQ` runs work with bounded concurrency.
- `IFifoQ` serializes the whole queue.

## 2. Compose a job graph

Create regular jobs through `IJobFactory` and terminate the graph with an enqueueable root.

```csharp
IJob extract = core.JobFactory.Job(
    async ct => await Task.CompletedTask,
    name: "Extract");

IJob transform = core.JobFactory.Job(
    async ct => await Task.CompletedTask,
    name: "Transform");

IJobRoot root = core.JobFactory.JobRoot(
    async ct => await Task.CompletedTask,
    name: "ImportRoot");

transform.After(extract);
root.After(transform);
```

The root-terminal rule matters here:

- `IJob` may depend only on other non-root jobs.
- `IJobRoot` is the enqueueable terminal node.
- `job.After(root)` is invalid in the current runtime.

## 3. Enqueue work

You can enqueue a prebuilt root graph or use the convenience overloads on the queue itself.

### Enqueue a composed graph

```csharp
parallelQ.Enqueue(root, CancellationToken.None);
fifoQ.Enqueue(root, CancellationToken.None);
```

### Enqueue simple delegates directly

```csharp
parallelQ.Enqueue(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Parallel-A");

parallelQ.EnqueueFifo(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Ordered-A");

parallelQ.Enqueue<int>(
    async (ct, batchSize) => await Task.CompletedTask,
    arg: 25,
    ct: CancellationToken.None,
    name: "LoadBatch");

fifoQ.Enqueue(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Step-1");
```

The same overload families exist for:

- synchronous `Action<CancellationToken>`
- asynchronous `Func<CancellationToken, Task>`
- one-argument delegates
- two-argument delegates

## 4. Payload-aware graphs

Use `IDataJobFactory` when the work item must carry a payload.

```csharp
public sealed class MeasurementPayload : IPayload
{
    public string SensorId { get; set; } = string.Empty;
    public double Value { get; set; }
    public string PayloadId => "measurements.persist/v1";
    public DateTime CollectionTime => DateTime.UtcNow;
}

public sealed class MeasurementPayloadHandler : IHandler
{
    public Task HandleAsync(IPayload payload, CancellationToken ct)
    {
        var typed = (MeasurementPayload)payload;
        return Task.CompletedTask;
    }
}

IHandler handler = new MeasurementPayloadHandler();

IDataJob<MeasurementPayload> persistMeasurement = core.DataJobFactory.DataJob(
    new MeasurementPayload { SensorId = "S-01", Value = 12.5 },
    handler,
    name: "PersistMeasurement");

IDataJobRoot<MeasurementPayload> measurementRoot = core.DataJobFactory.DataJobRoot(
    new MeasurementPayload { SensorId = "S-01", Value = 12.5 },
    handler,
    name: "MeasurementRoot");

measurementRoot.After(persistMeasurement);
parallelQ.Enqueue(measurementRoot, CancellationToken.None);
```

For cache-backed hydration, prefer Adapter `IApi.RegisterPayloadHandler(...)` so the payload `PayloadId` is treated as the stable persisted handler key. Core executes the public `IHandler` contract, while Adapter owns the internal `IPayloadHandlers` registry used during hydration.

The same composition rule applies here: build payload jobs first, then attach the payload root as the terminal enqueueable node.

## 5. Observe queue events

Every `IQ` is an `IObservable<IJobEvent>`.

### Subscribe with a custom observer

```csharp
public sealed class QueueObserver : IObserver<IJobEvent>
{
    public void OnNext(IJobEvent value)
    {
        Console.WriteLine($"{value.JobInfo.Name} -> {value.Status}");
    }

    public void OnError(Exception error)
    {
        Console.WriteLine(error.Message);
    }

    public void OnCompleted()
    {
        // ObserverHub does not publish OnCompleted; terminal job states arrive through OnNext.
    }
}

using IDisposable subscription = parallelQ.Subscribe(new QueueObserver());
```

The most useful event fields for monitoring are:

- `value.Status`
- `value.JobInfo.Id`
- `value.JobInfo.Name`
- `value.JobInfo.CrossQueueId`
- `value.Timestamp`
- `value.RetryCount`
- `value.Exception`

### Use the async event hook

`IQ` also exposes `OnJobEventChanged` for lightweight async forwarding:

```csharp
parallelQ.OnJobEventChanged = evt =>
{
    Console.WriteLine($"{evt.Timestamp:O} | {evt.JobInfo.Name} | {evt.Status}");
    return Task.CompletedTask;
};
```

For UI-thread dispatching, ready-made observers, custom observer examples, and dashboard bridge patterns, see [Fmacias.TplQueue.Observers README](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Observers/README.md).

## 6. Retry policies

Retry behavior can be defined either at queue level or root level.

```csharp
IParallelQ resilientQueue = core.QFactory.Parallel(
    Guid.NewGuid(),
    "resilient",
    maxParallelism: 4,
    logger: parallelLogger,
    retryPolicyFactory: () => NoRetryPolicy.Create());

IJobRoot resilientRoot = core.JobFactory.JobRoot(
    async ct => await Task.CompletedTask,
    retryPolicyFactory: () => NoRetryPolicy.Create(),
    name: "ResilientRoot");
```

Precedence is:

1. root-level retry policy
2. queue-level retry policy
3. default `NoRetryPolicy`

Concrete retry-policy families such as `LinearBackoff` and `ExponentialBackoff` are documented in [Fmacias.TplQueue.RetryPolicies](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.RetryPolicies/README.md).

## 7. Cache-backed orchestration

`TplQueue.Core` can create `ICacheQ`, but the actual cache implementation belongs to `TplQueue.Adapter`.

```csharp
ILogger<ICacheQ> cacheLogger = loggerFactory.CreateLogger<ICacheQ>();
ICacheQ cacheQ = core.QFactory.CacheQ(cacheLogger, payloadCache, parallelQ);
```

In that example:

- `payloadCache` is an adapter-side `IDataJobCache` implementation
- `parallelQ` is the execution queue used after cache-backed recovery or leasing

See:

- [Fmacias.TplQueue.Cache.Abstract](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Cache.Abstract/README.md)
- [Fmacias.TplQueue.Cache.MemCache](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Cache.MemCache/README.md)

## 8. Adapter-side convenience APIs

Use `TplQueue.Adapter` when you need:

- named queue creation through `IQFactoryAdapter`
- concrete retry-policy factories
- built-in observer factories and `IObserverDispatcher` support
- serialization helpers
- Microsoft DI registration helpers

The entry point for that layer is `IApi`, documented in [Fmacias.TplQueue](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue/README.md).
