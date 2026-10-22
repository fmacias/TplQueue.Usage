using Fmacias.TplQueue;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Integration.Test.Consumers;
using Fmacias.TplQueue.Snapshots;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class JobMonitorObserverTests
{
    [Test]
    public void OnNext_CapturesOncePerCallbackAndStoreDoesNotRetainRuntimeGraph()
    {
        // Arrange
        var api = Helper.GetApi(new Dictionary<string, IRetryPolicyOptions>(), new Dictionary<string, IQOptions>());
        var job = api.JobFactory.Job(_ => Task.CompletedTask, "Snapshot source");
        var info = job.CopyInfo();
        var error = new InvalidOperationException("Expected failure");
        error.Data["job"] = job;
        // A copied job that has not been queued has an empty queue ID; register a test source explicitly.
        var queueId = Guid.NewGuid();
        var queuedInfo = new SourceInfo(info, queueId);
        var source = new SourceEvent(queuedInfo, error);
        var store = new JobMonitorStore(new JobMonitorQueueCatalog(new[] {
            new JobMonitorQueue(queueId, "queue", "Queue", 2) }), NullLogger<JobMonitorStore>.Instance);
        var factory = new CountingFactory(api.ObserverFactory());
        var observer = new JobMonitorObserver(store, NullLogger<JobMonitorObserver>.Instance, factory);

        // Act: identical callback values still represent two occurrences.
        observer.OnNext(source);
        observer.OnNext(source);
        var snapshot = store.GetSnapshot();

        // Assert
        Assert.That(factory.Captures, Is.EqualTo(2));
        Assert.That(snapshot.Events, Has.Count.EqualTo(2));
        Assert.That(snapshot.Events[0], Is.Not.SameAs(snapshot.Events[1]));
        Assert.That(snapshot.Events[0].Diagnostic.Message, Is.EqualTo(error.Message));
        Assert.That(snapshot.Events[0].ExecutionChannel, Is.EqualTo(1));
        SnapshotRetentionAssert.IsDetachedFrom(snapshot, source, queuedInfo, info, job, error, error.Data);
        SnapshotRetentionAssert.IsDetachedFrom(store, source, queuedInfo, info, job, error, error.Data);
    }

    [Test]
    public void OnNext_IsolatesCaptureFailureAndRejectsNullBeforeCallingFactory()
    {
        // Arrange
        var api = Helper.GetApi(new Dictionary<string, IRetryPolicyOptions>(), new Dictionary<string, IQOptions>());
        var factory = new CountingFactory(api.ObserverFactory()) { FailCapture = true };
        var store = new JobMonitorStore(new JobMonitorQueueCatalog(new[] {
            new JobMonitorQueue(Guid.NewGuid(), "queue", "Queue", 1) }), NullLogger<JobMonitorStore>.Instance);
        var observer = new JobMonitorObserver(store, NullLogger<JobMonitorObserver>.Instance, factory);
        var source = new SourceEvent(api.JobFactory.Job(_ => Task.CompletedTask).CopyInfo(), null);

        // Act / Assert
        Assert.That(() => observer.OnNext(null!), Throws.ArgumentNullException);
        Assert.That(factory.Captures, Is.Zero);
        Assert.That(() => observer.OnNext(source), Throws.Nothing);
        Assert.That(factory.Captures, Is.EqualTo(1));
        Assert.That(store.GetSnapshot().Events, Is.Empty);
        Assert.That(() => observer.OnError(null!), Throws.ArgumentNullException);
    }

    private sealed class SourceEvent(IJobInfo info, Exception? error) : IJobExecutionEvent
    {
        public JobEventStatus Status => JobEventStatus.Failed;
        public DateTime Timestamp => new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        public IJobInfo JobInfo => info;
        public int RetryCount => 2;
        public Exception? Exception => error;
        public int? ExecutionChannel => 1;
    }

    private sealed class SourceInfo(IJobInfo source, Guid queueId) : IJobInfo
    {
        public Guid Id => source.Id;
        public string Name => source.Name;
        public bool IsCompleted => source.IsCompleted;
        public DateTime ExecutionStart => source.ExecutionStart;
        public TimeSpan ExecutionTime => source.ExecutionTime;
        public DateTime ExecutionEnd => source.ExecutionEnd;
        public TaskStatus Status => source.Status;
        public Guid CrossQueueId => queueId;
        public IReadOnlyCollection<IJobInfo> Dependencies => source.Dependencies;
    }

    private sealed class CountingFactory(IObserverFactory inner) : IObserverFactory
    {
        public int Captures { get; private set; }
        public bool FailCapture { get; init; }
        public JobEventSnapshot CreateJobEventSnapshot(IJobEvent source)
        {
            Captures++;
            if (FailCapture) throw new InvalidOperationException("Capture failed");
            return inner.CreateJobEventSnapshot(source);
        }
        public IObserverDispatcher CreateObserverDispatcher() => inner.CreateObserverDispatcher();
        public ILoggingObserver CreateLoggingObserver(ILogger<ILoggingObserver> logger) => inner.CreateLoggingObserver(logger);
        public IConsoleObserver CreateConsoleObserver() => inner.CreateConsoleObserver();
        public IProfilingObserver CreateProfilingObserver(ILogger<IProfilingObserver> logger) => inner.CreateProfilingObserver(logger);
        public IFileLoggingObserver CreateFileLoggingObserver(ILogger logger, string queueName) => inner.CreateFileLoggingObserver(logger, queueName);
    }
}
