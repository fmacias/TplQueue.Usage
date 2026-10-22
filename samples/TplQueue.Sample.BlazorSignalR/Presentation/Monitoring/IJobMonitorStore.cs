using Fmacias.TplQueue.Snapshots;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>Accepts detached observations from any transport and exposes the retained window.</summary>
internal interface IJobMonitorStore
{
    event EventHandler? Changed;
    void Apply(JobEventSnapshot observation);
    JobMonitorSnapshot GetSnapshot();
}
