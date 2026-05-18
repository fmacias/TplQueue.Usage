using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Integration.Test.Consumers.JobEventTransport;

namespace Fmacias.TplQueue.Integration.Test.Consumers
{
    [TestFixture]
    public sealed class JobEventTransportProjectionIntegrationTests
    {
        [Test]
        public async Task PublicQueueEventFeed_RemainsMetadataOnlyForPayloadRootProjection()
        {
            var serializer = Helper.CreatePayloadSerializer();
            var api = CreateApi();
            using var queue = CreateQueue(api);
            var observer = new RecordingTransportObserver(
                new JobEventTransportProjector(serializer, JobEventPayloadCaptureMode.None));
            using IDisposable subscription = queue.Subscribe(observer);

            var payload = new DashboardPayload
            {
                Label = "dashboard-metadata",
                Stage = "queued",
                Sequence = 1
            };
            var root = api.DataJobFactory.DataJobRoot(
                payload,
                CreateMutatingHandler("handled", 2),
                "dashboard-metadata-root",
                () => NoRetryPolicy.Create());

            root.Enqueue(queue, CancellationToken.None);
            queue.ResumePolling();

            await root.WaitUntilFinishedAsync();
            await WaitUntilAsync(() =>
                observer.Snapshot().Any(evt =>
                    evt.JobId == root.Id &&
                    evt.Status == JobEventStatus.RootSuccessed));

            var rootEvents = observer
                .Snapshot()
                .Where(evt => evt.JobId == root.Id)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(observer.ObserverError, Is.Null);
                Assert.That(rootEvents, Is.Not.Empty);
                Assert.That(rootEvents.All(evt => evt.SerializedPayload == null), Is.True);
                Assert.That(rootEvents.All(evt => evt.HasPayloadSnapshot == false), Is.True);
                Assert.That(rootEvents.Any(evt => evt.Status == JobEventStatus.RootSuccessed), Is.True);
                Assert.That(rootEvents.Any(evt => evt.CrossQueueId == queue.QueueId), Is.True);
            });
        }

        [Test]
        public void TerminalOnlyMode_ProjectsDetachedSerializedPayload_WhenPayloadAwareEventIsProvided()
        {
            var serializer = Helper.CreatePayloadSerializer();
            var payload = new DashboardPayload
            {
                Label = "dashboard-terminal",
                Stage = "handled",
                Sequence = 2
            };
            var jobId = Guid.NewGuid();
            var jobInfo = new PayloadAwareJobInfoStub(jobId, "dashboard-terminal-root", payload);
            var jobEvent = new JobEventStub(
                JobEventStatus.RootSuccessed,
                jobInfo,
                retryCount: 0);
            var projector = new JobEventTransportProjector(
                serializer,
                JobEventPayloadCaptureMode.TerminalOnly);

            var projectedEvent = projector.Project(jobEvent);
            var serializedSnapshot = projectedEvent.SerializedPayload;

            payload.Stage = "mutated-after-event";
            payload.Sequence = 999;

            var projectedPayload = (DashboardPayload)serializer.Deserialize(
                serializedSnapshot!,
                typeof(DashboardPayload));

            Assert.Multiple(() =>
            {
                Assert.That(projectedEvent.JobId, Is.EqualTo(jobId));
                Assert.That(projectedEvent.Name, Is.EqualTo("dashboard-terminal-root"));
                Assert.That(projectedEvent.Status, Is.EqualTo(JobEventStatus.RootSuccessed));
                Assert.That(projectedEvent.PayloadHandlerKey, Is.EqualTo(DashboardPayload.PayloadHandlerKey));
                Assert.That(projectedEvent.HasPayloadSnapshot, Is.True);
                Assert.That(serializedSnapshot, Does.Contain("\"Stage\":\"handled\""));
                Assert.That(serializedSnapshot, Does.Contain("\"Sequence\":2"));
                Assert.That(projectedPayload.Stage, Is.EqualTo("handled"));
                Assert.That(projectedPayload.Sequence, Is.EqualTo(2));
            });
        }

        private static IApi CreateApi()
        {
            return Helper.GetApi(
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());
        }

        private static IParallelQ CreateQueue(IApi api)
        {
            return api.QFactory.Parallel(
                Guid.NewGuid(),
                "transport-consumer-queue",
                maxParallelism: 1,
                logger: Helper.GetLogger<IParallelQ>(),
                retryPolicyFactory: () => NoRetryPolicy.Create());
        }

        private static IHandler CreateMutatingHandler(string stage, int sequence)
        {
            return DelegatePayloadHandler.Create((payload, ct) =>
            {
                var dashboardPayload = (DashboardPayload)payload;
                dashboardPayload.Stage = stage;
                dashboardPayload.Sequence = sequence;
                return Task.CompletedTask;
            });
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(5);

            while (!condition())
            {
                if (DateTime.UtcNow >= timeoutAt)
                {
                    throw new TimeoutException("Timed out waiting for the projected transport event.");
                }

                await Task.Delay(50);
            }
        }

        public sealed class DashboardPayload : IPayload
        {
            public const string PayloadHandlerKey = "test/integration/dashboard-transport-v1";

            public string Label { get; set; } = string.Empty;
            public string Stage { get; set; } = string.Empty;
            public int Sequence { get; set; }
            public string PayloadId => PayloadHandlerKey;
            public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
        }

        private sealed class PayloadAwareJobInfoStub : IDataJobInfo
        {
            private readonly DashboardPayload _payload;

            public PayloadAwareJobInfoStub(Guid id, string name, DashboardPayload payload)
            {
                Id = id;
                Name = name;
                _payload = payload ?? throw new ArgumentNullException(nameof(payload));
            }

            public Guid Id { get; }
            public string Name { get; }
            public bool IsCompleted => true;
            public DateTime ExecutionStart => DateTime.UtcNow;
            public TimeSpan ExecutionTime => TimeSpan.FromMilliseconds(10);
            public DateTime ExecutionEnd => DateTime.UtcNow;
            public TaskStatus Status => TaskStatus.RanToCompletion;
            public IReadOnlyCollection<IJobInfo> Dependencies => Array.Empty<IJobInfo>();
            public Guid CrossQueueId => Guid.NewGuid();
            public string PayloadHandlerKey => _payload.PayloadId;

            public string Serialize(IUniversalDataSerializer serializer)
            {
                if (serializer == null) throw new ArgumentNullException(nameof(serializer));
                return serializer.Serialize(_payload, _payload.GetType());
            }
        }

        private sealed class JobEventStub : IJobEvent
        {
            public JobEventStub(JobEventStatus status, IJobInfo jobInfo, int retryCount)
            {
                Status = status;
                JobInfo = jobInfo ?? throw new ArgumentNullException(nameof(jobInfo));
                RetryCount = retryCount;
            }

            public JobEventStatus Status { get; }
            public IJobInfo JobInfo { get; }
            public Exception? Exception => null;
            public DateTime Timestamp { get; } = DateTime.UtcNow;
            public int RetryCount { get; }

            public override string ToString()
            {
                return $"{Status}: {JobInfo.Name}";
            }
        }
    }
}
