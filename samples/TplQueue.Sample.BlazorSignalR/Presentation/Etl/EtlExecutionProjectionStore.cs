using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>
/// Builds an idempotent, thread-safe projection from the three queue event streams.
/// </summary>
internal sealed class EtlExecutionProjectionStore : IEtlExecutionProjectionStore
{
    private const int MaximumErrorLength = 256;
    private readonly EtlQueueCatalog _catalog;
    private readonly ILogger<EtlExecutionProjectionStore> _logger;
    private readonly ISimulationGraphCatalog? _graphs;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, MutableJob> _jobs = new();
    private readonly HashSet<EventFingerprint> _seenEvents = new();

    public EtlExecutionProjectionStore(
        EtlQueueCatalog catalog,
        ILogger<EtlExecutionProjectionStore> logger,
        ISimulationGraphCatalog? graphs = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _graphs = graphs;
    }

    public event EventHandler? Changed;

    public void Apply(IJobEvent jobEvent)
    {
        if (jobEvent == null) throw new ArgumentNullException(nameof(jobEvent));
        if (jobEvent.JobInfo == null)
        {
            throw new ArgumentException("A projected event must contain job metadata.", nameof(jobEvent));
        }

        var descriptor = _catalog.Resolve(jobEvent.JobInfo.CrossQueueId);
        var channel = (jobEvent as IJobExecutionEvent)?.ExecutionChannel;
        if (channel.HasValue && (channel.Value < 0 || channel.Value >= descriptor.MaxParallelism))
            throw new ArgumentOutOfRangeException(nameof(jobEvent), "Execution channel is outside the queue capacity.");
        var fingerprint = EventFingerprint.Create(jobEvent);

        lock (_sync)
        {
            if (!_seenEvents.Add(fingerprint))
            {
                return;
            }

            var job = GetOrCreateJob(jobEvent.JobInfo, descriptor, jobEvent.Timestamp);
            // A late pre-execution event must never erase captured execution identity.
            if (channel.HasValue && (job.ChannelObservedAt == null || ToOffset(jobEvent.Timestamp) >= job.ChannelObservedAt))
            {
                if (job.ExecutionChannel != channel) job.ChannelStartedAt = null;
                job.ExecutionChannel = channel;
                job.ChannelObservedAt = ToOffset(jobEvent.Timestamp);
            }
            // Running/terminal metadata cannot substitute for the time capacity was acquired.
            // A delayed Started event can enrich the same channel without regressing status.
            if (jobEvent.Status == JobEventStatus.Started && channel.HasValue && job.ExecutionChannel == channel)
                job.ChannelStartedAt = Earlier(job.ChannelStartedAt, ToOffset(jobEvent.Timestamp));
            CaptureDependencies(job, jobEvent.JobInfo);
            ApplyLifecycle(job, jobEvent);

            if (jobEvent.Status == JobEventStatus.RootSuccessed &&
                (_graphs?.GetRootJobIds(job.JobId).Count ?? 0) == 0)
            {
                // A root event carries the complete dependency graph. Use the
                // root event timestamp for any dependency that has not emitted
                // its own event yet; this is the last honest time available to
                // the passive projection and keeps the connector endpoint
                // visible without inventing a current wall-clock timestamp.
                AssignRoot(jobEvent.JobInfo, jobEvent.JobInfo.Id, new HashSet<Guid>(), jobEvent.Timestamp);
            }
        }

        NotifyChangedSubscribers();
    }

    public EtlDashboardSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var jobs = _jobs.Values
                .OrderBy(job => job.FirstObservedAt)
                .ThenBy(job => job.JobId)
                .Select(job => job.ToSnapshot(_graphs?.GetRootJobIds(job.JobId)))
                .ToArray();
            var queues = _catalog.Descriptors
                .Select(descriptor => CreateQueueSnapshot(descriptor, jobs))
                .ToArray();

