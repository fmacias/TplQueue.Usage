using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Snapshots;

namespace Fmacias.TplQueue.Integration.Test.Consumers
{
    [TestFixture]
    public sealed class JobEventSnapshotRetentionIntegrationTests
    {
        [Test]
        public void PublicFactory_OrdinaryEvent_DoesNotRetainSourceEventOrJobInfo()
        {
            // Arrange
            var api = CreateApi();
            var source = CreateOrdinaryEvent(api);

            // Act
            var snapshot = api.ObserverFactory().CreateJobEventSnapshot(source.Event);

            // Assert
            AssertOrdinaryMetadataWasCaptured(snapshot, source.Event.JobInfo);
            SnapshotRetentionAssert.IsDetachedFrom(snapshot, source.OriginalReferences);
        }

        [Test]
        public void PublicFactory_NestedDataJobsAndExceptions_DoNotRetainRuntimeGraph()
        {
            // Arrange
            var api = CreateApi();
            var jobs = CreateNestedDataJobs(api);
            var source = CreateEventWithExceptionsReferencingJobs(jobs);

            // Act
            var snapshot = api.ObserverFactory().CreateJobEventSnapshot(source.Event);

            // Assert
            AssertNestedJobMetadataWasCaptured(snapshot, jobs);
            AssertNestedExceptionMetadataWasCaptured(snapshot.Diagnostic);
            SnapshotRetentionAssert.IsDetachedFrom(snapshot, source.OriginalReferences);
        }

        [Test]
        public async Task PublicQueue_FailedDataJob_SnapshotDoesNotRetainDeliveredEventOrRuntimeObjects()
        {
            // Arrange
            var api = CreateApi();
            using var queue = CreateQueueWithoutRetries(api);
            var job = CreateFailingDataJob(api);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var observer = new FailureSnapshotObserver(api.ObserverFactory(), job.Root.Id);
            using var subscription = queue.Subscribe(observer);

            // Act
            var capture = await ExecuteAndCaptureFailureAsync(job.Root, queue, observer, cancellation.Token);

            // Assert
            AssertJobCompletedWithFailure(job.Root, cancellation.Token);
            AssertQueueFailureMetadataWasCaptured(capture.Source, capture.Snapshot, job.Root, queue);
            AssertQueueSnapshotIsDetached(capture.Source, capture.Snapshot, job, observer);
        }

        private static IApi CreateApi() => Helper.GetApi(
            new Dictionary<string, IRetryPolicyOptions>(), new Dictionary<string, IQOptions>());

        private static SnapshotSource CreateOrdinaryEvent(IApi api)
        {
            var job = api.JobFactory.Job(_ => Task.CompletedTask, "ordinary");
            var info = job.CopyInfo();
            var source = new SourceEvent(info);
            return new SnapshotSource(source, new object[] { source, info, info.Dependencies, job });
        }

