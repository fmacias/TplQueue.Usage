# UC03 — Multiple FIFO roots

**Status:** Pending

**Purpose:** Demonstrate FIFO serialization, visible backlog and recovery for several roots.

**Implementation:** The host already sends ETL and single-job roots to FIFO, but has no controlled backlog scenario. Add a focused Simulation workflow using Domain-created roots and the existing FIFO instance. Extend `ScheduledWorkflow` only as needed for a bounded sequence and queue selection; choose handler duration longer than arrival spacing and state the root count and admission cap. Keep the existing default workflows unchanged.

**Expected behavior:** Roots are submitted in a known order faster than they complete, wait visibly, execute according to the public FIFO contract on channel zero, and finish after arrivals stop. FIFO ordering edges remain distinct from composed root membership. Asynchronous observer delivery order is not handler execution order.

**Dependencies:** [UC02](uc02-sequential-etl-chain.md) and existing scheduled admission.

**Product limitations:** None identified; derive ordering from the supported FIFO contract rather than introducing sample scheduling semantics.
