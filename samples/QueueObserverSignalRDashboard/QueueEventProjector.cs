using Fmacias.TplQueue.Contracts;
using System.Threading;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class QueueEventProjector
{
    private readonly Guid _runId;
    private readonly string _scenario;
    private readonly Guid _queueId;
    private readonly ISystemTextJsonUniversalSerializer? _payloadSerializer;
    private readonly IReadOnlyDictionary<Guid, IDataJobNode>? _payloadJobsById;
    private readonly QueueEventPayloadCaptureMode _payloadCaptureMode;
    private int _sequence;

    public QueueEventProjector(
        Guid runId,
        string scenario,
        Guid queueId,
        ISystemTextJsonUniversalSerializer? payloadSerializer = null,
        IReadOnlyDictionary<Guid, IDataJobNode>? payloadJobsById = null,
        QueueEventPayloadCaptureMode payloadCaptureMode = QueueEventPayloadCaptureMode.None)
    {
        _runId = runId;
        _scenario = string.IsNullOrWhiteSpace(scenario)
            ? throw new ArgumentException("A scenario name is required.", nameof(scenario))
            : scenario;
        _queueId = queueId;
        _payloadSerializer = payloadSerializer;
        _payloadJobsById = payloadJobsById;
        _payloadCaptureMode = payloadCaptureMode;
    }

    public QueueEventDto Project(IJobEvent jobEvent)
    {
        if (jobEvent == null) throw new ArgumentNullException(nameof(jobEvent));

        var payloadJob = TryGetPayloadJob(jobEvent.JobInfo.Id);
        var serializedPayload = TrySerializePayload(jobEvent.Status, payloadJob);

        return new QueueEventDto
        {
            RunId = _runId,
            Scenario = _scenario,
            QueueId = _queueId,
            Sequence = Interlocked.Increment(ref _sequence),
            JobId = jobEvent.JobInfo.Id,
            Name = jobEvent.JobInfo.Name,
            Status = jobEvent.Status.ToString(),
            TimestampUtc = NormalizeUtc(jobEvent.Timestamp),
            RetryCount = jobEvent.RetryCount,
            CrossQueueId = jobEvent.JobInfo.CrossQueueId,
            ExceptionMessage = jobEvent.Exception?.Message,
            PayloadHandlerKey = payloadJob?.PayloadHandlerKey,
            PayloadTypeName = payloadJob?.PayloadType.FullName,
            HasPayloadSnapshot = serializedPayload != null,
            SerializedPayload = serializedPayload
        };
    }

    private IDataJobNode? TryGetPayloadJob(Guid jobId)
    {
        if (_payloadJobsById == null)
        {
            return null;
        }

        return _payloadJobsById.TryGetValue(jobId, out var payloadJob)
            ? payloadJob
            : null;
    }

    private string? TrySerializePayload(JobEventStatus status, IDataJobNode? payloadJob)
    {
        if (_payloadSerializer == null || payloadJob == null || !ShouldCapturePayload(status))
        {
            return null;
        }

        return _payloadSerializer.Serialize(payloadJob);
    }

    private bool ShouldCapturePayload(JobEventStatus status)
    {
        return _payloadCaptureMode switch
        {
            QueueEventPayloadCaptureMode.None => false,
            QueueEventPayloadCaptureMode.TerminalOnly => IsTerminal(status),
            QueueEventPayloadCaptureMode.All => true,
            _ => false
        };
    }

    private static bool IsTerminal(JobEventStatus status)
    {
        return status == JobEventStatus.Successed ||
            status == JobEventStatus.RootSuccessed ||
            status == JobEventStatus.Failed ||
            status == JobEventStatus.Canceled;
    }

    private static DateTime NormalizeUtc(DateTime timestamp)
    {
        return timestamp.Kind == DateTimeKind.Utc
            ? timestamp
            : timestamp.ToUniversalTime();
    }
}
