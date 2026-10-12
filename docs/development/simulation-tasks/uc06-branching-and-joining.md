# UC06 — Branching and joining

**Status:** Pending

**Purpose:** Demonstrate a root that depends on multiple prerequisite branches.

**Implementation:** Add a Domain graph-factory method exposed through a small Contracts extension, with two to four branches converging on a final root and five to ten unique jobs. Give branch outputs distinct business keys so the join can consume all required results. A Simulation workflow requests and submits this graph through existing runtime and scheduling services; register any new cache payload types and handlers when supporting Cache.

**Expected behavior:** The final handler runs only after every required branch completes. The monitor shows actual dependency edges and one composed root membership for all nodes. Branch topology permits concurrency only to the extent supported by the selected queue; it does not itself guarantee parallel execution.

**Dependencies:** Existing Domain factory, graph catalog, projection and scheduled admission; [graph sizing](../simulation-use-case-plan.md#graph-sizing).

**Product limitations:** None identified. Use supported graph construction and keep cyclic graphs outside the scenario.
