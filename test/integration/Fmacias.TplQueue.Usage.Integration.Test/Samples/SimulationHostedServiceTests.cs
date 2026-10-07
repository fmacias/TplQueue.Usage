using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Session;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationHostedServiceTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Host_AttachesBeforePollingAndStopsWorkflowsBeforeUnsubscribing(
        bool failPolling, bool failWorkflowStart)
    {
        // Arrange
        var calls = new List<string>();
        var runtime = new RecordingRuntime(calls, failPolling);
        var first = new RecordingWorkflow("first", calls, false);
        var second = new RecordingWorkflow("second", calls, failWorkflowStart);
        var store = new EtlExecutionProjectionStore(new EtlQueueCatalog(new[]
        {
            new EtlQueueDescriptor(AvailableQueue.FIFO, Guid.NewGuid(), "fifo", "FIFO", 0)
        }), NullLogger<EtlExecutionProjectionStore>.Instance);
        var observer = new EtlQueueObserver(store, NullLogger<EtlQueueObserver>.Instance);
        var simulation = new SimulationService(new[] { first, second }, runtime);
        using var host = new SampleEtlDemoHostedService(simulation, observer,
            NullLogger<SampleEtlDemoHostedService>.Instance);

        // Act
        if (failPolling || failWorkflowStart)
            Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None));
        else
        {
            await host.StartAsync(CancellationToken.None);
            await host.StopAsync(CancellationToken.None);
        }

        // Assert
        var expected = new List<string> { "subscribe", "resume" };
        if (!failPolling) expected.AddRange(new[] { "start first", "start second" });
        expected.AddRange(new[] { "stop first", "stop second", "unsubscribe" });
        Assert.That(calls, Is.EqualTo(expected));
        Assert.That(first.Completion.IsCompletedSuccessfully, Is.True);
        Assert.That(second.Completion.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void Constructor_RejectsMissingSimulation()
    {
        // Act / Assert
        Assert.That(() => new SampleEtlDemoHostedService(null!, null!, null!),
            Throws.ArgumentNullException);
    }

    private sealed class RecordingRuntime(List<string> calls, bool failPolling) : IEtlQueueRuntime
    {
        public IQ GetQueue(AvailableQueue queue) => throw new NotSupportedException();
        public IDisposable Subscribe(IObserver<IJobEvent> observer)
        {
            calls.Add("subscribe");
            return new Subscription(calls);
        }
        public void ResumePolling()
        {
            calls.Add("resume");
            if (failPolling) throw new InvalidOperationException("Expected polling failure.");
        }

        public void Enqueue<TPayload>(AvailableQueue availableQueue, IDataJobRoot<TPayload> root, CancellationToken cancellationToken) where TPayload : IPayload
        {
            throw new NotImplementedException();
        }

        public bool IsActive(Guid rootJobId)
        {
            throw new NotImplementedException();
        }

        private sealed class Subscription(List<string> calls) : IDisposable
        {
            public void Dispose() => calls.Add("unsubscribe");
        }
    }

    private sealed class RecordingWorkflow(string name, List<string> calls, bool failStart) : ISimulationWorkflow
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public void Start(CancellationToken cancellationToken)
        {
            calls.Add("start " + name);
            if (failStart) throw new InvalidOperationException("Expected workflow startup failure.");
        }
        public Task StopAsync()
        {
            calls.Add("stop " + name);
            _completion.TrySetResult(true);
            return Completion;
        }
        public ScenarioDeliverySnapshot GetSnapshot() => new(name, 0, 0, 0, null, Array.Empty<Guid>());
    }
}
