namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class QueueEventDto
{
    public Guid RunId { get; init; }
    public string Scenario { get; init; } = string.Empty;
    public Guid QueueId { get; init; }
    public int Sequence { get; init; }
    public Guid JobId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; }
    public int RetryCount { get; init; }
    public Guid CrossQueueId { get; init; }
    public string? ExceptionMessage { get; init; }
    public string? PayloadHandlerKey { get; init; }
    public string? PayloadTypeName { get; init; }
    public bool HasPayloadSnapshot { get; init; }
    public string? SerializedPayload { get; init; }
}
