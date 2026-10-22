using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Extensions;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Domain.Composition;
using TplQueue.Sample.Domain.Payloads;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationGraphIntegrationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CacheQueue_KeepsStandaloneIngestSeparateFromEtlGraph(bool singleFirst)
    {
        // Arrange: both scenarios use the same ingest payload type and registered handler.
        var api = API.Create(CoreApi.Create(), new Dictionary<string, IRetryPolicyOptions>(),
            new Dictionary<string, IQOptions>());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApi>(api);
        services.AddSingleton(api.DataJobFactory);
        services.AddSingleton(api.QFactory);
        services.AddSingleton(api.RetryPolicyAbstractFactory);
        services.AddSingleton(api.SystemTextSerializerFactory());
        services.AddTransient<ISystemTextJsonUniversalSerializer>(sp =>
            sp.GetRequiredService<ISystemTextJsonSerializerFactory>().Serializer(new JsonSerializerOptions()));
        services.AddSampleDomain();
        services.AddSampleEtlWorkflow();
        using var provider = services.BuildServiceProvider();
        provider.RegisterSampleEtlPayloadHandlers();
        var runtime = provider.GetRequiredService<IEtlQueueRuntime>();
        var queue = runtime.GetQueue(AvailableQueue.Cache);
        var store = new JobMonitorStore(new JobMonitorQueueCatalog(new[] {
            new JobMonitorQueue(queue.QueueId, queue.QueueId.ToString(),
                queue.Name, queue.MaxParallelism) }), NullLogger<JobMonitorStore>.Instance,
            provider.GetRequiredService<ISimulationGraphCatalog>());
        var factory = provider.GetRequiredService<ISampleJobFactory>();

        var single = factory.IngestMeasurementsSigleJobRoot(new[] {
            new LegacyMeasurement("test", 20m, TemperatureUnit.Celsius, DateTime.UtcNow) });

        var etl = factory.LoadMeasurementsJobRoot(new[] {
            new LegacyMeasurement("test", 20m, TemperatureUnit.Celsius, DateTime.UtcNow) });

        var transform = etl.Dependencies.Single();

        var ingest = transform.Dependencies.Single();
        var observer = new GraphCompletionObserver(store, api.ObserverFactory(), single.Id, etl.Id);
        using var subscription = runtime.Subscribe(observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.That(single.Dependencies, Is.Empty);

        // Act: exercise real persistence, hydration and detached observation capture.
        runtime.ResumePolling();
        if (singleFirst) runtime.Enqueue(AvailableQueue.Cache, single, timeout.Token);
        runtime.Enqueue(AvailableQueue.Cache, etl, timeout.Token);
        if (!singleFirst) runtime.Enqueue(AvailableQueue.Cache, single, timeout.Token);
        await observer.Completion.Task.WaitAsync(timeout.Token);
        var snapshot = store.GetSnapshot();
        var jobs = snapshot.Events.GroupBy(e => e.JobInfo.Id).ToArray();

        // Assert: four jobs form two graphs, with exactly the two ETL dependency edges.
        Assert.That(jobs.Select(job => job.Key), Is.EquivalentTo(new[] { single.Id, etl.Id, transform.Id, ingest.Id }));
        Assert.That(jobs.All(events => events.Any(e => e.Status is JobEventStatus.Successed or JobEventStatus.RootSuccessed)), Is.True);
        Assert.That(snapshot.RootJobIds[single.Id], Is.EqualTo(new[] { single.Id }));
        Assert.That(jobs.Single(job => job.Key == single.Id).SelectMany(e => e.JobInfo.Dependencies), Is.Empty);
        Assert.That(jobs.Where(job => job.Key != single.Id).Select(job => snapshot.RootJobIds[job.Key]),
            Is.All.EqualTo(new[] { etl.Id }));
        Assert.That(jobs.Single(job => job.Key == ingest.Id).SelectMany(e => e.JobInfo.Dependencies), Is.Empty);
        Assert.That(jobs.Single(job => job.Key == transform.Id).SelectMany(e => e.JobInfo.Dependencies).Select(d => d.Id).Distinct(),
            Is.EqualTo(new[] { ingest.Id }));
        Assert.That(jobs.Single(job => job.Key == etl.Id).SelectMany(e => e.JobInfo.Dependencies).Select(d => d.Id).Distinct(),
            Is.EqualTo(new[] { transform.Id }));
    }

    [TestCase(AvailableQueue.FIFO, "completed")]
    [TestCase(AvailableQueue.Parallel, "completed")]
    [TestCase(AvailableQueue.Cache, "completed")]
    [TestCase(AvailableQueue.FIFO, "failed")]
    [TestCase(AvailableQueue.Parallel, "failed")]
    [TestCase(AvailableQueue.Cache, "failed")]
    [TestCase(AvailableQueue.FIFO, "cancelled")]
    [TestCase(AvailableQueue.Parallel, "cancelled")]
    [TestCase(AvailableQueue.Cache, "cancelled")]
    public async Task RealQueue_PreservesIdentityFromRunningThroughTerminalOutcome(AvailableQueue queue, string outcome)
    {
        // Arrange: a two-job graph with a gated prerequisite, including actual CacheQ hydration.
        var api = API.Create(CoreApi.Create(), new Dictionary<string, IRetryPolicyOptions>(),
            new Dictionary<string, IQOptions>());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApi>(api);
        services.AddSingleton(api.DataJobFactory);
        services.AddSingleton(api.QFactory);
        services.AddSingleton(api.RetryPolicyAbstractFactory);
        services.AddSingleton(api.SystemTextSerializerFactory());
        services.AddTransient<ISystemTextJsonUniversalSerializer>(sp =>
            sp.GetRequiredService<ISystemTextJsonSerializerFactory>().Serializer(new JsonSerializerOptions()));
        services.AddSampleDomain();
        services.AddSampleEtlWorkflow();
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IEtlQueueRuntime>();
        var graphs = provider.GetRequiredService<ISimulationGraphCatalog>();
        var queues = Enum.GetValues<AvailableQueue>().Select(runtime.GetQueue).ToArray();
        var store = new JobMonitorStore(new JobMonitorQueueCatalog(queues.Select(q =>
            new JobMonitorQueue(q.QueueId, q.QueueId.ToString(), q.Name, q.MaxParallelism)).ToArray()),
            NullLogger<JobMonitorStore>.Instance, graphs);
        var payload = IngestMeasurementsPayload.Create(new[] {
            new LegacyMeasurement("test", 20m, TemperatureUnit.Celsius, DateTime.UtcNow) }, Guid.NewGuid());
        var handler = new GatedHandler(outcome);
        api.RegisterPayloadHandler(payload.HandlerKey, handler);
        var child = api.DataJobFactory.DataJob(payload, handler, "prerequisite");
        var root = api.DataJobFactory.DataJobRoot(payload, handler, "root", () => NoRetryPolicy.Create());
        child.Then(root);
        var observer = new ProjectionObserver(store, api.ObserverFactory(), child.Id, root.Id);
        using var subscription = runtime.Subscribe(observer);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            // Act: inspect running identity before releasing the prerequisite.
            runtime.ResumePolling();
            runtime.Enqueue(queue, root, cancellation.Token);
            await observer.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var running = store.GetSnapshot();
            Assert.That(running.RootJobIds[child.Id], Is.EqualTo(new[] { root.Id }));
            var started = running.Events.First(e => e.JobInfo.Id == child.Id && e.Status == JobEventStatus.Started);
            if (outcome == "cancelled") cancellation.Cancel();
            else handler.Release.TrySetResult(true);
            await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert: IDs survive hydrated objects and never rely on RootSuccessed for failures/cancellation.
            var snapshot = store.GetSnapshot();
            Assert.That(snapshot.Events.Select(e => e.JobInfo.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(snapshot.RootJobIds.Values, Is.All.EqualTo(new[] { root.Id }));
            var terminal = outcome switch {
                "completed" => JobEventStatus.RootSuccessed, "failed" => JobEventStatus.Failed, _ => JobEventStatus.Canceled };
            var rootEvent = snapshot.Events.Last(e => e.JobInfo.Id == root.Id && e.Status == terminal);
            Assert.That(rootEvent.JobInfo.Dependencies.Select(d => d.Id), Is.EqualTo(new[] { child.Id }));
            Assert.That(snapshot.Events, Does.Contain(started), "Bootstrap preserves the original channel-bearing start.");
        }
        finally { handler.Release.TrySetResult(true); cancellation.Cancel(); }
    }

    private sealed class GraphCompletionObserver(JobMonitorStore store, IObserverFactory factory, params Guid[] rootIds)
        : IObserver<IJobEvent>
    {
        private readonly HashSet<Guid> _remaining = new(rootIds);
        private readonly object _gate = new();
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnNext(IJobEvent value)
        {
            try
            {
                lock (_gate)
                {
                    store.Apply(factory.CreateJobEventSnapshot(value));
                    if (value.Status is JobEventStatus.Failed or JobEventStatus.Canceled)
                        Completion.TrySetException(new InvalidOperationException($"Unexpected {value.Status} for {value.JobInfo.Id}"));
                    if (value.Status == JobEventStatus.RootSuccessed && _remaining.Remove(value.JobInfo.Id)
                        && _remaining.Count == 0) Completion.TrySetResult(true);
                }
            }
            catch (Exception error) { OnError(error); }
        }

        public void OnError(Exception error) => Completion.TrySetException(error);
        public void OnCompleted() { }
    }

    private sealed class GatedHandler(string outcome) : IHandler
    {
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
        {
            await Release.Task.WaitAsync(cancellationToken);
            if (outcome == "failed") throw new InvalidOperationException("Expected prerequisite failure.");
        }
    }

    private sealed class ProjectionObserver(JobMonitorStore store, IObserverFactory factory, Guid childId, Guid rootId) : IObserver<IJobEvent>
    {
        public TaskCompletionSource<bool> Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnNext(IJobEvent value)
        {
            try
            {
                store.Apply(factory.CreateJobEventSnapshot(value));
                if (value.JobInfo.Id == childId && value.Status == JobEventStatus.Started) Running.TrySetResult(true);
                if (value.JobInfo.Id == rootId && value.Status is JobEventStatus.RootSuccessed or JobEventStatus.Failed or JobEventStatus.Canceled)
                    Terminal.TrySetResult(true);
            }
            catch (Exception error) { OnError(error); }
        }
        public void OnError(Exception error) { Running.TrySetException(error); Terminal.TrySetException(error); }
        public void OnCompleted() { }
    }
}
