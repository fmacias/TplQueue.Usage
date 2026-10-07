using System;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Runtime;

namespace TplQueue.Sample.Simulation.Workflows.SingleJob
{
    /// <summary>Schedules one independent ingest root per arrival on every available queue.</summary>
    internal sealed class SingleJobWorkflow : ScheduledWorkflow
    {
        private const string WorkflowId = "single";
        private const int ActiveRunLimit = 1;
        private readonly IMeasurementSource _measurements;
        private readonly ISampleJobFactory _jobsFactory;

        public SingleJobWorkflow(IEtlQueueRuntime runtime,
            IMeasurementSource measurements, ISampleJobFactory jobs) : base(runtime)
        {
            _measurements = measurements ?? throw new ArgumentNullException(nameof(measurements));
            _jobsFactory = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        protected override string ScenarioId => WorkflowId;
        protected override int MaximumActiveRuns => ActiveRunLimit;

        protected override Guid Submit(AvailableQueue queue, CancellationToken cancellationToken)
        {
            var root = _jobsFactory.IngestMeasurementsSigleJobRoot(_measurements.Collect(cancellationToken));
            Runtime.Enqueue(queue, root, cancellationToken);
            return root.Id;
        }
    }
}
