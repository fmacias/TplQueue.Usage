using System;
using System.Timers;

namespace TplQueue.Sample.Simulation
{
    internal interface IScenarioTimer : IDisposable
    {
        event Action? Elapsed;
        void Start(TimeSpan startupOffset, TimeSpan interval);
        void Stop();
    }

    /// <summary>One scenario's wall-clock timer. Callbacks never perform submissions themselves.</summary>
    internal sealed class ScenarioTimer : IScenarioTimer
    {
        private readonly object _sync = new object();
        private readonly Timer _timer = new Timer();
        private bool _running;
        private bool _firstTick;
        private double _interval;

        public ScenarioTimer() => _timer.Elapsed += OnElapsed;
        public event Action? Elapsed;

        public void Start(TimeSpan startupOffset, TimeSpan interval)
        {
            lock (_sync)
            {
                _interval = interval.TotalMilliseconds;
                _firstTick = true;
                _running = true;
                _timer.AutoReset = false;
                _timer.Interval = Math.Max(1, startupOffset.TotalMilliseconds);
                _timer.Start();
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _running = false;
                _timer.Stop();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _running = false;
                _timer.Elapsed -= OnElapsed;
                _timer.Dispose();
            }
        }

        private void OnElapsed(object sender, ElapsedEventArgs args)
        {
            lock (_sync)
            {
                if (!_running) return;
                if (_firstTick)
                {
                    _firstTick = false;
                    _timer.Interval = _interval;
                    _timer.AutoReset = true;
                    _timer.Start();
                }
            }
            // Stop may race this call; delivery has its own admission barrier.
            Elapsed?.Invoke();
        }
    }
}
