using System.Collections.Concurrent;
using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Domain.Composition;
using TplQueue.Sample.Domain.Handlers;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Simulation.Workflows.MultipleFifoRoots;

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

    [Test]
    public async Task MultipleFifoRoots_BuildsVisibleBacklogThenExecutesInSubmissionOrderPerQueue()
    {
        // Arrange: hold handlers while arrivals continue; observer order is not execution evidence.
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new ConcurrentQueue<(Guid Root, string Phase)>();
        using var context = new Context(multipleFifoRoots: true, release: release.Task, execution: execution);
        var simulation = context.Provider.GetRequiredService<ISimulationService>();
        var runtime = context.Provider.GetRequiredService<IEtlQueueRuntime>();
        var graphs = context.Provider.GetRequiredService<ISimulationGraphCatalog>();
        var fifoId = runtime.GetQueue(AvailableQueue.FIFO).QueueId;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            // Act: accept at least eight roots per queue while no handler can finish, then stop arrivals.
            simulation.Start(timeout.Token);
            while (simulation.GetSnapshot().Single().RootIds.Count < 24) await Task.Delay(10, timeout.Token);
            Assert.That(simulation.Completion.IsCompleted, Is.False);
            await simulation.StopAsync();
            var delivery = simulation.GetSnapshot().Single();
            // CacheQ hydration may publish observations later; inspect the serial queue's backlog here.
            while (context.Observer.Events.Where(e => e.Status == JobEventStatus.Enqueued && e.JobInfo.CrossQueueId == fifoId)
                    .Select(e => e.JobInfo.Id).Distinct().Count() < 8 ||
                context.Observer.Events.All(e => e.Status != JobEventStatus.Started || e.JobInfo.CrossQueueId != fifoId))
                await Task.Delay(10, timeout.Token);
            var backlog = JobMonitorMapper.Map(context.Store.GetSnapshot());
            var fifoBacklog = backlog.Jobs.Where(job => job.QueueId == fifoId.ToString()).ToArray();

            // Assert: the existing runtime FIFO is also the observed queue, with independent memberships.
            Assert.Multiple(() =>
            {
                Assert.That(delivery.ScenarioId, Is.EqualTo("multiple-fifo-roots"));
                Assert.That(delivery.RootIds.Count, Is.GreaterThanOrEqualTo(24));
                Assert.That(delivery.RootIds.Distinct().Count(), Is.EqualTo(delivery.RootIds.Count));
                Assert.That(delivery.FailedTicks, Is.Zero);
                Assert.That(delivery.RootIds.All(runtime.IsActive), Is.True);
                Assert.That(fifoBacklog.Length, Is.GreaterThanOrEqualTo(8));
                Assert.That(fifoBacklog.Count(job => job.Channel == null && job.State == "waiting"),
                    Is.EqualTo(fifoBacklog.Length - 1));
                Assert.That(context.Observer.Events.Any(e => e.Status == JobEventStatus.RootSuccessed), Is.False);
                foreach (var root in delivery.RootIds)
                    Assert.That(graphs.GetRootJobIds(root), Is.EqualTo(new[] { root }));
            });

            release.TrySetResult(true);
            await runtime.GetQueue(AvailableQueue.FIFO).WaitAsync().WaitAsync(timeout.Token);
            // Wait separately for observation: queue completion does not drain callbacks.
            await Task.WhenAll(delivery.RootIds.Select(context.Observer.Completion)).WaitAsync(timeout.Token);
            while (delivery.RootIds.Any(runtime.IsActive) ||
                context.Observer.Events.Where(e => e.Status == JobEventStatus.Started)
                    .Select(e => e.JobInfo.Id).Distinct().Count() < delivery.RootIds.Count)
                await Task.Delay(10, timeout.Token);
            var finished = JobMonitorMapper.Map(context.Store.GetSnapshot());

            Assert.Multiple(() =>
            {
                Assert.That(finished.Jobs, Has.Count.EqualTo(delivery.RootIds.Count));
                foreach (var queue in context.Queues)
                {
                    var queueJobs = finished.Jobs.Where(job => job.QueueId == queue.QueueId.ToString()).ToArray();
                    var queueRootIds = queueJobs.Select(job => Guid.Parse(job.Id)).ToHashSet();
                    var submissionOrder = delivery.RootIds.Where(queueRootIds.Contains);
                    Assert.That(queueJobs.Length, Is.GreaterThanOrEqualTo(8));
                    Assert.That(execution.Where(item => queueRootIds.Contains(item.Root)),
                        Is.EqualTo(submissionOrder.SelectMany(id => new[] { (id, "start"), (id, "finish") })),
                        $"FIFO handler order must be preserved independently in {queue.Name}.");
                    Assert.That(queueJobs.All(job => job.Channel >= 0 && job.Channel < queue.MaxParallelism), Is.True);
                }
                Assert.That(context.Calls.Values, Is.All.EqualTo(1));
                Assert.That(context.Calls, Has.Count.EqualTo(delivery.RootIds.Count));
                Assert.That(finished.Jobs.Where(job => job.QueueId == fifoId.ToString()).Select(job => job.Channel),
                    Is.All.EqualTo(0));
                Assert.That(finished.Jobs.Select(job => job.State), Is.All.EqualTo("completed"));
                Assert.That(finished.Jobs.All(job => job.EnqueuedAt.HasValue), Is.True);
                foreach (var job in finished.Jobs)
                    Assert.That(job.RootJobIds, Is.EqualTo(new[] { job.Id }),
                        "FIFO ordering edges must not merge composed root membership.");
                Assert.That(simulation.GetSnapshot().Single().RootIds, Is.EqualTo(delivery.RootIds));
            });
        }
        finally
        {
            release.TrySetResult(true);
            await simulation.StopAsync();
        }
    }

    [Test]
    public async Task MultipleFifoRoots_SubmitsFreshRootsThroughEnqueueFifo()
    {
        // Arrange
        using var context = new Context();
        var runtime = new FifoSubmissionRuntime();
        var workflow = new MultipleFifoRootsWorkflow(runtime,
            context.Provider.GetRequiredService<IMeasurementSource>(),
            context.Provider.GetRequiredService<ISampleJobFactory>());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // Act
            workflow.Start(timeout.Token);
            while (workflow.GetSnapshot().RootIds.Count < 6) await Task.Delay(10, timeout.Token);
            await workflow.StopAsync();

            // Assert
            Assert.That(runtime.Submissions.Take(6).Select(item => item.Queue),
                Is.EqualTo(Enum.GetValues<AvailableQueue>().Concat(Enum.GetValues<AvailableQueue>())));
            Assert.That(runtime.Submissions.Select(item => item.Root), Is.EqualTo(workflow.GetSnapshot().RootIds));
            Assert.That(runtime.Submissions.Select(item => item.Root).Distinct().Count(), Is.EqualTo(runtime.Submissions.Count));
            Assert.That(workflow.GetSnapshot().FailedTicks, Is.Zero);
        }
        finally { await workflow.StopAsync(); }
    }

    private sealed class FifoSubmissionRuntime : IEtlQueueRuntime
    {
        public ConcurrentQueue<(AvailableQueue Queue, Guid Root)> Submissions { get; } = new();
        public void EnqueueFifo<T>(AvailableQueue queue, IDataJobRoot<T> root, CancellationToken cancellationToken)
            where T : IPayload => Submissions.Enqueue((queue, root.Id));
        public void Enqueue<T>(AvailableQueue queue, IDataJobRoot<T> root, CancellationToken cancellationToken)
            where T : IPayload => throw new InvalidOperationException("UC03 must request FIFO enqueue explicitly.");
        public IQ GetQueue(AvailableQueue queue) => throw new NotSupportedException();
        public bool IsActive(Guid rootJobId) => throw new NotSupportedException();
        public void ResumePolling() => throw new NotSupportedException();
        public IDisposable Subscribe(IObserver<IJobEvent> observer) => throw new NotSupportedException();
    }

    [Test]
    public void MultipleFifoRoots_RejectsMissingDependencies()
    {
        // Arrange
        using var context = new Context();
        var runtime = context.Provider.GetRequiredService<IEtlQueueRuntime>();
        var measurements = context.Provider.GetRequiredService<IMeasurementSource>();
        var jobs = context.Provider.GetRequiredService<ISampleJobFactory>();

        // Act / Assert
        Assert.Multiple(() =>
        {
            Assert.That(() => new MultipleFifoRootsWorkflow(null!, measurements, jobs), Throws.ArgumentNullException);
            Assert.That(() => new MultipleFifoRootsWorkflow(runtime, null!, jobs), Throws.ArgumentNullException);
            Assert.That(() => new MultipleFifoRootsWorkflow(runtime, measurements, null!), Throws.ArgumentNullException);
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

        public Context(bool multipleFifoRoots = false, Task? release = null,
            ConcurrentQueue<(Guid Root, string Phase)>? execution = null)
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
            services.AddSingleton<IDataJobFactory>(new CountingFactory(api.DataJobFactory, Calls, release, execution));
            services.AddSingleton(api.QFactory);
            services.AddSingleton(api.RetryPolicyAbstractFactory);
            services.AddSingleton(api.SystemTextSerializerFactory());
            services.AddTransient<ISystemTextJsonUniversalSerializer>(sp =>
                sp.GetRequiredService<ISystemTextJsonSerializerFactory>().Serializer(new JsonSerializerOptions()));
            services.AddSampleDomain();
            if (multipleFifoRoots) services.AddSampleMultipleFifoRootsSimulation();
            else services.AddSampleSingleJobSimulation();
            Provider = services.BuildServiceProvider();
            api.RegisterPayloadHandler(IngestMeasurementsPayload.HandlerId,
                new CountingHandler(IngestMeasurementsHandler.Create(
                    Provider.GetRequiredService<IEtlExecutionDataStore>(),
                    NullLogger<IngestMeasurementsHandler>.Instance), Calls, release, execution));
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

    private sealed class CountingHandler(IHandler inner, ConcurrentDictionary<Guid, int> calls,
        Task? release = null, ConcurrentQueue<(Guid Root, string Phase)>? execution = null) : IHandler
    {
        public async Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
        {
            var id = ((IngestMeasurementsPayload)payload).EtlOperationId;
            calls.AddOrUpdate(id, 1, (_, count) => count + 1);
            execution?.Enqueue((id, "start"));
            if (release != null) await release.WaitAsync(cancellationToken);
            await inner.HandleAsync(payload, cancellationToken);
            execution?.Enqueue((id, "finish"));
        }
    }

    // Decorate the real factory without adding a test-only package dependency.
    private sealed class CountingFactory(IDataJobFactory inner, ConcurrentDictionary<Guid, int> calls,
        Task? release = null, ConcurrentQueue<(Guid Root, string Phase)>? execution = null) : IDataJobFactory
    {
        public IDataJobRoot<T> DataJobRoot<T>(Guid id, T payload, IHandler handler, string name = "",
            Func<IRetryPolicy>? retryPolicy = null) where T : IPayload =>
            inner.DataJobRoot(id, payload, new CountingHandler(handler, calls, release, execution), name, retryPolicy);
        public IDataJobRoot<T> DataJobRoot<T>(T payload, IHandler handler, string name = "",
            Func<IRetryPolicy>? retryPolicy = null) where T : IPayload =>
            DataJobRoot(Guid.NewGuid(), payload, handler, name, retryPolicy);
        public IDataJobRoot DataJobRoot(Guid id, string name, IPayload payload, IHandler handler,
            Func<IRetryPolicy>? retryPolicy = null) =>
            inner.DataJobRoot(id, name, payload, new CountingHandler(handler, calls, release, execution), retryPolicy);
        public IDataJob<T> DataJob<T>(T payload, IHandler handler, string name = "") where T : IPayload =>
            inner.DataJob(payload, new CountingHandler(handler, calls, release, execution), name);
        public IDataJob<T> DataJob<T>(Guid id, T payload, IHandler handler, string name = "") where T : IPayload =>
            inner.DataJob(id, payload, new CountingHandler(handler, calls, release, execution), name);
        public IDataJob DataJob(IJobNodeRecord record, IPayload payload, IHandler handler) =>
            inner.DataJob(record, payload, new CountingHandler(handler, calls, release, execution));
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
