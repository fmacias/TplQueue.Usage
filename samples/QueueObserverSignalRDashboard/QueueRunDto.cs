namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class QueueRunDto
{
    public Guid RunId { get; init; }
    public string Scenario { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? QueueName { get; init; }
    public Guid? QueueId { get; init; }
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public string? FailureMessage { get; init; }
}
