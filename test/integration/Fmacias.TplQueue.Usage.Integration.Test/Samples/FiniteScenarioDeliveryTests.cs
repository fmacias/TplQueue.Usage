using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class FiniteScenarioDeliveryTests
{
    [Test]
    public async Task FiniteTicks_SubmitExactBatchCountsAndIgnoreLateCallbacks()
    {
        // Arrange
        var timer = new ManualTimer();
        using var delivery = Create(timer, () => Guid.NewGuid(), repetitions: 3, roots: 2);
        delivery.Start(CancellationToken.None);

        // Act
        for (var i = 0; i < 3; i++)
        {
            timer.Fire();
            await delivery.PendingSubmission.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await delivery.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        timer.Fire();

        // Assert
        var snapshot = delivery.GetSnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Ticks, Is.EqualTo(3));
            Assert.That(snapshot.RootIds, Has.Count.EqualTo(6));
            Assert.That(snapshot.RootIds.Distinct().Count(), Is.EqualTo(6));
            Assert.That(snapshot.SkippedTicks, Is.Zero);
            Assert.That(timer.Stopped, Is.True);
            Assert.That(timer.StartupOffset, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(timer.Interval, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public async Task BusyTick_IsCountedAndSkippedWithoutOverlappingSubmission()
    {
        // Arrange
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var timer = new ManualTimer();
        using var delivery = Create(timer, () =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return Guid.NewGuid();
        }, repetitions: 2);
        delivery.Start(CancellationToken.None);

        try
        {
            // Act
            timer.Fire();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            timer.Fire();

            // Assert
            Assert.That(delivery.GetSnapshot().SkippedTicks, Is.EqualTo(1));
            Assert.That(delivery.Completion.IsCompleted, Is.False);
        }
        finally { release.Set(); }
        await delivery.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(delivery.GetSnapshot().RootIds, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Admission_SkipsWholeBatchUntilTerminalRootsReleaseCapacity()
    {
        // Arrange
        var active = true;
        var timer = new ManualTimer();
        using var delivery = Create(timer, Guid.NewGuid, repetitions: 3, roots: 2,
            maximum: 2, isActive: _ => active);
        delivery.Start(CancellationToken.None);

        // Act
        timer.Fire();
        await delivery.PendingSubmission.WaitAsync(TimeSpan.FromSeconds(5));
        var detached = delivery.GetSnapshot();
        timer.Fire();
        await delivery.PendingSubmission.WaitAsync(TimeSpan.FromSeconds(5));
        active = false;
        timer.Fire();
        await delivery.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(delivery.GetSnapshot().RootIds, Has.Count.EqualTo(4));
        Assert.That(delivery.GetSnapshot().SkippedTicks, Is.EqualTo(1));
        Assert.That(detached.RootIds, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SubmissionFailure_IsRecordedAndLaterTickCanContinue()
    {
        // Arrange
        var calls = 0;
        var timer = new ManualTimer();
        using var delivery = Create(timer, () => ++calls == 1
            ? throw new InvalidOperationException("submission failed") : Guid.NewGuid(), repetitions: 2);
        delivery.Start(CancellationToken.None);

        // Act
        timer.Fire();
        await delivery.PendingSubmission.WaitAsync(TimeSpan.FromSeconds(5));
        timer.Fire();
        await delivery.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(delivery.GetSnapshot().FailedTicks, Is.EqualTo(1));
        Assert.That(delivery.GetSnapshot().LastError, Does.Contain("submission failed"));
        Assert.That(delivery.GetSnapshot().RootIds, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Stop_WaitsForInFlightSubmissionAndRejectsLaterCallbacks()
    {
        // Arrange
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var timer = new ManualTimer();
        using var delivery = Create(timer, () =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return Guid.NewGuid();
        }, roots: 2);
        delivery.Start(CancellationToken.None);
        timer.Fire();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act: stop closes admission immediately; the accepted submission may finish.
        var stop = delivery.StopAsync();
        try
        {
            timer.Fire();
            Assert.That(stop.IsCompleted, Is.False);
        }
        finally { release.Set(); }
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        timer.Fire();

        // Assert
        Assert.That(delivery.GetSnapshot().RootIds, Has.Count.EqualTo(1));
        Assert.That(() => delivery.Start(CancellationToken.None), Throws.InvalidOperationException);
        delivery.Dispose();
        timer.Fire();
        Assert.That(delivery.GetSnapshot().RootIds, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CancellationBeforeTick_PreventsSubmissionWithoutRecordingFailure()
    {
        var timer = new ManualTimer();
        using var cancellation = new CancellationTokenSource();
        using var delivery = Create(timer, Guid.NewGuid);
        delivery.Start(cancellation.Token);
        cancellation.Cancel();

        timer.Fire();
        await delivery.StopAsync();

        Assert.That(delivery.GetSnapshot().RootIds, Is.Empty);
        Assert.That(delivery.GetSnapshot().FailedTicks, Is.Zero);
    }

    [Test]
    public async Task Lifecycle_IsSingleUseAndStopBeforeStartIsSafe()
    {
        var timer = new ManualTimer();
        using var delivery = Create(timer, Guid.NewGuid);
        await delivery.StopAsync();
        await delivery.StopAsync();
        Assert.That(() => delivery.Start(CancellationToken.None), Throws.InvalidOperationException);
        delivery.Dispose();
        Assert.That(() => delivery.Start(CancellationToken.None), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task RealTimer_ProducesFiniteTicksAndStops()
    {
        var settings = new SimulationScenarioSettings("timer", AvailableQueue.FIFO,
            TimeSpan.FromMilliseconds(100), TimeSpan.Zero, 1, 3, 1);
        using var delivery = new FiniteScenarioDelivery(settings, _ => Guid.NewGuid(),
            _ => false, new ScenarioTimer());

        delivery.Start(CancellationToken.None);
        await delivery.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await delivery.StopAsync();

        Assert.That(delivery.GetSnapshot().Ticks, Is.EqualTo(3));
        Assert.That(delivery.GetSnapshot().RootIds, Has.Count.EqualTo(3));
    }

    [TestCase(null, 0, 3000d, 0d, 1, 2, 2)]
    [TestCase(" ", 0, 3000d, 0d, 1, 2, 2)]
    [TestCase("id", 99, 3000d, 0d, 1, 2, 2)]
    [TestCase("id", 0, 0d, 0d, 1, 2, 2)]
    [TestCase("id", 0, -1d, 0d, 1, 2, 2)]
    [TestCase("id", 0, 2147483648d, 0d, 1, 2, 2)]
    [TestCase("id", 0, 3000d, -1d, 1, 2, 2)]
    [TestCase("id", 0, 3000d, 2147483648d, 1, 2, 2)]
    [TestCase("id", 0, 3000d, 0d, 0, 2, 2)]
    [TestCase("id", 0, 3000d, 0d, 1, 0, 2)]
    [TestCase("id", 0, 3000d, 0d, 1, 2, 0)]
    [TestCase("id", 0, 3000d, 0d, 3, 2, 2)]
    public void Settings_RejectInvalidValues(string? id, int queue, double interval,
        double offset, int roots, int repetitions, int maximum)
    {
        Assert.That(() => new SimulationScenarioSettings(id!, (AvailableQueue)queue,
            TimeSpan.FromMilliseconds(interval), TimeSpan.FromMilliseconds(offset),
            roots, repetitions, maximum), Throws.InstanceOf<ArgumentException>());
    }

    private static FiniteScenarioDelivery Create(ManualTimer timer, Func<Guid> submit,
        int repetitions = 2, int roots = 1, int maximum = 2, Func<Guid, bool>? isActive = null) =>
        new(new SimulationScenarioSettings("test", AvailableQueue.FIFO,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), roots, repetitions, maximum),
            _ => submit(), isActive ?? (_ => false), timer);

    private sealed class ManualTimer : IScenarioTimer
    {
        public event Action? Elapsed;
        public bool Stopped { get; private set; }
        public TimeSpan StartupOffset { get; private set; }
        public TimeSpan Interval { get; private set; }
        public void Start(TimeSpan startupOffset, TimeSpan interval)
        {
            StartupOffset = startupOffset;
            Interval = interval;
        }
        public void Stop() => Stopped = true;
        public void Dispose() => Stop();
        public void Fire() => Elapsed?.Invoke();
    }
}
