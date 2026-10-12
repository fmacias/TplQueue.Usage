# UC25 — Bounded continuous operation

**Status:** Partially implemented

**Purpose:** Demonstrate continuous queue work with bounded sample history and memory ownership.

**Implementation:** Continuous workflows and active-run admission already exist. Remaining work spans Simulation's accepted-root lists and graph catalog, the host projection's jobs/event fingerprints, and Domain measurements/summaries. Define a coherent Usage retention policy for completed graphs, shared dependencies and late events; reuse `IEtlExecutionDataStore.Remove` where safe and add only the missing sample cleanup contracts. Define a global active-work bound as additional workflows are registered.

**Expected behavior:** Retained completed history reaches a configured bound while active graphs and dependencies needed by active consumers remain intact. Eviction treats shared nodes coherently, handles selection of retired jobs, and prevents late/duplicate events from recreating unlimited expired history. Snapshots remain bounded by explicit active and retained limits. Existing continuous arrivals and Stop semantics remain available.

**Dependencies:** Existing continuous delivery/admission; [UC12](uc12-one-prerequisite-shared-by-several-roots.md) for shared-root retention, [UC19](uc19-burst-overload-and-recovery.md) for pressure scenarios, and UC23's existing stop lifecycle. Retention horizon/count and late-event policy remain to be selected.

**Product limitations:** No confirmed product blocker; current unbounded Usage collections are sample-owned. Record any additional public API requirement and blocked UC25 variant in the [README roadmap](../../../README.md#roadmap).
