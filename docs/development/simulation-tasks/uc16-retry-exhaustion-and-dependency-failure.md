# UC16 — Retry exhaustion and dependency failure

**Status:** Pending

**Purpose:** Demonstrate exhausted retry policies and failure propagation through dependencies.

**Implementation:** Reuse UC15's failure-capable Domain handler and factory. Add separate scenarios failing a leaf, an intermediate job and the root, using a finite existing retry policy. Simulation retains normal admission and graph registration. Include Cache variants through UC05's supported payload/handler path, keeping acknowledgment separate from execution.

**Expected behavior:** Each failing job retains its identity through attempts and reaches the terminal outcome defined by the public policy. Dependent handlers do not consume missing business output. Root and branch outcomes follow supported propagation semantics; the monitor shows observed failures and known root memberships without fabricating success or missing events.

**Dependencies:** [UC15](uc15-transient-failure-followed-by-success.md); UC05's Cache path for cache variants.

**Product limitations:** Failure propagation and cache acknowledgment follow the public contracts. Record an unavailable behavior or observation under the [README roadmap](../../../README.md#roadmap) with UC16; projection state is not a substitute.
