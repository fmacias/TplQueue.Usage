namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>Detached presentation values sent to the reusable job monitor.</summary>
public sealed record JobMonitorSnapshot(IReadOnlyList<JobMonitorQueue> Queues, IReadOnlyList<JobMonitorJob> Jobs);

/// <summary>Queue identity and its configured execution capacity.</summary>
public sealed record JobMonitorQueue(string Id, string Name, int MaxParallelism);

/// <summary>One observed job. A null channel explicitly means no execution slot is known.</summary>
public sealed record JobMonitorJob(string Id, string? RootJobId, string Name, string Description,
    string QueueId, int? Channel, DateTimeOffset ObservedAt, string State, double? DurationMs,
    IReadOnlyList<string> DependsOn, IReadOnlyDictionary<string, string> Metadata, bool IsRoot);

/// <summary>Maps presentation snapshots without exposing runtime objects or inventing execution facts.</summary>
internal static class JobMonitorMapper
{
    public static JobMonitorSnapshot Map(EtlDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new JobMonitorSnapshot(
            snapshot.Queues.Select(q => new JobMonitorQueue(q.GroupId, q.DisplayName, q.MaxParallelism)).ToArray(),
            snapshot.Jobs.Select(j => new JobMonitorJob(
                j.JobId.ToString(), j.RootJobId?.ToString(), j.Name,
                string.Empty, // The current observer contract supplies no description.
                j.QueueGroupId, j.ExecutionChannel, j.FirstObservedAt,
                j.Status switch { "queued" => "waiting", "canceled" => "cancelled", "running" when j.RetryCount > 0 => "retried", _ => j.Status },
                j.StartedAt.HasValue && j.EndedAt.HasValue ? Math.Max(0, (j.EndedAt.Value - j.StartedAt.Value).TotalMilliseconds) : null,
                j.DependencyJobIds.Select(id => id.ToString()).ToArray(),
                new Dictionary<string, string>
                {
                    ["event"] = j.LastEventType,
                    ["retryCount"] = j.RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["durationSource"] = "observer lifecycle timestamps"
                }, j.RootJobId == j.JobId)).ToArray());
    }
}
