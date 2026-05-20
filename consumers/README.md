# Consumers

This folder is reserved for representative consumer surfaces that exercise TplQueue from the outside, for example:

- small service-style hosts
- observer-driven diagnostics surfaces
- facility or scenario harnesses that prove integration expectations

The runnable consumer samples currently live in [samples/PackageConsumptionSmokeConsole](../samples/PackageConsumptionSmokeConsole/README.md), [samples/QueueObserverConsole](../samples/QueueObserverConsole/README.md), and [samples/QueueObserverSignalRDashboard](../samples/QueueObserverSignalRDashboard/README.md), with the validation surface under `test/`.

The first consumer-owned remote-observer projection for `TPLQ-V1-008` lives in the integration project under `test/integration/.../Consumers/JobEventTransport/`.
It demonstrates a SignalR-style transport DTO that keeps the public queue event feed metadata-only by default and captures serialized payload snapshots explicitly instead of forwarding the live backend payload object.

That same explicit projection idea is now exercised in the runnable SignalR dashboard sample through a second long-lived payload queue that publishes detached DataJob snapshots to the browser.
