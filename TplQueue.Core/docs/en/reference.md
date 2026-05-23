# TplQueue.Core

`TplQueue.Core` is the execution kernel of the TplQueue ecosystem. It provides dependency-aware `Job` graph execution, bounded-concurrency and FIFO queue dispatchers, retry-policy integration points, and observable lifecycle events for monitoring, diagnostics, and UI integration.

It is intentionally a low-level concurrency and execution library, not a business-workflow engine. Payload-specific persistence, cache-backed recovery, concrete retry-policy implementations, logging observers, serializer implementations, and DI helpers are provided by companion modules in [TplQueue.Adapter](https://github.com/fmacias/TplQueue.Adapter/blob/main/README.md).

## Install

Install from NuGet:

```bash
dotnet add package Fmacias.TplQueue.Core --version 0.1.0-preview.1
```

`TplQueue.Core` is distributed under the repository license file and currently requires license acceptance when consumed from NuGet. The package license is intended for official binary use; source-code and private-repository access are governed separately.

## Public usage repository

For package-based consumer samples, public integration tests, and observer-oriented scenarios that validate the binary line without private `TplQueue.Core` project references, see [TplQueue.Usage](https://github.com/fmacias/TplQueue.Usage).

`TplQueue.Usage` consumes the published packages and acts as the public-facing verification repository for the preview line. The current runnable sample entry points are [QueueObserverConsole](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/QueueObserverConsole) and [QueueObserverSignalRDashboard](https://github.com/fmacias/TplQueue.Usage/tree/main/samples/QueueObserverSignalRDashboard).

## Table of contents

- [Install](#install)
- [Public usage repository](#public-usage-repository)
- [Overview](#overview)
- [Goals](#goals)
- [Use cases](#use-cases)
- [C# language-version policy](#c-language-version-policy)
- [Jobs](#jobs)
- [Job roots](#job-roots)
- [Root-terminal composition rule](#root-terminal-composition-rule)
- [Payload jobs (`IDataJob`)](#payload-jobs-idatajob)
- [Payload job roots (`IDataJobRoot`)](#payload-job-roots-idatajobroot)
- [Queues](#queues)
  - [Parallel queue](#parallel-queue)
  - [FIFO queue](#fifo-queue)
  - [Cache queue](#cache-queue)
  - [Cross-queue shared job ownership](#cross-queue-shared-job-ownership)
  - [Why the dequeue strategy is reactive instead of polling](#why-the-dequeue-strategy-is-reactive-instead-of-polling)
- [Retry policies](#retry-policies)
- [Observers](#observers)
  - [Why `JobObserverHub` uses `SemaphoreSlim` instead of `Task.Run` as the dispatch model](#why-jobobserverhub-uses-semaphoreslim-instead-of-taskrun-as-the-dispatch-model)
- [Cache and persistence](#cache-and-persistence)
- [Service-by-service usage](#service-by-service-usage)
  - [`CoreApi`](#coreapi)
  - [`IJobFactory`](#ijobfactory)
  - [`IQFactory`](#iqfactory)
  - [`IDataJobFactory`](#idatajobfactory)
- [Build and test](#build-and-test)
- [Strong-name signing](#strong-name-signing)
- [License](#license)

## Overview

`TplQueue.Core` owns the runtime model for:

- `Job` and `JobRoot` graph composition
- queue-based dispatch through `IQ`, `IParallelQ`, `IFifoQ`, and `ICacheQ`
- bounded concurrency using `SemaphoreSlim`
- strict FIFO execution where required
- queue-level or root-level retry-policy selection
- observable execution through `IObservable<IJobEvent>`

The repository is optimized for controlled asynchronous execution on `.NET Standard 2.0`, where newer primitives such as `System.Threading.Channels` are not available by default. Internally, the runtime therefore relies on focused async coordination primitives such as `AsyncOrderedQueue`, `AsyncAutoResetEvent`, and `SemaphoreSlim` rather than continuous busy loops.

[`TplQueue.Adapter`](https://github.com/fmacias/TplQueue.Adapter/blob/main/README.md) complements this repository with concrete integrations such as logging observers, retry-policy factories, serializer implementations, cache implementations, and dependency-injection registration helpers.

## Goals

The main goal of `TplQueue.Core` is to solve real concurrency and execution-ordering problems.

It is designed to help with:

- controlled concurrent execution of background work
- synchronization of ordering-sensitive operations
- dependency-aware execution of multi-step job graphs
- queue-level execution policies such as bounded parallelism or strict FIFO behavior
- operational visibility through lifecycle events
- fault handling through retry-policy integration points
- future recovery or persistence scenarios through adapter-side cache and serialization modules

In practice, this means you can express work as small `Job` nodes, compose them into a graph, submit the graph through a queue, and observe what happened without coupling your domain logic to a specific host, UI, or transport.

## Use cases

Typical architectural uses include:

- backend worker services
- background processing inside web applications or microservices
- industrial integration layers and gateway processes
- ETL and data-ingestion flows and middlewares.
- IoT edge services that must coordinate ordered and parallel work
- desktop or thick-client applications that need observable background execution
- monitoring dashboards fed from job lifecycle events

A common pattern is to use `TplQueue.Core` as the execution substrate and then layer project-specific orchestration semantics outside the library.

## C# language-version policy

The shipped `netstandard2.0` product line is pinned to `LangVersion=9.0`.

This is a source-build policy for `TplQueue.Core`, not a runtime requirement for applications that reference the compiled package. Consumers targeting classic `.NET Framework` or any other `netstandard2.0`-compatible runtime do not need to change their own project `LangVersion` only to consume `Fmacias.TplQueue.Core`.

`9.0` is the intentional floor because it preserves the current nullable-aware source style and the C# 9 syntax already used in the product code, while removing the accidental dependency on `LangVersion=latest`. If you build this repository from source, use a .NET SDK that supports C# 9 or later.

## Jobs

`IJob` represents a single executable unit of work inside a graph. A job can be synchronous or asynchronous, and factory overloads support parameterless delegates as well as delegates with one or two arguments.

Create jobs through `IJobFactory`:

```csharp
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;

ICoreApi core = CoreApi.Create();

IJob extract = core.JobFactory.Job(async ct =>
{
    await Task.CompletedTask;
}, name: "Extract");

IJob transform = core.JobFactory.Job(async ct =>
{
    await Task.CompletedTask;
}, name: "Transform");
```

Dependencies are expressed explicitly instead of being hidden in callback chains. The usual composition style is `After(...)` or the fluent `Then(...)` extension:

```csharp
extract.Then(transform);
```

This produces a graph with a clear execution dependency.

Where possible, jobs should model deterministic and ideally idempotent work. That is especially useful when queues apply retries or when the same graph is monitored externally.

A regular `IJob` can depend only on other regular jobs. To terminate the graph in an enqueueable node, attach an `IJobRoot` at the end with `root.After(job)` or `job.Then(root)`.

### How the graph is expanded internally

> it documents one of the most important internal design decisions—the graph is traversed depth-first and appended child-first, root-last.

```csharp
public IReadOnlyList<IJob> ExpandGraph(IJobRoot jobRoot)
{
    if (jobRoot == null) throw new ArgumentNullException(nameof(jobRoot));

    var results = new List<IJob>();
    var visited = new HashSet<Guid>();

    DFS(jobRoot, visited, results);
    return results;
}

private static void DFS(
    IJob current,
    HashSet<Guid> visited,
    List<IJob> output)
{
    if (!visited.Add(current.Id))
        return;

    var jobs = current.GetJobsBatch();

    if (jobs != null)
    {
        foreach (var job in jobs)
        {
            if (job != null)
                DFS(job, visited, output);
        }
    }

    output.Add(current);
}
```

## Job roots

`IJobRoot` is the enqueueable entry point of a job graph. You build a graph from `IJob` and `IJobRoot` nodes, but the root is the element you submit to a queue and it is expected to remain the terminal node of that graph.

```csharp

IJobRoot root = core.JobFactory.JobRoot(
    async ct =>
    {
        await Task.CompletedTask;
    },
    name: "ImportRoot");

root.After(transform);
```

### Root-terminal composition rule

The current runtime prevents non-root jobs from depending on an `IJobRoot`. In practice, `job.After(root)` is invalid and throws `InvalidOperationException`, because the enqueued root must remain the last node in the graph.

Use one of these valid forms instead:

```csharp
extract.Then(transform);
transform.Then(root);

// Equivalent:
// root.After(transform);
```

This rule keeps graph ownership and enqueue semantics unambiguous: dependencies flow toward the root, and the root is the node that the queue claims and enqueues.

A root can also provide its own retry policy factory:

```csharp
using Fmacias.TplQueue.Defaults;

IJobRoot resilientRoot = core.JobFactory.JobRoot(
    async ct =>
    {
        await Task.CompletedTask;
    },
    retryPolicyFactory: () => NoRetryPolicy.Create(),
    name: "ResilientRoot");
```

Retry selection follows this precedence:

1. root-level retry policy, if the root provides one
2. queue-level retry policy, if the queue was configured with one
3. the default `NoRetryPolicy`

This lets a queue define a general operational policy while still allowing a particular graph to override it when needed.

## Payload jobs (`IDataJob`)

`IDataJob` extends `IJob` with a payload. It is the payload-aware node type used when execution must carry domain data together with the work item.

Publicly, the model is exposed through:

- `IDataJob`
- `IDataJob<T>`
- `IPayload`
- `IHandler`

Create payload jobs through `IDataJobFactory`:

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

var payloadJob = core.DataJobFactory.DataJob(
    new MeasurementPayload { SensorId = "S-01", Value = 12.5 },
    handler,
    name: "PersistMeasurement");
```

As with plain jobs, payload jobs can participate in dependency graphs through `After(...)` and `Then(...)`.

When payload jobs must be dehydrated and hydrated through adapter-side caches, register the handler behavior through the Adapter `IApi` facade. Core consumes the public `IHandler` contract, while Adapter owns key-based handler registration and the internal `IPayloadHandlers` resolution path used during hydration.

## Payload job roots (`IDataJobRoot`)

`IDataJobRoot` is the enqueueable payload-aware graph entry point.

Use it when a graph must carry a strongly typed payload from the start of execution, typically in scenarios involving persistence, cache-backed recovery, or integration with external transports.

```csharp
var payloadRoot = core.DataJobFactory.DataJobRoot(
    new MeasurementPayload { SensorId = "S-01", Value = 12.5 },
    handler,
    name: "MeasurementRoot");

payloadRoot.After(payloadJob);
```

Like `IJobRoot`, a payload root can define a retry policy factory that overrides the queue default for that graph.

The same terminal-node rule applies to payload-aware graphs: build payload jobs first, then attach the `IDataJobRoot` at the end. Do not model a non-root payload job as depending on a payload root.

## Adapter payload handler integration

Current state:

- keep `IHandler` as the public execution contract used by `IDataJobFactory`
- prefer adapter-side `IApi.RegisterPayloadHandler(...)` registration for cache hydration and plugin-style handler resolution
- keep Core's `UniversalPayloadHandler` wrapper internal to the execution pipeline

Serializer selection, cache hydration, and API-owned payload handler registration are documented in [TplQueue.Adapter](https://github.com/fmacias/TplQueue.Adapter/blob/main/README.md). Core stays focused on execution while Adapter owns payload handler registration, cache dehydration/hydration, and serializer modules.

## Queues

Queues are the runtime dispatchers that execute job graphs.

The public queue contracts are:

- `IQ`
- `IParallelQ`
- `IFifoQ`
- `ICacheQ`

Create queues through `IQFactory`:

```csharp
using Microsoft.Extensions.Logging;

ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.ClearProviders());
ILogger parallelLogger = loggerFactory.CreateLogger<IParallelQ>();
ILogger fifoLogger = loggerFactory.CreateLogger<IFifoQ>();

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

Concrete queues are hot by default in the current implementation. `QAbstract` creates the background scheduler in the constructor, so `ResumePolling()` is best understood as "resume after `Pause()`" rather than mandatory first activation. If you need to buffer work before releasing it, call `Pause()` immediately after queue creation, enqueue the roots, and then call `ResumePolling()`. `ICacheQ` uses `ResumePolling()` both to resume the wrapped queue and to enable cache leasing.

### Parallel queue

`IParallelQ` executes work with bounded concurrency. The bound is enforced internally through `SemaphoreSlim`.

```csharp
parallelQ.Enqueue(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Parallel-A");

parallelQ.Enqueue(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Parallel-B");
```

`IParallelQ` also supports FIFO-scoped enqueueing. That is useful when most work may run concurrently but a subset must remain ordered:

```csharp
parallelQ.EnqueueFifo(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Ordered-1");

parallelQ.EnqueueFifo(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Ordered-2");
```

Internally, FIFO-scoped additions are linked by dependency so that the ordered segment remains serialized even though the queue itself is still parallel.

### FIFO queue

`IFifoQ` enforces serialized execution for the whole queue.

```csharp
fifoQ.Enqueue(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Step-1");

fifoQ.Enqueue(
    async ct => await Task.CompletedTask,
    CancellationToken.None,
    name: "Step-2");
```

Use this queue when ordering is part of the contract and not just an optimization detail.

### Cache queue

`ICacheQ` extends queue execution with cache-backed dehydration and leasing of payload roots.

It is created from:

- an existing `IParallelQ`
- an `IDataJobCache`
- an `ILogger<ICacheQ>`

The cache itself is still an adapter concern. In a real application, `payloadCache` normally comes from [TplQueue.Adapter](https://github.com/fmacias/TplQueue.Adapter/blob/main/README.md) modules such as [Fmacias.TplQueue.Cache.MemCache](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Cache.MemCache/README.md), with serializer, type resolver, retry factory, and payload handler registration already composed there.

```csharp
// Assume payloadCache was created by an adapter cache module.
// IDataJobCache payloadCache;

ILogger<IParallelQ> innerLogger = loggerFactory.CreateLogger<IParallelQ>();
ILogger<ICacheQ> cacheLogger = loggerFactory.CreateLogger<ICacheQ>();

IParallelQ innerQueue = core.QFactory.Parallel(
    Guid.NewGuid(),
    "cache-inner-parallel",
    maxParallelism: 4,
    logger: innerLogger);

ICacheQ cacheQ = core.QFactory.CacheQ(
    cacheLogger,
    payloadCache,
    innerQueue);
```

`ICacheQ.Enqueue(...)` and `ICacheQ.EnqueueFifo(...)` do not immediately dispatch the original payload root. They first dehydrate the payload graph into the cache. Once `ResumePolling()` is called, `CacheQ` enables its leasing loop, hydrates pending roots from the cache when the wrapped parallel queue has capacity, and enqueues the hydrated root into that wrapped queue.

```csharp
IHandler handler = new MeasurementPayloadHandler();

IDataJobRoot<MeasurementPayload> root = core.DataJobFactory.DataJobRoot(
    new MeasurementPayload { SensorId = "S-01", Value = 12.5 },
    handler,
    name: "PersistMeasurement");

IDataJob<MeasurementPayload> child = core.DataJobFactory.DataJob(
    new MeasurementPayload { SensorId = "S-02", Value = 18.2 },
    handler,
    name: "PersistMeasurementChild");

root.After(child);

// Non-FIFO cache-backed dispatch:
cacheQ.Enqueue(root, CancellationToken.None);

// FIFO cache-backed dispatch is available when the hydrated graph must
// preserve root-level FIFO ordering:
// cacheQ.EnqueueFifo(root, CancellationToken.None);

cacheQ.LeasingPulseMs = 100;
cacheQ.ResumePolling();

```

During execution, `CacheQ` listens to job lifecycle events from the wrapped queue and updates the cache state:

- successful data-job nodes are acknowledged through `IDataJobCache.AckNode(...)`
- failed nodes are marked through `IDataJobCache.FailNode(...)`
- canceled nodes are marked through `IDataJobCache.CancelNode(...)`
- successful roots are finalized through `IDataJobCache.SuccessRootNode(...)`
- terminal roots are deleted when the cache reports that the graph can be removed

`ICacheQ` is still part of the Core runtime surface, but it delegates actual persistence, serialization, type resolution, and payload-handler registration concerns to adapter-side cache abstractions and implementations.

### Cancellation behavior and dispatcher slot release

Per-enqueue cancellation is tracked both before dispatch and during execution:

- if a token is already canceled when the scheduler is ready to dispatch the job, the job is marked as `Canceled`, it is not executed, and the dispatcher slot is released immediately
- if cancellation happens after execution has started, the running job observes the token, the queue publishes `Canceled`, and the slot is released during finalization so later jobs can continue

This matters most on FIFO queues and `maxParallelism: 1` dispatchers, where leaking the slot would stall every following `IJobRoot`.

### Cross-queue shared job ownership

The queue-ownership model is important when a shared job instance appears in more than one graph or queue relation.

The current behavior is:

- before enqueue, a shared job instance has no final queue owner
- the first queue that enqueues that specific job instance claims execution ownership
- later queues may still reference that job as a dependency
- later queues must not execute that same instance again
- `CrossQueueId` is observable runtime state, not user-configurable mutable setup

This first-enqueue-wins rule prevents double execution while still allowing graph composition before submission.

> It shows that the whole expanded graph is materialized and queued from the root entry point.

```csharp
protected void EnqueueGraph(IJobRoot jobRoot, bool isFifo, CancellationToken cancellationToken)
{
    var jobs = JobGraphExpander.Create().ExpandGraph(jobRoot);

    foreach (var jobNode in jobs)
    {
        _queue.Enqueue(
            item: jobNode,
            jobRootId: jobRoot.Id,
            isFifo: isFifo,
            queueId: QueueId,
            cancellationToken: cancellationToken
        );
    }
}
```
### Why the dequeue strategy is reactive instead of polling

The active queue path is reactive, not based on a permanent busy polling loop over the work buffer.

> It shows the three key runtime pieces together: bounded concurrency, async queue, and background scheduler startup.

```csharp
private readonly SemaphoreSlim _semaphore;
private readonly AsyncOrderedQueue<IJobNode> _queue;
private readonly CancellationTokenSource _cts = new CancellationTokenSource();
private readonly Task _schedulerTask;

protected QAbstract(
    Guid id,
    string name,
    int maxParallelism,
    Func<IRetryPolicy>? retryPolicyFactory = null)
{
    if (id == null || id == Guid.Empty) throw new ArgumentNullException(nameof(id));
    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
    if (maxParallelism <= 0) throw new ArgumentOutOfRangeException(nameof(maxParallelism));

    QueueId = id;
    Name = name;
    MaxParallelism = maxParallelism;

    if (retryPolicyFactory != null)
    {
        _jobQRetryPolicyFactory = retryPolicyFactory;
    }

    _semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
    _queue = new AsyncOrderedQueue<IJobNode>();
    _isPolling = true;
    _schedulerTask = Task.Run(() => SchedulerLoop());
}
```
> Clear proof that the queue is reactive and does not spin continuously.

```csharp
public async Task<(T Item, Guid RootId, CancellationToken CancellationToken, bool IsFifo)> DequeueAsync(
    CancellationToken cancellationToken)
{
    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();

        QueueEntry entry;
        lock (_sync)
        {
            if (_items.Count > 0)
            {
                entry = _items.Dequeue();
                return (entry.Item, entry.JobRootId, entry.CancellationToken, entry.IsFifo);
            }
        }

        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

Internally:

- `AsyncOrderedQueue<T>` stores queued items
- `AsyncAutoResetEvent` wakes the consumer only when work arrives
- `SemaphoreSlim` limits how many jobs may run concurrently
- `SchedulerLoop` waits for capacity first and then awaits the next queue item asynchronously

This is more appropriate than a classic `while(true)` polling loop that repeatedly checks whether work exists because it:

- avoids wasting CPU when the queue is idle
- reduces unnecessary allocations and wake-ups
- preserves async cancellation semantics cleanly
- scales better for long-lived services where idle periods are normal
- fits `.NET Standard 2.0` without introducing external dependencies

There is still a small timed wait when a queue is explicitly paused, because pause/resume is a different concern from normal dequeueing. In normal running mode, however, queue consumption is signal-driven rather than poll-driven.

> It completes the story—producer enqueues, then wakes exactly one awaiting consumer path.

```csharp
public void Enqueue(
    T item,
    Guid jobRootId,
    bool isFifo,
    Guid queueId,
    CancellationToken cancellationToken)
{
    if (item == null) throw new ArgumentNullException(nameof(item));
    if (queueId == Guid.Empty) throw new ArgumentException("Queue id cannot be empty.", nameof(queueId));

    lock (_sync)
    {
        var commandJob = Job.JobCommand(item);
        if (!commandJob.TryBindQueue(queueId))
        {
            return;
        }

        _items.Enqueue(new QueueEntry(item, jobRootId, isFifo, queueId, cancellationToken));
    }

    _signal.Set();
}
```

### Pause / ResumePolling behavior and buffered enqueue semantics

A queue can be paused and resumed again by the consumer. In the current implementation the scheduler is created and started with the queue itself, so `Pause()` / `ResumePolling()` work as a cooperative runtime gate over that already-running scheduler. Internally, this behavior is controlled through the `_isPolling` flag in `QAbstract`, while dequeue operations remain driven by `SemaphoreSlim`, `AsyncOrderedQueue<T>`, and `AsyncAutoResetEvent`. When the queue is paused, the scheduler now waits on an async resume signal instead of periodically sleeping.

A relevant implementation question is whether jobs enqueued while the queue is paused are preserved and later executed after `ResumePolling()` is called again.

The current implementation indicates that they are **normally preserved and later executed**, but with one important nuance: `Pause()` behaves as a **soft pause**, not as an immediate hard stop.

#### Step 1. Pause the queue

Pausing the queue only changes the `_isPolling` flag.

```csharp
public void Pause()
{
    EnsureNotDisposed();
    _isPolling = false;
}
```

The scheduler loop checks that flag on every iteration and waits for the resume signal when needed:

```csharp
private async Task SchedulerLoop()
{
    var ct = _cts.Token;

    try
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            if (!_isPolling)
            {
                await _resumeSignal.WaitAsync(ct).ConfigureAwait(false);
                continue;
            }

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);

            var (job, dequeuedjobRootId, jobCancellationToken, _) =
                await _queue.DequeueAsync(ct).ConfigureAwait(false);

            RunJobAsync(job, jobCancellationToken);
        }
    }
    ...
}
```

From this point on, the scheduler stops consuming new items while `_isPolling == false`, and it does so without a periodic polling delay.

However, the pause is not strictly instantaneous. If the scheduler already passed the `_isPolling` check before `Pause()` is called, one more job may still continue into dequeue and execution. This is why the current pause semantics should be understood as *cooperative* rather than *synchronous*.

---

#### Step 2. Enqueue a job while paused

Public enqueue operations still enqueue graphs normally while the queue is paused:

```csharp
protected IQ AddToQueue(IJobRoot jobRoot, bool isFifo, CancellationToken ct)
{
    EnsureNotDisposed();
    
    if (jobRoot == null) throw new ArgumentNullException(nameof(jobRoot));

    lock (EnqueueLock)
    {
        FifoAddition(jobRoot, isFifo);
        EnqueueGraph(jobRoot, isFifo, cancellationToken: ct);
    }
    return this;
}
```

```csharp
protected void EnqueueGraph(IJobRoot jobRoot, bool isFifo, CancellationToken cancellationToken)
{
    var jobs = JobGraphExpander.Create().ExpandGraph(jobRoot);

    foreach (var jobNode in jobs)
    {
        _queue.Enqueue(
            item: jobNode,
            jobRootId: jobRoot.Id,
            isFifo: isFifo,
            queueId: QueueId,
            cancellationToken: cancellationToken
        );
    }
}
```

At the queue-storage level, the item is still added to the internal queue:

```csharp
public void Enqueue(
    T item,
    Guid jobRootId,
    bool isFifo,
    Guid queueId,
    CancellationToken cancellationToken)
{
    if (item == null) throw new ArgumentNullException(nameof(item));
    if (queueId == Guid.Empty) throw new ArgumentException("Queue id cannot be empty.", nameof(queueId));

    lock (_sync)
    {
        var commandJob = Job.JobCommand(item);
        if (!commandJob.TryBindQueue(queueId))
        {
            return;
        }

        _items.Enqueue(new QueueEntry(item, jobRootId, isFifo, queueId, cancellationToken));
    }

    _signal.Set();
}
```

This is the key point: while the queue is paused, enqueue still works. The scheduler is temporarily not consuming, but the item is still buffered into `_items`.

---

#### Step 3. Enqueue another job while paused

The same process happens again for the next job. Another queue entry is added and another signal is emitted.

Conceptually, after two enqueue operations during pause, the internal queue state looks like this:

```text
_items:
  [job1, job2]
```

Assuming both jobs were accepted by `TryBindQueue(queueId)`, both remain buffered in memory.

---

#### Why the signal is not usually lost during pause

One of the most relevant implementation details is the internal `AsyncAutoResetEvent`.

When an enqueue operation happens, `Set()` is called:

```csharp
public void Set()
{
    TaskCompletionSource<bool>? toRelease = null;

    lock (_waiters)
    {
        if (_waiters.Count > 0)
        {
            var first = _waiters.First;
            _waiters.RemoveFirst();
            toRelease = first.Value;
        }
        else if (!_signaled)
        {
            _signaled = true;
        }
    }

    toRelease?.TrySetResult(true);
}
```

When the consumer later waits, `WaitAsync()` checks whether a signal was already stored:

```csharp
public Task WaitAsync(CancellationToken cancellationToken = default)
{
    lock (_waiters)
    {
        if (_signaled)
        {
            _signaled = false;
            return _completed;
        }

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        LinkedListNode<TaskCompletionSource<bool>> node = _waiters.AddLast(tcs);
        ...
        return tcs.Task;
    }
}
```

This matters because while the queue is paused there may not be an active waiter at the exact moment `Set()` is called. In that case, the event stores the signal in `_signaled = true`, which allows the future consumer path to resume correctly after restart.

This is one of the reasons jobs enqueued during pause are not normally lost.

---

#### Step 4. Start the queue again

Restarting the queue flips `_isPolling` back to `true` and wakes the paused scheduler:

```csharp
public void ResumePolling()
{
    EnsureNotDisposed();
    var wasPolling = _isPolling;
    _isPolling = true;

    if (!wasPolling)
    {
        _resumeSignal.Set();
    }
}
```

The next scheduler iteration resumes normal execution:

```csharp
await _semaphore.WaitAsync(ct).ConfigureAwait(false);

var (job, dequeuedjobRootId, jobCancellationToken, _) =
    await _queue.DequeueAsync(ct).ConfigureAwait(false);

RunJobAsync(job, jobCancellationToken);
```

At the dequeue level, if items are already buffered, the queue returns immediately:

```csharp
public async Task<(T Item, Guid RootId, CancellationToken CancellationToken, bool IsFifo)> DequeueAsync(
    CancellationToken cancellationToken)
{
    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();

        QueueEntry entry;
        lock (_sync)
        {
            if (_items.Count > 0)
            {
                entry = _items.Dequeue();
                return (entry.Item, entry.JobRootId, entry.CancellationToken, entry.IsFifo);
            }
        }

        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

So after `ResumePolling()` is called again, buffered items are dequeued and execution continues.

---

#### Practical result for this sequence

Given the sequence:

1. `Pause()`
2. enqueue job A
3. enqueue job B
4. `ResumePolling()`

the current implementation indicates the following behavior:

- the scheduler pauses consumption of new items
- job A is still stored in the internal queue
- job B is still stored in the internal queue
- the wake-up signal is preserved
- after `ResumePolling()`, the scheduler resumes and drains the buffered items

Under normal conditions, both jobs should execute.

---

#### Important caveats

This does **not** mean execution is unconditionally guaranteed in every situation.

The buffered jobs may still fail to execute later if:

- the queue is disposed before restart
- `TryBindQueue(queueId)` rejects the job
- the job cancellation token is already canceled when execution starts

For example, execution is skipped if cancellation has already been requested:

```csharp
if (executionCt.IsCancellationRequested)
{
    Publish(JobEventStatus.Canceled, job, retryPolicy);
    _semaphore.Release();
    return;
}
```

Also, `Pause()` should not be interpreted as an immediate hard stop. There is a race window in which one more job may still start if the scheduler had already passed the `_isPolling` check before pause was requested.

---

#### Conclusion

From the current implementation, buffered jobs are normally preserved and later executed after `ResumePolling()`.

The real behavioral nuance is different:

- enqueue-during-pause is supported by the current design
- pause is soft and cooperative
- pause is not a strict synchronous barrier

That distinction is important when documenting runtime expectations for consumers of the queue API.

## Retry policies

`TplQueue.Core` integrates retry behavior through abstractions and factory delegates. It decides when retries are needed, but it does not need to own every concrete retry-policy implementation itself.

At Core level, the main contracts are:

- `IRetryPolicy`
- `Func<IRetryPolicy>` on queues and roots

Concrete retry implementations are typically obtained through [TplQueue.Adapter](https://github.com/fmacias/TplQueue.Adapter/blob/main/README.md), including:

- `NoRetryPolicy`
- `LinearBackoff`
- `ExponentialBackoff`
- jitter-aware policy variants where configured
- named and descriptor-based policy creation

Queue-level example:

```csharp
IParallelQ resilientQ = core.QFactory.Parallel(
    Guid.NewGuid(),
    "resilient",
    maxParallelism: 4,
    logger: parallelLogger,
    retryPolicyFactory: () => NoRetryPolicy.Create());
```

For the concrete factories and policy modules, see the Adapter documentation:

- [TplQueue.Adapter retry policies](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.RetryPolicies/README.md)

## Observers

Every `IQ` implements `IObservable<IJobEvent>`. That means queue execution can be observed through standard observer subscriptions.

Per-job execution failures remain part of the normal event stream and arrive through observer `OnNext` as `IJobEvent` values with `JobEventStatus.Failed`. Observer `OnError` is reserved for fatal dispatcher failures that transition the queue into an unusable state. If a host wants centralized diagnostics for ordinary job failures, it should subscribe to queue events or set `OnJobEventChanged` and handle failed statuses explicitly.

Typical event consumers include:

- logs and diagnostics
- metrics and profiling
- dashboards
- desktop or web UI updates
- SignalR or Rx forwarding
- operational audit streams

Observer delivery is intentionally asynchronous and must not block queue orchestration. Internally, publication is buffered and pumped through `JobObserverHub`, while observer failures are isolated from the queue engine.

Core documents the event stream contract and the internal publication model. The consumer-facing guide for built-in observers, custom observers, UI dispatchers, and dashboard bridges now lives in the observer adapter package: [Fmacias.TplQueue.Observers README](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Observers/README.md).

### Why `JobObserverHub` uses `SemaphoreSlim` instead of `Task.Run` as the dispatch model

`JobObserverHub` uses a queued pump based on `ConcurrentQueue<ObserverMessage>` and `SemaphoreSlim` rather than spawning a fresh `Task.Run` for every publication.

That choice has practical advantages:

- it avoids one task allocation and scheduler dispatch per event message
- it centralizes publication flow in a single long-lived pump
- it preserves message buffering semantics cleanly
- it reduces contention and scheduler noise during event bursts
- it makes graceful drain-and-stop disposal possible

The trade-off is that observer delivery is still asynchronous but not infinitely parallel. A slow observer can delay later observer messages inside the pump. Observers should therefore stay fast and forward runtime information to a third-party consumer, such as SignalR or another monitoring channel, without doing heavy work in the callback.

In other words, `SemaphoreSlim` is used here as a lightweight async "message available" signal for queued publication work, while `Task.Run` is reserved for launching the pump itself and for actual job execution where a separate asynchronous execution flow is justified. Each accepted observer message releases one semaphore permit, and the pump processes one queued message for each acquired permit.

Observer implementations should keep unsubscription cleanup fast. `JobObserverHub` does not publish `OnCompleted`; job lifecycle terminal states are already reported through `IJobEvent`. Disposal clears observer references after already queued messages drain where possible, so slow cleanup can delay memory release and keep observer instances referenced longer than necessary.

The per-message semaphore contract is intentionally simple:

1. A normal publication is accepted only while the hub is not disposed.
2. Each accepted publication enqueues one `ObserverMessage` into the `ConcurrentQueue<ObserverMessage>`.
3. Each accepted publication releases one semaphore permit.
4. The pump waits for one permit and processes one queued message.
5. The queue provides thread-safe storage; the hub lock only protects the publication/disposal boundary and observer snapshots.
6. Disposal is a best-effort shutdown path. It is expected near queue shutdown, not in the hot event path.
7. Disposal may release one extra shutdown permit to wake an idle pump. If no accepted messages remain after disposal starts, the pump exits gracefully.
8. If a publication races with shutdown and the hub can no longer signal the pump, the issue is routed through the observer error sink as a warning instead of synchronizing the hot publication path.

> It captures the architecture of the observer pipeline in a very compact way.

```csharp
private readonly ConcurrentQueue<ObserverMessage> _messages = new();
private readonly SemaphoreSlim _semaphore = new(initialCount: 0);
private readonly Task _pumpTask;
private readonly ILogger _logger;

public JobObserverHub(ILogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _pumpTask = Task.Run(() => PumpLoop());
}
```

> It shows that subscriptions remain cheap and synchronized, while publication is handled separately by the background pump.

```csharp
public IDisposable Subscribe(IObserver<IJobEvent> observer)
{
    if (observer == null) throw new ArgumentNullException(nameof(observer));

    lock (_sync)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);

        if (!_observers.Contains(observer))
        {
            _observers.Add(observer);
        }
    }

    return new Unsubscriber(_observers, observer, _sync);
}
```
### How queue events are published internally

> it shows the isolation rule clearly—observer failures and async callback failures do not crash the queue engine.

```csharp
private void Publish(IJobEvent evt)
{
    if (evt == null) return;

    var handler = _onJobEventChanged;

    if (handler != null)
    {
        try
        {
            _ = handler(evt).ContinueWith(
                t => OnBackgroundError(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            OnBackgroundError(e);
        }
    }

    _observerHub.Publish(evt, OnObserverError);
}
```
## Cache and persistence

Persistence providers are not implemented directly inside `TplQueue.Core`.

What Core does provide is the runtime surface needed to integrate persistence-oriented scenarios:

- payload-aware jobs and roots
- `ICacheQ`
- cache queue orchestration hooks
- payload handler support

The actual persistence and serialization concerns belong mainly to adapter-side modules such as:

- [Fmacias.TplQueue.Cache.Abstract](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Cache.Abstract/README.md)
- [Fmacias.TplQueue.Cache.MemCache](https://github.com/fmacias/TplQueue.Adapter/blob/main/src/Fmacias.TplQueue.Cache.MemCache/README.md)
- `Fmacias.TplQueue.Serialization.SystemTextJson`
- `Fmacias.TplQueue.Serialization.Xml`

This separation keeps the Core runtime focused on execution semantics while still allowing dehydration, hydration, recovery, and cache-backed leasing in higher-level integrations.

## Service-by-service usage

### `CoreApi`

`CoreApi` is the main Core facade.

```csharp
ICoreApi core = CoreApi.Create();
```

It exposes:

- `IQFactory`
- `IJobFactory`
- `IDataJobFactory`

### `IJobFactory`

Use `IJobFactory` to create regular jobs and job roots.

```csharp
IJob validate = core.JobFactory.Job(
    async ct => await Task.CompletedTask,
    name: "Validate");

IJobRoot root = core.JobFactory.JobRoot(
    async ct => await Task.CompletedTask,
    name: "Root");

root.After(validate);
```

### `IQFactory`

Use `IQFactory` to create queue instances.

```csharp
IParallelQ mainQ = core.QFactory.Parallel(
    Guid.NewGuid(),
    "main",
    4,
    parallelLogger);

IFifoQ orderedQ = core.QFactory.Fifo(
    Guid.NewGuid(),
    "ordered",
    fifoLogger);
```

### `IDataJobFactory`

Use `IDataJobFactory` for payload-aware jobs and payload roots.

```csharp
var dataRoot = core.DataJobFactory.DataJobRoot(
    new MeasurementPayload { SensorId = "S-01", Value = 12.5 },
    handler,
    name: "MeasurementRoot");

var dataChild = core.DataJobFactory.DataJob(
    new MeasurementPayload { SensorId = "S-02", Value = 18.2 },
    handler,
    name: "MeasurementChild");

dataRoot.After(dataChild);
```

## Build and test

```powershell
dotnet restore .\core.sln --configfile .\NuGet.config
dotnet build .\core.sln
dotnet test .\core.sln
```

For deterministic coverage validation on the maintained test surface:

```powershell
.\coverage.ps1
.\coverage.ps1 -EnforceBaseline
```

The coverage script writes Cobertura reports, JSON summaries, and `artifacts/coverage/html/index.html` when the standard `ReportGenerator` tool is available. It also enforces the repository floor defined in `coverage-baseline.json`.

For local packaging:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\pack-local.ps1
```

`pack-local.ps1` packs `src\Fmacias.TplQueue.Core\Fmacias.TplQueue.Core.csproj`, writes the generated packages into `..\TplQueue.NugetLocal`, and registers that local source if it is missing.

## Strong-name signing

Normal source builds are unsigned. Official release packages are strong-named only when `pack-local.ps1` receives an external private key path and the matching full public key.

```powershell
.\pack-local.ps1 `
  -Version 0.1.0-preview.1 `
  -StrongNameKeyFile C:\secure\keys\Fmacias.TplQueue.official.snk `
  -StrongNamePublicKey <public-key>
```

The private `.snk` file is never stored in this repository. The Core project appends the public key to `InternalsVisibleTo` declarations only for official signed builds, so local unsigned test builds remain simple. For key creation, public-key extraction, and verification, see `..\WorkspaceTplQueue\docs\strong-name-signing.md`.

This is assembly strong-name signing only. NuGet package X.509 signing and obfuscation are not part of the current TplQueue release flow; the central policy is maintained in `..\WorkspaceTplQueue\docs\release-policy.md`.

## Versioning and release

The active public prototype line is `0.1.0-preview.1`. Until the first stable release, local builds and local package generation default to that preview line unless an explicit `-Version` is supplied.

For any coordinated release:

1. All publishable `Fmacias.TplQueue.*` packages use the same version.
2. The published package version, git tag `v<version>`, and GitHub Release title `TplQueue <version>` must match exactly.
3. Each contributing product repository should be tagged with that same version on the exact commit that produced the published package.
4. `1.0.0` is reserved for the first stable release and should not be used for preview packages.

Use `WorkspaceTplQueue\pack.ps1 -Version <version>` and `WorkspaceTplQueue\publish.ps1 -Version <version>` for coordinated release validation and publication. The detailed policy is maintained in `..\WorkspaceTplQueue\docs\release-policy.md`.

## License

`TplQueue.Core` is distributed under a custom binary-use EULA.

In practical terms:

- official binaries may be used in development, test, staging, and production environments, including commercial workloads
- source code, private repository access, and rights to build or distribute modified versions are not granted by the package license
- source access, support, maintenance, and custom engineering work require a separate written agreement with the licensor
- the package is provided without support, SLA, uptime commitment, or acceptance of bug-related operational costs such as downtime, QA effort, rollback work, or customer-side penalty exposure, subject to mandatory legal limits

Companion modules in `TplQueue.Adapter` are distributed under the MIT license.

# Roadmap

Completed recently:

1. `QAbstract.SchedulerLoop()` now waits reactively while the queue is paused, preserving the existing soft-pause semantics.

2. `CacheQ.LeaseLoopAsync()` now waits reactively while leasing is paused, preserving the existing lease pulse for active polling.

3. This README now links more directly to [TplQueue.Adapter](https://github.com/fmacias/TplQueue.Adapter/blob/main/README.md) and the related adapter modules that provide the concrete retry, observer, cache, and serialization integrations.

Note:

README files currently still carry both overview-style documentation and how-to guidance. Splitting README, dedicated documentation, and samples remains a separate future documentation concern.
