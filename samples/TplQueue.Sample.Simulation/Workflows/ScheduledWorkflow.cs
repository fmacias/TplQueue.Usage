using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Runtime;

namespace TplQueue.Sample.Simulation.Workflows
{
    /// <summary>Schedules fresh root submissions continuously until stopped or cancelled.</summary>
    internal abstract class ScheduledWorkflow : ISimulationWorkflow
    {
        private const int StartupOffsetMilliseconds = 1000;
        private readonly int _intervalMilliseconds;

        private readonly object _sync = new object();
        // Delivery history contains identities only; job instances are created by Submit on each arrival.
        private readonly List<Guid> _acceptedRootIds = new List<Guid>();
        private readonly List<AvailableQueue> _queues = new List<AvailableQueue>();
        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Timer? _timer;
        private CancellationToken _cancellationToken;
        private bool _started, _stopped, _busy;
        private int _ticks, _skippedTicks, _failedTicks;
        private string? _lastError;

        /// <summary>Configures the selected queues and interval for continuous arrivals.</summary>
        /// <param name="runtime">The shared runtime used to enqueue newly created roots.</param>
        /// <param name="queues">Distinct target queues; null selects all available queues.</param>
        /// <param name="intervalMilliseconds">Positive interval between arrival attempts.</param>
        protected ScheduledWorkflow(IEtlQueueRuntime runtime, IEnumerable<AvailableQueue>? queues = null,
            int intervalMilliseconds = 3000)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (intervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
            _intervalMilliseconds = intervalMilliseconds;
            foreach (var queue in queues ?? (AvailableQueue[])Enum.GetValues(typeof(AvailableQueue)))
            {
                if (!Enum.IsDefined(typeof(AvailableQueue), queue) || _queues.Contains(queue))
                    throw new ArgumentException("Queues must be defined and distinct.", nameof(queues));
                _queues.Add(queue);
            }
            if (_queues.Count == 0) throw new ArgumentException("At least one queue is required.", nameof(queues));
        }

        public Task Completion => _completion.Task;
        protected IEtlQueueRuntime Runtime { get; }

        /// <summary>Identifies the workflow in its aggregate delivery snapshot.</summary>
        protected abstract string ScenarioId { get; }

        public void Start(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_started || _stopped) throw new InvalidOperationException("A workflow can start only once.");
                cancellationToken.ThrowIfCancellationRequested();
                _started = true;
                _cancellationToken = cancellationToken;
                try
                {
                    // Assign a disabled timer first: even a zero-offset callback sees fully initialized state.
                    _timer = new Timer(OnTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _timer.Change(StartupOffsetMilliseconds, _intervalMilliseconds);
                }
                catch
                {
                    _stopped = true;
                    CompleteIfFinished();
                    throw;
                }
            }
        }

        public Task StopAsync()
        {
            lock (_sync)
            {
                _stopped = true;
                ReleaseTimer();
                CompleteIfFinished();
                return Completion;
            }
        }

        public ScenarioDeliverySnapshot GetSnapshot()
        {
            lock (_sync)
                return new ScenarioDeliverySnapshot(ScenarioId, _ticks, _skippedTicks,
                    _failedTicks, _lastError, _acceptedRootIds);
        }

        /// <summary>Creates a distinct root for the specified queue using the workflow's factory method.</summary>
        protected abstract Guid Submit(AvailableQueue queue, CancellationToken cancellationToken);

        private void OnTick(object? state)
        {
            lock (_sync)
            {
                // Dispose does not retract queued callbacks. This gate also prevents overlapping submissions.
                if (_stopped) return;
                if (_cancellationToken.IsCancellationRequested)
                {
                    _stopped = true;
                    ReleaseTimer();
                    CompleteIfFinished();
                    return;
                }
                _ticks++;
                if (_busy)
                {
                    _skippedTicks++;
                    return;
                }
                _busy = true;
            }

            // The timer already runs on a thread-pool thread; no extra Task.Run or event adapter is needed.
            var failed = false;
            try
            {
                foreach (var queue in _queues)
                {
                    lock (_sync)
                    {
                        if (_stopped || _cancellationToken.IsCancellationRequested) return;
                        // This submission is admitted. StopAsync waits for its callback to finish.
                    }
                    try
                    {
                        var rootId = Submit(queue, _cancellationToken);
                        lock (_sync) _acceptedRootIds.Add(rootId);
                    }
                    catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // A failed queue must not prevent delivery to the remaining queues.
                        failed = true;
                        lock (_sync) _lastError = exception.Message;
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                lock (_sync) _stopped = true;
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    failed = true;
                    _lastError = exception.Message;
                }
            }
            finally
            {
                lock (_sync)
                {
                    if (failed) _failedTicks++;
                    _busy = false;
                    if (_cancellationToken.IsCancellationRequested) _stopped = true;
                    CompleteIfFinished();
                }
            }
        }

        private void CompleteIfFinished()
        {
            if (_busy || !_stopped) return;
            ReleaseTimer();
            _completion.TrySetResult(true);
        }

        private void ReleaseTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
