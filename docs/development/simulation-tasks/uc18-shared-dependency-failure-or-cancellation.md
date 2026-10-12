# UC18 — Shared-dependency failure or cancellation

**Status:** Pending

**Purpose:** Demonstrate failure and cancellation consequences for roots sharing work.

**Implementation:** Reuse UC12's supported shared composition with UC16/UC17 handler and cancellation controls. Add distinct variants for failing the shared prerequisite, canceling its owning execution, and canceling one consumer. Select cancellation-token ownership according to the public contract before composing the scenario; do not assume consumer cancellation owns shared work.

**Expected behavior:** Each consumer reports its actual outcome and never consumes unavailable output. The shared job keeps one identity, all root memberships and all dependency edges. Unrelated roots stay independent. Canceling one consumer has only the effects supported by shared-work ownership; the monitor must not infer cancellation of the prerequisite.

**Dependencies:** [UC12](uc12-one-prerequisite-shared-by-several-roots.md), [UC16](uc16-retry-exhaustion-and-dependency-failure.md), [UC17](uc17-cancellation-at-different-phases.md).

**Product limitations:** Shared cancellation ownership remains a contract-dependent design decision. Record any missing capability, affected API and blocked UC18 variant under the [README roadmap](../../../README.md#roadmap).
