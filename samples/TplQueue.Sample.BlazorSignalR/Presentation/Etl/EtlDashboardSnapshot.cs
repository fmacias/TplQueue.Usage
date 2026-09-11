using System.Collections.ObjectModel;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>Immutable job lifecycle projection consumed by the dashboard.</summary>
internal sealed record EtlJobSnapshot(
    Guid JobId,
    Guid? RootJobId,
    string Name,
    string QueueGroupId,
    string QueueDisplayName,
    string Status,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset? EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int RetryCount,
    string? Error);

/// <summary>Immutable aggregate counts for one queue group.</summary>
internal sealed record EtlQueueSnapshot(
    string GroupId,
    string DisplayName,
    int Order,
    int JobCount,
    int RunningCount,
    int CompletedCount,
    int FailedCount);

/// <summary>Immutable application-wide ETL dashboard state.</summary>
internal sealed class EtlDashboardSnapshot
{
    public EtlDashboardSnapshot(
        IReadOnlyList<EtlQueueSnapshot> queues,
        IReadOnlyList<EtlJobSnapshot> jobs)
    {
        if (queues == null) throw new ArgumentNullException(nameof(queues));
        if (jobs == null) throw new ArgumentNullException(nameof(jobs));

        Queues = new ReadOnlyCollection<EtlQueueSnapshot>(queues.ToArray());
        Jobs = new ReadOnlyCollection<EtlJobSnapshot>(jobs.ToArray());
    }

    public IReadOnlyList<EtlQueueSnapshot> Queues { get; }
    public IReadOnlyList<EtlJobSnapshot> Jobs { get; }
}
