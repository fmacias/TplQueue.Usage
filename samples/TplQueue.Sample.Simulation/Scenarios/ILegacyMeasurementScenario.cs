using System;
using System.Collections.Generic;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Scenarios
{

    /// <summary>Runs the predefined workload that adapts consumer data to the shared ETL workflow.</summary>
    internal interface ILegacyMeasurementScenario
    {
        Guid Run(AvailableQueue availlableQueue, CancellationToken cancellationToken);
    }

}
