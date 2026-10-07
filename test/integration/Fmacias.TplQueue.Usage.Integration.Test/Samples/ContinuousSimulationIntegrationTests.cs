using System.Collections.Concurrent;
using System.Text.Json;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Microsoft.Extensions.DependencyInjection;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Domain.Composition;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class ContinuousSimulationIntegrationTests
{
    [Test]
    public async Task StopCombinedSimulation_LetsAllAcceptedGraphsFinishWithoutCancellation()
    {
        // Arrange: real module, queues, hydration and registered handlers.
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
        services.AddSampleSingleJobSimulation();
        using var provider = services.BuildServiceProvider();
        provider.RegisterSampleEtlPayloadHandlers();
        var workflows = provider.GetServices<ISimulationWorkflow>().ToArray();
        Assert.That(workflows, Has.Length.EqualTo(2));
        Assert.That(provider.GetServices<ISimulationWorkflow>().ToArray(), Is.EqualTo(workflows));
        var simulation = provider.GetRequiredService<ISimulationService>();
        Assert.That(provider.GetRequiredService<ISimulationService>(), Is.SameAs(simulation));
        var observer = new Observer();
        using var subscription = simulation.Subscribe(observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act: stop after the first six submissions while the real handlers are still running.
        simulation.Start(timeout.Token);
        while (simulation.GetSnapshot().Any(snapshot => snapshot.RootIds.Count < 3))
            await Task.Delay(10, timeout.Token);
        await simulation.StopAsync();
        await simulation.Completion;
        var stopped = simulation.GetSnapshot();
        var roots = stopped.SelectMany(s => s.RootIds).ToArray();
        Assert.That(workflows.All(workflow => workflow.Completion.IsCompletedSuccessfully), Is.True);
        Assert.That(timeout.IsCancellationRequested, Is.False);
        await Task.WhenAll(roots.Select(observer.Completed)).WaitAsync(timeout.Token);

        // Assert: Stop closes admission without cancelling either workload's graphs.
        Assert.That(stopped.Select(s => s.ScenarioId), Is.EquivalentTo(new[]
            { "etl", "single" }));
        Assert.That(stopped.Select(s => s.RootIds.Count), Is.All.EqualTo(3));
        Assert.That(roots.Distinct().Count(), Is.EqualTo(6));
        Assert.That(stopped.Select(s => s.FailedTicks + s.SkippedTicks), Is.All.Zero);
        Assert.That(observer.Events.Any(e => e.Status is JobEventStatus.Failed or JobEventStatus.Canceled), Is.False);
        Assert.That(observer.Events.Select(e => e.JobInfo.Id).Distinct().Count(), Is.EqualTo(12));
        var completedByQueue = observer.Events.Where(e => e.Status == JobEventStatus.RootSuccessed)
            .GroupBy(e => e.JobInfo.CrossQueueId).ToArray();
        Assert.That(completedByQueue, Has.Length.EqualTo(3));
        Assert.That(completedByQueue.Select(queue => queue.Count()), Is.All.EqualTo(2));
        Assert.That(observer.Events.Where(e => e.Status == JobEventStatus.RootSuccessed)
            .Select(e => e.JobInfo.Name), Has.Exactly(3).EqualTo("Single job: ingest measurements"));
        Assert.That(observer.Events.Where(e => e.Status == JobEventStatus.RootSuccessed)
            .Select(e => e.JobInfo.Name), Has.Exactly(3).EqualTo("Load measurement summary"));
        Assert.That(simulation.GetSnapshot().SelectMany(snapshot => snapshot.RootIds), Is.EquivalentTo(roots));
    }

    private sealed class Observer : IObserver<IJobEvent>
    {
        public ConcurrentQueue<IJobEvent> Events { get; } = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _completed = new();
        private TaskCompletionSource<bool> Source(Guid id) => _completed.GetOrAdd(id,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        public Task Completed(Guid id) => Source(id).Task;
        public void OnNext(IJobEvent value)
        {
            Events.Enqueue(value);
            if (value.Status == JobEventStatus.RootSuccessed) Source(value.JobInfo.Id).TrySetResult(true);
            if (value.Status is JobEventStatus.Failed or JobEventStatus.Canceled)
                Source(value.JobInfo.Id).TrySetException(new InvalidOperationException($"Unexpected {value.Status}"));
        }
        public void OnError(Exception error) { foreach (var source in _completed.Values) source.TrySetException(error); }
        public void OnCompleted() { }
    }
}
