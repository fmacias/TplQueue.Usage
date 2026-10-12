# UC14 — Cross-queue bottleneck

**Status:** Pending

**Purpose:** Demonstrate waiting on a slow prerequisite owned by another queue.

**Implementation:** Reuse UC10's supported graph with a controlled slow Domain handler and a bounded set of consumer roots. Simulation uses the existing admission/runtime path and queue capacities. Include independent work to make available versus occupied capacity observable; keep the dependency graph acyclic.

**Expected behavior:** Consumers cannot bypass the prerequisite and progress after it completes. Unrelated jobs follow the runtime's capacity rules; dependency waiting can occupy a channel. The monitor shows actual queue boundaries and waiting/completed states without claiming that channel ownership means handler execution.

**Dependencies:** [UC10](uc10-two-queue-shared-dependency.md).

**Product limitations:** No confirmed additional limitation. If a supported acyclic composition stalls because of product capacity semantics, record the behavior and UC14 in the [README roadmap](../../../README.md#roadmap).
