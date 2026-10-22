namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>Validates and resolves queue metadata without retaining runtime queue instances.</summary>
internal sealed class JobMonitorQueueCatalog
{
    private readonly IReadOnlyDictionary<Guid, JobMonitorQueue> _byQueueId;

    public JobMonitorQueueCatalog(IEnumerable<JobMonitorQueue> queues)
    {
        ArgumentNullException.ThrowIfNull(queues);
        var items = queues.ToArray();
        if (items.Length == 0 || items.Any(q => q is null || q.QueueId == Guid.Empty ||
                string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Name) ||
                q.MaxParallelism < 1 || q.MaxParallelism > 1024))
            throw new ArgumentException("Queues require IDs, names and capacity between 1 and 1024.", nameof(queues));
        if (items.Select(q => q.QueueId).Distinct().Count() != items.Length ||
            items.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Queue IDs and display IDs must be unique.", nameof(queues));
        Queues = Array.AsReadOnly(items);
        _byQueueId = items.ToDictionary(q => q.QueueId);
    }

    public IReadOnlyList<JobMonitorQueue> Queues { get; }

    public JobMonitorQueue Resolve(Guid queueId) => _byQueueId.TryGetValue(queueId, out var queue)
        ? queue : throw new InvalidOperationException($"Queue '{queueId}' is not part of the monitor catalog.");
}
