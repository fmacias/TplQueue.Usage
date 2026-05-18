# Overview

`TplQueue.Usage` is the public verification surface for the TplQueue package line.

The repository exists so a consumer can inspect:

- how to compose jobs and job roots through the public packages
- how to create queues through the adapter facade
- how observer subscriptions behave from the outside
- how to validate package-based behavior without private source access

This repository is intentionally package-oriented. Tests and samples are written the way an external application would consume TplQueue:

- package references
- consumer-owned configuration
- consumer-owned observers or built-in observers created through the public factory

The repository carries the public-safe integration scenarios adapted from `TplQueue.Core/test/Fmacias.TplQueue.Integration.Test`, after removing private project-reference assumptions. The `samples` folder is the facility-style example surface for consumers, with [QueueObserverConsole](../samples/QueueObserverConsole/README.md) and [QueueObserverSignalRDashboard](../samples/QueueObserverSignalRDashboard/README.md) documenting their supported execution modes and observer-facing behavior. The SignalR dashboard sample also shows the DI-oriented composition path where `appsettings.json` defines named retry policies and dispatcher options, and the host reuses two injected queue instances for its whole lifetime: one metadata queue and one payload queue.

The integration suite also carries consumer-owned observer projections for remote transport scenarios. Those tests validate that the public queue event feed remains metadata-only for consumers, while serialized payload snapshots stay an explicit projection choice instead of a live payload reference carried by every observer event. The payload path in the SignalR sample demonstrates that explicit projection with terminal DataJob snapshots that can be inspected in the browser.
