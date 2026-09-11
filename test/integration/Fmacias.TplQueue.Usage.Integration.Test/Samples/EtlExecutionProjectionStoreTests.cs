using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class EtlExecutionProjectionStoreTests
    {
        private static readonly Guid ParallelQueueId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid FifoQueueId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        private static readonly Guid CacheQueueId = Guid.Parse("10000000-0000-0000-0000-000000000003");

        [Test]
        public void EnqueuedEvent_CreatesAnImmutableQueuedJobSnapshot()
        {
            var store = CreateStore();
            var changedCount = 0;
            store.Changed += (_, _) => changedCount++;
            var jobId = Guid.NewGuid();
            var timestamp = Utc(10, 15, 0);

            store.Apply(Event(
                JobEventStatus.Enqueued,
                Job(jobId, "Ingest measurements", ParallelQueueId),
                timestamp));

            var snapshot = store.GetSnapshot();
            var job = snapshot.Jobs.Single();

            Assert.Multiple(() =>
            {
                Assert.That(job.JobId, Is.EqualTo(jobId));
                Assert.That(job.QueueGroupId, Is.EqualTo("parallel"));
                Assert.That(job.QueueDisplayName, Is.EqualTo("ParallelQ"));
                Assert.That(job.Status, Is.EqualTo("queued"));
                Assert.That(job.FirstObservedAt, Is.EqualTo(timestamp));
                Assert.That(job.EnqueuedAt, Is.EqualTo(timestamp));
                Assert.That(job.StartedAt, Is.Null);
                Assert.That(job.EndedAt, Is.Null);
                Assert.That(changedCount, Is.EqualTo(1));
                Assert.That(snapshot.Jobs, Is.InstanceOf<ReadOnlyCollection<EtlJobSnapshot>>());
            });
        }

        [Test]
        public void RunningAndSuccessEvents_SetLifecycleTimestamps()
        {
            var store = CreateStore();
            var jobId = Guid.NewGuid();
            var queuedAt = Utc(10, 15, 0);
            var startedAt = queuedAt.AddSeconds(1);
            var endedAt = startedAt.AddSeconds(2);
            var jobInfo = Job(jobId, "Transform measurements", FifoQueueId);

            store.Apply(Event(JobEventStatus.Enqueued, jobInfo, queuedAt));
            store.Apply(Event(JobEventStatus.Running, jobInfo, startedAt, retryCount: 1));
            store.Apply(Event(JobEventStatus.Successed, jobInfo, endedAt, retryCount: 1));

            var job = store.GetSnapshot().Jobs.Single();

            Assert.Multiple(() =>
            {
                Assert.That(job.Status, Is.EqualTo("completed"));
                Assert.That(job.EnqueuedAt, Is.EqualTo(queuedAt));
                Assert.That(job.StartedAt, Is.EqualTo(startedAt));
                Assert.That(job.EndedAt, Is.EqualTo(endedAt));
                Assert.That(job.RetryCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void FailedEvent_StoresAShortSingleLineErrorSummary()
        {
            var store = CreateStore();
            var message = "  invalid payload\r\n" + new string('x', 400);

            store.Apply(Event(
                JobEventStatus.Failed,
                Job(Guid.NewGuid(), "Load measurement summary", CacheQueueId),
                Utc(10, 15, 0),
                exception: new InvalidOperationException(message)));

            var job = store.GetSnapshot().Jobs.Single();

            Assert.Multiple(() =>
            {
                Assert.That(job.Status, Is.EqualTo("failed"));
                Assert.That(job.EndedAt, Is.Not.Null);
                Assert.That(job.Error, Does.StartWith("invalid payload "));
                Assert.That(job.Error, Does.Not.Contain("\r"));
                Assert.That(job.Error, Does.Not.Contain("\n"));
                Assert.That(job.Error, Has.Length.LessThanOrEqualTo(256));
            });
        }

        [Test]
        public void DuplicateEvent_DoesNotDuplicateOrRenotifyTheJob()
        {
            var store = CreateStore();
            var changedCount = 0;
            store.Changed += (_, _) => changedCount++;
            var jobEvent = Event(
                JobEventStatus.Running,
                Job(Guid.NewGuid(), "Transform measurements", ParallelQueueId),
                Utc(10, 15, 0));

            store.Apply(jobEvent);
            store.Apply(jobEvent);

            Assert.Multiple(() =>
            {
                Assert.That(store.GetSnapshot().Jobs, Has.Count.EqualTo(1));
                Assert.That(changedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void OlderRunningEvent_EnrichesTimestampsWithoutRegressingTerminalStatus()
        {
            var store = CreateStore();
            var jobId = Guid.NewGuid();
            var endedAt = Utc(10, 15, 5);
            var startedAt = endedAt.AddSeconds(-4);
            var jobInfo = Job(jobId, "Load measurement summary", ParallelQueueId);

            store.Apply(Event(JobEventStatus.RootSuccessed, jobInfo, endedAt));
            store.Apply(Event(JobEventStatus.Running, jobInfo, startedAt));

            var job = store.GetSnapshot().Jobs.Single();

            Assert.Multiple(() =>
            {
                Assert.That(job.Status, Is.EqualTo("completed"));
                Assert.That(job.StartedAt, Is.EqualTo(startedAt));
                Assert.That(job.EndedAt, Is.EqualTo(endedAt));
            });
        }

        [Test]
        public void RuntimeQueueIds_MapToTheThreeStableGroups()
        {
            var store = CreateStore();

            store.Apply(Event(
                JobEventStatus.Started,
                Job(Guid.NewGuid(), "Parallel job", ParallelQueueId),
                Utc(10, 15, 0)));
            store.Apply(Event(
                JobEventStatus.Running,
                Job(Guid.NewGuid(), "FIFO job", FifoQueueId),
                Utc(10, 15, 1)));
            store.Apply(Event(
                JobEventStatus.Failed,
                Job(Guid.NewGuid(), "Cache job", CacheQueueId),
                Utc(10, 15, 2)));

            var queues = store.GetSnapshot().Queues;

            Assert.Multiple(() =>
            {
                Assert.That(queues.Select(queue => queue.GroupId),
                    Is.EqualTo(new[] { "parallel", "fifo", "cache" }));
                Assert.That(queues.Select(queue => queue.DisplayName),
                    Is.EqualTo(new[] { "ParallelQ", "FifoQ", "CacheQ" }));
                Assert.That(queues.Select(queue => queue.JobCount),
                    Is.EqualTo(new[] { 1, 1, 1 }));
                Assert.That(queues.Single(queue => queue.GroupId == "cache").FailedCount,
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void RootSuccessEvent_BackfillsRootIdAcrossTheDependencyGraph()
        {
            var store = CreateStore();
            var ingest = Job(Guid.NewGuid(), "Ingest measurements", CacheQueueId);
            var transform = Job(
                Guid.NewGuid(),
                "Transform measurements",
                CacheQueueId,
                ingest);
            var root = Job(
                Guid.NewGuid(),
                "Load measurement summary",
                CacheQueueId,
                transform);

            store.Apply(Event(JobEventStatus.Successed, ingest, Utc(10, 15, 1)));
            store.Apply(Event(JobEventStatus.Successed, transform, Utc(10, 15, 2)));
            store.Apply(Event(JobEventStatus.RootSuccessed, root, Utc(10, 15, 3)));

            var jobs = store.GetSnapshot().Jobs;

            Assert.Multiple(() =>
            {
                Assert.That(jobs, Has.Count.EqualTo(3));
                Assert.That(jobs.Select(job => job.RootJobId),
                    Is.All.EqualTo(root.Id));
                Assert.That(jobs.Single(job => job.JobId == root.Id).RootJobId,
                    Is.EqualTo(root.Id));
            });
        }

        [Test]
        public void OutOfOrderFifoRootEvents_PreserveEachRootBoundary()
        {
            var store = CreateStore();
            var firstChild = Job(Guid.NewGuid(), "First ingest", FifoQueueId);
            var firstRoot = Job(
                Guid.NewGuid(),
                "First load",
                FifoQueueId,
                firstChild);
            var secondChild = Job(Guid.NewGuid(), "Second ingest", FifoQueueId);
            var secondRoot = Job(
                Guid.NewGuid(),
                "Second load",
                FifoQueueId,
                secondChild,
                firstRoot);

            store.Apply(Event(JobEventStatus.Successed, firstChild, Utc(10, 15, 1)));
            store.Apply(Event(JobEventStatus.Successed, secondChild, Utc(10, 15, 2)));
            store.Apply(Event(JobEventStatus.RootSuccessed, secondRoot, Utc(10, 15, 4)));
            store.Apply(Event(JobEventStatus.RootSuccessed, firstRoot, Utc(10, 15, 3)));

            var jobs = store.GetSnapshot().Jobs;

            Assert.Multiple(() =>
            {
                Assert.That(jobs.Single(job => job.JobId == firstChild.Id).RootJobId,
                    Is.EqualTo(firstRoot.Id));
                Assert.That(jobs.Single(job => job.JobId == firstRoot.Id).RootJobId,
                    Is.EqualTo(firstRoot.Id));
                Assert.That(jobs.Single(job => job.JobId == secondChild.Id).RootJobId,
                    Is.EqualTo(secondRoot.Id));
                Assert.That(jobs.Single(job => job.JobId == secondRoot.Id).RootJobId,
                    Is.EqualTo(secondRoot.Id));
            });
        }

        [Test]
        public void MultipleQueueStreams_UpdateOneStoreConcurrently()
        {
            const int jobsPerQueue = 40;
            var store = CreateStore();
            var queueIds = new[] { ParallelQueueId, FifoQueueId, CacheQueueId };

            Parallel.For(0, jobsPerQueue * queueIds.Length, index =>
            {
                var queueId = queueIds[index % queueIds.Length];
                store.Apply(Event(
                    JobEventStatus.Running,
                    Job(Guid.NewGuid(), $"Job {index}", queueId),
                    Utc(10, 15, 0).AddMilliseconds(index)));
            });

            var snapshot = store.GetSnapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Jobs, Has.Count.EqualTo(jobsPerQueue * queueIds.Length));
                Assert.That(snapshot.Queues.Select(queue => queue.JobCount),
                    Is.All.EqualTo(jobsPerQueue));
                Assert.That(snapshot.Queues.Select(queue => queue.RunningCount),
                    Is.All.EqualTo(jobsPerQueue));
            });
        }

        [Test]
        public void Apply_RejectsAnEventFromAnUncataloguedQueue()
        {
            var store = CreateStore();
            var jobEvent = Event(
                JobEventStatus.Running,
                Job(Guid.NewGuid(), "Unknown queue job", Guid.NewGuid()),
                Utc(10, 15, 0));

            Assert.That(
                () => store.Apply(jobEvent),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ChangedNotification_IsolatesFailingCircuitSubscribers()
        {
            var store = CreateStore();
            var successfulSubscriberCalls = 0;
            store.Changed += (_, _) => throw new InvalidOperationException("Disposed circuit");
            store.Changed += (_, _) => successfulSubscriberCalls++;

            Assert.That(
                () => store.Apply(Event(
                    JobEventStatus.Running,
                    Job(Guid.NewGuid(), "Observed job", ParallelQueueId),
                    Utc(10, 15, 0))),
                Throws.Nothing);
            Assert.That(successfulSubscriberCalls, Is.EqualTo(1));
        }

        [Test]
        public void Observer_ProjectsEventsWithoutRedispatchingThem()
        {
            var store = CreateStore();
            var observer = new EtlQueueObserver(
                store,
                NullLogger<EtlQueueObserver>.Instance);
            var jobEvent = Event(
                JobEventStatus.Enqueued,
                Job(Guid.NewGuid(), "Observed job", ParallelQueueId),
                Utc(10, 15, 0));

            observer.OnNext(jobEvent);

            Assert.That(store.GetSnapshot().Jobs, Has.Count.EqualTo(1));
        }

        [Test]
        public void TimelineMapper_UsesStableGroupsStatusesAndEncodedTooltipText()
        {
            var store = CreateStore();
            var jobInfo = Job(
                Guid.NewGuid(),
                "<strong>Load</strong>",
                CacheQueueId);
            store.Apply(Event(
                JobEventStatus.Failed,
                jobInfo,
                Utc(10, 15, 0),
                exception: new InvalidOperationException("<script>alert(1)</script>")));

            var timeline = EtlTimelineMapper.Map(store.GetSnapshot());
            var item = timeline.Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(timeline.Groups.Select(group => group.Id),
                    Is.EqualTo(new[] { "parallel", "fifo", "cache" }));
                Assert.That(item.Type, Is.EqualTo("range"));
                Assert.That(item.ClassName, Is.EqualTo("tplq-status-failed"));
                Assert.That(item.IsRunning, Is.False);
                Assert.That(item.Title, Does.Contain("&lt;strong&gt;"));
                Assert.That(item.Title, Does.Contain("&lt;script&gt;"));
                Assert.That(item.Title, Does.Not.Contain("<script>"));
            });
        }

        private static EtlExecutionProjectionStore CreateStore()
        {
            var descriptors = new[]
            {
                new EtlQueueDescriptor(
                    AvailableQueue.Parallel,
                    ParallelQueueId,
                    "parallel",
                    "ParallelQ",
                    Order: 0),
                new EtlQueueDescriptor(
                    AvailableQueue.FIFO,
                    FifoQueueId,
                    "fifo",
                    "FifoQ",
                    Order: 1),
                new EtlQueueDescriptor(
                    AvailableQueue.Cache,
                    CacheQueueId,
                    "cache",
                    "CacheQ",
                    Order: 2)
            };
            var catalog = new EtlQueueCatalog(descriptors);
            return new EtlExecutionProjectionStore(
                catalog,
                NullLogger<EtlExecutionProjectionStore>.Instance);
        }

        private static FakeJobEvent Event(
            JobEventStatus status,
            IJobInfo jobInfo,
            DateTimeOffset timestamp,
            int retryCount = 0,
            Exception? exception = null)
        {
            return new FakeJobEvent(
                status,
                jobInfo,
                timestamp.UtcDateTime,
                retryCount,
                exception);
        }

        private static FakeJobInfo Job(
            Guid id,
            string name,
            Guid queueId,
            params IJobInfo[] dependencies)
        {
            return new FakeJobInfo(id, name, queueId, dependencies);
        }

        private static DateTimeOffset Utc(int hour, int minute, int second)
        {
            return new DateTimeOffset(2026, 7, 29, hour, minute, second, TimeSpan.Zero);
        }

        private sealed class FakeJobEvent : IJobEvent
        {
            public FakeJobEvent(
                JobEventStatus status,
                IJobInfo jobInfo,
                DateTime timestamp,
                int retryCount,
                Exception? exception)
            {
                Status = status;
                JobInfo = jobInfo;
                Timestamp = timestamp;
                RetryCount = retryCount;
                Exception = exception;
            }

            public JobEventStatus Status { get; }
            public IJobInfo JobInfo { get; }
            public Exception? Exception { get; }
            public DateTime Timestamp { get; }
            public int RetryCount { get; }

            public override string ToString()
            {
                return $"{JobInfo.Id}:{Status}";
            }
        }

        private sealed class FakeJobInfo : IJobInfo
        {
            public FakeJobInfo(
                Guid id,
                string name,
                Guid crossQueueId,
                IReadOnlyCollection<IJobInfo> dependencies)
            {
                Id = id;
                Name = name;
                CrossQueueId = crossQueueId;
                Dependencies = dependencies;
            }

            public Guid Id { get; }
            public string Name { get; }
            public bool IsCompleted => false;
            public DateTime ExecutionStart => default;
            public TimeSpan ExecutionTime => default;
            public DateTime ExecutionEnd => default;
            public TaskStatus Status => TaskStatus.WaitingForActivation;
            public IReadOnlyCollection<IJobInfo> Dependencies { get; }
            public Guid CrossQueueId { get; }
        }
    }
}
