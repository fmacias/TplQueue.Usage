# UC20 — Large shallow and deep narrow graphs

**Status:** Pending

**Purpose:** Demonstrate graph size and dependency depth as separate dimensions.

**Implementation:** Extend Domain graph factories with explicit shallow/wide and narrow/deep variants. Apply [normal graph sizing](../simulation-use-case-plan.md#graph-sizing) and cycle/size/depth validation at scenario construction. Simulation submits bounded runs through the existing runtime. A larger stress variant requires an explicit scenario size; it must not silently alter normal workloads.

**Expected behavior:** Unique-node counts, root counts and longest-path depth are visible in scenario descriptions. Shared references count once globally. Invalid sizes or unsupported depth are rejected clearly. Monitor search, connected-graph focus and grouped markers remain usable within the demonstrated size, preserving exact timestamps and edges; no universal performance claim follows from a graph-size setting.

**Dependencies:** [UC06](uc06-branching-and-joining.md), [UC07](uc07-diamond-graph.md), existing membership projection and graph-sizing decisions.

**Product limitations:** The historical private-runtime depth guard is not a supported sample limit. Use current public constraints; record any required product enhancement and UC20 under the [README roadmap](../../../README.md#roadmap).
