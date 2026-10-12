# UC07 — Diamond graph

**Status:** Pending

**Purpose:** Demonstrate a shared prerequisite inside one dependency graph.

**Implementation:** Extend the Domain graph factory with four unique jobs: shared prerequisite A, branches B and C depending on A, and root D depending on B and C. Reuse the same A instance on both paths. Submit through a focused Simulation workflow; do not clone the prerequisite to imitate sharing.

**Expected behavior:** A executes once in the success variant; B and C consume its result, and D waits for both. The monitor shows four nodes and four dependency edges with one root membership, even though traversal reaches A twice. Counting and graph focus use unique identities.

**Dependencies:** [UC06](uc06-branching-and-joining.md).

**Product limitations:** None confirmed. If the selected public factory rejects the required binding, record the affected API and UC07 under the [README roadmap](../../../README.md#roadmap).
