# UC15 — Transient failure followed by success

**Status:** Pending

**Purpose:** Demonstrate retrying a job under an existing TplQueue retry policy.

**Implementation:** Domain already requests an exponential-backoff policy for Load but has no deliberate transient failure. Add a focused handler/payload variant with a bounded number of failures keyed to stable job/operation identity, and select an existing policy through `IRetryPolicyAbstractFactory`. Simulation submits it through the established workflow. Keep attempt state meaningful across Cache hydration when that variant is included.

**Expected behavior:** The same job fails a known number of attempts and then succeeds, with no new job ID for retries. Downstream handlers run after successful prerequisite completion and business output is not duplicated. Observer retry counts and eventual terminal state reach the monitor; coalescing need not paint every brief transition.

**Dependencies:** Existing Domain handlers, factory retry-policy integration, scheduled delivery and observer retry metadata.

**Product limitations:** None identified; use supported retry policies rather than implementing retries in the scheduler.