        private static void AssertOrdinaryMetadataWasCaptured(JobEventSnapshot snapshot, IJobInfo original)
        {
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.JobInfo.Id, Is.EqualTo(original.Id));
                Assert.That(snapshot.JobInfo.Name, Is.EqualTo("ordinary"));
                Assert.That(snapshot.JobInfo.Dependencies, Is.Empty);
                Assert.That(snapshot.HasException, Is.False);
                Assert.That(snapshot.ExecutionChannel, Is.Null);
            });
        }

        /// <summary>Creates live public DataJobs, each retaining its own payload and the shared handler.</summary>
        private static DataJobGraph CreateNestedDataJobs(IApi api)
        {
            var handler = DelegatePayloadHandler.Create((_, _) => Task.CompletedTask);
            var leaf = api.DataJobFactory.DataJob(new RetentionPayload(), handler, "leaf");
            var child = api.DataJobFactory.DataJob(new RetentionPayload(), handler, "child");
            var root = api.DataJobFactory.DataJobRoot(new RetentionPayload(), handler, "root");
            child.After(leaf);
            root.After(child);
            return new DataJobGraph(root, child, leaf, handler);
        }

        private static SnapshotSource CreateEventWithExceptionsReferencingJobs(DataJobGraph jobs)
        {
            var error = CreateNestedExceptionsReferencingJobs(jobs);
            var source = new SourceEvent(jobs.Root, error);
            error.Data["event"] = source;
            var originals = CollectNestedSourceReferences(source, jobs, error);
            return new SnapshotSource(source, originals);
        }

        private static AggregateException CreateNestedExceptionsReferencingJobs(DataJobGraph jobs)
        {
            var inner = new InvalidOperationException("inner failure");
            var branch = new Exception("branch failure", inner);
            branch.Data["job-info"] = jobs.Child;
            inner.Data["payload"] = jobs.Leaf.Payload;
            inner.Data["handler"] = jobs.Handler;
            inner.Data["task"] = jobs.Root.WaitUntilFinishedAsync();
            return new AggregateException("outer failure", branch, inner);
        }

        private static object[] CollectNestedSourceReferences(
            IJobEvent source, DataJobGraph jobs, AggregateException error)
        {
            var branch = error.InnerExceptions[0];
            var inner = error.InnerExceptions[1];
            return new object[]
            {
                source, jobs.Root, jobs.Child, jobs.Leaf,
                jobs.Root.Payload, jobs.Child.Payload, jobs.Leaf.Payload,
                jobs.Root.Payload.State, jobs.Child.Payload.State, jobs.Leaf.Payload.State,
                jobs.Handler, jobs.Root.WaitUntilFinishedAsync(), error, branch, inner,
                error.Data, branch.Data, inner.Data, error.InnerExceptions
            };
        }

        private static void AssertNestedJobMetadataWasCaptured(JobEventSnapshot snapshot, DataJobGraph jobs)
        {
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.JobInfo.Id, Is.EqualTo(jobs.Root.Id));
                Assert.That(snapshot.JobInfo.Dependencies.Single().Id, Is.EqualTo(jobs.Child.Id));
                Assert.That(snapshot.JobInfo.Dependencies.Single().Dependencies.Single().Id,
                    Is.EqualTo(jobs.Leaf.Id));
            });
        }

        private static void AssertNestedExceptionMetadataWasCaptured(JobExceptionDiagnostic diagnostic)
        {
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.InnerExceptions.Select(item => item.Message),
                    Is.EqualTo(new[] { "branch failure", "inner failure" }));
                Assert.That(diagnostic.InnerExceptions[0].InnerExceptions.Single().Message,
                    Is.EqualTo("inner failure"));
            });
        }

        private static IParallelQ CreateQueueWithoutRetries(IApi api)
        {
            return api.QFactory.Parallel(Guid.NewGuid(), "snapshot-retention",
                maxParallelism: 1, logger: Helper.GetLogger<IParallelQ>(),
                retryPolicyFactory: () => NoRetryPolicy.Create());
        }

        private static FailingDataJob CreateFailingDataJob(IApi api)
        {
            var payload = new RetentionPayload();
            var error = new InvalidOperationException("handler failure");
            var handler = DelegatePayloadHandler.Create((_, _) => Task.FromException(error));
            var root = api.DataJobFactory.DataJobRoot(payload, handler, "failing-data-job",
                () => NoRetryPolicy.Create());
            error.Data["payload"] = payload;
            error.Data["job"] = root;
            error.Data["handler"] = handler;
            return new FailingDataJob(root, error, handler);
        }

        private static async Task<(IJobEvent Source, JobEventSnapshot Snapshot)> ExecuteAndCaptureFailureAsync(
            IJobRoot root, IParallelQ queue, FailureSnapshotObserver observer, CancellationToken cancellationToken)
        {
            root.Enqueue(queue, cancellationToken);
            queue.ResumePolling();
            return await observer.Captured.WaitAsync(cancellationToken);
        }

        private static void AssertJobCompletedWithFailure(IJobRoot root, CancellationToken cancellationToken)
        {
            var completion = root.WaitUntilFinishedAsync();
            Assert.CatchAsync<Exception>(async () => await completion.WaitAsync(cancellationToken));
            Assert.That(completion.IsFaulted, Is.True);
        }

        private static void AssertQueueFailureMetadataWasCaptured(
            IJobEvent source, JobEventSnapshot snapshot, IJobRoot root, IParallelQ queue)
        {
            Assert.Multiple(() =>
            {
                Assert.That(source.Exception, Is.Not.Null);
                Assert.That(snapshot.Status, Is.EqualTo(JobEventStatus.Failed));
                Assert.That(snapshot.JobInfo.Id, Is.EqualTo(root.Id));
                Assert.That(snapshot.JobInfo.CrossQueueId, Is.EqualTo(queue.QueueId));
                Assert.That(snapshot.HasException, Is.True);
            });
        }

        private static void AssertQueueSnapshotIsDetached(
            IJobEvent source, JobEventSnapshot snapshot, FailingDataJob job, FailureSnapshotObserver observer)
        {
            // Keep originals alive for identity checks; traverse only from the produced snapshot.
            SnapshotRetentionAssert.IsDetachedFrom(snapshot, source, source.JobInfo,
                source.JobInfo.Dependencies, source.Exception!, job.Error, job.Error.Data,
                job.Root, job.Root.Payload, job.Root.Payload.State, job.Handler,
                job.Root.WaitUntilFinishedAsync(), observer);
        }

        private sealed record SnapshotSource(IJobEvent Event, object[] OriginalReferences);

        private sealed record DataJobGraph(
            IDataJobRoot<RetentionPayload> Root,
            IDataJob<RetentionPayload> Child,
            IDataJob<RetentionPayload> Leaf,
            IHandler Handler);

        private sealed record FailingDataJob(
            IDataJobRoot<RetentionPayload> Root, Exception Error, IHandler Handler);

        private sealed class RetentionPayload : IPayload
        {
            public object State { get; } = new();
            public string PayloadId { get; } = Guid.NewGuid().ToString();
            public string HandlerKey => "test/snapshot-retention";
            public DateTime CollectionTime { get; } = DateTime.UtcNow;
        }

        private sealed class SourceEvent(IJobInfo info, Exception? error = null) : IJobEvent
        {
            public JobEventStatus Status => Exception == null ? JobEventStatus.Successed : JobEventStatus.Failed;
            public IJobInfo JobInfo { get; } = info;
            public Exception? Exception { get; } = error;
            public DateTime Timestamp { get; } = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            public int RetryCount => 0;
            public override string ToString() => $"{Status}: {JobInfo.Name}";
        }

        /// <summary>
        /// Captures through the public factory in the actual queue callback. Source retention here is
        /// intentional test instrumentation; only the snapshot is the root of the retention assertion.
        /// </summary>
        private sealed class FailureSnapshotObserver(IObserverFactory factory, Guid jobId) : IObserver<IJobEvent>
        {
            private readonly TaskCompletionSource<(IJobEvent Source, JobEventSnapshot Snapshot)> _captured =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<(IJobEvent Source, JobEventSnapshot Snapshot)> Captured => _captured.Task;

            public void OnNext(IJobEvent value)
            {
                if (IsFailureForObservedJob(value))
                    CaptureSnapshotOrReportError(value);
            }

            private bool IsFailureForObservedJob(IJobEvent value)
            {
                return value.JobInfo.Id == jobId && value.Status == JobEventStatus.Failed;
            }

            private void CaptureSnapshotOrReportError(IJobEvent value)
            {
                try
                {
                    _captured.TrySetResult((value, factory.CreateJobEventSnapshot(value)));
                }
                catch (Exception error)
                {
                    _captured.TrySetException(error);
                }
            }

            public void OnError(Exception error) => _captured.TrySetException(error);

            public void OnCompleted() => _captured.TrySetException(
                new AssertionException("The queue completed without delivering the expected failure event."));
        }
    }
}
