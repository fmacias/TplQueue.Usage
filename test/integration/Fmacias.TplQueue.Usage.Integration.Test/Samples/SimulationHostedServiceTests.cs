using System.Reflection;
using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationHostedServiceTests
{
    [Test]
    public async Task Host_AttachesAllObserversBeforeStartAndRetainsThemUntilStopFinishes()
    {
        // Arrange
        var fifo = DispatchProxy.Create<IFifoQ, EtlQueueRuntimeCancellationCleanupTests.QueueProxy>();
        var parallel = DispatchProxy.Create<IParallelQ, EtlQueueRuntimeCancellationCleanupTests.QueueProxy>();
        var cache = DispatchProxy.Create<ICacheQ, EtlQueueRuntimeCancellationCleanupTests.QueueProxy>();
        var proxies = new[] { (object)fifo, parallel, cache }
            .Cast<EtlQueueRuntimeCancellationCleanupTests.QueueProxy>().ToArray();
        var simulation = new RecordingSimulation(() => proxies.All(queue => queue.ActiveSubscriptions == 1));
        var catalog = new EtlQueueCatalog(new[]
        {
            new EtlQueueDescriptor(AvailableQueue.FIFO, fifo.QueueId, "fifo", "FIFO", 0)
        });
        var observer = new EtlQueueObserver(new EtlExecutionProjectionStore(catalog,
            NullLogger<EtlExecutionProjectionStore>.Instance), NullLogger<EtlQueueObserver>.Instance);
        using var host = new SampleEtlDemoHostedService(simulation, fifo, parallel, cache,
            observer, NullLogger<SampleEtlDemoHostedService>.Instance);

        // Act
        await host.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StopAsync(timeout.Token);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(simulation.StartCalls, Is.EqualTo(1));
            Assert.That(simulation.AttachedAtStart, Is.True);
            Assert.That(simulation.AttachedAtStop, Is.True);
            Assert.That(simulation.Token.IsCancellationRequested, Is.True);
            Assert.That(proxies.Select(queue => queue.ActiveSubscriptions), Is.All.Zero);
        });
    }

    private sealed class RecordingSimulation(Func<bool> observersAttached) : ISimulationService
    {
        public int StartCalls { get; private set; }
        public bool AttachedAtStart { get; private set; }
        public bool AttachedAtStop { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task Completion => Task.CompletedTask;
        public void Start(CancellationToken cancellationToken)
        {
            StartCalls++;
            Token = cancellationToken;
            AttachedAtStart = observersAttached();
        }
        public Task StopAsync()
        {
            AttachedAtStop = observersAttached();
            return Task.CompletedTask;
        }
        public IReadOnlyList<ScenarioDeliverySnapshot> GetSnapshot() => Array.Empty<ScenarioDeliverySnapshot>();
        public void Dispose() { }
    }
}
