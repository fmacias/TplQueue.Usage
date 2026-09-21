# Architecture

This section groups the repository-level boundary and transport notes for `TplQueue.Usage`.

## Main topics

- public package-consumption boundary
- private `TplQueue.Core` source separation
- consumer-owned observer projection
- explicit payload snapshot transport instead of implicit live payload forwarding

## Boundary and transport material

- [Source-access boundary](source-access-boundary.md)
- [Consumers README](../../consumers/README.md)

The SignalR dashboard sample is the runnable reference implementation for the observer-to-DTO transport shape:

- [QueueObserverSignalRDashboard](../../samples/QueueObserverSignalRDashboard/README.md)

## Blazor Interactive Server sample

- [Blazor architecture, execution channels and contract ownership](blazor-consumer-sample.md)
- [Historical ScatterChart implementation snapshot](blazor-signalr-current-state.md)

The Blazor ETL timeline uses an in-process projection and the built-in circuit.
It is separate from the custom transport sample linked above.
