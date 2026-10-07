using System;
using System.Collections.Generic;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface IEtlExecutionDataStore
    {
        void Remove(Guid etlOperationId);
        void StoreMeasurements(Guid etlOperationId, IEnumerable<decimal> measurements);
        void StoreSummary(Guid etlOperationId, string summary);
        bool TryGetMeasurements(Guid etlOperationId, out IReadOnlyList<decimal>? measurements);
        bool TryGetSummary(Guid etlOperationId, out string? summary);
    }
}