# UC19 — Burst, overload and recovery

**Status:** Pending

**Purpose:** Demonstrate bounded admission while arrival demand exceeds queue service capacity.

**Implementation:** Extend the existing scheduled admission logic for a bounded burst and then a bounded period of faster arrivals, using UC03/UC04 Domain workloads. Account for each accepted root; the current `Submit` hook returns only one root ID, so batch delivery must extend that accounting rather than hide several roots behind one ID. Keep skip-on-busy/capacity behavior and no catch-up backlog.

**Expected behavior:** Accepted work forms a visible queue backlog while skipped attempts create no jobs. Delivery counters distinguish ticks with skips/failures from job counts. After arrivals stop, accepted jobs can finish and capacity becomes available. Every run is limited in size and duration; this is a load demonstration, not unbounded pressure.

**Dependencies:** [UC03](uc03-multiple-fifo-roots.md), [UC04](uc04-multiple-parallel-roots.md), and existing scheduled admission.

**Product limitations:** None identified; admission accounting belongs to Usage.
