using System;
using System.Collections.Generic;
using System.Threading;
using TplQueue.Sample.Etl.Contracts.Dto;

namespace TplQueue.Sample.Simulation.Scenarios
{

    internal interface ILegacyMeasurementCollector
    {
        IReadOnlyList<LegacyMeasurement> Collect(CancellationToken cancellationToken);
    }

}
