using System;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Runtime;

namespace TplQueue.Sample.Simulation.Workflows.Etl
{
    /// <summary>Schedules a distinct Ingest, Transform and Load graph on every available queue.</summary>
    internal sealed class EtlSimulationWorkflow : ScheduledWorkflow
    {
        private const string WorkflowId = "etl";
        private const int ActiveRunLimit = 2;
        private readonly IMeasurementSource _measurementsSource;
        private readonly ISampleJobFactory _jobsFactory;

        public EtlSimulationWorkflow(IEtlQueueRuntime runtime, IMeasurementSource measurementsSource,
            ISampleJobFactory jobs) : base(runtime)
        {
            _measurementsSource = measurementsSource ?? throw new ArgumentNullException(nameof(measurementsSource));
            _jobsFactory = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        protected override string ScenarioId => WorkflowId;
        protected override int MaximumActiveRuns => ActiveRunLimit;

        protected override Guid Submit(AvailableQueue queue, CancellationToken cancellationToken)
        {
            var root = _jobsFactory.LoadMeasurementsJobRoot(_measurementsSource.Collect(cancellationToken));
            Runtime.Enqueue(queue, root, cancellationToken);
            return root.Id;
        }
    }
}
