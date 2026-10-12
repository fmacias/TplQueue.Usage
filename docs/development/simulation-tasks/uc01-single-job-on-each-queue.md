# UC01 — Single job on each queue

**Status:** Implemented

**Purpose:** Demonstrate an independent TplQueue root on FIFO, Parallel and Cache.

**Implementation:** `SingleJobWorkflow` already derives from `ScheduledWorkflow` and calls `ISampleJobFactory.IngestMeasurementsSigleJobRoot` (the existing API spelling). Domain supplies the ingest payload and handler; Simulation submits through `IEtlQueueRuntime`. Preserve this implementation and the shared host registration. UC01 remains a completed historical milestone; its former finite profile is summarized in the [historical record](../simulation-continuous-demo.md#uc01-completed-milestone).

**Expected behavior:** Each admitted tick creates one fresh root/job per queue, with no composed prerequisites and a 500 ms ingest delay. A full tick adds three roots and three unique jobs; busy/capacity skips can reduce this. Normalized measurements are stored by operation ID. FIFO may add ordering edges. Observed enqueue and channel-bearing Started positions select the same job; channels come from the runtime.

**Dependencies:** Existing scheduled workflow, Domain factory, cache hydration, graph catalog and observer projection.

**Product limitations:** No confirmed blocker for the current scenario. The historical capacity-one Cache observation is tracked in the [README roadmap](../../../README.md#roadmap); it is not a new package guarantee.
