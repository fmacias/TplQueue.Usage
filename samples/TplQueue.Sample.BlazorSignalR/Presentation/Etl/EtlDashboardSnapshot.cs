using System.Collections.ObjectModel;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

internal sealed record EtlJobSnapshot(
    Guid JobId,
    Guid? RootJobId,
    IReadOnlyList<Guid> DependencyJobIds,
    string Name,
    string QueueGroupId,
    string QueueDisplayName,
    string Status,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset? EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int RetryCount,
    string? Error,
    string LastEventType,
    DateTimeOffset LastEventAt,
    int? ExecutionChannel = null);

internal sealed record EtlQueueSnapshot(
    string GroupId,
    string DisplayName,
    int Order,
    int JobCount,
    int RunningCount,
    int CompletedCount,
    int FailedCount,
    int MaxParallelism = 1);

internal sealed class EtlDashboardSnapshot
{
    public EtlDashboardSnapshot(IReadOnlyList<EtlQueueSnapshot> queues, IReadOnlyList<EtlJobSnapshot> jobs)
    {
        Queues = new ReadOnlyCollection<EtlQueueSnapshot>((queues ?? throw new ArgumentNullException(nameof(queues))).ToArray());
        Jobs = new ReadOnlyCollection<EtlJobSnapshot>((jobs ?? throw new ArgumentNullException(nameof(jobs))).ToArray());
    }

    public IReadOnlyList<EtlQueueSnapshot> Queues { get; }
    public IReadOnlyList<EtlJobSnapshot> Jobs { get; }
}
