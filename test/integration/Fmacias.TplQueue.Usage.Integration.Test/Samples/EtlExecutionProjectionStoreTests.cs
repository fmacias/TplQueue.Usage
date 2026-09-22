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
                Assert.That(jobs.Single(job => job.JobId == transform.Id).DependencyJobIds,
                    Is.EqualTo(new[] { ingest.Id }));
                Assert.That(jobs.Single(job => job.JobId == root.Id).DependencyJobIds,
                    Is.EqualTo(new[] { transform.Id }));
            });
        }

        [Test]
        public void ScatterTimeline_ConnectsDirectDependenciesInExecutionDirection()
        {
            var store = CreateStore();
            var ingest = Job(Guid.NewGuid(), "Ingest measurements", CacheQueueId);
            var transform = Job(Guid.NewGuid(), "Transform measurements", CacheQueueId, ingest);
            var root = Job(Guid.NewGuid(), "Load measurement summary", CacheQueueId, transform);

            store.Apply(Event(JobEventStatus.Successed, ingest, Utc(10, 15, 1)));
            store.Apply(Event(JobEventStatus.Successed, transform, Utc(10, 15, 2)));
            store.Apply(Event(JobEventStatus.RootSuccessed, root, Utc(10, 15, 3)));

            var timeline = EtlScatterTimelineMapper.Map(store.GetSnapshot());

            Assert.That(timeline.Connections, Is.EquivalentTo(new[]
            {
                new EtlScatterConnection(ingest.Id, transform.Id),
                new EtlScatterConnection(transform.Id, root.Id)
            }));
        }

        [Test]
        public void ScatterTimeline_AlignsJobsFromTheSameRootSequenceAcrossQueues()
        {
            var store = CreateStore();
            var firstDependency = Job(Guid.NewGuid(), "First dependency", FifoQueueId);
            var firstRoot = Job(Guid.NewGuid(), "First root", ParallelQueueId, firstDependency);
            var secondDependency = Job(Guid.NewGuid(), "Second dependency", FifoQueueId);
            var secondRoot = Job(Guid.NewGuid(), "Second root", ParallelQueueId, secondDependency);
            var reference = Utc(10, 15, 0);

            store.Apply(Event(JobEventStatus.Successed, firstDependency, reference.AddSeconds(1)));
            store.Apply(Event(JobEventStatus.RootSuccessed, firstRoot, reference.AddSeconds(2)));
            store.Apply(Event(JobEventStatus.Successed, secondDependency, reference.AddSeconds(3)));
            store.Apply(Event(JobEventStatus.RootSuccessed, secondRoot, reference.AddSeconds(4)));

            var timeline = EtlScatterTimelineMapper.Map(
                store.GetSnapshot(),
                new HashSet<string>(StringComparer.Ordinal) { "parallel", "fifo" },
                reference,
                pastWindowMilliseconds: 5_000,
                futureWindowMilliseconds: 5_000,
                expandPastWindowForRetainedJobs: false);
            var points = timeline.Points.ToDictionary(point => point.JobId);

            var parallelDelta = points[secondRoot.Id].DisplayedY - points[firstRoot.Id].DisplayedY;
            var fifoDelta = points[secondDependency.Id].DisplayedY - points[firstDependency.Id].DisplayedY;

            Assert.Multiple(() =>
            {
                Assert.That(points[firstDependency.Id].RootJobId, Is.EqualTo(firstRoot.Id));
                Assert.That(points[secondDependency.Id].RootJobId, Is.EqualTo(secondRoot.Id));
                Assert.That(fifoDelta, Is.EqualTo(parallelDelta).Within(0.0001));
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
        public void ScatterTimeline_UsesStableLanesActualTimestampAndIndividualPoint()
        {
            var store = CreateStore();
            var jobInfo = Job(Guid.NewGuid(), "Load measurements", CacheQueueId);
            var observedAt = Utc(10, 15, 0);
            store.Apply(Event(JobEventStatus.Failed, jobInfo, observedAt,
                exception: new InvalidOperationException("failure")));

            var timeline = EtlScatterTimelineMapper.Map(store.GetSnapshot());
            var point = timeline.Points.Single();
            Assert.Multiple(() =>
            {
                Assert.That(timeline.Queues.Select(queue => queue.GroupId), Is.EqualTo(new[] { "parallel", "fifo", "cache" }));
                Assert.That(point.JobId, Is.EqualTo(jobInfo.Id));
                Assert.That(point.QueueDisplayName, Is.EqualTo("CacheQ"));
                Assert.That(point.Status, Is.EqualTo("failed"));
                Assert.That(point.EventType, Is.EqualTo(nameof(JobEventStatus.Failed)));
                Assert.That(point.ActualTimestamp, Is.EqualTo(observedAt));
            });
        }

        [Test]
        public void ScatterTimeline_UsesFixedSignedReferenceAndQueueSelection()
        {
            var store = CreateStore();
            var before = Job(Guid.NewGuid(), "Before reference", ParallelQueueId);
            var after = Job(Guid.NewGuid(), "After reference", ParallelQueueId);
            var reference = Utc(10, 15, 1);

            store.Apply(Event(JobEventStatus.Enqueued, before, Utc(10, 15, 0)));
            store.Apply(Event(JobEventStatus.Enqueued, after, Utc(10, 15, 2)));

            var timeline = EtlScatterTimelineMapper.Map(
                store.GetSnapshot(),
                new HashSet<string>(StringComparer.Ordinal) { "parallel" },
                reference,
                pastWindowMilliseconds: 5_000,
                futureWindowMilliseconds: 5_000);
            var points = timeline.Points.OrderBy(point => point.DisplayedX).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(timeline.Queues.Select(queue => queue.GroupId), Is.EqualTo(new[] { "parallel" }));
                Assert.That(points.Select(point => point.DisplayedX), Is.EqualTo(new[] { -1_000d, 1_000d }));
                Assert.That(points[0].ActualTimestamp, Is.EqualTo(Utc(10, 15, 0)));
                Assert.That(points[1].ActualTimestamp, Is.EqualTo(Utc(10, 15, 2)));
            });
        }

        [Test]
        public void ExecutionChannel_SurvivesLateWaitingEvents_AndSnapshotIsDetached()
        {
            // Arrange
            var store = CreateStore();
            var info = Job(Guid.NewGuid(), "channel test", ParallelQueueId);
            var time = Utc(10, 15, 0);
            store.Apply(Event(JobEventStatus.Enqueued, info, time));
            var waiting = store.GetSnapshot();

            // Act: terminal metadata may arrive before the earlier start event.
            store.Apply(new ChannelEvent(JobEventStatus.Successed, info, time.AddSeconds(2), 2));
            store.Apply(Event(JobEventStatus.Enqueued, info, time.AddMilliseconds(1)));
            store.Apply(new ChannelEvent(JobEventStatus.Running, info, time.AddSeconds(1), 2));
            store.Apply(new ChannelEvent(JobEventStatus.Started, info, time.AddMilliseconds(500), 2));
            var snapshot = store.GetSnapshot();

            // Assert
            Assert.That(waiting.Jobs.Single().ExecutionChannel, Is.Null);
            Assert.That(snapshot.Jobs.Single().ExecutionChannel, Is.EqualTo(2));
            Assert.That(snapshot.Jobs.Single().Status, Is.EqualTo("completed"));
            Assert.That(snapshot.Queues.Single(q => q.GroupId == "parallel").MaxParallelism, Is.EqualTo(3));
            var dto = JobMonitorMapper.Map(snapshot);
            Assert.That(dto.Jobs.Single().Channel, Is.EqualTo(2));
            Assert.That(dto.Jobs.Single().ObservedAt, Is.EqualTo(time.AddMilliseconds(500)));
            Assert.That(dto.Jobs.Single().Metadata["enqueuedAt"], Is.EqualTo(time.ToString("O")));
        }

        [Test]
        public void Monitor_SeparatesSequentialStartsThatShareAnEnqueueTimestampAndChannel()
        {
            // Arrange
            var store = CreateStore();
            var time = Utc(10, 15, 0);
            var first = Job(Guid.NewGuid(), "first", FifoQueueId);
            var second = Job(Guid.NewGuid(), "second", FifoQueueId);
            store.Apply(Event(JobEventStatus.Enqueued, first, time));
            store.Apply(Event(JobEventStatus.Enqueued, second, time));
            var waiting = JobMonitorMapper.Map(store.GetSnapshot());

            // Act
            store.Apply(new ChannelEvent(JobEventStatus.Started, first, time.AddSeconds(1), 0));
            store.Apply(new ChannelEvent(JobEventStatus.Successed, first, time.AddMilliseconds(1500), 0));
            store.Apply(new ChannelEvent(JobEventStatus.Started, second, time.AddSeconds(2), 0));
            var mapped = JobMonitorMapper.Map(store.GetSnapshot()).Jobs;

            // Assert
            Assert.That(waiting.Jobs.All(j => j.Channel == null && j.ObservedAt == time), Is.True);
            Assert.That(mapped.All(j => j.Channel == 0), Is.True);
            Assert.That(mapped.Single(j => j.Name == "first").ObservedAt, Is.EqualTo(time.AddSeconds(1)));
            Assert.That(mapped.Single(j => j.Name == "second").ObservedAt, Is.EqualTo(time.AddSeconds(2)));
            Assert.That(mapped.All(j => j.Metadata["timestampSource"] == "Started"), Is.True);
        }

        [TestCase(JobEventStatus.Running)]
        [TestCase(JobEventStatus.Successed)]
        [TestCase(JobEventStatus.Failed)]
        [TestCase(JobEventStatus.Canceled)]
        public void Monitor_DoesNotInventStartTime_WhenChannelArrivesBeforeStarted(JobEventStatus status)
        {
            // Arrange
            var store = CreateStore();
            var info = Job(Guid.NewGuid(), "late start", ParallelQueueId);
            var time = Utc(10, 15, 0);
            store.Apply(Event(JobEventStatus.Enqueued, info, time));
            store.Apply(new ChannelEvent(status, info, time.AddSeconds(2), 1));
            var before = JobMonitorMapper.Map(store.GetSnapshot()).Jobs.Single();

            // Act
            store.Apply(new ChannelEvent(JobEventStatus.Started, info, time.AddSeconds(1), 1));
            var after = JobMonitorMapper.Map(store.GetSnapshot()).Jobs.Single();

            // Assert
            Assert.That(before.Channel, Is.Null);
            Assert.That(before.ObservedAt, Is.EqualTo(time));
            Assert.That(before.Metadata["timestampSource"], Is.EqualTo("Enqueued"));
            Assert.That(after.Channel, Is.EqualTo(1));
            Assert.That(after.ObservedAt, Is.EqualTo(time.AddSeconds(1)));
            Assert.That(after.State, Is.EqualTo(before.State));
        }

        [Test]
        public void Monitor_StartPositionSurvivesLaterRunningAndLateEnqueueEvents()
        {
            // Arrange
            var store = CreateStore();
            var info = Job(Guid.NewGuid(), "stable start", ParallelQueueId);
            var time = Utc(10, 15, 0);
            store.Apply(new ChannelEvent(JobEventStatus.Started, info, time.AddSeconds(1), 2));

            // Act
            store.Apply(new ChannelEvent(JobEventStatus.Running, info, time.AddSeconds(3), 2));
            store.Apply(Event(JobEventStatus.Enqueued, info, time));
            store.Apply(new ChannelEvent(JobEventStatus.Started, info, time.AddSeconds(1), 2));
            var mapped = JobMonitorMapper.Map(store.GetSnapshot()).Jobs.Single();

            // Assert
            Assert.That(mapped.ObservedAt, Is.EqualTo(time.AddSeconds(1)));
            Assert.That(mapped.Channel, Is.EqualTo(2));
            Assert.That(mapped.Metadata["enqueuedAt"], Is.EqualTo(time.ToString("O")));
        }

        [Test]
        public void InvalidRuntimeChannel_IsRejectedBeforeMutatingProjection()
        {
            // Arrange
            var store = CreateStore();
            var info = Job(Guid.NewGuid(), "invalid channel", ParallelQueueId);
            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Apply(
                new ChannelEvent(JobEventStatus.Running, info, Utc(10, 15, 0), 3)));
            Assert.That(store.GetSnapshot().Jobs, Is.Empty);
        }

        [Test]
        public void LegacyEvents_RemainUnassigned_AndTransportContainsOnlyPresentationValues()
        {
            // Arrange
            var store = CreateStore();
            store.Apply(Event(JobEventStatus.Canceled, Job(Guid.NewGuid(), "legacy", FifoQueueId), Utc(10, 15, 0)));
            // Act
            var dto = JobMonitorMapper.Map(store.GetSnapshot());
            var json = System.Text.Json.JsonSerializer.Serialize(dto,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            // Assert
            Assert.That(dto.Jobs.Single().Channel, Is.Null);
            Assert.That(dto.Jobs.Single().State, Is.EqualTo("cancelled"));
            Assert.That(json, Does.Contain("\"channel\":null"));
            Assert.That(json, Does.Not.Contain("jobInfo"));
            Assert.That(json, Does.Not.Contain("exception"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Monitor_RetainsTypedEnqueueHistory_AfterCompletionAndLateDelivery(bool enqueueArrivesLate)
        {
            // Arrange
            var store = CreateStore();
            var info = Job(Guid.NewGuid(), "enqueue history", ParallelQueueId);
            var time = Utc(10, 15, 0);
            if (!enqueueArrivesLate) store.Apply(Event(JobEventStatus.Enqueued, info, time));

            // Act
            store.Apply(new ChannelEvent(JobEventStatus.Started, info, time.AddSeconds(1), 1));
            store.Apply(new ChannelEvent(JobEventStatus.Successed, info, time.AddSeconds(2), 1));
            if (enqueueArrivesLate) store.Apply(Event(JobEventStatus.Enqueued, info, time));
            var dto = JobMonitorMapper.Map(store.GetSnapshot());
            var json = System.Text.Json.JsonSerializer.Serialize(dto,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            using var document = System.Text.Json.JsonDocument.Parse(json);

            // Assert: a fresh browser receives both times, but still only one logical job.
            var job = dto.Jobs.Single();
            Assert.That(job.EnqueuedAt, Is.EqualTo(time));
            Assert.That(job.ObservedAt, Is.EqualTo(time.AddSeconds(1)));
            Assert.That(job.Channel, Is.EqualTo(1));
            Assert.That(job.State, Is.EqualTo("completed"));
            Assert.That(document.RootElement.GetProperty("jobs")[0].GetProperty("enqueuedAt").GetDateTimeOffset(), Is.EqualTo(time));
        }

        [Test]
        public void Monitor_DoesNotInventEnqueueHistory_WhenOnlyStartedWasObserved()
        {
            // Arrange
            var store = CreateStore();
            var info = Job(Guid.NewGuid(), "start only", ParallelQueueId);
            store.Apply(new ChannelEvent(JobEventStatus.Started, info, Utc(10, 15, 0), 0));

            // Act
            var job = JobMonitorMapper.Map(store.GetSnapshot()).Jobs.Single();

            // Assert
            Assert.That(job.EnqueuedAt, Is.Null);
            Assert.That(job.Channel, Is.EqualTo(0));
        }

        private sealed class ChannelEvent : FakeJobEvent, IJobExecutionEvent
        {
            public ChannelEvent(JobEventStatus status, IJobInfo info, DateTimeOffset time, int channel)
                : base(status, info, time.UtcDateTime, 0, null) => ExecutionChannel = channel;
            public int? ExecutionChannel { get; }
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
                    Order: 0, MaxParallelism: 3),
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

        private class FakeJobEvent : IJobEvent
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
