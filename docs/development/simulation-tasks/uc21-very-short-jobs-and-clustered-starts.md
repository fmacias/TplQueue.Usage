# UC21 — Very short jobs and clustered starts

**Status:** Pending

**Purpose:** Demonstrate fast queue work and inspection of dense observed timestamps.

**Implementation:** The monitor already supports grouping, counts and interval inspection; the sample has no dedicated short-job workload. Add a bounded Simulation scenario using small-duration Domain handlers and UC04's capacity setup. Reuse existing JobMonitor layout and selection behavior without changing observed timestamps.

**Expected behavior:** Many short jobs can produce closely spaced starts, accurate unique job counts, grouped markers and selectable individual jobs. Enqueue and Started markers remain two positions of one job. Real execution does not guarantee identical timestamps; coalescing may show a terminal state without painting every intermediate state.

**Dependencies:** [UC04](uc04-multiple-parallel-roots.md) and existing JobMonitor grouping.

**Product limitations:** None identified.
