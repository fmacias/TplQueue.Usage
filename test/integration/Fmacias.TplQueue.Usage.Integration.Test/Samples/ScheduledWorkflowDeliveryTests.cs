using System.Collections.Concurrent;
using Fmacias.TplQueue.Contracts;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Workflows;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class ScheduledWorkflowDeliveryTests
{
    [Test]
    public async Task SelectedQueues_KeepReceivingFreshRootsWhileEarlierRootsRemainActive()
    {
        // Arrange
        var runtime = new ActiveRuntime();
        var workflow = new Workflow(runtime, new[] { AvailableQueue.FIFO, AvailableQueue.Parallel });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // Act
            workflow.Start(timeout.Token);
            while (workflow.GetSnapshot().RootIds.Count < 16) await Task.Delay(10, timeout.Token);
            Assert.That(workflow.Completion.IsCompleted, Is.False);
            await workflow.StopAsync();
            var snapshot = workflow.GetSnapshot();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(workflow.Attempts.Count, Is.GreaterThanOrEqualTo(16));
                Assert.That(workflow.Attempts.Take(2), Is.EquivalentTo(new[]
                    { AvailableQueue.FIFO, AvailableQueue.Parallel }));
                Assert.That(workflow.Attempts, Does.Not.Contain(AvailableQueue.Cache));
                Assert.That(snapshot.RootIds.Distinct().Count(), Is.EqualTo(snapshot.RootIds.Count));
                Assert.That(runtime.Active.Count, Is.EqualTo(snapshot.RootIds.Count),
                    "Earlier active roots must not suppress arrivals; Stop must not drain or cancel them.");
                Assert.That(workflow.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(() => workflow.Start(CancellationToken.None), Throws.InvalidOperationException);
            });
        }
        finally { await workflow.StopAsync(); }
    }

    [Test]
    public async Task FailedSubmission_DoesNotPreventFreshSubmissionsOnLaterTicks()
    {
        // Arrange
        var runtime = new ActiveRuntime();
        var workflow = new Workflow(runtime, new[] { AvailableQueue.FIFO }, failFirst: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // Act
            workflow.Start(timeout.Token);
            while (workflow.GetSnapshot().RootIds.Count < 8) await Task.Delay(10, timeout.Token);
            await workflow.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(workflow.Attempts.Count, Is.EqualTo(workflow.GetSnapshot().RootIds.Count + 1));
                Assert.That(workflow.GetSnapshot().RootIds.Count, Is.GreaterThanOrEqualTo(8));
                Assert.That(workflow.GetSnapshot().FailedTicks, Is.EqualTo(1));
                Assert.That(workflow.GetSnapshot().LastError, Is.EqualTo("Expected submission failure."));
            });
        }
        finally { await workflow.StopAsync(); }
    }

    [Test]
    public async Task ActiveBacklog_DoesNotStopDeliveryAfterSixRoots()
    {
        // Arrange
        var runtime = new ActiveRuntime();
        var workflow = new Workflow(runtime, new[] { AvailableQueue.FIFO });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // Act: retain every root as active while new ticks continue submitting.
            workflow.Start(timeout.Token);
            while (workflow.GetSnapshot().RootIds.Count < 12) await Task.Delay(10, timeout.Token);
            Assert.That(workflow.Completion.IsCompleted, Is.False);
            await workflow.StopAsync();

            // Assert
            Assert.That(workflow.GetSnapshot().RootIds.Count, Is.GreaterThanOrEqualTo(12));
            Assert.That(runtime.Active.Count, Is.EqualTo(workflow.GetSnapshot().RootIds.Count));
            Assert.That(workflow.Attempts.Count, Is.EqualTo(workflow.GetSnapshot().RootIds.Count));
        }
        finally { await workflow.StopAsync(); }
    }

    [Test]
    public async Task StopDuringSubmission_WaitsForAcceptanceWithoutSubmittingAgain()
    {
        // Arrange
        var runtime = new ActiveRuntime();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new Workflow(runtime, new[] { AvailableQueue.FIFO }, beforeSubmit: () =>
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
            Assert.That(workflow.GetSnapshot().RootIds, Has.Count.EqualTo(1));
            Assert.That(workflow.Attempts, Has.Count.EqualTo(1));
        }
        finally { release.Set(); await workflow.StopAsync(); }
    }

    [Test]
    public async Task HostCancellationDuringSubmission_ClosesDeliveryWithoutRecordingAFailure()
    {
        // Arrange
        var runtime = new ActiveRuntime();
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new Workflow(runtime, new[] { AvailableQueue.FIFO }, beforeSubmit: () =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        try
        {
            // Act
            workflow.Start(cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            release.Set();
            await workflow.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert
            Assert.That(workflow.GetSnapshot().RootIds, Is.Empty);
            Assert.That(workflow.GetSnapshot().FailedTicks, Is.Zero);
            Assert.That(workflow.Attempts, Has.Count.EqualTo(1));
        }
        finally { release.Set(); await workflow.StopAsync(); }
    }

    [Test]
    public void InvalidSchedule_RejectsEmptyUnknownOrDuplicateQueuesAndNonpositiveIntervals()
    {
        // Arrange
        var runtime = new ActiveRuntime();
        var fifo = new[] { AvailableQueue.FIFO };

        // Act / Assert
        Assert.Multiple(() =>
        {
            Assert.That(() => new Workflow(null!, fifo), Throws.ArgumentNullException);
            Assert.That(() => new Workflow(runtime, Array.Empty<AvailableQueue>()), Throws.ArgumentException);
            Assert.That(() => new Workflow(runtime, new[] { (AvailableQueue)99 }), Throws.ArgumentException);
            Assert.That(() => new Workflow(runtime, new[] { AvailableQueue.FIFO, AvailableQueue.FIFO }),
                Throws.ArgumentException);
            Assert.That(() => new Workflow(runtime, fifo, interval: 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Workflow(runtime, fifo, interval: -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    private sealed class Workflow(ActiveRuntime runtime, IEnumerable<AvailableQueue> queues,
        bool failFirst = false, Action? beforeSubmit = null, int interval = 20)
        : ScheduledWorkflow(runtime, interval)
    {
        public ConcurrentQueue<AvailableQueue> Attempts { get; } = new();
        protected override string ScenarioId => "continuous-test";
        protected override Guid Submit(AvailableQueue queue, CancellationToken cancellationToken)
        {
            Attempts.Enqueue(queue);
            beforeSubmit?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (failFirst && Attempts.Count == 1) throw new InvalidOperationException("Expected submission failure.");
            var id = Guid.NewGuid();
            runtime.Active.TryAdd(id, true);
            return id;
        }
    }

    private sealed class ActiveRuntime : IEtlQueueRuntime
    {
        public ConcurrentDictionary<Guid, bool> Active { get; } = new();
        public bool IsActive(Guid rootJobId) => Active.ContainsKey(rootJobId);
        public IQ GetQueue(AvailableQueue queue) => throw new NotSupportedException();
        public void ResumePolling() => throw new NotSupportedException();
        public IDisposable Subscribe(IObserver<IJobEvent> observer) => throw new NotSupportedException();
        public void Enqueue<T>(AvailableQueue queue, IDataJobRoot<T> root, CancellationToken cancellationToken)
            where T : IPayload => throw new NotSupportedException();
        public void EnqueueFifo<T>(AvailableQueue queue, IDataJobRoot<T> root, CancellationToken cancellationToken)
            where T : IPayload => throw new NotSupportedException();
    }
}
