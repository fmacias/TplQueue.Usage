using Fmacias.TplQueue.Snapshots;
using Microsoft.Extensions.Logging;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>
/// Retains original immutable observations under a lock. It neither reconciles lifecycle state nor
/// fingerprints events: equal metadata can describe distinct occurrences without a producer event ID.
/// </summary>
internal sealed class JobMonitorStore : IJobMonitorStore
{
    private readonly JobMonitorQueueCatalog _catalog;
    private readonly ILogger<JobMonitorStore> _logger;
    private readonly ISimulationGraphCatalog? _graphs;
    private readonly int _maximumEvents;
    private readonly object _sync = new();
    private readonly LinkedList<JobEventSnapshot> _events = new();
    private readonly Dictionary<Guid, List<LinkedListNode<JobEventSnapshot>>> _jobs = new();
    private readonly Queue<Guid> _jobOrder = new();
    private long _revision, _droppedEventCount;

    public JobMonitorStore(JobMonitorQueueCatalog catalog, ILogger<JobMonitorStore> logger,
        ISimulationGraphCatalog? graphs = null, JobMonitorRetentionOptions? retention = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _graphs = graphs;
        _maximumEvents = (retention ?? new JobMonitorRetentionOptions()).MaximumEvents;
        if (_maximumEvents < 1) throw new ArgumentOutOfRangeException(nameof(retention));
    }

    public event EventHandler? Changed;

    /// <summary>Appends one occurrence after validation, then notifies circuits outside the lock.</summary>
    public void Apply(JobEventSnapshot observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var queue = _catalog.Resolve(observation.JobInfo.CrossQueueId);
        if (observation.ExecutionChannel is int channel && (channel < 0 || channel >= queue.MaxParallelism))
            throw new ArgumentOutOfRangeException(nameof(observation), "Execution channel is outside queue capacity.");
        lock (_sync)
        {
            var id = observation.JobInfo.Id;
            if (!_jobs.TryGetValue(id, out var occurrences))
            {
                occurrences = new List<LinkedListNode<JobEventSnapshot>>();
                _jobs.Add(id, occurrences);
                _jobOrder.Enqueue(id);
            }
            else if (occurrences[0].Value.JobInfo.CrossQueueId != observation.JobInfo.CrossQueueId)
                throw new InvalidOperationException("An observed job cannot change queue identity.");

            occurrences.Add(_events.AddLast(observation));
            _revision++;
            EvictOldestJobs();
        }
        NotifySubscribers();
    }

    /// <summary>Atomically captures retained occurrences and copies composition membership for observed jobs.</summary>
    public JobMonitorSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var roots = new Dictionary<Guid, IReadOnlyList<Guid>>();
            if (_graphs != null)
                foreach (var id in _jobs.Keys) roots.Add(id, _graphs.GetRootJobIds(id));
            return new JobMonitorSnapshot(_catalog.Queues, _events, roots, _revision, _droppedEventCount);
        }
    }

    private void EvictOldestJobs()
    {
        while (_events.Count > _maximumEvents)
        {
            var id = _jobOrder.Dequeue();
            var occurrences = _jobs[id];
            foreach (var occurrence in occurrences) _events.Remove(occurrence);
            _droppedEventCount += occurrences.Count;
            _jobs.Remove(id);
        }
    }

    private void NotifySubscribers()
    {
        var subscribers = Changed;
        if (subscribers == null) return;
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(this, EventArgs.Empty); }
            catch (Exception error) { _logger.LogWarning(error, "A monitor subscriber rejected an observation update."); }
        }
    }
}
