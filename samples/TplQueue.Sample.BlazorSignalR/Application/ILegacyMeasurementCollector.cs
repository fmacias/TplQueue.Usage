using TplQueue.Sample.Etl.Contracts.Dto;

namespace TplQueue.Sample.BlazorSignalR.Application;

internal interface ILegacyMeasurementCollector
{
    IReadOnlyList<LegacyMeasurement> Collect(CancellationToken cancellationToken);
}
