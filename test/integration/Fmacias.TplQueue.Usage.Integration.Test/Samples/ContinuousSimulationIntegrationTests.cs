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
    [TestCase(false)]
    [TestCase(true)]
    public async Task StopCombinedSimulation_LetsAllAcceptedGraphsFinishWithoutCancellation(bool includeMultipleFifoRoots)
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
        if (includeMultipleFifoRoots) services.AddSampleMultipleFifoRootsSimulation();
        using var provider = services.BuildServiceProvider();
        provider.RegisterSampleEtlPayloadHandlers();
        var workflows = provider.GetServices<ISimulationWorkflow>().ToArray();
        Assert.That(workflows, Has.Length.EqualTo(includeMultipleFifoRoots ? 3 : 2));
        Assert.That(provider.GetServices<ISimulationWorkflow>().ToArray(), Is.EqualTo(workflows));
        var simulation = provider.GetRequiredService<ISimulationService>();
        Assert.That(provider.GetRequiredService<ISimulationService>(), Is.SameAs(simulation));
        var observer = new Observer();
        using var subscription = simulation.Subscribe(observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act: let UC03 continue past the former six-root limit, then explicitly stop every workflow.
        simulation.Start(timeout.Token);
        try
        {
            while (simulation.GetSnapshot().Any(snapshot => snapshot.RootIds.Count < 3))
                await Task.Delay(10, timeout.Token);
            if (includeMultipleFifoRoots)
            {
                while (simulation.GetSnapshot().Single(snapshot => snapshot.ScenarioId == "multiple-fifo-roots")
                    .RootIds.Count < 24) await Task.Delay(10, timeout.Token);
                Assert.That(simulation.Completion.IsCompleted, Is.False,
                    "Every workflow must keep delivering until the session is explicitly stopped.");
                Assert.That(workflows.All(workflow => !workflow.Completion.IsCompleted), Is.True);
            }
        }
        finally { await simulation.StopAsync(); }
        await simulation.Completion;
        var stopped = simulation.GetSnapshot();
        var roots = stopped.SelectMany(s => s.RootIds).ToArray();
        Assert.That(workflows.All(workflow => workflow.Completion.IsCompletedSuccessfully), Is.True);
        Assert.That(timeout.IsCancellationRequested, Is.False);
        await Task.WhenAll(roots.Select(observer.Completed)).WaitAsync(timeout.Token);

        // Assert: Stop closes admission without cancelling any workload's graphs.
        Assert.That(stopped.Select(s => s.ScenarioId), Is.EquivalentTo(includeMultipleFifoRoots
            ? new[] { "etl", "single", "multiple-fifo-roots" } : new[] { "etl", "single" }));
        var etlRoots = stopped.Single(s => s.ScenarioId == "etl").RootIds.Count;
        var singleRoots = stopped.Single(s => s.ScenarioId == "single").RootIds.Count;
        var fifoRoots = includeMultipleFifoRoots
            ? stopped.Single(s => s.ScenarioId == "multiple-fifo-roots").RootIds : Array.Empty<Guid>();
        Assert.That(etlRoots, Is.GreaterThanOrEqualTo(3));
        Assert.That(singleRoots, Is.GreaterThanOrEqualTo(3));
        if (includeMultipleFifoRoots)
            Assert.That(fifoRoots.Count, Is.GreaterThanOrEqualTo(24));
        else
        {
            Assert.That(etlRoots, Is.EqualTo(3));
            Assert.That(singleRoots, Is.EqualTo(3));
            Assert.That(stopped.Select(s => s.SkippedTicks), Is.All.Zero);
        }
        Assert.That(roots.Distinct().Count(), Is.EqualTo(roots.Length));
        Assert.That(stopped.Select(s => s.FailedTicks), Is.All.Zero);
        Assert.That(observer.Events.Any(e => e.Status is JobEventStatus.Failed or JobEventStatus.Canceled), Is.False);
        Assert.That(observer.Events.Select(e => e.JobInfo.Id).Distinct().Count(),
            Is.EqualTo(etlRoots * 3 + singleRoots + fifoRoots.Count));
        var completedByQueue = observer.Events.Where(e => e.Status == JobEventStatus.RootSuccessed)
            .GroupBy(e => e.JobInfo.CrossQueueId).ToArray();
        Assert.That(completedByQueue, Has.Length.EqualTo(3));
        if (includeMultipleFifoRoots)
        {
            var runtime = provider.GetRequiredService<IEtlQueueRuntime>();
            var uc03ByQueue = observer.Events
                .Where(e => e.Status == JobEventStatus.RootSuccessed && fifoRoots.Contains(e.JobInfo.Id))
                .GroupBy(e => e.JobInfo.CrossQueueId).ToArray();
            Assert.That(uc03ByQueue.Select(group => group.Key), Is.EquivalentTo(
                Enum.GetValues<AvailableQueue>().Select(queue => runtime.GetQueue(queue).QueueId)));
            Assert.That(uc03ByQueue.Select(group => group.Select(e => e.JobInfo.Id).Distinct().Count()),
                Is.All.GreaterThanOrEqualTo(8));
        }
        if (!includeMultipleFifoRoots)
            Assert.That(completedByQueue.Select(queue => queue.Count()), Is.All.EqualTo(2));
        Assert.That(observer.Events.Where(e => e.Status == JobEventStatus.RootSuccessed)
            .Select(e => e.JobInfo.Name), Has.Exactly(singleRoots + fifoRoots.Count).EqualTo("Single job: ingest measurements"));
        Assert.That(observer.Events.Where(e => e.Status == JobEventStatus.RootSuccessed)
            .Select(e => e.JobInfo.Name), Has.Exactly(etlRoots).EqualTo("Load measurement summary"));
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
