// Historical pure mapper retained solely for existing regression tests.
// The running Blazor host uses JobMonitorSnapshot and the shared Web Component.
namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

public sealed record EtlScatterPoint(
    Guid JobId,
    Guid? RootJobId,
    string Name,
    string QueueGroupId,
    string QueueDisplayName,
    string Status,
    string EventType,
    DateTimeOffset ActualTimestamp,
    double DisplayedX,
    double DisplayedY,
    string? Error,
    int RetryCount);

public sealed record EtlScatterConnection(Guid FromJobId, Guid ToJobId);

public sealed record EtlScatterLane(
    string GroupId,
    string DisplayName,
    int Order,
    double MinY,
    double MaxY,
    int SequenceCount,
    int ParallelCapacity);

internal sealed record EtlScatterTimeline(
    IReadOnlyList<EtlQueueSnapshot> Queues,
    IReadOnlyList<EtlScatterLane> Lanes,
    IReadOnlyList<EtlScatterPoint> Points,
    IReadOnlyList<EtlScatterConnection> Connections,
    DateTimeOffset ReferenceTimestamp,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    int ChartHeight);

internal static class EtlScatterTimelineMapper
{
    public const double PreferredPointDiameter = 16;
    public const double MinimumPointDiameter = 14;
    public const double MinimumGap = 4;
    public const double MinimumPointSpacing = MinimumPointDiameter + MinimumGap;
    public const double PlotWidthPixels = 850;
    public const double LaneSlotHeight = 1;
    public const double LanePadding = .75;
    public const double LaneGap = 1;
    public const double SequenceRowGap = 1;
    public const double DefaultPastWindowMilliseconds = 10_000;
    public const double DefaultFutureWindowMilliseconds = 10_000;
    public const double DefaultTimeTickMilliseconds = 500;
    public const int MinimumChartHeight = 620;
    public const double ChartPixelsPerYAxisUnit = 20;

    /// <summary>
    /// Compatibility overload for tests and callers that only need a snapshot.
    /// The snapshot's earliest event is used as the reference so the first point
    /// remains visible at the left edge of the original view.
    /// </summary>
    public static EtlScatterTimeline Map(EtlDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var reference = snapshot.Jobs.Count == 0
            ? DateTimeOffset.UtcNow
            : snapshot.Jobs.Min(job => job.LastEventAt);
        return Map(snapshot, null, reference, DefaultPastWindowMilliseconds, DefaultFutureWindowMilliseconds);
    }

