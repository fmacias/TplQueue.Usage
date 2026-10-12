# UC09 — Independent workloads across queues

**Status:** Partially implemented

**Purpose:** Demonstrate independent arrivals and root identities across queue types.

**Implementation:** ETL and SingleJob already run together, each with one timer spanning all three queues. Remaining work is deliberately different queue/workflow intervals and offsets. Extend the existing `ScheduledWorkflow` scheduling and queue-selection inputs only enough for these scenarios, preserving current defaults and singleton runtime identity. The earlier 2/3/5-second intervals are a possible demonstration, not existing configuration.

**Expected behavior:** Different schedules produce independent accepted-root counts and fresh graph identities. Queue-local channels and observed queue ownership remain separate from scenario identity. Capacity skips affect delivery rather than creating phantom jobs. Additional browsers observe the same session without multiplying arrivals.

**Dependencies:** [UC01](uc01-single-job-on-each-queue.md), [UC02](uc02-sequential-etl-chain.md), and UC05's existing Cache success path. [UC03](uc03-multiple-fifo-roots.md)/[UC04](uc04-multiple-parallel-roots.md) supply optional backlog variants.

**Product limitations:** None identified; missing schedule configuration is a Usage concern.
