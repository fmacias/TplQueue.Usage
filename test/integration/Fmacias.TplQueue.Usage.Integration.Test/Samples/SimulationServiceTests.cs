using Fmacias.TplQueue.Contracts;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Session;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationServiceTests
{
    [Test]
    public void Constructor_RejectsMissingDependencies()
    {
        // Arrange
        var runtime = new RecordingRuntime();
        var workflow = new RecordingWorkflow();

        // Act / Assert
        Assert.That(() => new SimulationService(null!, runtime), Throws.ArgumentNullException);
        Assert.That(() => new SimulationService(Array.Empty<ISimulationWorkflow>(), runtime), Throws.ArgumentException);
        Assert.That(() => new SimulationService(new ISimulationWorkflow[] { null! }, runtime), Throws.ArgumentException);
        Assert.That(() => new SimulationService(new[] { workflow }, null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task StartAndStop_ControlAllWorkflowsAndShareCompletion()
    {
        // Arrange
        var runtime = new RecordingRuntime();
        var first = new RecordingWorkflow();
        var second = new RecordingWorkflow();
        var service = new SimulationService(new[] { first, second }, runtime);
        using var cancellation = new CancellationTokenSource();

        // Act
        service.Start(cancellation.Token);
        Assert.That(() => service.Start(cancellation.Token), Throws.InvalidOperationException);
        Assert.That(service.Completion.IsCompleted, Is.False);
        var stopping = service.StopAsync();
        Assert.That(service.StopAsync(), Is.SameAs(stopping));
        await stopping;
        await service.Completion;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ResumeCalls, Is.EqualTo(1));
            foreach (var workflow in new[] { first, second })
            {
                Assert.That(workflow.StartCalls, Is.EqualTo(1));
                Assert.That(workflow.StopCalls, Is.EqualTo(1));
                Assert.That(workflow.Token, Is.EqualTo(cancellation.Token));
            }
            Assert.That(cancellation.IsCancellationRequested, Is.False);
            Assert.That(service.GetSnapshot(), Has.Count.EqualTo(2));
            Assert.That(() => service.Start(CancellationToken.None), Throws.InvalidOperationException);
        });
    }

    [Test]
    public async Task FailedStart_StopsAllWorkflowsIncludingThoseNotYetStarted()
    {
        // Arrange
        var first = new RecordingWorkflow();
        var second = new RecordingWorkflow { FailStart = true };
        var third = new RecordingWorkflow();
        var service = new SimulationService(new[] { first, second, third }, new RecordingRuntime());

        // Act
        Assert.That(() => service.Start(CancellationToken.None), Throws.InvalidOperationException);
        await service.StopAsync();

        // Assert
        Assert.That(new[] { first, second, third }.Select(workflow => workflow.StopCalls), Is.All.EqualTo(1));
        Assert.That(third.StartCalls, Is.Zero);
        Assert.That(() => service.Start(CancellationToken.None), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Stop_StillStopsOtherWorkflowsWhenOneThrowsSynchronously()
    {
        // Arrange
        var first = new RecordingWorkflow { FailStop = true };
        var second = new RecordingWorkflow();
        var service = new SimulationService(new[] { first, second }, new RecordingRuntime());

        // Act / Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync());
        await second.Completion;
        Assert.That(first.StopCalls, Is.EqualTo(1));
        Assert.That(second.StopCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelledStart_DoesNotStartPollingAndStopBeforeStartPreventsRestart()
    {
        // Arrange
        var runtime = new RecordingRuntime();
        var workflow = new RecordingWorkflow();
        var service = new SimulationService(new[] { workflow }, runtime);

        // Act / Assert
        Assert.That(() => service.Start(new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(runtime.ResumeCalls, Is.Zero);
        Assert.That(workflow.StartCalls, Is.Zero);
        await service.StopAsync();
        Assert.That(() => service.Start(CancellationToken.None), Throws.InvalidOperationException);
    }

    private sealed class RecordingRuntime : IEtlQueueRuntime
    {
        public int ResumeCalls { get; private set; }
        public void ResumePolling() => ResumeCalls++;
        public IQ GetQueue(AvailableQueue queue) => throw new NotSupportedException();
        public IDisposable Subscribe(IObserver<IJobEvent> observer) => throw new NotSupportedException();

        public void Enqueue<TPayload>(AvailableQueue availableQueue, IDataJobRoot<TPayload> root, CancellationToken cancellationToken) where TPayload : IPayload
        {
            throw new NotImplementedException();
        }

        public bool IsActive(Guid rootJobId)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class RecordingWorkflow : ISimulationWorkflow
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailStart { get; init; }
        public bool FailStop { get; init; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task Completion => _completion.Task;
        public void Start(CancellationToken cancellationToken)
        {
            StartCalls++;
            Token = cancellationToken;
            if (FailStart) throw new InvalidOperationException("Expected startup failure.");
        }
        public Task StopAsync()
        {
            StopCalls++;
            if (FailStop) throw new InvalidOperationException("Expected stop failure.");
            _completion.TrySetResult(true);
            return Completion;
        }
        public ScenarioDeliverySnapshot GetSnapshot() => new("test", 0, 0, 0, null, Array.Empty<Guid>());
    }
}
