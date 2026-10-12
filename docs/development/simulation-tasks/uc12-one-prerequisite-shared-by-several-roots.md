# UC12 — One prerequisite shared by several roots

**Status:** Pending

**Purpose:** Demonstrate shared work consumed by several independently identifiable roots.

**Implementation:** Extend the supported UC10 Domain composition to a small, explicit number of consumers, initially three roots sharing one prerequisite. Register every root before submissions so `ISimulationGraphCatalog` retains all memberships. Reuse the existing multi-root DTO mapping and graph-focus behavior; no new global run ID or duplicated shared node is required.

**Expected behavior:** The minimal topology has four globally unique jobs, three roots and three edges. Each root reaches two jobs, while global counts include the shared prerequisite only once. Membership remains stable across running and terminal outcomes; selection follows the connected dependency graph, and completion order cannot overwrite other memberships.

**Dependencies:** [UC10](uc10-two-queue-shared-dependency.md) and the existing graph catalog.

**Product limitations:** Subject to UC10's supported root-binding shape. Any blocking limitation belongs in the [README roadmap](../../../README.md#roadmap) with UC12 identified.
