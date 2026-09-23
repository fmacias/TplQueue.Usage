# Current Issue: Backend-Owned Passive Observable ETL Sample

> Historical implementation record. Project names and validation counts below
> describe the original ETL iteration. The implementation is now
> `TplQueue.Sample.Simulation`; see the maintained
> [architecture guide](docs/architecture/blazor-consumer-sample.md).

## Status

Implementation complete and validated with backend-owned `IParallelQ`, `IFifoQ`,
and real `ICacheQ` runtimes.

Implemented:

- reusable `TplQueue.Sample.Etl.Contracts` and `TplQueue.Sample.Etl` projects;
- immutable consumer-input and `IPayload` contracts;
- fixed Ingest, Transform, and Load graph construction;
- minimal inter-step execution data store and prerequisite guards;
- stable handler registration and restricted payload type resolver;
- singleton three-queue runtime and observer subscriptions;
- consumer-domain mapping in `TplQueue.Sample.BlazorSignalR`;
- backend-hosted deterministic demonstration workload;
- thread-safe, idempotent lifecycle projection and passive Interactive Server dashboard;
- one `vis-timeline` with stable ParallelQ, FifoQ, and CacheQ groups;
- success, failure, cancellation, serialization, and host integration tests.

The executable path submits two fixed three-job roots to each queue. CacheQ
dehydrates, leases, hydrates, resolves handlers, and dispatches through its
dedicated inner queue. The dashboard is observation-only: workload submission
is owned by `SampleEtlDemoHostedService`, and the browser has no start, cancel,
queue-selection, job-construction, or API control surface.

The coordinated Debug validation builds all 23 workspace projects with zero
warnings and errors and passes all 528 tests, including the hosted dashboard
acceptance test and the CacheQ lifecycle path.

The local package baseline for this work is `0.1.0-preview.2` from the sibling `TplQueue.NugetLocal` feed.

## Goal

Build a reusable backend sample module that demonstrates how a legacy or modern .NET application can make predefined operations, domain workflows, job graphs, and related data visible and traceable through TplQueue.

The backend developer owns the workflow. The module constructs the jobs, selects the queue and retry behavior, registers handlers, enqueues the graph, and projects execution status. A host such as Blazor, a console application, or a worker only invokes a semantic operation and observes its status.

This is not a generic orchestration control plane. The browser must not configure arbitrary jobs, handlers, graph dependencies, queues, retry policies, or payload schemas.

## Architectural Position

TplQueue has its own reusable execution domain (`IJob`, `IDataJob`, job graphs, queues, retries, cache, and observers). From the consuming application's perspective it is infrastructure used by a backend application/integration module.

```text
Consumer domain and legacy data
        |
        v
Consumer application/integration boundary
        | maps domain data
        v
Shared ETL workflow module
  - immutable payload contracts
  - handlers
  - predefined graph factory
  - queue runtime
  - raw `IJobEvent` streams
        |
        v
Fmacias.TplQueue.Abstractions
        ^
        |
TplQueue.Core and TplQueue.Adapter runtime

Hosted service / Blazor / Console / Worker
        |
        +-- invokes a predefined semantic workflow operation
        +-- projects and observes immutable lifecycle metadata
```

Consumer applications may reference `Fmacias.TplQueue.Abstractions` when they need to exchange `IPayload`-based contracts with the shared module. Consumer business entities should normally remain independent of TplQueue. The consumer integration layer maps them into the payload contracts understood by the workflow module.

Directly implementing `IPayload` on a legacy domain object is acceptable as a documented migration step, but it is not the preferred final boundary.

## Proposed Projects

### `samples/TplQueue.Sample.Etl.Contracts`

Target `netstandard2.0` and reference only `Fmacias.TplQueue.Abstractions`.

Own:

- immutable payload DTOs;
- the public `IEtlWorkflow` facade;
- stable handler identifiers.

### `samples/TplQueue.Sample.Etl`

Target `netstandard2.0` where practical. Reference the contracts project and TplQueue abstractions. Concrete composition may additionally reference the required Adapter packages.

Own:

- payload handlers;
- the predefined job-graph factory;
- the singleton queue runtime;
- handler registration;
- observer subscription and disposal;
- prerequisite guards;
- the minimal inter-step execution data store;
- forwarding the immutable metadata already carried by `IJobEvent`;
- dependency-injection registration such as `AddSampleEtlWorkflow`.

Avoid a generic `BLL` assembly that becomes a miscellaneous bucket. This project is specifically the sample's backend workflow/application module.

### `samples/TplQueue.Sample.BlazorSignalR`

