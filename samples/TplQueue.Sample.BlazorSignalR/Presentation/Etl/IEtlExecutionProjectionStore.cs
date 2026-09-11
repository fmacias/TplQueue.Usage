using Fmacias.TplQueue.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>Materializes immutable job-event metadata for passive presentation.</summary>
internal interface IEtlExecutionProjectionStore
{
    event EventHandler? Changed;

    void Apply(IJobEvent jobEvent);

    EtlDashboardSnapshot GetSnapshot();
}
