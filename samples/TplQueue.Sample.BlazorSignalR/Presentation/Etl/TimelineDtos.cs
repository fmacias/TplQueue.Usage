using System.Net;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>One stable vis-timeline group.</summary>
internal sealed record EtlTimelineGroup(string Id, string Content, int Order);

/// <summary>One safe, serialized vis-timeline item.</summary>
internal sealed record EtlTimelineItem(
    string Id,
    string Group,
    string Content,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    string Type,
    string ClassName,
    bool IsRunning);

/// <summary>Immutable data transferred to the local timeline module.</summary>
internal sealed record EtlTimelineSnapshot(
    IReadOnlyList<EtlTimelineGroup> Groups,
    IReadOnlyList<EtlTimelineItem> Items);

/// <summary>Maps projected job metadata into display-only timeline values.</summary>
internal static class EtlTimelineMapper
{
    public static EtlTimelineSnapshot Map(EtlDashboardSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        var groups = snapshot.Queues
            .OrderBy(queue => queue.Order)
            .Select(queue => new EtlTimelineGroup(
                queue.GroupId,
                queue.DisplayName,
                queue.Order))
            .ToArray();
        var items = snapshot.Jobs
            .Select(MapJob)
            .ToArray();

        return new EtlTimelineSnapshot(groups, items);
    }

    private static EtlTimelineItem MapJob(EtlJobSnapshot job)
    {
        var start = job.StartedAt ?? job.EnqueuedAt ?? job.FirstObservedAt;
        var isRunning = job.Status == "running";
        var hasRange = isRunning || job.EndedAt.HasValue;
        var encodedName = WebUtility.HtmlEncode(job.Name);
        var encodedError = string.IsNullOrWhiteSpace(job.Error)
            ? string.Empty
            : $"<br>Error: {WebUtility.HtmlEncode(job.Error)}";
        var title =
            $"{encodedName}<br>Status: {WebUtility.HtmlEncode(job.Status)}" +
            $"<br>Job: {job.JobId:D}<br>Retries: {job.RetryCount}{encodedError}";

        return new EtlTimelineItem(
            job.JobId.ToString("D"),
            job.QueueGroupId,
            job.Name,
            title,
            start,
            hasRange ? job.EndedAt : null,
            hasRange ? "range" : "point",
            $"tplq-status-{job.Status}",
            isRunning);
    }
}
