using System;
using System.Threading;
using Fmacias.TplQueue.Contracts;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Handlers;
using TplQueue.Sample.Simulation.Payloads;
using TplQueue.Sample.Simulation.Runtime;

namespace TplQueue.Sample.Simulation.Scenarios
{
    /// <summary>Submits one cache-compatible ingest root without composing any dependencies.</summary>
    internal sealed class SingleJobScenario
    {
        private readonly ILegacyMeasurementCollector _collector;
        private readonly IDataJobFactory _factory;
        private readonly IngestMeasurementsHandler _handler;
        private readonly EtlQueueRuntime _runtime;

        public SingleJobScenario(ILegacyMeasurementCollector collector, IDataJobFactory factory,
            IngestMeasurementsHandler handler, EtlQueueRuntime runtime)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>Creates a fresh root identity and submits it to the selected real queue.</summary>
        public Guid Run(AvailableQueue queue, CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(typeof(AvailableQueue), queue)) throw new ArgumentOutOfRangeException(nameof(queue));
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            var payload = IngestMeasurementsPayload.Create(_collector.Collect(cancellationToken), id);
            var root = _factory.DataJobRoot(id, payload, _handler, "Single job: ingest measurements");
            _runtime.Enqueue(queue, root, cancellationToken);
            return id;
        }
    }
}
