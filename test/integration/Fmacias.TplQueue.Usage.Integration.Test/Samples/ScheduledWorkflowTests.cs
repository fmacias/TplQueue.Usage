using System.Collections.Concurrent;
using System.Reflection;
using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Runtime;
using TplQueue.Sample.Simulation.Workflows;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class ScheduledWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task OneTick_AttemptsEveryQueueAndRetainsSuccessfulRoots(bool failFifo)
    {
        // Arrange
        using var queues = new Queues();
        using var runtime = queues.CreateRuntime();
        var workflow = new RecordingWorkflow(runtime, queue =>
        {
            if (failFifo && queue == AvailableQueue.FIFO)
                throw new InvalidOperationException("Expected FIFO failure.");
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            // Act
            workflow.Start(timeout.Token);
            while (workflow.Attempts.Count < 3) await Task.Delay(10, timeout.Token);
            await workflow.StopAsync();
            var snapshot = workflow.GetSnapshot();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(workflow.Attempts, Is.EquivalentTo(Enum.GetValues<AvailableQueue>()));
                Assert.That(snapshot.RootIds.Count, Is.EqualTo(failFifo ? 2 : 3));
                Assert.That(snapshot.RootIds.Distinct().Count(), Is.EqualTo(snapshot.RootIds.Count));
                Assert.That(snapshot.Ticks, Is.EqualTo(1));
                Assert.That(snapshot.FailedTicks, Is.EqualTo(failFifo ? 1 : 0));
                Assert.That(snapshot.SkippedTicks, Is.Zero);
                Assert.That(workflow.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(timeout.IsCancellationRequested, Is.False);
                Assert.That(() => workflow.Start(CancellationToken.None), Throws.InvalidOperationException);
            });
        }
        finally { await workflow.StopAsync(); }
    }

    [Test]
    public async Task StopDuringSubmission_WaitsForItAndPreventsRemainingQueueSubmissions()
    {
        // Arrange
        using var queues = new Queues();
        using var runtime = queues.CreateRuntime();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new RecordingWorkflow(runtime, _ =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });

        try
        {
            // Act
            workflow.Start(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stopping = workflow.StopAsync();
            Assert.That(stopping.IsCompleted, Is.False);
            release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert
            Assert.That(workflow.Attempts, Has.Count.EqualTo(1));
            Assert.That(workflow.GetSnapshot().RootIds, Has.Count.EqualTo(1));
        }
        finally { release.Set(); await workflow.StopAsync(); }
    }

    [Test]
    public async Task CancelledStart_RejectsDeliveryAndStopBeforeStartPreventsRestart()
    {
        // Arrange
        using var queues = new Queues();
        using var runtime = queues.CreateRuntime();
        var workflow = new RecordingWorkflow(runtime, _ => { });

        // Act / Assert
        Assert.That(() => workflow.Start(new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
        await workflow.StopAsync();
        Assert.That(() => workflow.Start(CancellationToken.None), Throws.InvalidOperationException);
        Assert.That(workflow.Attempts, Is.Empty);
        Assert.That(workflow.Completion.IsCompletedSuccessfully, Is.True);
    }

    private sealed class RecordingWorkflow(EtlQueueRuntime runtime, Action<AvailableQueue> submit)
        : ScheduledWorkflow(runtime)
    {
        public ConcurrentQueue<AvailableQueue> Attempts { get; } = new();
        protected override string ScenarioId => "test";
        protected override int MaximumActiveRuns => 1;
        protected override Guid Submit(AvailableQueue queue, CancellationToken cancellationToken)
        {
            Attempts.Enqueue(queue);
            submit(queue);
            return Guid.NewGuid();
        }
    }

    private sealed class Queues : ISampleFifoQ, ISampleParallelQ, ISampleCacheQ
    {
        public IFifoQ InnerFifoQ { get; } = DispatchProxy.Create<IFifoQ, QueueSubscriptionProxy>();
        public IParallelQ InnerParallelQ { get; } = DispatchProxy.Create<IParallelQ, QueueSubscriptionProxy>();
        public ICacheQ InnerCacheQ { get; } = DispatchProxy.Create<ICacheQ, QueueSubscriptionProxy>();
        public EtlQueueRuntime CreateRuntime() => new(this, this, this,
            NullLogger<EtlQueueRuntime>.Instance, new SimulationGraphCatalog());
        public void Dispose() { }
    }
}

/// <summary>Records observer attachment without running queue dispatchers in lifecycle tests.</summary>
public class QueueSubscriptionProxy : DispatchProxy
{
    private readonly Guid _queueId = Guid.NewGuid();
    public int SubscriptionCount { get; private set; }
    public int ResumeCalls { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_QueueId") return _queueId;
        if (targetMethod?.Name == "get_Name") return "Test queue";
        if (targetMethod?.Name == "get_MaxParallelism") return 2;
        if (targetMethod?.Name == "ResumePolling") { ResumeCalls++; return null; }
        if (targetMethod?.Name == "Subscribe")
        {
            SubscriptionCount++;
            return new Subscription(() => SubscriptionCount--);
        }
        throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
