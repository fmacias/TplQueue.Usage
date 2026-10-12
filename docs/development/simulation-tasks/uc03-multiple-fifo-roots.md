# UC03 — Multiple FIFO roots

**Status:** Implemented; build, test execution and browser acceptance pending human validation.

**Purpose:** Demonstrate FIFO serialization, visible backlog and recovery for several roots.

**Implementation:** `MultipleFifoRootsWorkflow` uses the existing Domain single-ingest factory through Contracts and submits only to the shared runtime FIFO. It starts after one second and continuously attempts one fresh root every 100 ms until Stop or host cancellation. There is no lifetime root limit or sample active-root cap, in UC03 or the other workflows. The existing ingest handler takes 500 ms, so queued roots accumulate and remain observable. `ScheduledWorkflow` provides queue selection, interval timing and stop coordination. Submit creates jobs on each arrival; stored root IDs are history, not cached work. The existing guard against overlapping submission callbacks remains. No additional scheduler or Domain reference is introduced in Simulation.

**Launch:** After building the Debug sample, run from the Usage repository root:

```powershell
dotnet run --no-build --project .\samples\TplQueue.Sample.BlazorSignalR
```

UC03 is always registered alongside ETL and SingleJob, which retain their existing schedules on all three queues. Every added workflow must be included in the host without configuration flags, profiles or mutually exclusive selection. The host starts arrivals independently of browsers; restart the host to repeat UC03.

**Expected behavior:** Roots are submitted in a known order faster than they complete, wait visibly, execute according to the public FIFO contract on channel zero, and finish after arrivals stop. FIFO ordering edges remain distinct from composed root membership. Asynchronous observer delivery order is not handler execution order.

UC03 roots retain their relative submission order; ETL and SingleJob submissions may interleave on the same FIFO. All workflows continue until stopped. FIFO execution capacity limits running work, not sample submissions. Stop arrivals explicitly to demonstrate backlog recovery.

**Completion and failure:** The delivery snapshot (`multiple-fifo-roots`) records accepted root IDs in submission order. Workflow Completion means arrivals and in-progress submissions have stopped, not that jobs or observers have drained. **Stop arrivals** stops every workflow without cancelling accepted work. A failed submission is recorded and later ticks attempt fresh jobs. Queued/running roots never suppress new submissions. Only overlapping submission callbacks are skipped; queue backlog is handled by the product FIFO and observed by the presentation pipeline.

**Validation prepared:** `ScheduledWorkflowDeliveryTests` replaces finite/capped-delivery assertions with continuous fresh-root delivery, including more than six still-active roots, selected queues, failures, stop/cancellation and invalid intervals. `SimulationRegistrationTests` covers additive/idempotent registration. `SingleJobSimulationTests.MultipleFifoRoots_BuildsVisibleBacklogThenExecutesInSubmissionOrderOnChannelZero` gates the real Domain handler, proves more than six roots are enqueued and projected before any handler finishes, then stops arrivals and verifies serial FIFO execution, channel zero, completion and independent memberships. `ContinuousSimulationIntegrationTests` composes all three continuous workflows and checks accepted work finishes after explicit session stop. The original two-workflow test case remains. These tests have not been executed for this change.

**Browser acceptance:** Expand **U** to inspect increasing waiting roots and retained enqueue markers while jobs execute on FIFO channel zero. All workflows keep submitting fresh jobs. **Stop arrivals** ends the whole session's arrivals; keep the host running until queued work finishes. Opening another tab must observe the same workload without restarting it. Backlog and drain time depend on how long arrivals run.

**Dependencies:** [UC02](uc02-sequential-etl-chain.md) and existing scheduled admission.

**Product limitations:** None identified; derive ordering from the supported FIFO contract rather than introducing sample scheduling semantics.
