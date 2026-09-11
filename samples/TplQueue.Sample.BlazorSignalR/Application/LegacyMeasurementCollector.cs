using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Etl.Contracts.Dto;

namespace TplQueue.Sample.BlazorSignalR.Application;

/// <summary>Simulates data already collected by a legacy backend component.</summary>
internal sealed class LegacyMeasurementCollector : ILegacyMeasurementCollector
{
    public IReadOnlyList<LegacyMeasurement> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collectedAt = DateTime.UtcNow;

        return new[]
        {
            new LegacyMeasurement(
                "boiler-outlet",
                72.5m,
                TemperatureUnit.Celsius,
                collectedAt.AddSeconds(-3)),
            new LegacyMeasurement(
                "warehouse-zone-a",
                68.0m,
                TemperatureUnit.Fahrenheit,
                collectedAt.AddSeconds(-2)),
            new LegacyMeasurement(
                "warehouse-zone-b",
                20.5m,
                TemperatureUnit.Celsius,
                collectedAt.AddSeconds(-1))
        };
    }
}
