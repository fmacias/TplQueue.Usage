using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Etl.Contracts;

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
        /// <param name="intervalMilliseconds">Positive interval between arrival attempts.</param>
        /// 
        protected ScheduledWorkflow(IEtlQueueRuntime runtime, int intervalMilliseconds = 3000)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (intervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
            _intervalMilliseconds = intervalMilliseconds;
            _queues = new List<AvailableQueue>() { AvailableQueue.Cache, AvailableQueue.FIFO, AvailableQueue.Parallel };
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
                _timer = new Timer(OnTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _timer.Change(StartupOffsetMilliseconds, _intervalMilliseconds);

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
            try
            {
                foreach (var queue in _queues)
                {
                    var rootId = Submit(queue, _cancellationToken);
                    lock (_sync) _acceptedRootIds.Add(rootId);
                }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                lock (_sync) _stopped = true;
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