            return new EtlDashboardSnapshot(queues, jobs);
        }
    }

    private MutableJob GetOrCreateJob(
        IJobInfo jobInfo,
        EtlQueueDescriptor descriptor,
        DateTime timestamp)
    {
        if (_jobs.TryGetValue(jobInfo.Id, out var existing))
        {
            existing.FirstObservedAt = Earlier(existing.FirstObservedAt, ToOffset(timestamp));
            return existing;
        }

        var created = new MutableJob(
            jobInfo.Id,
            jobInfo.Name,
            descriptor.GroupId,
            descriptor.DisplayName,
            ToOffset(timestamp));
        _jobs.Add(created.JobId, created);
        return created;
    }

    private void ApplyLifecycle(MutableJob job, IJobEvent jobEvent)
    {
        var observedAt = ToOffset(jobEvent.Timestamp);
        var mappedStatus = MapStatus(jobEvent.Status);

        if (IsEnqueued(jobEvent.Status))
        {
            job.EnqueuedAt = Earlier(job.EnqueuedAt, observedAt);
        }

        if (IsRunning(jobEvent.Status))
        {
            job.StartedAt = Earlier(job.StartedAt, observedAt);
        }

        if (IsTerminal(jobEvent.Status))
        {
            job.EndedAt = Later(job.EndedAt, observedAt);
        }

        if (StatusRank(mappedStatus) > StatusRank(job.Status) ||
            (StatusRank(mappedStatus) == StatusRank(job.Status) && observedAt >= job.StatusObservedAt))
        {
            job.Status = mappedStatus;
            job.LastEventType = jobEvent.Status.ToString();
            job.LastEventAt = observedAt;
            job.StatusObservedAt = observedAt;
        }

        job.RetryCount = Math.Max(job.RetryCount, jobEvent.RetryCount);
        if (jobEvent.Exception != null)
        {
            job.Error = SummarizeError(jobEvent.Exception.Message);
        }
    }

    private void AssignRoot(IJobInfo jobInfo, Guid rootJobId, ISet<Guid> visited, DateTime observedAt)
    {
        if (!visited.Add(jobInfo.Id))
        {
            return;
        }

        var descriptor = _catalog.Resolve(jobInfo.CrossQueueId);
        var job = GetOrCreateJob(jobInfo, descriptor, observedAt);

        if (jobInfo.Id != rootJobId && job.RootJobId == jobInfo.Id)
        {
            return;
        }

        job.RootJobId = rootJobId;
        CaptureDependencies(job, jobInfo);

        foreach (var dependency in jobInfo.Dependencies ?? Array.Empty<IJobInfo>())
        {
            AssignRoot(dependency, rootJobId, visited, observedAt);
        }
    }

    private static void CaptureDependencies(MutableJob job, IJobInfo jobInfo)
    {
        foreach (var dependency in jobInfo.Dependencies ?? Array.Empty<IJobInfo>())
        {
            job.DependencyJobIds.Add(dependency.Id);
        }
    }

    private static EtlQueueSnapshot CreateQueueSnapshot(
        EtlQueueDescriptor descriptor,
        IReadOnlyCollection<EtlJobSnapshot> jobs)
    {
        var queueJobs = jobs
            .Where(job => string.Equals(
                job.QueueGroupId,
                descriptor.GroupId,
                StringComparison.Ordinal))
            .ToArray();

        return new EtlQueueSnapshot(
            descriptor.GroupId,
            descriptor.DisplayName,
            descriptor.Order,
            queueJobs.Length,
            queueJobs.Count(job => job.Status == "running"),
            queueJobs.Count(job => job.Status == "completed"),
            queueJobs.Count(job => job.Status == "failed"),
            descriptor.MaxParallelism);
    }

    private void NotifyChangedSubscribers()
    {
        var subscribers = Changed;
        if (subscribers == null)
        {
            return;
        }

        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "An ETL dashboard subscriber rejected a projection update.");
            }
        }
    }

    private static bool IsEnqueued(JobEventStatus status) =>
        status is JobEventStatus.Cache or JobEventStatus.Enqueueing or JobEventStatus.Enqueued;

    private static bool IsRunning(JobEventStatus status) =>
        status is JobEventStatus.Dequeued or JobEventStatus.Started or JobEventStatus.Running;

    private static bool IsTerminal(JobEventStatus status) =>
        status is JobEventStatus.Successed or JobEventStatus.RootSuccessed or
            JobEventStatus.Failed or JobEventStatus.Canceled;

    private static string MapStatus(JobEventStatus status) => status switch
    {
        JobEventStatus.Successed or JobEventStatus.RootSuccessed => "completed",
        JobEventStatus.Failed => "failed",
        JobEventStatus.Canceled => "canceled",
        JobEventStatus.Dequeued or JobEventStatus.Started or JobEventStatus.Running => "running",
        _ => "queued"
    };

    private static int StatusRank(string status) => status switch
    {
        "completed" or "failed" or "canceled" => 3,
        "running" => 2,
        _ => 1
    };

    private static DateTimeOffset ToOffset(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private static DateTimeOffset? Earlier(DateTimeOffset? first, DateTimeOffset second) =>
        !first.HasValue || second < first.Value ? second : first;

    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset second) =>
        !first.HasValue || second > first.Value ? second : first;

    private static string SummarizeError(string? message)
    {
        var singleLine = string.Join(
                " ",
                (message ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0))
            .Trim();

        if (singleLine.Length <= MaximumErrorLength)
        {
            return singleLine;
        }

        return singleLine.Substring(0, MaximumErrorLength);
    }

    private readonly record struct EventFingerprint(
        Guid JobId,
        JobEventStatus Status,
        DateTime Timestamp,
        int RetryCount,
        string? Error,
        int? ExecutionChannel)
    {
        public static EventFingerprint Create(IJobEvent jobEvent) => new(
            jobEvent.JobInfo.Id,
            jobEvent.Status,
            jobEvent.Timestamp,
            jobEvent.RetryCount,
            jobEvent.Exception?.Message,
            (jobEvent as IJobExecutionEvent)?.ExecutionChannel);
    }

    private sealed class MutableJob
    {
        public MutableJob(
            Guid jobId,
            string name,
            string queueGroupId,
            string queueDisplayName,
            DateTimeOffset firstObservedAt)
        {
            JobId = jobId;
            Name = name;
            QueueGroupId = queueGroupId;
            QueueDisplayName = queueDisplayName;
            FirstObservedAt = firstObservedAt;
            StatusObservedAt = firstObservedAt;
            LastEventAt = firstObservedAt;
        }

        public Guid JobId { get; }
        public Guid? RootJobId { get; set; }
        public HashSet<Guid> DependencyJobIds { get; } = new();
        public string Name { get; }
        public string QueueGroupId { get; }
        public string QueueDisplayName { get; }
        public string Status { get; set; } = "queued";
        public DateTimeOffset StatusObservedAt { get; set; }
        public string LastEventType { get; set; } = "Observed";
        public DateTimeOffset LastEventAt { get; set; }
        public DateTimeOffset FirstObservedAt { get; set; }
        public DateTimeOffset? EnqueuedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int RetryCount { get; set; }
        public string? Error { get; set; }
        public int? ExecutionChannel { get; set; }
        public DateTimeOffset? ChannelObservedAt { get; set; }
        public DateTimeOffset? ChannelStartedAt { get; set; }

        public EtlJobSnapshot ToSnapshot(IReadOnlyList<Guid>? registeredRoots)
        {
            var roots = registeredRoots is { Count: > 0 }
                ? registeredRoots
                : RootJobId.HasValue ? new[] { RootJobId.Value } : Array.Empty<Guid>();
            // Keep the legacy singular value only when unambiguous (or this job is itself a root).
            var rootId = roots.Contains(JobId) ? JobId : roots.Count == 1 ? roots[0] : (Guid?)null;
            return new(
                JobId,
                rootId,
                DependencyJobIds.OrderBy(id => id).ToArray(),
                Name,
                QueueGroupId,
                QueueDisplayName,
                Status,
                FirstObservedAt,
                EnqueuedAt,
                StartedAt,
                EndedAt,
                RetryCount,
                Error,
                LastEventType,
                LastEventAt,
                ExecutionChannel,
                ChannelStartedAt,
                Array.AsReadOnly(roots.ToArray()));
        }
    }
}
