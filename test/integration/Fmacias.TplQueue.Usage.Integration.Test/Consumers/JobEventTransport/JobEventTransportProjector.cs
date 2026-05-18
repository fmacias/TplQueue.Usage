using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Consumers.JobEventTransport
{
    internal sealed class JobEventTransportProjector
    {
        private readonly IUniversalDataSerializer _serializer;
        private readonly JobEventPayloadCaptureMode _payloadCaptureMode;

        public JobEventTransportProjector(
            IUniversalDataSerializer serializer,
            JobEventPayloadCaptureMode payloadCaptureMode)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _payloadCaptureMode = payloadCaptureMode;
        }

        public JobEventTransportDto Project(IJobEvent jobEvent)
        {
            if (jobEvent == null) throw new ArgumentNullException(nameof(jobEvent));

            var payloadJobInfo = jobEvent.JobInfo as IDataJobInfo;
            var serializedPayload = TrySerializePayloadSnapshot(jobEvent.Status, payloadJobInfo);

            return new JobEventTransportDto
            {
                JobId = jobEvent.JobInfo.Id,
                Name = jobEvent.JobInfo.Name,
                Status = jobEvent.Status,
                TimestampUtc = NormalizeUtc(jobEvent.Timestamp),
                RetryCount = jobEvent.RetryCount,
                CrossQueueId = jobEvent.JobInfo.CrossQueueId,
                ExceptionMessage = jobEvent.Exception?.Message,
                PayloadHandlerKey = payloadJobInfo?.PayloadHandlerKey,
                HasPayloadSnapshot = serializedPayload != null,
                SerializedPayload = serializedPayload
            };
        }

        private string? TrySerializePayloadSnapshot(
            JobEventStatus status,
            IDataJobInfo? payloadJobInfo)
        {
            if (payloadJobInfo == null || !ShouldCapturePayload(status))
            {
                return null;
            }

            return payloadJobInfo.Serialize(_serializer);
        }

        private bool ShouldCapturePayload(JobEventStatus status)
        {
            return _payloadCaptureMode switch
            {
                JobEventPayloadCaptureMode.None => false,
                JobEventPayloadCaptureMode.TerminalOnly => IsTerminal(status),
                JobEventPayloadCaptureMode.All => true,
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
}