    public static EtlScatterTimeline Map(
        EtlDashboardSnapshot snapshot,
        IReadOnlySet<string>? selectedQueueGroups,
        DateTimeOffset referenceTimestamp,
        double pastWindowMilliseconds = DefaultPastWindowMilliseconds,
        double futureWindowMilliseconds = DefaultFutureWindowMilliseconds,
        bool expandPastWindowForRetainedJobs = true,
        double panOffsetMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var reference = referenceTimestamp.ToUniversalTime();
        var pastWindow = Math.Max(1, Math.Abs(pastWindowMilliseconds));
        var futureWindow = Math.Max(1, Math.Abs(futureWindowMilliseconds));

        var selectedQueues = snapshot.Queues
            .Where(queue => selectedQueueGroups is null || selectedQueueGroups.Contains(queue.GroupId))
            .OrderBy(queue => queue.Order)
            .ThenBy(queue => queue.GroupId, StringComparer.Ordinal)
            .ToArray();
        var selectedQueueIds = selectedQueues.Select(queue => queue.GroupId).ToHashSet(StringComparer.Ordinal);
        var selectedJobs = snapshot.Jobs
            .Where(job => selectedQueueIds.Contains(job.QueueGroupId))
            .Select(job => new
            {
                Job = job,
                RawX = (job.LastEventAt.ToUniversalTime() - reference).TotalMilliseconds,
                SequenceId = job.RootJobId ?? job.JobId
            })
            .ToArray();

        // Keep retained historical jobs visible when the dashboard is opened
        // after the workload has already completed. The reference stays fixed;
        // only the left edge expands beyond the default past window.
        var earliestRawX = selectedJobs.Length == 0 ? 0 : selectedJobs.Min(item => item.RawX);
        var historicalMargin = Math.Max(1, EtlScatterTimelineMapper.MinimumPointSpacing);
        var minX = -pastWindow + panOffsetMilliseconds;
        if (expandPastWindowForRetainedJobs && Math.Abs(panOffsetMilliseconds) < double.Epsilon)
        {
            minX = Math.Min(minX, earliestRawX - historicalMargin);
        }

        var maxX = futureWindow + panOffsetMilliseconds;
        var visibleJobs = selectedJobs
            .Where(item => item.RawX >= minX && item.RawX <= maxX)
            .OrderBy(item => item.RawX)
            .ThenBy(item => item.Job.JobId)
            .ToArray();

        // A 20px minimum point spacing is converted to milliseconds for this
        // fixed window. X remains the real signed time; only the vertical slot
        // changes when close events would otherwise overlap.
        var minimumTimeSeparation = MinimumPointSpacing / PlotWidthPixels * (maxX - minX);
        var slotAssignments = new Dictionary<Guid, int>();
        var slotLastXBySequenceQueue = new Dictionary<(string QueueGroupId, Guid SequenceId), List<double>>();

        // A root sequence owns one deterministic row. Jobs in the same queue
        // and sequence stay on that row even when their timestamps are far
        // apart; only genuinely parallel points use the small vertical offsets.
        //
        // Keep a shared sequence row when a dependency chain crosses queues,
        // but compact sequences that exist in only one queue. Reserving every
        // global sequence row in every queue makes the chart unnecessarily tall
        // for the common case where each queue owns independent work.
        var sequenceGroups = visibleJobs
            .GroupBy(item => item.SequenceId)
            .Select(group => new
            {
                SequenceId = group.Key,
                FirstX = group.Min(item => item.RawX),
                QueueIds = group.Select(item => item.Job.QueueGroupId)
                    .ToHashSet(StringComparer.Ordinal)
            })
            .OrderBy(group => group.FirstX)
            .ThenBy(group => group.SequenceId)
            .ToArray();
        var sharedSequenceGroups = sequenceGroups
            .Where(group => group.QueueIds.Count > 1)
            .ToArray();
        var sharedRowBySequenceId = sharedSequenceGroups
            .Select((group, index) => new { group.SequenceId, Index = index })
            .ToDictionary(item => item.SequenceId, item => item.Index);
        var sequenceIndexByQueueAndId = new Dictionary<(string QueueGroupId, Guid SequenceId), int>();
        var sequenceCount = 0;

        foreach (var queue in selectedQueues)
        {
            foreach (var sequence in sharedSequenceGroups.Where(group => group.QueueIds.Contains(queue.GroupId)))
            {
                sequenceIndexByQueueAndId[(queue.GroupId, sequence.SequenceId)] =
                    sharedRowBySequenceId[sequence.SequenceId];
            }

            var localRow = sharedSequenceGroups.Length;
            foreach (var sequence in sequenceGroups.Where(group =>
                         group.QueueIds.Count == 1 && group.QueueIds.Contains(queue.GroupId)))
            {
                sequenceIndexByQueueAndId[(queue.GroupId, sequence.SequenceId)] = localRow++;
            }

            sequenceCount = Math.Max(sequenceCount, localRow);
        }

        foreach (var item in visibleJobs)
        {
            var key = (item.Job.QueueGroupId, item.SequenceId);
            if (!slotLastXBySequenceQueue.TryGetValue(key, out var slots))
            {
                slots = new List<double>();
                slotLastXBySequenceQueue[key] = slots;
            }

            var slot = 0;
            while (slot < slots.Count && item.RawX - slots[slot] < minimumTimeSeparation)
            {
                slot++;
            }

            if (slot == slots.Count)
            {
                slots.Add(item.RawX);
            }
            else
            {
                slots[slot] = item.RawX;
            }

            slotAssignments[item.Job.JobId] = slot;
        }

        var parallelCapacity = Math.Max(
            1,
            slotLastXBySequenceQueue.Values.Select(slots => slots.Count).DefaultIfEmpty(1).Max());
        var maxOffsetMagnitude = MaxParallelOffsetMagnitude(parallelCapacity);
        var sequenceRowHeight = Math.Max(1, (maxOffsetMagnitude * 2) + 1);
        var sequenceRowPitch = sequenceRowHeight + SequenceRowGap;
        var laneContentHeight = sequenceCount == 0
            ? sequenceRowHeight
            : (sequenceCount * sequenceRowPitch) - SequenceRowGap;

        var lanes = new List<EtlScatterLane>(selectedQueues.Length);
        var laneMinY = 0d;
        foreach (var queue in selectedQueues.OrderByDescending(queue => queue.Order))
        {
            var laneMaxY = laneMinY + (LanePadding * 2) + (laneContentHeight * LaneSlotHeight);
            lanes.Add(new EtlScatterLane(
                queue.GroupId,
                queue.DisplayName,
                queue.Order,
                laneMinY,
                laneMaxY,
                sequenceCount,
                parallelCapacity));
            laneMinY = laneMaxY + LaneGap;
        }

        var laneByQueue = lanes.ToDictionary(lane => lane.GroupId, StringComparer.Ordinal);
        var points = visibleJobs
            .Select(item =>
            {
                var lane = laneByQueue[item.Job.QueueGroupId];
                var slot = slotAssignments[item.Job.JobId];
                var sequenceIndex = sequenceIndexByQueueAndId[(item.Job.QueueGroupId, item.SequenceId)];
                var parallelOffset = ParallelOffsetForSlot(slot);
                var sequenceRowCenterY = lane.MinY
                    + LanePadding
                    + ((sequenceIndex * sequenceRowPitch) + maxOffsetMagnitude) * LaneSlotHeight;
                return new EtlScatterPoint(
                    item.Job.JobId,
                    item.Job.RootJobId,
                    item.Job.Name,
                    item.Job.QueueGroupId,
                    item.Job.QueueDisplayName,
                    item.Job.Status,
                    item.Job.LastEventType,
                    item.Job.LastEventAt,
                    item.RawX,
                    sequenceRowCenterY + (parallelOffset * LaneSlotHeight),
                    item.Job.Error,
                    item.Job.RetryCount);
            })
            .ToArray();

        var pointIds = points.Select(point => point.JobId).ToHashSet();
        var connections = snapshot.Jobs
            .Where(job => pointIds.Contains(job.JobId))
            // IJobInfo.Dependencies is owned by the dependent job. The line
            // therefore always runs from dependency (source) to job (target).
            .SelectMany(job => (job.DependencyJobIds ?? Array.Empty<Guid>())
                .Select(dependencyId => new EtlScatterConnection(dependencyId, job.JobId)))
            .Where(connection => pointIds.Contains(connection.FromJobId) && pointIds.Contains(connection.ToJobId))
            .Distinct()
            .ToArray();

        var chartMaxY = Math.Max(1, lanes.Count == 0 ? 1 : lanes.Max(lane => lane.MaxY));
        var chartHeight = Math.Max(MinimumChartHeight,
            (int)Math.Ceiling(100 + (chartMaxY * ChartPixelsPerYAxisUnit)));

        return new EtlScatterTimeline(
            selectedQueues,
            lanes,
            points,
            connections,
            reference,
            minX,
            maxX,
            lanes.Count == 0 ? 0 : lanes.Min(lane => lane.MinY),
            chartMaxY,
            chartHeight);
    }

    private static int ParallelOffsetForSlot(int slot)
    {
        if (slot == 0)
        {
            return 0;
        }

        var magnitude = (slot + 1) / 2;
        return slot % 2 == 1 ? -magnitude : magnitude;
    }

    private static int MaxParallelOffsetMagnitude(int capacity) => Math.Max(0, capacity / 2);
}
