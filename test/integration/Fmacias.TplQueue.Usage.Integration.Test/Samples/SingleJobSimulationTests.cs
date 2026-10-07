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
using TplQueue.Sample.Domain.Composition;
using TplQueue.Sample.Domain.Handlers;
using TplQueue.Sample.Domain.Payloads;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SingleJobSimulationTests
{
    [Test]
    public void DomainQueues_AreSharedWithTheRuntimeAndRetainConfiguredNamesAndCapacity()
    {
        // Arrange / Act
        using var context = new Context();
        var provider = context.Provider;
        var runtime = provider.GetRequiredService<IEtlQueueRuntime>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GetQueue(AvailableQueue.FIFO), Is.SameAs(context.Queues[0]));
            Assert.That(runtime.GetQueue(AvailableQueue.Parallel), Is.SameAs(context.Queues[1]));
            Assert.That(runtime.GetQueue(AvailableQueue.Cache), Is.SameAs(context.Queues[2]));
            Assert.That(runtime.GetQueue(AvailableQueue.Parallel).Name, Is.EqualTo("ParallelQ"));
            Assert.That(runtime.GetQueue(AvailableQueue.Parallel).MaxParallelism, Is.EqualTo(2));
            Assert.That(runtime.GetQueue(AvailableQueue.Cache).MaxParallelism, Is.EqualTo(2));
            Assert.That(context.Queues.Select(queue => queue.QueueId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(provider.GetRequiredService<ISampleCache>().Cache,
                Is.Not.SameAs(provider.GetRequiredService<ISampleCache>().Cache));
        });
    }

    [Test]
    public async Task SingleJobWorkflows_DeliverIndependentJobsUntilStopped()
    {
        // Arrange
        using var context = new Context();
        var simulation = context.Provider.GetServices<ISimulationWorkflow>().Single();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        simulation.Start(cancellation.Token);
        while (simulation.GetSnapshot().RootIds.Count < 6)
            await Task.Delay(10, cancellation.Token);
        await simulation.StopAsync();
        await simulation.Completion.WaitAsync(cancellation.Token);
        var delivery = simulation.GetSnapshot();
        await Task.WhenAll(delivery.RootIds.Select(context.Observer.Completion))
            .WaitAsync(cancellation.Token);
        await simulation.StopAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(delivery.ScenarioId, Is.EqualTo("single"));
            Assert.That(delivery.RootIds, Has.Count.EqualTo(6));
            Assert.That(delivery.RootIds.Distinct().Count(), Is.EqualTo(6));
            Assert.That(delivery.Ticks, Is.EqualTo(2));
            Assert.That(delivery.SkippedTicks + delivery.FailedTicks, Is.Zero);
            Assert.That(context.Calls.Count, Is.EqualTo(6));
            Assert.That(context.Calls.Values, Is.All.EqualTo(1));
            Assert.That(JobMonitorMapper.Map(context.Store.GetSnapshot()).Jobs, Has.Count.EqualTo(6));
        });
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
            services.AddSampleDomain();
            services.AddSampleSingleJobSimulation();
            Provider = services.BuildServiceProvider();
            api.RegisterPayloadHandler(IngestMeasurementsPayload.HandlerId,
                new CountingHandler(IngestMeasurementsHandler.Create(
                    Provider.GetRequiredService<IEtlExecutionDataStore>(),
                    NullLogger<IngestMeasurementsHandler>.Instance), Calls));
            var runtime = Provider.GetRequiredService<IEtlQueueRuntime>();
            Queues = Enum.GetValues<AvailableQueue>().Select(runtime.GetQueue).ToArray();
            Store = new EtlExecutionProjectionStore(new EtlQueueCatalog(Queues.Select((q, i) =>
                new EtlQueueDescriptor((AvailableQueue)i, q.QueueId, q.QueueId.ToString(), q.Name, i, q.MaxParallelism)).ToArray()),
                NullLogger<EtlExecutionProjectionStore>.Instance, Provider.GetRequiredService<ISimulationGraphCatalog>());
            Observer = new RecordingObserver(Store);
            _subscription = runtime.Subscribe(Observer);
            runtime.ResumePolling();
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
