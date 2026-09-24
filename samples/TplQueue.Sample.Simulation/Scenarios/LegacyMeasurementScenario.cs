using System;
using System.Collections.Generic;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Scenarios
{

    /// <summary>Submits consumer-owned measurements to the predefined shared ETL workflow.</summary>
    internal sealed class LegacyMeasurementScenario : ILegacyMeasurementScenario
    {
        private readonly ILegacyMeasurementCollector _collector;
        private readonly IEtlWorkflow _workflow;

        public LegacyMeasurementScenario(
            ILegacyMeasurementCollector collector,
            IEtlWorkflow workflow)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public Guid Run(AvailableQueue availlableQueue, CancellationToken cancellationToken)
        {
            var legacyMeasurements = _collector.Collect(cancellationToken);

            return _workflow.EnqueueMeasurements(availlableQueue, legacyMeasurements, cancellationToken);
        }
    }

}
