using System.Collections.Concurrent;
using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Simulation.Handlers;
using TplQueue.Sample.Simulation.Payloads;
using TplQueue.Sample.Simulation.Runtime;
using TplQueue.Sample.Simulation.Scenarios;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SingleJobSimulationTests
{
    [TestCase(AvailableQueue.FIFO)]
    [TestCase(AvailableQueue.Parallel)]
    [TestCase(AvailableQueue.Cache)]
    public async Task IndependentRoot_ExecutesOnce_AndLaterRootReusesReleasedChannel(AvailableQueue queue)
    {
        // Arrange: real queues, payload serialization and handler; a decorator counts actual invocations.
        using var context = new Context();
        var scenario = context.Provider.GetRequiredService<SingleJobScenario>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act: finish each root before submitting the next; observer completion is a separate barrier.
        var first = scenario.Run(queue, cancellation.Token);
        await context.Observer.Completion(first).WaitAsync(cancellation.Token);
        await context.Queues.Single(q => q.QueueId == context.Observer.Events.First(e => e.JobInfo.Id == first).JobInfo.CrossQueueId)
            .WaitAsync().WaitAsync(cancellation.Token);
        var firstSnapshot = JobMonitorMapper.Map(context.Store.GetSnapshot());
        var second = scenario.Run(queue, cancellation.Token);
        await context.Observer.Completion(second).WaitAsync(cancellation.Token);
        await context.Queues.Single(q => q.QueueId == context.Observer.Events.First(e => e.JobInfo.Id == second).JobInfo.CrossQueueId)
            .WaitAsync().WaitAsync(cancellation.Token);
        var third = scenario.Run(queue, cancellation.Token);
        await context.Observer.Completion(third).WaitAsync(cancellation.Token);

        // Assert: one job per run, event-owned placement and independent membership even with FIFO ordering edges.
        Assert.That(firstSnapshot.Jobs, Has.Count.EqualTo(1));
        Assert.That(firstSnapshot.Jobs.Single().DependsOn, Is.Empty);
        Assert.That(second, Is.Not.EqualTo(first));
        var snapshot = JobMonitorMapper.Map(context.Store.GetSnapshot());
        Assert.That(snapshot.Jobs, Has.Count.EqualTo(3));
        var starts = new List<IJobEvent>();
        foreach (var id in new[] { first, second, third })
        {
            var events = context.Observer.Events.Where(e => e.JobInfo.Id == id).ToArray();
            var start = events.Single(e => e.Status == JobEventStatus.Started);
            var enqueued = events.Single(e => e.Status == JobEventStatus.Enqueued);
            var job = snapshot.Jobs.Single(j => j.Id == id.ToString());
            starts.Add(start);
            Assert.Multiple(() =>
            {
                Assert.That(context.Calls[id], Is.EqualTo(1), "actual handler invocation, including cache hydration");
                Assert.That(events.Count(e => e.Status == JobEventStatus.RootSuccessed), Is.EqualTo(1));
                Assert.That(events.Any(e => e.Status is JobEventStatus.Failed or JobEventStatus.Canceled), Is.False);
                Assert.That(((IJobExecutionEvent)enqueued).ExecutionChannel, Is.Null);
                Assert.That(job.Channel, Is.EqualTo(((IJobExecutionEvent)start).ExecutionChannel));
                Assert.That(job.Channel, Is.GreaterThanOrEqualTo(0));
                Assert.That(job.EnqueuedAt, Is.EqualTo(new DateTimeOffset(enqueued.Timestamp)));
                Assert.That(job.ObservedAt, Is.EqualTo(new DateTimeOffset(start.Timestamp)));
                Assert.That(job.State, Is.EqualTo("completed"));
                Assert.That(job.IsRoot, Is.True);
                Assert.That(job.RootJobIds, Is.EqualTo(new[] { id.ToString() }));
                Assert.That(context.Provider.GetRequiredService<EtlExecutionDataStore>().TryGetMeasurements(id, out _), Is.True);
            });
        }
        Assert.That(((IJobExecutionEvent)starts[2]).ExecutionChannel,
            Is.EqualTo(((IJobExecutionEvent)starts[0]).ExecutionChannel));
        Assert.That(starts[2].Timestamp, Is.GreaterThanOrEqualTo(context.Observer.Events.Single(e =>
            e.JobInfo.Id == first && e.Status == JobEventStatus.RootSuccessed).Timestamp));
    }

    [Test]
    public async Task SingleJobPreset_DeliversSixIndependentJobsThroughFiniteTimers()
    {
        // Arrange
        using var context = new Context();
        var simulation = context.Provider.GetRequiredService<ISimulationService>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        simulation.Start(cancellation.Token);
        await simulation.Completion.WaitAsync(cancellation.Token);
        var delivery = simulation.GetSnapshot();
        await Task.WhenAll(delivery.SelectMany(s => s.RootIds).Select(context.Observer.Completion))
            .WaitAsync(cancellation.Token);
        await simulation.StopAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(delivery, Has.Count.EqualTo(3));
            Assert.That(delivery.Select(s => s.RootIds.Count), Is.All.EqualTo(2));
            Assert.That(delivery.Select(s => s.Ticks), Is.All.EqualTo(2));
            Assert.That(delivery.Select(s => s.SkippedTicks + s.FailedTicks), Is.All.Zero);
            Assert.That(context.Calls.Count, Is.EqualTo(6));
            Assert.That(context.Calls.Values, Is.All.EqualTo(1));
            Assert.That(JobMonitorMapper.Map(context.Store.GetSnapshot()).Jobs, Has.Count.EqualTo(6));
        });
    }

    [Test]
    public void InvalidKindOrQueueAndPreCancelledSubmission_DoNotCreateJobs()
    {
        // Arrange
        using var context = new Context();
        var scenario = context.Provider.GetRequiredService<SingleJobScenario>();

        // Act / Assert
        Assert.That(() => new SimulationScenarioSettings("invalid", AvailableQueue.FIFO,
            TimeSpan.FromSeconds(3), TimeSpan.Zero, 1, 1, 1, (SimulationScenarioKind)99),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(new SimulationScenarioSettings("legacy", AvailableQueue.FIFO,
            TimeSpan.FromSeconds(3), TimeSpan.Zero, 1, 1, 1).Kind, Is.EqualTo(SimulationScenarioKind.Etl));
        Assert.That(() => scenario.Run((AvailableQueue)99, CancellationToken.None),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => scenario.Run(AvailableQueue.Cache, new CancellationToken(true)),
            Throws.TypeOf<OperationCanceledException>());
        Assert.That(context.Calls, Is.Empty);
        Assert.That(context.Observer.Events, Is.Empty);
    }

    private sealed class Context : IDisposable
    {
        public ServiceProvider Provider { get; }
        public IQ[] Queues { get; }
        public EtlExecutionProjectionStore Store { get; }
        public RecordingObserver Observer { get; }
        public ConcurrentDictionary<Guid, int> Calls { get; } = new();
        private readonly IDisposable _subscription;

        public Context()
        {
            var api = API.Create(CoreApi.Create(), new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>
                {
                    ["ParallelQ"] = new QOptions(Guid.NewGuid(), 2, null),
                    ["CacheQ"] = new QOptions(Guid.NewGuid(), 2, null)
                });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IApi>(api);
            services.AddSingleton<IDataJobFactory>(new CountingFactory(api.DataJobFactory, Calls));
            services.AddSingleton(api.QFactory);
            services.AddSingleton(api.RetryPolicyAbstractFactory);
            services.AddSingleton(api.SystemTextSerializerFactory());
            services.AddTransient<ISystemTextJsonUniversalSerializer>(sp =>
                sp.GetRequiredService<ISystemTextJsonSerializerFactory>().Serializer(new JsonSerializerOptions()));
            services.AddSampleSingleJobSimulation();
            Provider = services.BuildServiceProvider();
            api.RegisterPayloadHandler(IngestMeasurementsPayload.HandlerId,
                new CountingHandler(Provider.GetRequiredService<IngestMeasurementsHandler>(), Calls));
            Queues = new IQ[] { Provider.GetRequiredService<IFifoQ>(), Provider.GetRequiredService<IParallelQ>(),
                Provider.GetRequiredService<ICacheQ>() };
            Store = new EtlExecutionProjectionStore(new EtlQueueCatalog(Queues.Select((q, i) =>
                new EtlQueueDescriptor((AvailableQueue)i, q.QueueId, q.QueueId.ToString(), q.Name, i, q.MaxParallelism)).ToArray()),
                NullLogger<EtlExecutionProjectionStore>.Instance, Provider.GetRequiredService<ISimulationGraphCatalog>());
            Observer = new RecordingObserver(Store);
            _subscription = Provider.GetRequiredService<EtlQueueRuntime>().Subscribe(Observer);
        }

        public void Dispose() { _subscription.Dispose(); Provider.Dispose(); }
    }

    private sealed class CountingHandler(IHandler inner, ConcurrentDictionary<Guid, int> calls) : IHandler
    {
        public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
        {
            calls.AddOrUpdate(((IngestMeasurementsPayload)payload).EtlOperationId, 1, (_, count) => count + 1);
            return inner.HandleAsync(payload, cancellationToken);
        }
    }

    // Decorate the real factory without adding a test-only package dependency.
    private sealed class CountingFactory(IDataJobFactory inner, ConcurrentDictionary<Guid, int> calls) : IDataJobFactory
    {
        public IDataJobRoot<T> DataJobRoot<T>(Guid id, T payload, IHandler handler, string name = "",
            Func<IRetryPolicy>? retryPolicy = null) where T : IPayload =>
            inner.DataJobRoot(id, payload, new CountingHandler(handler, calls), name, retryPolicy);
        public IDataJobRoot<T> DataJobRoot<T>(T payload, IHandler handler, string name = "",
            Func<IRetryPolicy>? retryPolicy = null) where T : IPayload =>
            DataJobRoot(Guid.NewGuid(), payload, handler, name, retryPolicy);
        public IDataJobRoot DataJobRoot(Guid id, string name, IPayload payload, IHandler handler,
            Func<IRetryPolicy>? retryPolicy = null) =>
            inner.DataJobRoot(id, name, payload, new CountingHandler(handler, calls), retryPolicy);
        public IDataJob<T> DataJob<T>(T payload, IHandler handler, string name = "") where T : IPayload =>
            inner.DataJob(payload, new CountingHandler(handler, calls), name);
        public IDataJob<T> DataJob<T>(Guid id, T payload, IHandler handler, string name = "") where T : IPayload =>
            inner.DataJob(id, payload, new CountingHandler(handler, calls), name);
        public IDataJob DataJob(IJobNodeRecord record, IPayload payload, IHandler handler) =>
            inner.DataJob(record, payload, new CountingHandler(handler, calls));
    }

    private sealed class RecordingObserver(EtlExecutionProjectionStore store) : IObserver<IJobEvent>
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _completed = new();
        public ConcurrentQueue<IJobEvent> Events { get; } = new();
        private TaskCompletionSource<bool> Source(Guid id) => _completed.GetOrAdd(id,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        public Task Completion(Guid id) => Source(id).Task;
        public void OnNext(IJobEvent value)
        {
            try
            {
                store.Apply(value);
                Events.Enqueue(value);
                if (value.Status == JobEventStatus.RootSuccessed) Source(value.JobInfo.Id).TrySetResult(true);
                if (value.Status is JobEventStatus.Failed or JobEventStatus.Canceled)
                    Source(value.JobInfo.Id).TrySetException(new InvalidOperationException($"Unexpected {value.Status}"));
            }
            catch (Exception error) { Source(value.JobInfo.Id).TrySetException(error); }
        }
        public void OnError(Exception error) { foreach (var source in _completed.Values) source.TrySetException(error); }
        public void OnCompleted() { }
    }
}
