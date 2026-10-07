using System.Collections.Concurrent;
using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Domain.Composition;
using TplQueue.Sample.Simulation.Runtime;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationQueueIntegrationTests
{
    [Test]
    public async Task DefaultSimulation_ExecutesSixRootsAndEighteenJobsAcrossRealQueues()
    {
        // Arrange: the same real queue/module composition as the host, with observers attached first.
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
        var simulation = provider.GetServices<ISimulationWorkflow>().Single();
        var runtime = provider.GetRequiredService<IEtlQueueRuntime>();
        var observer = new RecordingObserver();
        using var subscription = runtime.Subscribe(observer);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Assert.That(simulation.GetSnapshot().RootIds, Is.Empty);

        // Act
        runtime.ResumePolling();
        simulation.Start(cancellation.Token);
        Assert.That(() => simulation.Start(cancellation.Token), Throws.InvalidOperationException);
        while (simulation.GetSnapshot().RootIds.Count < 6)
            await Task.Delay(10, cancellation.Token);
        await simulation.StopAsync();
        await simulation.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        await observer.Completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await simulation.StopAsync();
        var snapshot = simulation.GetSnapshot();

        // Assert: terminal cleanup also releases scenario admission, including cache-rehydrated roots.
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ScenarioId, Is.EqualTo("etl"));
            Assert.That(snapshot.Ticks, Is.EqualTo(2));
            Assert.That(snapshot.RootIds, Has.Count.EqualTo(6));
            Assert.That(snapshot.FailedTicks + snapshot.SkippedTicks, Is.Zero);
            Assert.That(snapshot.RootIds.Distinct().Count(), Is.EqualTo(6));
            Assert.That(observer.Jobs.Count, Is.EqualTo(18));
            Assert.That(observer.Roots.Count, Is.EqualTo(6));
            Assert.That(observer.Errors, Is.Empty);
        });
        // Runtime and test observers dispatch independently; wait for cleanup rather than assume their order.
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (snapshot.RootIds.Any(runtime.IsActive))
            await Task.Delay(10, cleanupTimeout.Token);
        Assert.That(() => simulation.Start(CancellationToken.None), Throws.InvalidOperationException);
    }

    private sealed class RecordingObserver : IObserver<IJobEvent>
    {
        public ConcurrentDictionary<Guid, bool> Jobs { get; } = new();
        public ConcurrentDictionary<Guid, bool> Roots { get; } = new();
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnNext(IJobEvent value)
        {
            Jobs.TryAdd(value.JobInfo.Id, true);
            if (value.Exception != null) Errors.Enqueue(value.Exception);
            if (value.Status == JobEventStatus.RootSuccessed) Roots.TryAdd(value.JobInfo.Id, true);
            if (Roots.Count == 6) Completed.TrySetResult(true);
        }
        public void OnError(Exception error) => Errors.Enqueue(error);
        public void OnCompleted() { }
    }
}
