using System;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Workflows.MultipleFifoRoots
{
    /// <summary>Continuously submits fresh Domain roots with FIFO ordering on every available queue.</summary>
    internal sealed class MultipleFifoRootsWorkflow : ScheduledWorkflow
    {
        private const int ArrivalIntervalMilliseconds = 100;
        private readonly IMeasurementSource _measurements;
        private readonly ISampleJobFactory _jobs;

        public MultipleFifoRootsWorkflow(IEtlQueueRuntime runtime, IMeasurementSource measurements,
            ISampleJobFactory jobs)
            : base(runtime, intervalMilliseconds: ArrivalIntervalMilliseconds)
        {
            _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        protected override string ScenarioId => "multiple-fifo-roots";

        protected override Guid Submit(AvailableQueue queue, CancellationToken cancellationToken)
        {
            // The existing 500 ms ingest handler is slower than the 100 ms arrival spacing.
            var root = _jobs.IngestMeasurementsSigleJobRoot(_measurements.Collect(cancellationToken));
            Runtime.EnqueueFifo(queue, root, cancellationToken);
            return root.Id;
        }
    }
}