Own only consumer-domain adaptation, host composition, the backend sample
scenario, and Interactive Server presentation.

The Blazor sample may:

- submit or trigger a predefined sample operation;
- provide consumer-collected sample data;
- display graph structure and execution state;
- display the raw lifecycle metadata exposed by `IJobEvent`;
- request cancellation through the injected workflow facade.

It must not expose generic TplQueue construction or configuration controls.

## Consumer Data Ingestion

The sample must include data collected by the consumer application. The consumer maps its own data into an immutable shared payload and calls one semantic facade method.

```csharp
public interface IEtlWorkflow
{
    Guid EnqueueMeasurements(
        AvailableQueue availableQueue,
        IReadOnlyList<LegacyMeasurement> legacyMeasurements,
        CancellationToken cancellationToken);

    bool Cancel(Guid rootJobId);
}
```

If the data has already been acquired before the workflow call, the first traced step is **Ingest**, not **Extract**:

```text
Consumer collection -> Ingest -> Transform -> Load
```

To demonstrate acquisition itself as a traced job, add a separate collector port and execute it inside an Extract handler:

```text
Extract through collector port -> Transform -> Load
```

The initial implementation should support the consumer-collected path because it clearly demonstrates integration with a legacy application.

## Payload Design

Payloads are immutable input snapshots. A payload contains all data required by its handler plus stable workflow metadata.

Minimum metadata:

- `PayloadId`: stable identifier for the payload instance;
- `HandlerKey`: stable handler-resolution key;
- `CollectionTime`;
- `OperationId`;
- optional source-system identifier;
- immutable collected data.

Example handler key:

```text
sample.etl.ingest-measurements.v1
```

Use the root `JobId` as the execution `OperationId`; dependency jobs keep their own IDs. Do not overload `PayloadId` with either meaning.

Payload types used with `ICacheQ` must be public and safely serializable. The canonical immutable sample should use the System.Text.Json adapter. Cache hydration must be tested explicitly. Handler keys and payload contract versions must remain stable.

For durable or untrusted persisted data, use a controlled `ITypeResolver` that recognizes only supported sample payload types and can map legacy type names. Do not rely indefinitely on unrestricted assembly-qualified type discovery.

## Inter-Step Data and Metadata Snapshots

Do not add a general mutable `Result` property to every payload.

The default model is:

```text
immutable payload input + minimal internal execution data store
```

The workflow module owns an internal store keyed by the root job identifier. It contains only the normalized measurement values and transformed summary needed by downstream handlers. It does not track lifecycle statuses, timestamps, retries, job descriptors, or presentation state.

This avoids ambiguous state during retries and works after cache serialization and graph hydration. A controlled assign-once terminal result on a specialized execution envelope may be evaluated later, but it must not become the implicit inter-step data bus.

TplQueue observer events provide immutable job metadata snapshots and are not required to retain payload objects. At graph construction time, the module therefore records a safe association:

```text
root JobId -> ETL execution data
```

The dedicated observer copies `IJobEvent` metadata into an idempotent,
thread-safe projection. Blazor reads immutable projection snapshots and never
receives the live `IDataJob` or arbitrary serialized payload.

## Predefined Graph and Queue Runtime

The first graph is fixed in backend code:

```text
Ingest collected measurements
        |
        v
Transform measurements
        |
        v
Load transformed data
```

Construct the graph through `IDataJobFactory` and the contracts available in Abstractions. Prefer the interface-level `After(...)` methods so the shared workflow does not depend on Core extension methods merely for graph composition.

The module owns:

- one queue runtime per application process;
- one observer subscription per queue lifetime;
- stable payload-handler registration;
- graph creation per operation;
- the root job ID as the execution identifier;
- cancellation propagation;
- disposal of subscriptions and queue resources.

Use a fixed backend queue policy. The demonstration submits the same predefined
workflow twice to each of `IParallelQ`, `IFifoQ`, and `ICacheQ`; these choices
remain backend composition rather than client configuration.

Immutable JSON payload round-tripping and real `ICacheQ` execution are covered.
`MemCache` remains process-local behavior and is not distributed durability.

## Current Core Constraints

### Completion dependency is not success dependency

The current Core behavior allows a dependent/root job to execute after a prerequisite completes with an exception. Therefore `Ingest -> Transform -> Load` does not automatically mean “continue only on success.”

The sample implements a module-level prerequisite guard. Before executing Transform or Load, the handler checks the execution data store. If the required prior step did not produce data, the handler fails without performing its business operation; that failure is reported by the normal `IJobEvent` lifecycle.

