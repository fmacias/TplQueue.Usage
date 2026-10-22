using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;
using TplQueue.Sample.Etl.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class JobMonitorStoreTests
{
    private static readonly Guid QueueId = Guid.NewGuid();

    [Test]
    public void Apply_PreservesEachOccurrenceIncludingEqualValuesAndRepeatedReferences()
    {
        // Arrange
        var store = CreateStore();
        var running = Observation();
        var notifications = 0;
        store.Changed += (_, _) => notifications++;

        // Act
        store.Apply(running);
        var before = store.GetSnapshot();
        store.Apply(running);
        store.Apply(Observation(running.JobInfo.Id));
        var after = store.GetSnapshot();

        // Assert: snapshot identity and occurrence multiplicity are separate from visual job identity.
        Assert.That(before.Events, Has.Count.EqualTo(1));
        Assert.That(after.Events, Has.Count.EqualTo(3));
        Assert.That(after.Events[0], Is.SameAs(running));
        Assert.That(after.Events[1], Is.SameAs(running));
        Assert.That(after.Revision, Is.EqualTo(3));
        Assert.That(notifications, Is.EqualTo(3));
        Assert.That(() => ((IList<JobEventSnapshot>)after.Events).Clear(), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Bootstrap_RetainsEnqueueStartRetryAndRootSuccessInArrivalOrder()
    {
        // Arrange
        var store = CreateStore();
        var id = Guid.NewGuid();
        var events = new[] { Observation(id, JobEventStatus.Enqueued), Observation(id, JobEventStatus.Successed),
            Observation(id, JobEventStatus.Started), Observation(id, JobEventStatus.Running),
            Observation(id, JobEventStatus.RootSuccessed) };

        // Act: late start remains an occurrence rather than overwriting the terminal event.
        foreach (var item in events) store.Apply(item);

        // Assert
        Assert.That(store.GetSnapshot().Events, Is.EqualTo(events));
    }

    [Test]
    public void Retention_EvictsWholeOldestJobIncludingItsEventsAndMembership()
    {
        // Arrange
        var graphs = new Graphs();
        var first = Guid.NewGuid();
        graphs.Roots[first] = new[] { Guid.NewGuid() };
        var store = CreateStore(graphs, maximumEvents: 3);
        store.Apply(Observation(first, JobEventStatus.Enqueued));
        store.Apply(Observation(first, JobEventStatus.Started));
        var before = store.GetSnapshot();
        var second = Guid.NewGuid();

        // Act
        store.Apply(Observation(second, JobEventStatus.Enqueued));
        store.Apply(Observation(second, JobEventStatus.Started));
        var after = store.GetSnapshot();

        // Assert: survivors retain their enqueue/start pair; old delivered snapshots stay detached.
        Assert.That(after.Events.Select(e => e.JobInfo.Id), Is.All.EqualTo(second));
        Assert.That(after.Events.Select(e => e.Status), Is.EqualTo(new[] { JobEventStatus.Enqueued, JobEventStatus.Started }));
        Assert.That(after.RootJobIds.ContainsKey(first), Is.False);
        Assert.That(after.DroppedEventCount, Is.EqualTo(2));
        Assert.That(before.Events, Has.Count.EqualTo(2));
        Assert.That(before.RootJobIds[first], Is.EqualTo(graphs.Roots[first]));
    }

    [Test]
    public void Retention_EnforcesBoundEvenWhenOneJobExceedsIt()
    {
        // Arrange
        var store = CreateStore(maximumEvents: 2);
        var repeated = Observation();

        // Act
        for (var i = 0; i < 10; i++) store.Apply(repeated);

        // Assert: an oversized job is evicted as a unit, rather than inventing partial positions.
        Assert.That(store.GetSnapshot().Events, Has.Count.LessThanOrEqualTo(2));
        Assert.That(store.GetSnapshot().DroppedEventCount, Is.EqualTo(9));
    }

    [Test]
    public void Snapshot_JoinsOnlyObservedJobsAndCopiesSharedMembership()
    {
        // Arrange
        var graphs = new Graphs();
        var child = Guid.NewGuid();
        var roots = new[] { Guid.NewGuid(), Guid.NewGuid() };
        graphs.Roots[child] = roots;
        graphs.Roots[Guid.NewGuid()] = new[] { Guid.NewGuid() };
        var store = CreateStore(graphs);
        store.Apply(Observation(child));

        // Act
        var before = store.GetSnapshot();
        roots[0] = Guid.NewGuid();
        var after = store.GetSnapshot();

        // Assert
        Assert.That(before.RootJobIds, Has.Count.EqualTo(1));
        Assert.That(before.RootJobIds[child], Is.Not.EqualTo(after.RootJobIds[child]));
        Assert.That(after.Events, Has.Count.EqualTo(1));
        Assert.That(after.Events[0].ExecutionChannel, Is.EqualTo(1));
    }

    [Test]
    public void ConcurrentQueuesAndCircuitSubscriptions_KeepAllOccurrences()
    {
        // Arrange
        var secondQueue = Guid.NewGuid();
        var catalog = new JobMonitorQueueCatalog(new[] {
            new JobMonitorQueue(QueueId, "first", "First", 2), new JobMonitorQueue(secondQueue, "second", "Second", 2) });
        var store = new JobMonitorStore(catalog, NullLogger<JobMonitorStore>.Instance);
        var notifications = 0;
        store.Changed += (_, _) => throw new InvalidOperationException("Disconnected circuit");
        EventHandler subscriber = (_, _) => { store.GetSnapshot(); Interlocked.Increment(ref notifications); };
        store.Changed += subscriber;
        var bootstrap = store.GetSnapshot(); // subscribe first, then bootstrap, as Dashboard does

        // Act
        Parallel.For(0, 120, i => store.Apply(Observation(queueId: i % 2 == 0 ? QueueId : secondQueue)));
        var lateCircuit = store.GetSnapshot();
        store.Changed -= subscriber;
        store.Apply(Observation());

        // Assert
        Assert.That(bootstrap.Events, Is.Empty);
        Assert.That(lateCircuit.Events, Has.Count.EqualTo(120));
        Assert.That(lateCircuit.Events.GroupBy(e => e.JobInfo.CrossQueueId).Select(g => g.Count()), Is.All.EqualTo(60));
        Assert.That(notifications, Is.EqualTo(120));
        Assert.That(store.GetSnapshot().Events, Has.Count.EqualTo(121));
    }

    [Test]
    public void InvalidInput_DoesNotMutateOrNotify()
    {
        // Arrange
        var store = CreateStore();
        var notifications = 0;
        store.Changed += (_, _) => notifications++;

        // Act / Assert
        Assert.That(() => store.Apply(null!), Throws.ArgumentNullException);
        Assert.That(() => store.Apply(Observation(queueId: Guid.NewGuid())), Throws.InvalidOperationException);
        Assert.That(() => store.Apply(Observation(channel: -1)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => store.Apply(Observation(channel: 2)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(store.GetSnapshot().Events, Is.Empty);
        Assert.That(notifications, Is.Zero);
        Assert.That(() => CreateStore(maximumEvents: 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void JsonTransport_ContainsOriginalSnapshotContractAndDetachedMetadata()
    {
        // Arrange
        var store = CreateStore();
        store.Apply(Observation());

        // Act
        var json = JobMonitorJson.Serialize(store.GetSnapshot());
        using var document = JsonDocument.Parse(json);
        var observation = document.RootElement.GetProperty("events")[0];

        // Assert
        Assert.That(observation.GetProperty("jobInfo").GetProperty("crossQueueId").GetGuid(), Is.EqualTo(QueueId));
        Assert.That(observation.GetProperty("status").GetInt32(), Is.EqualTo((int)JobEventStatus.Running));
        Assert.That(observation.GetProperty("diagnostic").GetProperty("hasException").GetBoolean(), Is.False);
        Assert.That(document.RootElement.TryGetProperty("jobs", out _), Is.False);
    }

    [Test]
    public void JsonTransport_PreservesDeepDependencyMetadataBeyondDefaultInteropDepth()
    {
        // Arrange: FIFO ordering can create deep graphs without introducing additional observed jobs.
        var info = Observation().JobInfo;
        for (var depth = 0; depth < 80; depth++)
            info = new JobInfoSnapshot(Guid.NewGuid(), "Parent", false, default, default, default,
                TaskStatus.Created, QueueId, new[] { info });
        var store = CreateStore();
        store.Apply(new JobEventSnapshot(JobEventStatus.Enqueued, DateTime.UtcNow, 0, null, info, JobExceptionDiagnostic.None));

        // Act
        using var document = JsonDocument.Parse(JobMonitorJson.Serialize(store.GetSnapshot()),
            new JsonDocumentOptions { MaxDepth = 2048 });
        var node = document.RootElement.GetProperty("events")[0].GetProperty("jobInfo");
        var count = 0;
        while (node.GetProperty("dependencies").GetArrayLength() > 0)
        {
            node = node.GetProperty("dependencies")[0];
            count++;
        }

        // Assert
        Assert.That(count, Is.EqualTo(80));
        Assert.That(store.GetSnapshot().Events, Has.Count.EqualTo(1));
    }

    private static JobEventSnapshot Observation(Guid? jobId = null, JobEventStatus status = JobEventStatus.Running,
        Guid? queueId = null, int? channel = 1) => new(status, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc), 0, channel,
        new JobInfoSnapshot(jobId ?? Guid.NewGuid(), "Test", false, default, default, default, TaskStatus.Running,
            queueId ?? QueueId, Array.Empty<JobInfoSnapshot>()), JobExceptionDiagnostic.None);

    private static JobMonitorStore CreateStore(ISimulationGraphCatalog? graphs = null, int maximumEvents = 20_000) =>
        new(new JobMonitorQueueCatalog(new[] { new JobMonitorQueue(QueueId, "parallel", "ParallelQ", 2) }),
            NullLogger<JobMonitorStore>.Instance, graphs, new JobMonitorRetentionOptions { MaximumEvents = maximumEvents });

    private sealed class Graphs : ISimulationGraphCatalog
    {
        public Dictionary<Guid, Guid[]> Roots { get; } = new();
        public IReadOnlyList<Guid> GetRootJobIds(Guid jobId) => Roots.GetValueOrDefault(jobId, Array.Empty<Guid>());
        public void Register(IJobInfo root) => throw new NotSupportedException();
    }
}
