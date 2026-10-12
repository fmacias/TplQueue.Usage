# UC13 — Cross-queue ownership variants

**Status:** Pending

**Purpose:** Demonstrate how submission order affects a shared job's execution owner.

**Implementation:** Reuse UC10's Domain graphs and Simulation submission path. Add separate bounded variants for reversed ordered submissions and concurrent submissions, each with fresh job identities. Keep concurrency local to the scenario and account for every accepted root; do not create replacement queues.

**Expected behavior:** Ordered variants show the owner selected by the public enqueue contract. In the race variant either queue may win, but the shared job has one actual execution owner and one node. The monitor displays observed ownership and real dependencies without predicting a race winner.

**Dependencies:** [UC10](uc10-two-queue-shared-dependency.md).

**Product limitations:** No additional blocker identified beyond UC10's public ownership/binding constraints; report violations under the [README roadmap](../../../README.md#roadmap).
