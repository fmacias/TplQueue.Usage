# UC22 — Long execution and idle periods

**Status:** Pending

**Purpose:** Demonstrate observer-driven presentation during long work and quiet arrival periods.

**Implementation:** Add a focused Simulation scenario alternating controlled long Domain work with deliberate arrival gaps. Reuse `ScheduledWorkflow`, extending only the arrival pattern needed for the scenario; do not make the session's terminal `StopAsync` act as pause/resume. Preserve the existing projection and JobMonitor refresh path.

**Expected behavior:** Accepted events update accumulated job state. During periods with no new events the live view stays still; the next snapshot advances live time. View Pause/history retain their reference while incoming state updates, and Follow live restores following. No browser polling or redraw timer is required.

**Dependencies:** Existing scheduled workflow, observer projection and JobMonitor time controls.

**Product limitations:** None identified. Arrival gaps and handler durations are sample behavior.
