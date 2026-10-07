using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Runtime;

namespace TplQueue.Sample.Simulation.Workflows
{
    /// <summary>Shares timer ownership and bounded admission between the ETL and single-job workflows.</summary>
    internal abstract class ScheduledWorkflow : ISimulationWorkflow
    {
        private const int StartupOffsetMilliseconds = 1000;
        private const int IntervalMilliseconds = 3000;

        private readonly object _sync = new object();
        private readonly List<Guid> _roots = new List<Guid>();
        private readonly Dictionary<AvailableQueue, List<Guid>> _activeRoots =
            new Dictionary<AvailableQueue, List<Guid>>();
        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Timer? _timer;
        private CancellationToken _cancellationToken;
        private bool _started, _stopped, _busy;
        private int _ticks, _skippedTicks, _failedTicks;
        private string? _lastError;

        protected ScheduledWorkflow(IEtlQueueRuntime runtime)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            foreach (AvailableQueue queue in Enum.GetValues(typeof(AvailableQueue)))
                _activeRoots.Add(queue, new List<Guid>());
        }

        public Task Completion => _completion.Task;
        protected IEtlQueueRuntime Runtime { get; }

        /// <summary>Identifies the workflow in its aggregate delivery snapshot.</summary>
        protected abstract string ScenarioId { get; }

        /// <summary>Limits accepted roots per queue that have not yet reached a terminal outcome.</summary>
        protected abstract int MaximumActiveRuns { get; }

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
                    _timer.Change(StartupOffsetMilliseconds, IntervalMilliseconds);
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
                    _failedTicks, _lastError, _roots);
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
            var skipped = false;
            var failed = false;
            try
            {
                foreach (var queueRoots in _activeRoots)
                {
                    lock (_sync)
                    {
                        if (_stopped || _cancellationToken.IsCancellationRequested) return;
                        // This submission is admitted. StopAsync waits for its callback to finish.
                    }
                    queueRoots.Value.RemoveAll(root => !Runtime.IsActive(root));
                    if (queueRoots.Value.Count >= MaximumActiveRuns)
                    {
                        skipped = true;
                        continue;
                    }
                    try
                    {
                        var rootId = Submit(queueRoots.Key, _cancellationToken);
                        queueRoots.Value.Add(rootId);
                        lock (_sync) _roots.Add(rootId);
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
                    if (skipped) _skippedTicks++;
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
