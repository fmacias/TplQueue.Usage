using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Consumers.JobEventTransport
{
    internal sealed class JobEventTransportDto
    {
        public Guid JobId { get; init; }
        public string Name { get; init; } = string.Empty;
        public JobEventStatus Status { get; init; }
        public DateTime TimestampUtc { get; init; }
        public int RetryCount { get; init; }
        public Guid CrossQueueId { get; init; }
        public string? ExceptionMessage { get; init; }
        public string? PayloadHandlerKey { get; init; }
        public bool HasPayloadSnapshot { get; init; }
        public string? SerializedPayload { get; init; }
    }
}
