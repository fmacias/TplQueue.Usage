# UC24 — Browser lifecycle during execution

**Status:** Implemented

**Purpose:** Demonstrate that backend queue execution outlives individual browser views.

**Implementation:** The host-owned workload, singleton projection/session, `Dashboard.razor` initial snapshot and subscription, and `JobMonitor.razor` interop lifetime already implement this separation. Preserve those paths for late connections, navigation, reconnects and multiple viewers. No new workflow or browser-triggered submission is needed. This status describes source behavior, not a new interactive validation result.

**Expected behavior:** Viewers receive the current accumulated snapshot, then coalesced observer-driven updates. Selection and viewport are local to each component. Leaving a page detaches its notification/interop resources without stopping queues; another viewer sees the same jobs. Stop arrivals in any circuit stops the shared session, and all circuits observe Completion. Reconnecting does not restart arrivals or guarantee restoration of a destroyed component's local viewport.

**Dependencies:** Existing continuous host, shared session/projection and JobMonitor integration. UC09's varied schedules and UC23's future lifecycle extensions are optional variants, not prerequisites for this behavior.

**Product limitations:** None identified for the implemented browser/backend boundary.
