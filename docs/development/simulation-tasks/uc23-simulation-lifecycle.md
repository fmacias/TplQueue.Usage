# UC23 — Simulation lifecycle

**Status:** Partially implemented

**Purpose:** Demonstrate distinct arrival control, job completion and cancellation boundaries.

**Implementation:** Single-use Start, idempotent StopAsync, pending-submission completion, timer cleanup and host shutdown already exist in `SimulationService`/`ScheduledWorkflow`. Every workflow continuously submits fresh jobs until Stop or host cancellation, without sample active-root caps or lifetime root limits. Extend these services and their Usage contracts only for explicit pause-arrivals/resume, drain and cancellation scenarios. Keep final Stop terminal; a fresh session is a separate lifecycle decision, not a second Start on the existing object.

**Expected behavior:** Pause-arrivals leaves accepted work running; resume creates no duplicate schedule. Final Stop releases workflow timers and waits for admitted submissions without canceling or draining jobs. A separate drain operation waits for its documented execution boundary, and cancellation requests report observed outcomes. Completion retains its existing meaning of stopped arrivals/submissions. View Pause never controls workloads; host shutdown applies the host token and releases runtime-owned resources.

**Dependencies:** Existing session/workflow lifecycle and [UC17](uc17-cancellation-at-different-phases.md) for cancellation extensions.

**Product limitations:** No confirmed product blocker. If a requested drain boundary needs unavailable public completion/acknowledgment information, record the API and UC23 under the [README roadmap](../../../README.md#roadmap).
