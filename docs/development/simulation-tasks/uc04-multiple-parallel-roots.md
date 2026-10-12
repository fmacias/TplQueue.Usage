# UC04 — Multiple parallel roots

**Status:** Pending

**Purpose:** Demonstrate bounded parallel capacity, queued work and channel reuse.

**Implementation:** The host submits recurring roots but has no deliberate capacity-saturating scenario. Add a Simulation workflow targeting the runtime's Parallel queue, using independent Domain roots with readable controlled durations. Submit a bounded set larger than configured `MaxParallelism` and retain admission accounting for every accepted root.

**Expected behavior:** Unrelated roots remain distinct. Available capacity allows overlapping executions while additional work waits. Channels stay within the configured range and become reusable after release; a channel denotes capacity ownership and can include dependency waits. The monitor shows observed positions and backlog without assigning lanes from root IDs.

**Dependencies:** [UC02](uc02-sequential-etl-chain.md), existing admission and runtime queue catalog.

**Product limitations:** None identified. Do not prescribe a particular channel number or completion order for competing roots.
