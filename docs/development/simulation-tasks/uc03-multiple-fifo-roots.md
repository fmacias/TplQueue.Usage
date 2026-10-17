# UC03 — Multiple FIFO roots

**Status:** Implemented; build, test execution and browser acceptance pending human validation.

**Purpose:** Demonstrate FIFO serialization, visible backlog and recovery for several roots.

**Implementation:** `MultipleFifoRootsWorkflow` uses the existing Domain single-ingest factory through Contracts and submits to all three shared runtime queues: FIFO, ParallelQ and CacheQ. It starts after one second and continuously attempts one fresh root per queue every 100 ms until Stop or host cancellation. Each tick creates three distinct job IDs; no job instance is reused between queues. There is no lifetime root limit or sample active-root cap, in UC03 or the other workflows. The existing ingest handler takes 500 ms, so queued roots accumulate and remain observable. `ScheduledWorkflow` provides queue selection, interval timing and stop coordination. Submit creates jobs on each arrival; stored root IDs are history, not cached work. The existing guard against overlapping submission callbacks remains. No additional scheduler or Domain reference is introduced in Simulation.

UC03 submits through `IEtlQueueRuntime.EnqueueFifo`. The runtime exposes both
enqueue modes: FIFO requests call `EnqueueFifo` on ParallelQ and CacheQ, while
the inherently serial FifoQ uses its public `Enqueue` method. All three queues
receive FIFO submissions from UC03. Both modes preserve graph registration before dispatch,
linked cancellation and terminal/failure cleanup.

**Launch:** After building the Debug sample, run from the Usage repository root:

```powershell
dotnet run --no-build --project .\samples\TplQueue.Sample.BlazorSignalR
```

UC03 is always registered alongside ETL and SingleJob, which retain their existing schedules on all three queues. Every added workflow must be included in the host without configuration flags, profiles or mutually exclusive selection. The host starts arrivals independently of browsers; restart the host to repeat UC03.

**Expected behavior:** Roots are submitted in a known order per queue faster than they complete, wait visibly, execute according to each queue's public FIFO submission contract, and finish after arrivals stop. FIFO uses channel zero; ParallelQ and CacheQ retain their real observed execution channels. FIFO ordering edges remain distinct from composed root membership. Asynchronous observer delivery order is not handler execution order.

UC03 roots retain their relative submission order within each queue; there is no global execution order across queues. ETL and SingleJob submissions may interleave on all three shared queues. All workflows continue until stopped. Queue execution capacity limits running work, not sample submissions. Stop arrivals explicitly to demonstrate backlog recovery.

**Completion and failure:** The delivery snapshot (`multiple-fifo-roots`) records accepted root IDs in submission order across all three queues. Workflow Completion means arrivals and in-progress submissions have stopped, not that jobs or observers have drained. **Stop arrivals** stops every workflow without cancelling accepted work. A failed submission is recorded without preventing attempts on the remaining queues or later ticks. Queued/running roots never suppress new submissions. Only overlapping submission callbacks are skipped; queue backlog is handled by the product queues and observed by the presentation pipeline.

**Validation prepared:** `ScheduledWorkflowDeliveryTests` covers continuous fresh-root delivery, including more than six still-active roots, selected queues, failures, stop/cancellation and invalid intervals. `SimulationRegistrationTests` covers additive/idempotent registration. `SingleJobSimulationTests.MultipleFifoRoots_BuildsVisibleBacklogThenExecutesInSubmissionOrderPerQueue` gates both direct and hydrated handlers, accepts at least eight roots per queue, inspects the FIFO backlog, then stops arrivals and verifies handler ordering per queue, observed channels, completion and independent memberships. `ContinuousSimulationIntegrationTests` composes all three continuous workflows and checks that UC03 reaches every queue and accepted work finishes after explicit session stop. The original two-workflow test case remains. These tests have not been executed for this change.

**Browser acceptance:** Expand **U** in each queue to inspect observed waiting roots and retained enqueue markers. FIFO executes on channel zero; ParallelQ and CacheQ display their observed channels, with CacheQ observations following its cache/hydration lifecycle. All workflows keep submitting fresh jobs. **Stop arrivals** ends the whole session's arrivals; keep the host running until queued work finishes. Opening another tab must observe the same workload without restarting it. Backlog and drain time depend on how long arrivals run.

Runtime dispatch coverage checks both enqueue modes for all three queue types,
including the generic CacheQ path, graph registration, linked cancellation and
cleanup after submission failure or terminal observation. A workflow regression
checks that UC03 calls `EnqueueFifo` for every fresh root. Execution remains pending
human validation.

**Dependencies:** [UC02](uc02-sequential-etl-chain.md) and existing scheduled admission.

**Product limitations:** None identified; derive ordering from the supported FIFO contract rather than introducing sample scheduling semantics.
