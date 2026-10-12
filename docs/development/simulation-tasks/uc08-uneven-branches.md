# UC08 — Uneven branches

**Status:** Pending

**Purpose:** Demonstrate dependency waiting when branches take different amounts of time.

**Implementation:** Reuse UC06's Domain graph factory with one deliberately slower branch. Keep duration control in Domain payload/handler behavior and scenario selection in Simulation. Use the existing scheduler and runtime; no per-job timers or new orchestration layer.

**Expected behavior:** Completed branches remain completed while the root waits for the slow prerequisite. The join handler cannot consume incomplete branch output. Observed channel occupancy may include dependency waiting and does not imply continuous handler CPU activity. The monitor retains exact lifecycle times and all dependency edges.

**Dependencies:** [UC06](uc06-branching-and-joining.md).

**Product limitations:** None identified.
