# UC17 — Cancellation at different phases

**Status:** Pending

**Purpose:** Demonstrate cancellation while queued, executing, waiting for dependencies or waiting to retry.

**Implementation:** `EtlQueueRuntime` already links per-root cancellation sources and has `Cancel(Guid)`, but `IEtlQueueRuntime` does not expose it and no phase-specific workflow exists. Add the smallest Usage-owned contract/control needed to use that implementation from Simulation; avoid concrete runtime casts. Trigger cancellation from observed milestones or controlled Domain handler gates, and retain cleanup of cancellation registrations.

**Expected behavior:** Each variant requests cancellation at its intended phase and reports actual terminal outcomes and remaining dependencies. Repeated or unknown-root requests have defined sample results. Unrelated roots remain unaffected except where a supported shared dependency creates a real relationship. Stopping arrivals remains distinct from cancellation; root membership survives canceled outcomes.

**Dependencies:** Existing runtime token linkage and graph projection. [UC14](uc14-cross-queue-bottleneck.md) for dependency waiting; [UC15](uc15-transient-failure-followed-by-success.md) for retry waiting. Queued/executing variants can proceed independently.

**Product limitations:** The missing sample interface member is a Usage gap, not a product blocker. Record any unsupported phase semantics in the [README roadmap](../../../README.md#roadmap).
