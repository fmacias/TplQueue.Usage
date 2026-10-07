using System.Collections.Generic;
using System.Threading;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Supplies collected measurements to either simulation workflow.</summary>
    public interface IMeasurementSource
    {
        /// <summary>Collects one batch, honouring cancellation before reading measurements.</summary>
        IReadOnlyList<LegacyMeasurement> Collect(CancellationToken cancellationToken);
    }
}
