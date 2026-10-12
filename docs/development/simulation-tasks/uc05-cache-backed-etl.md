# UC05 — Cache-backed ETL

**Status:** Partially implemented

**Purpose:** Demonstrate CacheQ payload hydration, handler resolution and ETL execution.

**Implementation:** The normal ETL workflow already targets Cache. Domain owns `SampleCacheQ`, `SampleCache`, the payload type allowlist, public hydration constructors and `RegisterSampleEtlPayloadHandlers`; reuse them. Remaining work is a focused scenario distinguishing execution completion from cache acknowledgment, with failure/cancellation variants supplied by UC16/UC17. Observe supported cache APIs through the runtime-owned instance, not a second cache wrapper.

**Expected behavior:** The success path produces the same three-job business result and identities after hydration. Payloads remain serializable data and handler keys resolve registered implementations. Cache acknowledgment is a separate boundary from job completion and observer delivery. Failure/cancellation outcomes follow the selected public cache policy. This in-memory sample does not demonstrate recovery after process restart.

**Dependencies:** [UC02](uc02-sequential-etl-chain.md); [UC16](uc16-retry-exhaustion-and-dependency-failure.md) and [UC17](uc17-cancellation-at-different-phases.md) only for their variants.

**Product limitations:** No confirmed blocker for the existing success path. Capacity-one behavior and any unavailable acknowledgment observation must be recorded under the [README roadmap](../../../README.md#roadmap), without adding a product workaround.
