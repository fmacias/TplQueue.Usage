using System;
using System.Collections.Generic;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Measurements
{
    /// <summary>Simulates measurements already collected by a backend component.</summary>
    internal sealed class SampleMeasurementSource : IMeasurementSource
    {
        public IReadOnlyList<LegacyMeasurement> Collect(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collectedAt = DateTime.UtcNow;
            return new[]
            {
                new LegacyMeasurement("boiler-outlet", 72.5m, TemperatureUnit.Celsius, collectedAt.AddSeconds(-3)),
                new LegacyMeasurement("warehouse-zone-a", 68.0m, TemperatureUnit.Fahrenheit, collectedAt.AddSeconds(-2)),
                new LegacyMeasurement("warehouse-zone-b", 20.5m, TemperatureUnit.Celsius, collectedAt.AddSeconds(-1))
            };
        }
    }
}
