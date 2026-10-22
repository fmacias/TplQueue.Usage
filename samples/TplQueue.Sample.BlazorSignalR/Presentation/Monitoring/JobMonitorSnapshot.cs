using System.Collections.ObjectModel;
using Fmacias.TplQueue.Snapshots;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>
/// A detached retained window of original observations, plus queue capacity and composition membership.
/// Events remain in arrival order, including repeated values; this is not a per-job state DTO.
/// </summary>
public sealed class JobMonitorSnapshot
{
    public JobMonitorSnapshot(IEnumerable<JobMonitorQueue> queues, IEnumerable<JobEventSnapshot> events,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> rootJobIds, long revision, long droppedEventCount)
    {
        ArgumentNullException.ThrowIfNull(queues);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(rootJobIds);
        Queues = Array.AsReadOnly(queues.ToArray());
        Events = Array.AsReadOnly(events.ToArray());
        RootJobIds = new ReadOnlyDictionary<Guid, IReadOnlyList<Guid>>(rootJobIds.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<Guid>)Array.AsReadOnly(pair.Value.Distinct().Order().ToArray())));
        Revision = revision;
        DroppedEventCount = droppedEventCount;
    }

    public IReadOnlyList<JobMonitorQueue> Queues { get; }
    public IReadOnlyList<JobEventSnapshot> Events { get; }
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> RootJobIds { get; }
    /// <summary>Number of accepted occurrences, including those already evicted.</summary>
    public long Revision { get; }
    /// <summary>Number of observations removed by retention; never presented as complete session history.</summary>
    public long DroppedEventCount { get; }

    public static JobMonitorSnapshot Empty { get; } = new(Array.Empty<JobMonitorQueue>(),
        Array.Empty<JobEventSnapshot>(), new Dictionary<Guid, IReadOnlyList<Guid>>(), 0, 0);
}
