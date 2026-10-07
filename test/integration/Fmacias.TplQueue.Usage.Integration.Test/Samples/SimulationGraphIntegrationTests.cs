using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Extensions;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Domain.Composition;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Simulation.Runtime;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationGraphIntegrationTests
{
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
        var store = new EtlExecutionProjectionStore(new EtlQueueCatalog(queues.Select((q, i) =>
            new EtlQueueDescriptor((AvailableQueue)i, q.QueueId, q.QueueId.ToString(), q.Name, i, q.MaxParallelism)).ToArray()),
            NullLogger<EtlExecutionProjectionStore>.Instance, graphs);
        var payload = IngestMeasurementsPayload.Create(new[] {
            new LegacyMeasurement("test", 20m, TemperatureUnit.Celsius, DateTime.UtcNow) }, Guid.NewGuid());
        var handler = new GatedHandler(outcome);
        api.RegisterPayloadHandler(payload.HandlerKey, handler);
        var child = api.DataJobFactory.DataJob(payload, handler, "prerequisite");
        var root = api.DataJobFactory.DataJobRoot(payload, handler, "root", () => NoRetryPolicy.Create());
        child.Then(root);
        var observer = new ProjectionObserver(store, child.Id, root.Id);
        using var subscription = runtime.Subscribe(observer);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            // Act: inspect running identity before releasing the prerequisite.
            runtime.ResumePolling();
            runtime.Enqueue(queue, root, cancellation.Token);
            await observer.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var running = JobMonitorMapper.Map(store.GetSnapshot()).Jobs.Single(j => j.Id == child.Id.ToString());
            Assert.That(running.RootJobIds, Is.EqualTo(new[] { root.Id.ToString() }));
            Assert.That(running.State, Is.EqualTo("running"));
            if (outcome == "cancelled") cancellation.Cancel();
            else handler.Release.TrySetResult(true);
            await observer.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert: IDs survive hydrated objects and never rely on RootSuccessed for failures/cancellation.
            var snapshot = JobMonitorMapper.Map(store.GetSnapshot());
            Assert.That(snapshot.Jobs, Has.Count.EqualTo(2));
            Assert.That(snapshot.Jobs.Select(j => j.RootJobIds), Is.All.EqualTo(new[] { root.Id.ToString() }));
            Assert.That(snapshot.Jobs.Single(j => j.IsRoot).State, Is.EqualTo(outcome));
            Assert.That(snapshot.Jobs.Single(j => j.IsRoot).DependsOn, Is.EqualTo(new[] { child.Id.ToString() }));
            Assert.That(snapshot.Jobs.Single(j => j.Id == child.Id.ToString()).Channel, Is.EqualTo(running.Channel));
        }
        finally { handler.Release.TrySetResult(true); cancellation.Cancel(); }
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

    private sealed class ProjectionObserver(EtlExecutionProjectionStore store, Guid childId, Guid rootId) : IObserver<IJobEvent>
    {
        public TaskCompletionSource<bool> Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnNext(IJobEvent value)
        {
            try
            {
                store.Apply(value);
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
