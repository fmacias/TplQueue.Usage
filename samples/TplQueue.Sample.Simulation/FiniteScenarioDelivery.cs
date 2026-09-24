using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation
{
    /// <summary>Serializes bounded submissions while counting busy/capacity ticks without catch-up.</summary>
    internal sealed class FiniteScenarioDelivery : IDisposable
    {
        private readonly object _sync = new object();
        private readonly SimulationScenarioSettings _settings;
        private readonly Func<CancellationToken, Guid> _submit;
        private readonly Func<Guid, bool> _isActive;
        private readonly IScenarioTimer _timer;
        private readonly List<Guid> _roots = new List<Guid>();
        private readonly List<Guid> _activeRoots = new List<Guid>();
        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _pendingSubmission = Task.CompletedTask;
        private CancellationToken _cancellationToken;
        private bool _started, _stopped, _disposed, _busy;
        private int _ticks, _skippedTicks, _failedTicks;
        private string? _lastError;

        public FiniteScenarioDelivery(SimulationScenarioSettings settings,
            Func<CancellationToken, Guid> submit, Func<Guid, bool> isActive, IScenarioTimer timer)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _submit = submit ?? throw new ArgumentNullException(nameof(submit));
            _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
            _timer = timer ?? throw new ArgumentNullException(nameof(timer));
            _timer.Elapsed += OnTick;
        }

        public Task Completion => _completion.Task;
        internal Task PendingSubmission { get { lock (_sync) return _pendingSubmission; } }

        public void Start(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FiniteScenarioDelivery));
                if (_started || _stopped) throw new InvalidOperationException("A finite scenario can start only once.");
                cancellationToken.ThrowIfCancellationRequested();
                _started = true;
                _cancellationToken = cancellationToken;
                try { _timer.Start(_settings.StartupOffset, _settings.Interval); }
                catch
                {
                    _stopped = true;
                    _timer.Stop();
                    CompleteIfFinished();
                    throw;
                }
            }
        }

        /// <summary>Closes admission immediately; a previously admitted submission may finish before this task completes.</summary>
        public Task StopAsync()
        {
            lock (_sync)
            {
                _stopped = true;
                if (!_disposed) _timer.Stop();
                CompleteIfFinished();
                return _pendingSubmission;
            }
        }

        public ScenarioDeliverySnapshot GetSnapshot()
        {
            lock (_sync)
                return new ScenarioDeliverySnapshot(_settings.ScenarioId, _ticks, _skippedTicks,
                    _failedTicks, _lastError, _roots);
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Elapsed -= OnTick;
                _timer.Dispose();
            }
        }

        private void OnTick()
        {
            lock (_sync)
            {
                if (!_started || _stopped || _disposed || _ticks == _settings.Repetitions) return;
                if (_cancellationToken.IsCancellationRequested)
                {
                    _stopped = true;
                    _timer.Stop();
                    CompleteIfFinished();
                    return;
                }

                _ticks++;
                if (_ticks == _settings.Repetitions) _timer.Stop();
                if (_busy)
                {
                    _skippedTicks++;
                    return;
                }

                _busy = true;
                // Store every asynchronous submission. The worker observes all scenario failures.
                _pendingSubmission = Task.Run(SubmitBatch);
            }
        }

        private void SubmitBatch()
        {
            try
            {
                // Only this worker touches the active set. Observer cleanup may release capacity concurrently.
                _activeRoots.RemoveAll(root => !_isActive(root));
                if (_activeRoots.Count > _settings.MaximumActiveRuns - _settings.RootsPerTick)
                {
                    lock (_sync) _skippedTicks++;
                    return;
                }

                for (var index = 0; index < _settings.RootsPerTick; index++)
                {
                    lock (_sync)
                    {
                        if (_stopped || _cancellationToken.IsCancellationRequested) return;
                        // This root is now admitted. Stop waits for this worker before returning.
                    }
                    var rootId = _submit(_cancellationToken);
                    _activeRoots.Add(rootId);
                    lock (_sync) _roots.Add(rootId);
                }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                lock (_sync)
                {
                    _stopped = true;
                    _timer.Stop();
                }
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _failedTicks++;
                    _lastError = exception.Message;
                }
            }
            finally
            {
                lock (_sync)
                {
                    _busy = false;
                    CompleteIfFinished();
                }
            }
        }

        private void CompleteIfFinished()
        {
            if (!_busy && (_stopped || _ticks == _settings.Repetitions))
                _completion.TrySetResult(true);
        }
    }
}