A future Core enhancement may introduce explicit success-dependent graph semantics. The sample must not imply that behavior already exists.

### Retry policy is graph-wide

The root retry policy propagates to dependencies. The initial sample uses one predefined graph-wide retry policy. Per-step retry controls are out of scope until Core exposes them explicitly.

Pass the selected retry-policy factory explicitly when creating the root. Do not depend on the currently questionable queue-level fallback path.

### Cached graphs are not closure-based workflows

Handlers must not exchange data through captured closures or assumptions about shared in-memory payload references. Cache hydration reconstructs nodes and payload instances. All required input must be serialized in a payload or retrieved through the explicit execution data store.

## Dependency Injection and Lifetimes

`AddSampleEtlWorkflow` should register:

- the workflow facade;
- the graph factory;
- the queue runtime as a singleton;
- the execution data store;
- each stable payload handler;
- any serializer, cache, observer, and retry-policy configuration needed by the fixed sample.

Handler registration must occur once. Duplicate keys with different handlers are configuration errors.

If a handler needs scoped application services, it should create and dispose a scope during `HandleAsync` through an injected scope factory. Do not resolve a scoped dependency into the singleton handler/runtime and retain it.

## Interactive Server Host Surface

`SampleEtlDemoHostedService` invokes `ILegacyMeasurementScenario`, which
collects consumer-owned data and calls the shared `IEtlWorkflow` facade. Queue
observers materialize `IJobEvent` values in the singleton projection, and the
passive Razor dashboard receives projection changes through its Blazor
Interactive Server SignalR circuit.

The dashboard and workflow run in the same process and communicate through
injected application contracts.

## Implementation Sequence

Follow TDD and keep the shared module usable without Blazor.

1. Add contract tests for immutable payloads and identifiers.
2. Create the Contracts project and `IEtlWorkflow` facade.
3. Add execution-data-store coverage through the fixed workflow.
4. Implement the minimal in-memory execution data store.
5. Add handler tests for valid input, cancellation, failures, retries, and prerequisite guards.
6. Implement Ingest, Transform, and Load handlers.
7. Add graph-factory tests covering node names, IDs, dependencies, handler keys, and retry propagation.
8. Implement the predefined graph factory.
9. Add immutable JSON payload round-trip coverage and verify the Core/Adapter `ICacheQ` lease path.
10. Implement the singleton queue runtime and dedicated observer subscription.
11. Add `AddSampleEtlWorkflow` composition and integration tests.
12. Add a console or test host proving the shared module works without a frontend.
13. Replace the Blazor template with a passive Interactive Server dashboard and backend-hosted legacy-workload scenario.
14. Add a complete success path, handler-failure path, cancellation path, and host-integration path.
15. Validate against the local `0.1.0-preview.2` package line.

## Acceptance Criteria

- A consumer supplies its own collected data without exposing its domain entity directly.
- The shared module maps or accepts that data as an immutable `IPayload` contract.
- The backend creates a predefined three-step job graph.
- Queue choice, retry policy, handlers, and graph topology are fixed in backend composition.
- The root job ID identifies the execution and every node produces traceable metadata.
- The dedicated observer materializes immutable `IJobEvent` values in an idempotent lifecycle projection.
- No live job, queue, handler, or arbitrary payload object crosses the host boundary.
- Immutable payloads preserve their values through the configured JSON serializer.
- Downstream business operations do not run after failed prerequisites.
- The module runs from a non-Blazor host.
- The hosted service invokes the backend scenario; Blazor displays status without configuring or controlling TplQueue.
- Tests cover success, failure, cancellation, immutable payload serialization, host integration, and raw observer events.

Pending acceptance work:

- add focused retry and invalid-input cases if the sample later exposes a retrying workflow variant or accepts caller-provided batches.

## Explicit Non-Goals

- generic frontend job or graph design;
- client-selected queue or retry configuration;
- runtime handler upload or arbitrary code execution;
- dynamic workflow scripting;
- treating TplQueue as a distributed broker;
- claiming durable or exactly-once execution from `MemCache`;
- coupling consumer domain entities permanently to TplQueue contracts;
- using mutable payloads as an implicit workflow data bus.

## Package Baseline

Local coordinated packages have been generated as `0.1.0-preview.2`. Until repository defaults are advanced, package-consumption validation must explicitly select that version, for example:

```powershell
.\build.ps1 -TplQueuePackageVersion 0.1.0-preview.2
.\test.ps1 -TplQueuePackageVersion 0.1.0-preview.2
```

Public documentation remains on the published `0.1.0-preview.1` line until `preview.2` is intentionally released.
