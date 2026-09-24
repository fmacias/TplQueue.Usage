namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>
/// Detached materialized view of observed job events, sent to the reusable job monitor.
/// Replace the snapshot after a projection change; do not mutate it after delivery.
/// This is accumulated job state, not a one-to-one runtime event transport contract.
/// </summary>
public sealed record JobMonitorSnapshot(IReadOnlyList<JobMonitorQueue> Queues, IReadOnlyList<JobMonitorJob> Jobs);

/// <summary>Queue identity and its configured execution capacity.</summary>
public sealed record JobMonitorQueue(string Id, string Name, int MaxParallelism);

/// <summary>
/// One observed job. Assigned positions use a captured Started event timestamp.
/// A null channel means the channel or its acquisition timestamp is not yet known.
/// EnqueuedAt retains the observed pre-execution position after channel assignment.
/// RootJobIds preserves every composed run membership. RootJobId is null for shared non-root jobs.
/// </summary>
public sealed record JobMonitorJob(string Id, string? RootJobId, string Name, string Description,
    string QueueId, int? Channel, DateTimeOffset ObservedAt, string State, double? DurationMs,
    IReadOnlyList<string> DependsOn, IReadOnlyDictionary<string, string> Metadata, bool IsRoot,
    DateTimeOffset? EnqueuedAt = null,
    IReadOnlyList<string>? RootJobIds = null);

/// <summary>Maps presentation snapshots without exposing runtime objects or inventing execution facts.</summary>
internal static class JobMonitorMapper
{
    public static JobMonitorSnapshot Map(EtlDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new JobMonitorSnapshot(
            snapshot.Queues.Select(q => new JobMonitorQueue(q.GroupId, q.DisplayName, q.MaxParallelism)).ToArray(),
            snapshot.Jobs.Select(MapJob).ToArray());
    }

    /// <summary>Pairs a channel with its acquisition time and retains the observed enqueue position.</summary>
    private static JobMonitorJob MapJob(EtlJobSnapshot job)
    {
        var assigned = job.ExecutionChannel.HasValue && job.ChannelStartedAt.HasValue;
        var metadata = new Dictionary<string, string>
        {
            ["event"] = job.LastEventType,
            ["retryCount"] = job.RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["durationSource"] = "observer lifecycle timestamps",
            ["timestampSource"] = assigned ? "Started" : job.EnqueuedAt.HasValue ? "Enqueued" : "FirstObserved"
        };
        if (job.EnqueuedAt.HasValue)
            metadata["enqueuedAt"] = job.EnqueuedAt.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        return new JobMonitorJob(
            job.JobId.ToString(), job.RootJobId?.ToString(), job.Name,
            string.Empty, // The current observer contract supplies no description.
            job.QueueGroupId, assigned ? job.ExecutionChannel : null,
            assigned ? job.ChannelStartedAt!.Value : job.EnqueuedAt ?? job.FirstObservedAt,
            job.Status switch { "queued" => "waiting", "canceled" => "cancelled", "running" when job.RetryCount > 0 => "retried", _ => job.Status },
            job.StartedAt.HasValue && job.EndedAt.HasValue ? Math.Max(0, (job.EndedAt.Value - job.StartedAt.Value).TotalMilliseconds) : null,
            job.DependencyJobIds.Select(id => id.ToString()).ToArray(), metadata, job.RootJobId == job.JobId, job.EnqueuedAt,
            Array.AsReadOnly((job.RootJobIds ?? (job.RootJobId.HasValue ? new[] { job.RootJobId.Value } : Array.Empty<Guid>()))
                .Select(id => id.ToString()).ToArray()));
    }
}
