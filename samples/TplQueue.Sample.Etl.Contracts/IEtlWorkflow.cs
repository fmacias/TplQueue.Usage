using System;
using System.Collections.Generic;
using System.Threading;
using TplQueue.Sample.Etl.Contracts.Dto;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Provides the predefined sample ETL workflow.</summary>
    public interface IEtlWorkflow
    {
        /// <summary>Enqueues collected measurements and returns the root job identifier.</summary>
        Guid EnqueueMeasurements(
            AvailableQueue availableQueue,
            IReadOnlyList<LegacyMeasurement> legacyMeasurements, 
            CancellationToken cancellationToken);

        /// <summary>Cancels the graph identified by its root job identifier.</summary>
        bool Cancel(Guid rootJobId);
    }
}
