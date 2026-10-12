# UC02 — Sequential ETL chain

**Status:** Implemented

**Purpose:** Demonstrate dependency-ordered business work through a three-job ETL graph.

**Implementation:** `EtlSimulationWorkflow` already requests `ISampleJobFactory.LoadMeasurementsJobRoot` and submits a distinct graph to each queue. `SampleJobFactory` composes Ingest -> Transform -> Load through the public graph API. Reuse the current workflow; no additional scheduler or finite-profile restoration is needed.

**Expected behavior:** Load is the final root, not the first step. Ingest normalizes measurements, Transform stores a summary, and Load logs that summary. Handler delays are 500/700/400 ms; downstream handlers require prerequisite output. Each admitted queue submission adds one root and three unique jobs, with explicit dependency edges and root membership visible before terminal success. A full ETL tick adds three roots and nine jobs.

**Dependencies:** Existing `ISampleJobFactory`, `IMeasurementSource`, `IEtlExecutionDataStore`, scheduled delivery and graph-aware projection.

**Product limitations:** None identified for the implemented chain. Cache acknowledgment and failure/cancellation variants belong to UC05/UC16/UC17.
