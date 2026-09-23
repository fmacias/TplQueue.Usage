using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TplQueue.Sample.Simulation.Runtime
{
    /// <summary>
    /// Holds only the business data passed between steps of an ETL execution.
    /// Job lifecycle information remains owned by <c>IJobEvent</c>.
    /// </summary>
    internal sealed class EtlExecutionDataStore
    {
        private readonly ConcurrentDictionary<Guid, IReadOnlyList<decimal>> _measurements =
            new ConcurrentDictionary<Guid, IReadOnlyList<decimal>>();

        private readonly ConcurrentDictionary<Guid, string> _summaries =
            new ConcurrentDictionary<Guid, string>();

        public void StoreMeasurements(Guid etlOperationId, IEnumerable<decimal> measurements)
        {
            if (etlOperationId == Guid.Empty)
            {
                throw new ArgumentException("An operation identifier is required.", nameof(etlOperationId));
            }

            if (measurements == null)
            {
                throw new ArgumentNullException(nameof(measurements));
            }

            _measurements[etlOperationId] = measurements.ToArray();
        }

        public bool TryGetMeasurements(Guid etlOperationId, out IReadOnlyList<decimal>? measurements)
        {
            return _measurements.TryGetValue(etlOperationId, out measurements);
        }

        public void StoreSummary(Guid etlOperationId, string summary)
        {
            if (etlOperationId == Guid.Empty)
            {
                throw new ArgumentException("An operation identifier is required.", nameof(etlOperationId));
            }

            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            _summaries[etlOperationId] = summary;
        }

        public bool TryGetSummary(Guid etlOperationId, out string? summary)
        {
            return _summaries.TryGetValue(etlOperationId, out summary);
        }

        public void Remove(Guid etlOperationId)
        {
            _measurements.TryRemove(etlOperationId, out _);
            _summaries.TryRemove(etlOperationId, out _);
        }
    }
}
