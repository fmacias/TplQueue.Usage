# UC10 — Two-queue shared dependency

**Status:** Pending

**Purpose:** Demonstrate a real dependency whose execution owner differs from a consuming root's queue.

**Implementation:** Add a Domain factory returning a supported two-root composition with one shared job instance and explicit per-root queue destinations. Simulation registers both root memberships before controlled submission through `IEtlQueueRuntime`. Reuse the existing public graph/root-binding APIs and establish their supported topology before implementing it. A minimal two-root/one-prerequisite form has three unique jobs; use an explicitly described supported topology if root binding requires more.

**Expected behavior:** The shared prerequisite executes once on its actual owning queue; both consumers wait for its result. The monitor shows one shared node, both root memberships and real cross-queue dependency edges. Rejected ownership claims must not be represented as accepted enqueue operations. Queue IDs and channels are observed facts, never assigned from scenario labels.

**Dependencies:** [UC07](uc07-diamond-graph.md) and existing multi-root graph membership.

**Product limitations:** Shared root binding and ownership must be supported by public APIs. A projection fixture alone does not establish runtime support. Record any blocking API/behavior and UC10 in the [README roadmap](../../../README.md#roadmap); do not mutate `CrossQueueId` or clone work as a substitute.
